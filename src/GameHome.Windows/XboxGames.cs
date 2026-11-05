// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Xml.Linq;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using Windows.Management.Deployment;

namespace GameHome.Windows;

/// <summary>Et installert Xbox-/Microsoft Store-spill (GDK): pakken, appen og programfilene spillet kjører som.</summary>
public sealed record XboxGame(string FamilyName, string AppId, string Title, string InstallDirectory, IReadOnlyList<string> Executables)
{
    /// <summary>App-ID-en Windows starter appen med («PFN!App»).</summary>
    public string Aumid => $"{FamilyName}!{AppId}";
}

/// <summary>
/// Xbox-spill fra Windows' egen pakkeliste for brukeren (ingen Xbox-app trengs). En pakke er et spill når den har en
/// <c>MicrosoftGame.config</c> (GDK-spill, for eksempel Balatro: PlayStack.Balatro, love.exe; målt 2026-11-02).
/// Navn og programfiler leses fra den filen.
/// </summary>
public static class XboxGames
{
    public static IReadOnlyList<XboxGame> Scan(Action<string>? log = null)
    {
        var games = new List<XboxGame>();
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle
                        || package.InstalledLocation?.Path is not { } location)
                    {
                        continue;
                    }

                    var config = Path.Combine(location, "MicrosoftGame.config");
                    if (!File.Exists(config) || Read(config, package.Id.FamilyName, location) is not { } game)
                    {
                        continue;
                    }

                    games.Add(game);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    // Pakker som ikke kan leses (for eksempel under oppdatering), hoppes over.
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            log?.Invoke($"Kunne ikke lese pakkelisten: {ex.Message}");
        }

        return games;
    }

    /// <summary>Leser navn og programfiler fra MicrosoftGame.config.</summary>
    public static XboxGame? Read(string configPath, string familyName, string installDirectory)
    {
        try
        {
            var root = XDocument.Load(configPath).Root;
            if (root is null)
            {
                return null;
            }

            var visuals = root.Element("ShellVisuals");
            var title = visuals?.Attribute("DefaultDisplayName")?.Value;
            var executables = root.Element("ExecutableList")?.Elements("Executable")
                .Where(e => e.Attribute("TargetDeviceFamily")?.Value is null or "PC")
                .ToList() ?? [];
            var appId = executables.Select(e => e.Attribute("Id")?.Value).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (string.IsNullOrWhiteSpace(title) || appId is null)
            {
                return null;
            }

            var names = executables.Select(e => e.Attribute("Name")?.Value).OfType<string>()
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
            return new XboxGame(familyName, appId, title, installDirectory, names);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Som butikkliste for biblioteket: ID-en er app-ID-en («PFN!App»), som også brukes til å starte spillet.</summary>
    public static IReadOnlyList<StoreEntry> AsStoreEntries(IReadOnlyList<XboxGame> games) =>
        [.. games.Select(g => new StoreEntry(g.Aumid, g.Title, g.InstallDirectory))];
}

/// <summary>
/// Starter et Xbox-spill med Windows' dokumenterte API (<c>AppListEntry.LaunchAsync</c>), og følger spillets prosess
/// (en programfil fra MicrosoftGame.config, fra spillets mappe eller C:\XboxGames). Ingen Xbox-app trengs.
/// </summary>
public sealed class XboxRunner : IRunner
{
    public const string Id = "xbox";

    public string RunnerId => Id;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public async Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var parts = action.Target.Split('!', 2);
        if (parts.Length != 2)
        {
            throw new LaunchException(LaunchFailure.StartFailed, $"Ugyldig Xbox-app-ID: {action.Target}");
        }

        var package = new PackageManager().FindPackagesForUser(string.Empty, parts[0]).FirstOrDefault()
            ?? throw new LaunchException(LaunchFailure.FileMissing, "Spillet er ikke installert lenger");
        var entries = await package.GetAppListEntriesAsync();
        var entry = entries.FirstOrDefault(e => e.AppUserModelId == action.Target)
            ?? (entries.Count > 0 ? entries[0] : null)
            ?? throw new LaunchException(LaunchFailure.StartFailed, "Fant ikke appen i pakken");

        var before = Process.GetProcesses().Select(p =>
        {
            using (p)
            {
                return p.Id;
            }
        }).ToHashSet();
        if (!await entry.LaunchAsync())
        {
            throw new LaunchException(LaunchFailure.StartFailed, "Windows startet ikke spillet");
        }

        // Spillets prosess: en programfil fra MicrosoftGame.config, fra installasjonen eller C:\XboxGames.
        var config = Path.Combine(package.InstalledLocation.Path, "MicrosoftGame.config");
        var game = File.Exists(config) ? XboxGames.Read(config, parts[0], package.InstalledLocation.Path) : null;
        var names = game?.Executables ?? [];
        for (var i = 0; i < 60 && !ct.IsCancellationRequested; i++)
        {
            foreach (var candidate in Process.GetProcesses())
            {
                if (!before.Contains(candidate.Id) && names.Contains(candidate.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    var directory = SafeDirectory(candidate) ?? package.InstalledLocation.Path;
                    return new LocalProcessSession(action, candidate, directory);
                }

                candidate.Dispose();
            }

            await Task.Delay(1000, ct);
        }

        throw new LaunchException(LaunchFailure.StartFailed, "Spillet startet, men GameHome fant ikke spillets prosess innen ett minutt");
    }

    private static string? SafeDirectory(Process process)
    {
        try
        {
            return Path.GetDirectoryName(process.MainModule?.FileName);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
