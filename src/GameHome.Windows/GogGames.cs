// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using GameHome.Sources.Gog;
using Microsoft.Win32;

namespace GameHome.Windows;

/// <summary>Et installert GOG-spill: ID, navn, mappe og programfilen spillet starter med.</summary>
public sealed record GogGame(
    string GameId,
    string Title,
    string InstallDirectory,
    string Executable,
    string? Arguments,
    string WorkingDirectory);

/// <summary>
/// GOG-spill fra registeret, <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;id&gt;</c> (32-bits visning), som GOGs
/// installasjonsprogram skriver også uten GOG Galaxy. Verdinavnene <c>gameID</c>, <c>gameName</c>, <c>path</c> og
/// <c>dependsOn</c> følger GameFinder (<c>GameFinder.StoreHandlers.GOG/GOGHandler.cs</c>); <c>exe</c> brukes bare som
/// reserve når <c>goggame-&lt;id&gt;.info</c> mangler. Programfilen leses fra .info-filen (<see cref="GogInfoReader"/>).
/// </summary>
public static class GogGames
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";

    /// <summary>Alle installerte GOG-spill. Et spill som ikke kan leses, hoppes over; de andre leses likevel.</summary>
    public static IReadOnlyList<GogGame> Scan(Action<string>? log = null)
    {
        var games = new List<GogGame>();
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var root = hklm.OpenSubKey(GamesKey);
            if (root is null)
            {
                return games;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is not null && Read(key, name) is { } game && !games.Exists(g => g.GameId == game.GameId))
                    {
                        games.Add(game);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    log?.Invoke($"Hoppet over GOG-spill {name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log?.Invoke($"Kunne ikke lese GOG-registeret: {ex.Message}");
        }

        return games;
    }

    /// <summary>Ett spill, lest på nytt (for å starte det med oppdaterte stier).</summary>
    public static GogGame? Find(string gameId) => Scan().FirstOrDefault(g => g.GameId == gameId);

    /// <summary>Som butikkliste for biblioteket: ID-en er GOGs spill-ID.</summary>
    public static IReadOnlyList<StoreEntry> AsStoreEntries(IReadOnlyList<GogGame> games) =>
        [.. games.Select(g => new StoreEntry(g.GameId, g.Title, g.InstallDirectory))];

    private static GogGame? Read(RegistryKey key, string keyName)
    {
        // Tillegg (DLC) peker på hovedspillet med dependsOn og har ingen egen programfil.
        if (key.GetValue("dependsOn") is { } dependsOn && !string.IsNullOrWhiteSpace(dependsOn.ToString()))
        {
            return null;
        }

        var gameId = key.GetValue("gameID")?.ToString() is { Length: > 0 } id ? id : keyName;
        if (key.GetValue("path") is not string directory || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var info = ReadInfo(directory, gameId);
        var title = key.GetValue("gameName") as string is { Length: > 0 } registryName ? registryName : info?.Name;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (info?.PrimaryGameTask is { } task)
        {
            var executable = GogInfoReader.Combine(directory, task.Path);
            var workingDirectory = task.WorkingDirectory is { } relative
                ? GogInfoReader.Combine(directory, relative)
                : Path.GetDirectoryName(executable) ?? directory;
            return File.Exists(executable)
                ? new GogGame(gameId, title, directory, executable, task.Arguments, workingDirectory)
                : null;
        }

        // Reserve for eldre installasjoner uten .info-fil.
        if (key.GetValue("exe") is string exe && File.Exists(exe))
        {
            return new GogGame(gameId, title, directory, exe, null, Path.GetDirectoryName(exe) ?? directory);
        }

        return null;
    }

    private static GogGameInfo? ReadInfo(string directory, string gameId)
    {
        var path = Path.Combine(directory, GogInfoReader.FileName(gameId));
        try
        {
            return File.Exists(path) ? GogInfoReader.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Starter et GOG-spill direkte fra programfilen i den primære spilleoppgaven (GOG-spill er uten DRM). GOG Galaxy
/// startes ikke.
/// </summary>
public sealed class GogRunner : IRunner
{
    public const string Id = "gog";

    public string RunnerId => Id;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var game = GogGames.Find(action.Target)
            ?? throw new LaunchException(LaunchFailure.FileMissing, "Spillet er ikke installert lenger");

        var arguments = string.Join(' ', new[] { game.Arguments, action.Arguments }.Where(a => !string.IsNullOrWhiteSpace(a)));
        var start = new ProcessStartInfo(game.Executable)
        {
            UseShellExecute = false,
            WorkingDirectory = game.WorkingDirectory,
            Arguments = arguments,
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new LaunchException(LaunchFailure.StartFailed, "Prosessen startet ikke");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }

        return Task.FromResult<ISession>(new LocalProcessSession(action, process, game.InstallDirectory));
    }
}
