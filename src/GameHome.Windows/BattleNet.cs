// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using Microsoft.Win32;

namespace GameHome.Windows;

/// <summary>Et installert Battle.net-spill: koden Battle.net starter det med, navnet, mappen og programfilene.</summary>
public sealed record BattleNetGame(string Code, string Title, string InstallDirectory, IReadOnlyList<string> Executables);

/// <summary>
/// World of Warcraft-utgaver fra installasjonsmappen (<c>World of Warcraft\_forever_</c> osv.; målt 2026-11-04 på Legion
/// Go). Spillene startes via Battle.net, som står for innloggingen: <c>Battle.net.exe --exec="launch KODE"</c>
/// (kodene er ikke dokumentert av Blizzard, men brukt av Lutris og LaunchBox: WoW, WoWC, WoWF).
/// </summary>
public static class BattleNetGames
{
    // Mappe under «World of Warcraft» → kode og navn.
    private static readonly (string Folder, string Code, string Title)[] WowFlavors =
    [
        ("_retail_", "WoW", "World of Warcraft"),
        ("_classic_", "WoWC", "World of Warcraft Classic"),
        ("_forever_", "WoWF", "World of Warcraft Forever"),
    ];

    public static IReadOnlyList<BattleNetGame> Scan()
    {
        var games = new List<BattleNetGame>();
        foreach (var root in WowRoots())
        {
            foreach (var (folder, code, title) in WowFlavors)
            {
                var directory = Path.Combine(root, folder);
                if (games.Exists(g => g.Code == code) || !Directory.Exists(directory))
                {
                    continue;
                }

                var executables = Directory.EnumerateFiles(directory, "Wow*.exe")
                    .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
                if (executables.Count > 0)
                {
                    games.Add(new BattleNetGame(code, title, directory, executables));
                }
            }
        }

        return games;
    }

    /// <summary>Programfilene til en kode, lest på nytt fra disk (for å finne spillets prosess).</summary>
    public static IReadOnlyList<string> ExecutablesFor(string code) =>
        Scan().FirstOrDefault(g => g.Code == code)?.Executables ?? [];

    /// <summary>Battle.net.exe fra Windows' liste over installerte programmer, ellers standardplasseringen.</summary>
    public static string? LauncherPath()
    {
        foreach (var key in new[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net",
        })
        {
            using var uninstall = Registry.LocalMachine.OpenSubKey(key);
            if (uninstall?.GetValue("InstallLocation") is string location
                && Path.Combine(location, "Battle.net.exe") is var exe && File.Exists(exe))
            {
                return exe;
            }
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    public static IReadOnlyList<StoreEntry> AsStoreEntries(IReadOnlyList<BattleNetGame> games) =>
        [.. games.Select(g => new StoreEntry(g.Code, g.Title, g.InstallDirectory))];

    private static IEnumerable<string> WowRoots()
    {
        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var root = Path.Combine(Environment.GetFolderPath(programs), "World of Warcraft");
            if (Directory.Exists(root))
            {
                yield return root;
            }
        }
    }
}

/// <summary>
/// Starter et Battle.net-spill via Battle.net (som logger inn) og følger spillets prosess. Kjører ikke Battle.net fra
/// før, starter første kall ofte bare Battle.net; da sendes kallet én gang til.
/// </summary>
public sealed class BattleNetRunner : IRunner
{
    public const string Id = "battlenet";

    private static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SecondCall = TimeSpan.FromSeconds(20);

    public string RunnerId => Id;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public async Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var launcher = BattleNetGames.LauncherPath()
            ?? throw new LaunchException(LaunchFailure.FileMissing, "Battle.net er ikke installert");
        var names = BattleNetGames.ExecutablesFor(action.Target);
        if (names.Count == 0)
        {
            throw new LaunchException(LaunchFailure.FileMissing, "Spillet er ikke installert lenger");
        }

        var before = Process.GetProcesses().Select(p =>
        {
            using (p)
            {
                return p.Id;
            }
        }).ToHashSet();

        Exec(launcher, action.Target);
        var started = DateTime.UtcNow;
        var secondSent = false;
        while (DateTime.UtcNow - started < GiveUp && !ct.IsCancellationRequested)
        {
            foreach (var candidate in Process.GetProcesses())
            {
                if (!before.Contains(candidate.Id) && names.Contains(candidate.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    return new LocalProcessSession(action, candidate, action.WorkingDirectory);
                }

                candidate.Dispose();
            }

            if (!secondSent && DateTime.UtcNow - started > SecondCall)
            {
                secondSent = true;
                Exec(launcher, action.Target);
            }

            await Task.Delay(1000, ct);
        }

        throw new LaunchException(LaunchFailure.StartFailed,
            "Battle.net startet ikke spillet innen to minutter. Sjekk at du er logget inn i Battle.net.");
    }

    private static void Exec(string launcher, string code)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(launcher)
            {
                UseShellExecute = false,
                Arguments = $"--exec=\"launch {code}\"",
                WorkingDirectory = Path.GetDirectoryName(launcher) ?? string.Empty,
            });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }
    }
}
