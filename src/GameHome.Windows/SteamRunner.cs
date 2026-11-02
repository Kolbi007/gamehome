// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GameHome.Windows;

/// <summary>Finner Steam på denne maskinen.</summary>
public static class SteamClient
{
    internal const string SteamKey = @"Software\Valve\Steam";

    /// <summary>Steam-mappen fra registeret, eller null hvis Steam ikke er installert.</summary>
    public static string? FindRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SteamKey);
        var path = key?.GetValue("SteamPath") as string;
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Replace('/', '\\'));
    }
}

/// <summary>
/// Starter Steam-spill via Steam selv (<c>steam://rungameid/</c>), så Steam tar seg av oppdateringer, sky-lagring og DRM.
/// </summary>
public sealed class SteamRunner : IRunner
{
    public string RunnerId => GameLibrary.SteamRunnerId;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (SteamClient.FindRoot() is null)
        {
            throw new LaunchException(LaunchFailure.StartFailed, "Steam er ikke installert");
        }

        // Følg registeret før start, så vi ikke går glipp av en rask endring.
        var session = new SteamSession(action);
        try
        {
            Process.Start(new ProcessStartInfo($"steam://rungameid/{action.Target}") { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            session.Dispose();
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }

        return Task.FromResult<ISession>(session);
    }
}

public enum SteamSessionState { Starting, Running, Ended }

/// <summary>
/// En Steam-økt. Spillet kjører når Steam setter <c>RunningAppID</c> til spillets appid, og er ferdig når verdien endres
/// igjen. Registeret følges med <c>RegNotifyChangeKeyValue</c>: ingen polling mens spillet kjører.
/// </summary>
public sealed class SteamSession : IForegroundSession
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    private readonly TaskCompletionSource<SessionEnd> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _appId;
    private readonly string? _installDirectory;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _watcher;
    private readonly Timer _startTimer;
    private bool _closeRequested;

    public SteamSession(PlayAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PlayActionId = action.Id;
        _appId = action.Target;
        _installDirectory = action.WorkingDirectory;
        _startTimer = new Timer(_ => OnStartTimeout(), null, StartTimeout, Timeout.InfiniteTimeSpan);
        _watcher = new Thread(Watch) { IsBackground = true, Name = "GameHome Steam-signal" };
        _watcher.Start();
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public string PlayActionId { get; }

    public SessionKind Kind => SessionKind.Local;

    public Task<SessionEnd> Completion => _completion.Task;

    public SteamSessionState State { get; private set; } = SteamSessionState.Starting;

    public string Status => State switch
    {
        SteamSessionState.Starting => "Starter via Steam …",
        SteamSessionState.Running => "Kjører via Steam",
        _ => "Avsluttet",
    };

    public nint MainWindowHandle
    {
        get
        {
            foreach (var process in GameProcesses())
            {
                using (process)
                {
                    if (process.MainWindowHandle != 0)
                    {
                        return process.MainWindowHandle;
                    }
                }
            }

            return 0;
        }
    }

    public event EventHandler<string>? Note;

    public event EventHandler? StatusChanged;

    public Task RequestCloseAsync()
    {
        _closeRequested = true;
        var any = false;
        foreach (var process in GameProcesses())
        {
            using (process)
            {
                any |= process.MainWindowHandle != 0 && process.CloseMainWindow();
            }
        }

        Note?.Invoke(this, any ? "Ba spillet avslutte normalt (WM_CLOSE)" : "Fant ikke spillets vindu å lukke");
        return Task.CompletedTask;
    }

    public Task ForceTerminateAsync()
    {
        _closeRequested = true;
        foreach (var process in GameProcesses())
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    Note?.Invoke(this, $"Tvangsavsluttet {process.ProcessName}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Note?.Invoke(this, $"Kunne ikke avslutte {process.ProcessName}: {ex.Message}");
                }
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stop.Set();
        _startTimer.Dispose();
        if (_watcher.IsAlive && Thread.CurrentThread != _watcher)
        {
            _watcher.Join(TimeSpan.FromSeconds(2));
        }

        _stop.Dispose();
    }

    private void Watch()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SteamClient.SteamKey);
            if (key is null)
            {
                Finish(SessionEndReason.Unknown, "Fant ikke Steam i registeret");
                return;
            }

            using var changed = new AutoResetEvent(false);
            while (!_completion.Task.IsCompleted)
            {
                // Meld interesse før lesing, så en endring mellom lesing og venting ikke går tapt.
                var result = NativeMethods.RegNotifyChangeKeyValue(
                    key.Handle, watchSubtree: false, NativeMethods.RegNotifyChangeLastSet, changed.SafeWaitHandle, asynchronous: true);
                if (result != 0)
                {
                    Finish(SessionEndReason.Unknown, string.Create(CultureInfo.InvariantCulture, $"RegNotifyChangeKeyValue feilet ({result})"));
                    return;
                }

                OnRunningAppId(Convert.ToString(key.GetValue("RunningAppID"), CultureInfo.InvariantCulture) ?? "0");
                if (_completion.Task.IsCompleted || WaitHandle.WaitAny([changed, _stop]) == 1)
                {
                    return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Økten ble lukket mens vi ventet.
        }
    }

    private void OnRunningAppId(string runningAppId)
    {
        var isOurs = runningAppId == _appId;
        if (State == SteamSessionState.Starting && isOurs)
        {
            State = SteamSessionState.Running;
            _startTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Note?.Invoke(this, $"Steam melder at spillet kjører (RunningAppID {runningAppId})");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (State == SteamSessionState.Running && !isOurs)
        {
            Note?.Invoke(this, $"Steam melder at spillet er ferdig (RunningAppID {runningAppId})");
            Finish(_closeRequested ? SessionEndReason.ClosedByUser : SessionEndReason.Exited, null);
        }
    }

    private void OnStartTimeout()
    {
        if (State == SteamSessionState.Starting)
        {
            Finish(SessionEndReason.LaunchFailed, "Steam startet ikke spillet innen 3 minutter");
        }
    }

    private void Finish(SessionEndReason reason, string? detail)
    {
        State = SteamSessionState.Ended;
        if (_completion.TrySetResult(new SessionEnd(reason, Detail: detail)))
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Prosesser som kjører fra spillets installasjonsmappe. Leses bare når brukeren ber om det.</summary>
    private List<Process> GameProcesses()
    {
        var result = new List<Process>();
        if (string.IsNullOrEmpty(_installDirectory))
        {
            return result;
        }

        var prefix = _installDirectory.TrimEnd('\\') + "\\";
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainModule?.FileName is { } path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(process);
                    continue;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Systemprosesser kan ikke leses.
            }

            process.Dispose();
        }

        return result;
    }
}

internal static partial class NativeMethods
{
    internal const uint RegNotifyChangeLastSet = 0x4;

    [LibraryImport("advapi32.dll")]
    internal static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        SafeWaitHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
