// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using GameHome.Sources.PlayStation;
using Microsoft.Win32;

namespace GameHome.Windows;

/// <summary>En PlayStation som er koblet til chiaki-ng (registrert der av brukeren).</summary>
public sealed record PlayStationConsole(string Nickname, bool IsPs5)
{
    public string Title => IsPs5 ? "PlayStation 5" : "PlayStation 4";
}

/// <summary>
/// chiaki-ng (åpen kildekode, AGPL-3.0, https://github.com/chiaki-ng/chiaki-ng) strømmer fra PS5 og PS4 slik Moonlight
/// strømmer fra PC. GameHome starter bare programmet (ADR 0008); ingen kode er tatt inn. Fakta fra chiaki-ngs kildekode og
/// egne tester 2026-11-05:
/// <list type="bullet">
/// <item><c>chiaki.exe --fullscreen --exit-app-on-stream-exit stream &lt;navn&gt; &lt;adresse&gt;</c> strømmer uten
/// meny og avslutter når strømmen slutter. Valg må stå før <c>stream</c>.</item>
/// <item>Registrerte konsoller ligger i <c>HKCU\Software\Chiaki\Chiaki\registered_hosts</c> (Qt-innstillinger).</item>
/// <item>Konsollen finnes med en UDP-kringkasting «SRCH» til port 9302 (PS5) og 987 (PS4).</item>
/// </list>
/// </summary>
public static class Chiaki
{
    private const string SettingsKey = @"Software\Chiaki\Chiaki\registered_hosts";
    private const int Ps5Target = 1000000; // chiaki-ngs ChiakiTarget: PS5-verdiene begynner her.

    public static string? FindExe()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var exe = Path.Combine(Environment.GetFolderPath(root), "chiaki-ng", "chiaki.exe");
            if (File.Exists(exe))
            {
                return exe;
            }
        }

        return null;
    }

    /// <summary>Konsollene brukeren har koblet til i chiaki-ng. Tom liste hvis chiaki-ng mangler eller ingen er koblet til.</summary>
    public static IReadOnlyList<PlayStationConsole> RegisteredConsoles()
    {
        if (FindExe() is null)
        {
            return [];
        }

        using var hosts = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (hosts is null)
        {
            return [];
        }

        var consoles = new List<PlayStationConsole>();
        foreach (var name in hosts.GetSubKeyNames())
        {
            using var host = hosts.OpenSubKey(name);
            if (host?.GetValue("server_nickname") is not string nickname || string.IsNullOrWhiteSpace(nickname))
            {
                continue;
            }

            var target = int.TryParse(Convert.ToString(host.GetValue("target"), CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0;
            consoles.Add(new PlayStationConsole(nickname, target >= Ps5Target));
        }

        return consoles;
    }

    public static IReadOnlyList<StoreEntry> AsStoreEntries(IReadOnlyList<PlayStationConsole> consoles) =>
        [.. consoles.Select(c => new StoreEntry(c.Nickname, c.Title, null))];

    /// <summary>Søker etter PlayStation på det lokale nettverket. Svarer ingen innen tiden, er listen tom.</summary>
    public static async Task<IReadOnlyList<PlayStationFound>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var search = Encoding.ASCII.GetBytes(PlayStationDiscovery.Search);
        foreach (var port in new[] { 9302, 987 })
        {
            await udp.SendAsync(search, new IPEndPoint(IPAddress.Broadcast, port), ct).ConfigureAwait(false);
        }

        var found = new Dictionary<string, PlayStationFound>(StringComparer.OrdinalIgnoreCase);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var reply = await udp.ReceiveAsync(window.Token).ConfigureAwait(false);
                if (PlayStationDiscovery.Parse(Encoding.ASCII.GetString(reply.Buffer), reply.RemoteEndPoint.Address) is { } console)
                {
                    found[console.Nickname] = console;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Tiden er ute; svarene vi fikk, gjelder.
        }

        return [.. found.Values];
    }
}

/// <summary>Strømmer fra en PlayStation med chiaki-ng. <see cref="PlayAction.Target"/> er konsollens navn i chiaki-ng.</summary>
public sealed class ChiakiRunner : IRunner
{
    public const string Id = "chiaki";

    public string RunnerId => Id;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public async Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var exe = Chiaki.FindExe() ?? throw new LaunchException(LaunchFailure.FileMissing, "chiaki-ng er ikke installert");

        var found = await Chiaki.DiscoverAsync(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        var console = found.FirstOrDefault(c => string.Equals(c.Nickname, action.Target, StringComparison.OrdinalIgnoreCase))
            ?? throw new LaunchException(LaunchFailure.StartFailed,
                "Fant ikke konsollen på nettverket. Slå den på, eller sett hvilemodus til å holde nettverket på.");
        if (!console.Awake)
        {
            throw new LaunchException(LaunchFailure.StartFailed,
                "Konsollen er i hvilemodus. Slå den på med PS-knappen og prøv igjen.");
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                ArgumentList =
                {
                    "--fullscreen", "--exit-app-on-stream-exit",
                    "stream", console.Nickname, console.Address.ToString(),
                },
            }) ?? throw new LaunchException(LaunchFailure.StartFailed, "chiaki-ng startet ikke");
            return new LocalProcessSession(action, process, Path.GetDirectoryName(exe));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }
    }
}
