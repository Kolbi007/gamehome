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

    private readonly TaskCompletionSource<SessionEnd> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _directory;
    private readonly DateTime _started = DateTime.UtcNow;
    private Process _process;
    private bool _closeRequested;

    public LocalProcessSession(PlayAction action, Process process)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(process);
        PlayActionId = action.Id;
        _directory = Path.GetDirectoryName(action.Target) ?? string.Empty;
        _process = process;
        Watch(process);
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public string PlayActionId { get; }

    public SessionKind Kind => SessionKind.Local;

    public Task<SessionEnd> Completion => _completion.Task;

    public int ProcessId => _process.Id;

    /// <summary>Hovedvinduet til spillet, for å gi det forgrunnen igjen. 0 hvis det ikke har et.</summary>
    public nint MainWindowHandle
    {
        get
        {
            try
            {
                _process.Refresh();
                return _process.HasExited ? 0 : _process.MainWindowHandle;
            }
            catch (InvalidOperationException)
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
            if (!_process.HasExited)
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

    public void Dispose() => _process.Dispose();

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
