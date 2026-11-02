// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;

namespace GameHome.Windows;

/// <summary>
/// Starter et spill direkte fra programfilen. Ingen Job Object med kill-on-close: krasjer GameHome, lever spillet videre.
/// </summary>
public sealed class ExecutableRunner : IRunner
{
    public string RunnerId => GameLibrary.ExecutableRunnerId;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!File.Exists(action.Target))
        {
            throw new LaunchException(LaunchFailure.FileMissing, $"Finner ikke {action.Target}");
        }

        var start = new ProcessStartInfo(action.Target)
        {
            UseShellExecute = false,
            WorkingDirectory = action.WorkingDirectory ?? Path.GetDirectoryName(action.Target) ?? string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(action.Arguments))
        {
            start.Arguments = action.Arguments;
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new LaunchException(LaunchFailure.StartFailed, "Prosessen startet ikke");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }

        return Task.FromResult<ISession>(new LocalProcessSession(action, process));
    }
}

public enum LaunchFailure { FileMissing, StartFailed }

public sealed class LaunchException : Exception
{
    public LaunchException(LaunchFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    public LaunchException()
    {
    }

    public LaunchException(string message) : base(message)
    {
    }

    public LaunchException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public LaunchFailure Failure { get; }
}

/// <summary>
/// En lokal økt. Avslutter startprosessen raskt og et annet program fra samme mappe kjører (en liten oppstarter),
/// følger økten det programmet i stedet.
/// </summary>
public sealed class LocalProcessSession : IForegroundSession
{
    private static readonly TimeSpan LauncherWindow = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan GameSearchWindow = TimeSpan.FromSeconds(60);

    private readonly TaskCompletionSource<SessionEnd> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _directory;
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly HashSet<int> _before;
    private readonly Timer _search;
    private Process _process;
    private Process? _game;
    private bool _closeRequested;

    public LocalProcessSession(PlayAction action, Process process, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(process);
        PlayActionId = action.Id;
        _directory = directory ?? Path.GetDirectoryName(action.Target) ?? string.Empty;
        _process = process;

        Watch(process);

        // Launcher som blir liggende (for eksempel en oppstarter som starter spillet og venter): finn spillet i samme
        // mappe, så økten slutter når spillet slutter, og M2 og «Tilbake til spillet» treffer spillets vindu.
        // Av som standard (FocusExperiments): undersøkes som mulig årsak til fokusfeil ved strømstart 2026-11-02.
        if (FocusExperiments.FollowGameProcess)
        {
            // Prosessene som fantes før start: bare nye prosesser undersøkes når spillet letes etter (billig).
            _before = [.. Process.GetProcesses().Select(p =>
            {
                using (p)
                {
                    return p.Id;
                }
            })];
            _search = new Timer(_ => SearchForGame(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
        else
        {
            _before = [];
            _search = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public string PlayActionId { get; }

    public SessionKind Kind => SessionKind.Local;

    public Task<SessionEnd> Completion => _completion.Task;

    public int ProcessId => _process.Id;

    /// <summary>Prosessen som er selve spillet (spillet, ikke en oppstarter som blir liggende), for forgrunnen.</summary>
    public int GameProcessId => _game is { } game ? game.Id : _process.Id;

    /// <summary>Hovedvinduet til spillet, for å gi det forgrunnen igjen. 0 hvis det ikke har et.</summary>
    public nint MainWindowHandle
    {
        get
        {
            try
            {
                if (_game is { } game)
                {
                    game.Refresh();
                    if (!game.HasExited && game.MainWindowHandle != 0)
                    {
                        return game.MainWindowHandle;
                    }
                }

                _process.Refresh();
                return _process.HasExited ? 0 : _process.MainWindowHandle;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return 0;
            }
        }
    }

    public string Status => $"PID {ProcessId}";

    public event EventHandler? StatusChanged;

    /// <summary>Skrives til loggen av appen, for eksempel når økten følger en oppstarter.</summary>
    public event EventHandler<string>? Note;

    public Task RequestCloseAsync()
    {
        _closeRequested = true;
        try
        {
            if (_game is { HasExited: false } game)
            {
                game.CloseMainWindow();
            }
            else if (!_process.HasExited)
            {
                _process.CloseMainWindow();
            }
        }
        catch (InvalidOperationException)
        {
        }

        return Task.CompletedTask;
    }

    public Task ForceTerminateAsync()
    {
        _closeRequested = true;
        try
        {
            if (_game is { HasExited: false } game)
            {
                game.Kill(entireProcessTree: true);
            }

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _search.Dispose();
        _game?.Dispose();
        _process.Dispose();
    }

    /// <summary>
    /// Hvert 2. sekund i høyst ett minutt: finnes en ny prosess fra spillmappen (ikke startprosessen), er det spillet.
    /// Bare prosesser som ikke fantes ved start, undersøkes.
    /// </summary>
    private void SearchForGame()
    {
        if (_game is not null || _completion.Task.IsCompleted || DateTime.UtcNow - _started > GameSearchWindow)
        {
            _search.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        foreach (var candidate in Process.GetProcesses())
        {
            if (_game is null && candidate.Id != _process.Id && !_before.Contains(candidate.Id) && IsFromGameFolder(candidate))
            {
                _game = candidate;
                Note?.Invoke(this, $"Spillet kjører som {candidate.ProcessName} (PID {candidate.Id}); {_process.ProcessName} er oppstarteren");
                candidate.EnableRaisingEvents = true;
                candidate.Exited += (_, _) => OnGameExited(candidate);
                if (candidate.HasExited)
                {
                    OnGameExited(candidate);
                }

                continue;
            }

            candidate.Dispose();
        }
    }

    private bool IsFromGameFolder(Process candidate)
    {
        try
        {
            return !string.IsNullOrEmpty(_directory)
                && candidate.MainModule?.FileName is { } path && path.StartsWith(_directory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Spillet er avsluttet mens oppstarteren lever videre: økten er slutt (oppstarteren røres ikke).</summary>
    private void OnGameExited(Process game)
    {
        if (!ReferenceEquals(game, _game) || _completion.Task.IsCompleted)
        {
            return;
        }

        int? exitCode = null;
        try
        {
            exitCode = game.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        bool launcherAlive;
        try
        {
            launcherAlive = !_process.HasExited;
        }
        catch (InvalidOperationException)
        {
            launcherAlive = false;
        }

        if (launcherAlive)
        {
            Note?.Invoke(this, $"Spillet avsluttet ({exitCode}); oppstarteren kjører fortsatt, økten er slutt");
            _completion.TrySetResult(new SessionEnd(_closeRequested ? SessionEndReason.ClosedByUser : SessionEndReason.Exited, exitCode));
        }
    }

    private void Watch(Process process)
    {
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => OnExited(process);
        if (process.HasExited)
        {
            OnExited(process);
        }
    }

    private void OnExited(Process process)
    {
        if (!ReferenceEquals(process, _process) || _completion.Task.IsCompleted)
        {
            return;
        }

        int? exitCode = null;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        if (!_closeRequested && DateTime.UtcNow - _started < LauncherWindow && FindSuccessor(process.Id) is { } next)
        {
            Note?.Invoke(this, $"Startprosessen avsluttet ({exitCode}); følger {next.ProcessName} (PID {next.Id})");
            StatusChanged?.Invoke(this, EventArgs.Empty);
            _process = next;
            process.Dispose();
            Watch(next);
            return;
        }

        var reason = _closeRequested ? SessionEndReason.ClosedByUser : SessionEndReason.Exited;
        _completion.TrySetResult(new SessionEnd(reason, exitCode));
    }

    private Process? FindSuccessor(int previousId)
    {
        if (string.IsNullOrEmpty(_directory))
        {
            return null;
        }

        foreach (var candidate in Process.GetProcesses())
        {
            if (candidate.Id == previousId)
            {
                candidate.Dispose();
                continue;
            }

            try
            {
                var path = candidate.MainModule?.FileName;
                if (path is not null && path.StartsWith(_directory, StringComparison.OrdinalIgnoreCase)
                    && candidate.StartTime.ToUniversalTime() >= _started)
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Systemprosesser og prosesser som allerede er borte, kan ikke leses.
            }

            candidate.Dispose();
        }

        return null;
    }
}
