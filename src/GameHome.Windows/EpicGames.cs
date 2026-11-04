// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using GameHome.Sources.Epic;

namespace GameHome.Windows;

/// <summary>
/// Epic-spill fra Epic-klientens manifestfiler i <c>%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests</c>
/// (<see cref="EpicManifestReader"/>). Epic-klienten trenger ikke å kjøre for å finne spillene.
/// </summary>
public static class EpicGames
{
    /// <summary>Mappen Epic-klienten legger manifestene i.</summary>
    public static string ManifestsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>Alle fullstendig installerte spill. En fil som ikke kan leses, hoppes over.</summary>
    public static IReadOnlyList<EpicManifest> Scan(Action<string>? log = null) =>
        EpicManifestReader.ReadAll(ManifestsDirectory, log);

    /// <summary>Ett spill, lest på nytt fra manifestene.</summary>
    public static EpicManifest? Find(string appName) =>
        Scan().FirstOrDefault(g => string.Equals(g.AppName, appName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Som butikkliste for biblioteket: ID-en er Epics <c>AppName</c>.</summary>
    public static IReadOnlyList<StoreEntry> AsStoreEntries(IReadOnlyList<EpicManifest> games) =>
        [.. games.Select(g => new StoreEntry(g.AppName, g.DisplayName, g.InstallLocation))];
}

/// <summary>
/// Starter et Epic-spill via Epic-klienten (som står for innlogging og eierskap) med
/// <see cref="EpicManifest.LaunchUri"/>, og følger spillets prosess: en ny prosess fra <c>LaunchExecutable</c> i
/// installasjonsmappen.
/// </summary>
public sealed class EpicRunner : IRunner
{
    public const string Id = "epic";

    private static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(2);

    public string RunnerId => Id;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public async Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var game = EpicGames.Find(action.Target)
            ?? throw new LaunchException(LaunchFailure.FileMissing, "Spillet er ikke installert lenger");
        var executable = game.ExecutablePath;
        var name = Path.GetFileNameWithoutExtension(executable);

        var before = Process.GetProcesses().Select(p =>
        {
            using (p)
            {
                return p.Id;
            }
        }).ToHashSet();

        try
        {
            Process.Start(new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }

        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < GiveUp && !ct.IsCancellationRequested)
        {
            foreach (var candidate in Process.GetProcesses())
            {
                if (!before.Contains(candidate.Id)
                    && string.Equals(candidate.ProcessName, name, StringComparison.OrdinalIgnoreCase)
                    && IsSameFile(candidate, executable))
                {
                    return new LocalProcessSession(action, candidate, game.InstallLocation);
                }

                candidate.Dispose();
            }

            await Task.Delay(1000, ct);
        }

        throw new LaunchException(LaunchFailure.StartFailed,
            "Epic startet ikke spillet innen to minutter. Sjekk at du er logget inn i Epic Games Launcher.");
    }

    // Kan ikke stien leses (for eksempel en prosess med høyere rettigheter), godtas navnet alene.
    private static bool IsSameFile(Process process, string executable)
    {
        try
        {
            return process.MainModule?.FileName is not { } file
                || string.Equals(Path.GetFullPath(file), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }
}
