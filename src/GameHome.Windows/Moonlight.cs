// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using Microsoft.Win32;

namespace GameHome.Windows;

/// <summary>En host Moonlight er paret med. Bare navn og adresser; nøkler og sertifikater leses aldri.</summary>
public sealed record MoonlightHost(string Uuid, string Name, string? Address, byte[]? Mac = null)
{
    /// <summary>
    /// Navn som vises i GameHome. Windows-standardnavn som LAPTOP-1G28Q2RP sier lite; de vises som «Gaming Laptop» eller
    /// «Gaming PC». Andre navn vises som de er.
    /// </summary>
    public string DisplayName =>
        Name.StartsWith("LAPTOP-", StringComparison.OrdinalIgnoreCase) ? "Gaming Laptop"
        : Name.StartsWith("DESKTOP-", StringComparison.OrdinalIgnoreCase) ? "Gaming PC"
        : Name;
}

/// <summary>Resultatet av et Moonlight-kall fra kommandolinjen.</summary>
public sealed record MoonlightResult(int ExitCode, string Output, string Errors, TimeSpan Elapsed, bool TimedOut);

/// <summary>
/// Kaller Moonlight (moonlight-qt) som egen prosess. GameHome bygger ikke inn Moonlight-kode. Kallene må gjøres fra
/// skrivebordsøkten: Moonlight starter grafikk også for <c>list</c> og henger uten skrivebord (docs/architecture.md).
/// </summary>
public static class MoonlightClient
{
    private const string SettingsKey = @"Software\Moonlight Game Streaming Project\Moonlight";

    public static string? FindExe()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Path.Combine(Environment.GetFolderPath(root), "Moonlight Game Streaming", "Moonlight.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Moonlight Game Streaming", "Moonlight.exe");
        return File.Exists(local) ? local : null;
    }

    /// <summary>Hostene Moonlight kjenner, fra Moonlights egne innstillinger (QSettings i registeret).</summary>
    public static IReadOnlyList<MoonlightHost> FindHosts()
    {
        var hosts = new List<MoonlightHost>();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey + @"\hosts");
        if (key is null)
        {
            return hosts;
        }

        foreach (var name in key.GetSubKeyNames())
        {
            using var host = key.OpenSubKey(name);
            var uuid = host?.GetValue("uuid") as string;
            if (string.IsNullOrWhiteSpace(uuid))
            {
                continue;
            }

            var address = host!.GetValue("manualaddress") as string;
            if (string.IsNullOrWhiteSpace(address))
            {
                address = host.GetValue("localaddress") as string;
            }

            // MAC-adressen (for Wake-on-LAN) lagrer Moonlight som Qt-tekst «@ByteArray(...)»; den er ikke hemmelig.
            var mac = host.GetValue("mac") switch
            {
                byte[] raw when raw.Length == GameHome.Core.Network.WakeOnLan.MacLength => raw,
                string text => GameHome.Core.Network.WakeOnLan.ParseQtByteArray(text),
                _ => null,
            };

            hosts.Add(new MoonlightHost(uuid, host.GetValue("hostname") as string ?? uuid,
                string.IsNullOrWhiteSpace(address) ? null : address, mac));
        }

        return hosts;
    }

    /// <summary>Kjører Moonlight med argumentene, leser stdout og stderr og avbryter etter tidsgrensen.</summary>
    public static async Task<MoonlightResult> RunAsync(IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        var exe = FindExe() ?? throw new LaunchException(LaunchFailure.FileMissing, "Moonlight er ikke installert");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new LaunchException(LaunchFailure.StartFailed, "Moonlight startet ikke");

        // Begge strømmene leses samtidig, ellers kan Moonlights logging på stderr fylle røret og blokkere.
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        var stdout = await SafeRead(output).ConfigureAwait(false);
        var stderr = await SafeRead(errors).ConfigureAwait(false);
        return new MoonlightResult(timedOut ? -2 : process.ExitCode, stdout, stderr, watch.Elapsed, timedOut);
    }

    /// <summary>
    /// <c>list &lt;host&gt; --csv</c>. Moonlight skriver listen og blir så hengende (bakgrunnsnedlasting av forsidebilder,
    /// målt 2026-11-02: listen kom, prosessen avsluttet ikke på 70 s). Listen regnes derfor som ferdig når headeren er
    /// kommet og det har vært stille i ett sekund; da lukkes Moonlight.
    /// </summary>
    public static async Task<MoonlightResult> ListAppsAsync(string host, TimeSpan timeout, CancellationToken ct = default)
    {
        var exe = FindExe() ?? throw new LaunchException(LaunchFailure.FileMissing, "Moonlight er ikke installert");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("list");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add("--csv");

        var watch = Stopwatch.StartNew();
        var lines = new List<string>();
        var errors = new System.Text.StringBuilder();
        var headerSeen = false;
        var lastLine = Stopwatch.StartNew();
        var gate = new object();

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (gate)
            {
                lines.Add(e.Data);
                headerSeen |= e.Data.StartsWith("Name,", StringComparison.Ordinal);
                lastLine.Restart();
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (gate)
                {
                    errors.AppendLine(e.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new LaunchException(LaunchFailure.StartFailed, "Moonlight startet ikke");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var complete = false;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), limit.Token).ConfigureAwait(false);
                lock (gate)
                {
                    complete = headerSeen && lastLine.Elapsed > TimeSpan.FromSeconds(1);
                }

                if (complete)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }

        var exited = process.HasExited;
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }
        else
        {
            process.WaitForExit(); // tømmer gjenværende utdata
        }

        string output;
        string stderr;
        lock (gate)
        {
            output = string.Join('\n', lines);
            stderr = errors.ToString();
            complete |= headerSeen;
        }

        var exitCode = exited ? process.ExitCode : complete ? 0 : -2;
        return new MoonlightResult(exitCode, output, stderr, watch.Elapsed, TimedOut: !exited && !complete);
    }

    private static async Task<string> SafeRead(Task<string> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Strømmer et spill fra en host med <c>Moonlight.exe stream &lt;host&gt; "&lt;app&gt;"</c>. Økten er Moonlight-prosessen.
/// Å lukke Moonlight kobler fra; spillet på hosten avsluttes bare med <see cref="StreamSession.EndRemoteAppAsync"/>.
/// </summary>
public sealed class MoonlightRunner : IRunner
{
    public string RunnerId => GameLibrary.MoonlightRunnerId;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.Detach;

    public Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var host = action.HostId ?? throw new LaunchException(LaunchFailure.StartFailed, "Spillemåten mangler host");
        var exe = MoonlightClient.FindExe() ?? throw new LaunchException(LaunchFailure.FileMissing, "Moonlight er ikke installert");

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add("stream");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(action.Target);
        start.ArgumentList.Add("--display-mode");
        start.ArgumentList.Add("fullscreen");

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new LaunchException(LaunchFailure.StartFailed, "Moonlight startet ikke");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }

        return Task.FromResult<ISession>(new StreamSession(action, host, process));
    }
}

/// <summary>En stream-økt: Moonlight lokalt, spillet på hosten.</summary>
public sealed class StreamSession : IForegroundSession
{
    private readonly LocalProcessSession _client;
    private readonly string _host;

    public StreamSession(PlayAction action, string host, Process moonlight)
    {
        _host = host;
        _client = new LocalProcessSession(action, moonlight);
        _client.Note += (_, note) => Note?.Invoke(this, note);
    }

    public string SessionId => _client.SessionId;

    public string PlayActionId => _client.PlayActionId;

    public SessionKind Kind => SessionKind.Stream;

    public Task<SessionEnd> Completion => _client.Completion;

    public nint MainWindowHandle => _client.MainWindowHandle;

    /// <summary>
    /// Om Moonlight viser streamen. Moonlight (moonlight-qt) strømmer i et eget SDL-vindu, adskilt fra Qt-vinduet som viser
    /// «Establishing connection». Hypotese til den er testet: vindusklassen er «SDL_app».
    /// </summary>
    public bool IsStreamVisible => Foreground.HasVisibleWindow(_client.ProcessId, "SDL_app");

    /// <summary>Moonlight-prosessen.</summary>
    public int ClientProcessId => _client.ProcessId;

    /// <summary>Selve streamvinduet (SDL) hvis det er synlig, ellers 0. Moonlights skjulte hovedvindu brukes aldri.</summary>
    public nint StreamWindowHandle => Foreground.FindVisibleWindow(_client.ProcessId, "SDL_app");

    public string Status => "Strømmer";

    public event EventHandler<string>? Note;

    public event EventHandler? StatusChanged
    {
        add => _client.StatusChanged += value;
        remove => _client.StatusChanged -= value;
    }

    /// <summary>Kobler fra: Moonlight lukkes, spillet fortsetter på hosten.</summary>
    public Task RequestCloseAsync() => _client.RequestCloseAsync();

    public Task ForceTerminateAsync() => _client.ForceTerminateAsync();

    /// <summary>Avslutter spillet på hosten (<c>Moonlight.exe quit</c>). Kun etter bekreftelse fra brukeren.</summary>
    public async Task<MoonlightResult> EndRemoteAppAsync()
    {
        var result = await MoonlightClient.RunAsync(["quit", _host], TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        Note?.Invoke(this, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Avslutt fjernspill: exit {result.ExitCode} etter {result.Elapsed.TotalSeconds:F1} s"));
        if (result.ExitCode != 0)
        {
            return result;
        }

        await CloseClientAfterRemoteEndAsync().ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Spillet på hosten er avsluttet (via Moonlight eller Vibepollos API). Moonlight blir stående med «connection
    /// terminated» (målt 2026-11-02), så den lokale klienten lukkes: først normalt, så med tvang etter 5 s. Det rammer
    /// bare Moonlight, ikke spillet.
    /// </summary>
    public async Task CloseClientAfterRemoteEndAsync()
    {
        await _client.RequestCloseAsync().ConfigureAwait(false);
        var closed = await Task.WhenAny(_client.Completion, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) == _client.Completion;
        if (!closed)
        {
            await _client.ForceTerminateAsync().ConfigureAwait(false);
            Note?.Invoke(this, "Moonlight lukket etter avsluttet fjernspill");
        }
    }

    public void Dispose() => _client.Dispose();
}
