// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;

namespace GameHome.Core.Library;

/// <summary>
/// Biblioteket i minnet: spill og spillemåter. Lagring ligger i GameHome.Data; denne klassen har bare reglene.
/// </summary>
public sealed class GameLibrary
{
    public const string ExecutableRunnerId = "exe";
    public const string SteamRunnerId = "steam";

    // Filnavn som sier lite om spillet; da brukes mappenavnet i stedet.
    private static readonly HashSet<string> GenericExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "launcher", "start", "play", "run", "main", "app", "client",
        "win64-shipping", "win32-shipping", "win64", "win32", "x64", "x86", "bin", "binaries",
    };

    private readonly List<Game> _games = [];
    private readonly List<PlayAction> _actions = [];

    public GameLibrary()
    {
    }

    public GameLibrary(IEnumerable<Game> games, IEnumerable<PlayAction> actions)
    {
        _games.AddRange(games);
        _actions.AddRange(actions);
    }

    public IReadOnlyList<Game> Games => _games;

    public IReadOnlyList<PlayAction> PlayActions => _actions;

    /// <summary>Synlige spill sortert etter tittel. Spill der ingen spillemåte er installert, vises ikke.</summary>
    public IReadOnlyList<Game> VisibleGames =>
        [.. _games
            .Where(g => !g.Overrides.Hidden && _actions.Exists(a => a.GameId == g.Id && a.Availability != Availability.Missing))
            .OrderBy(DisplayTitle, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// Oppdaterer biblioteket med det en butikk sier er installert. Nye spill legges til med butikkens ID; spill som
    /// ikke lenger er installert, merkes <see cref="Availability.Missing"/> og beholdes med brukerens rettelser.
    /// </summary>
    public StoreSyncResult SyncStore(string runnerId, IReadOnlyCollection<StoreEntry> installed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);
        ArgumentNullException.ThrowIfNull(installed);

        var added = new List<Game>();
        var updatedCount = 0;
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in installed)
        {
            present.Add(entry.Id);
            var index = _actions.FindIndex(a => a.RunnerId == runnerId && a.Target == entry.Id);
            if (index >= 0)
            {
                var updated = _actions[index] with
                {
                    Availability = Availability.Installed,
                    WorkingDirectory = entry.InstallDirectory,
                };
                if (updated != _actions[index])
                {
                    _actions[index] = updated;
                    updatedCount++;
                }

                continue;
            }

            var game = new Game(NewId(), entry.Title, [new ExternalIdentity(runnerId, entry.Id)], ArtSet.Empty, UserOverrides.None);
            _games.Add(game);
            _actions.Add(new PlayAction(
                NewId(), game.Id, PlayKind.Store, runnerId, entry.Id,
                Arguments: null, WorkingDirectory: entry.InstallDirectory, LaunchMode.ClientOnDemand,
                ProfileId: null, Availability.Installed));
            added.Add(game);
        }

        var missing = 0;
        for (var i = 0; i < _actions.Count; i++)
        {
            if (_actions[i].RunnerId == runnerId && !present.Contains(_actions[i].Target)
                && _actions[i].Availability != Availability.Missing)
            {
                _actions[i] = _actions[i] with { Availability = Availability.Missing };
                missing++;
            }
        }

        return new StoreSyncResult(added, updatedCount, missing);
    }

    public static string DisplayTitle(Game game) => game.Overrides.Title ?? game.Title;

    public Game? Find(string gameId) => _games.FirstOrDefault(g => g.Id == gameId);

    public IReadOnlyList<PlayAction> PlayActionsFor(string gameId) => [.. _actions.Where(a => a.GameId == gameId)];

    /// <summary>Standard spillemåte: brukerens valg, ellers den første.</summary>
    public PlayAction? DefaultPlayAction(string gameId)
    {
        var actions = PlayActionsFor(gameId);
        var preferred = Find(gameId)?.Overrides.DefaultPlayActionId;
        return actions.FirstOrDefault(a => a.Id == preferred) ?? (actions.Count > 0 ? actions[0] : null);
    }

    /// <summary>
    /// Legger til et spill manuelt fra en programfil. Finnes samme fil allerede, returneres det eksisterende spillet.
    /// </summary>
    public (Game Game, bool Added) AddExecutable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        var existing = _actions.FirstOrDefault(a =>
            a.RunnerId == ExecutableRunnerId && string.Equals(a.Target, exePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && Find(existing.GameId) is { } known)
        {
            return (known, false);
        }

        var game = new Game(NewId(), TitleFromPath(exePath), [], ArtSet.Empty, UserOverrides.None);
        var action = new PlayAction(
            NewId(), game.Id, PlayKind.Executable, ExecutableRunnerId, exePath,
            Arguments: null, WorkingDirectory: DirectoryOf(exePath), LaunchMode.Direct,
            ProfileId: null, Availability.Installed);

        _games.Add(game);
        _actions.Add(action);
        return (game, true);
    }

    /// <summary>Fjerner spillet fra GameHome. Selve spillet på disken røres ikke.</summary>
    public bool Remove(string gameId)
    {
        var removed = _games.RemoveAll(g => g.Id == gameId) > 0;
        _actions.RemoveAll(a => a.GameId == gameId);
        return removed;
    }

    /// <summary>Foreslår en tittel fra stien. Virker for både Windows- og Unix-stier, så det kan testes overalt.</summary>
    public static string TitleFromPath(string exePath)
    {
        var parts = exePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return exePath;
        }

        var file = parts[^1];
        var dot = file.LastIndexOf('.');
        var name = dot > 0 ? file[..dot] : file;

        // Gå oppover til en mappe som ikke er generisk (for eksempel Spill\Binaries\Win64\Game.exe).
        var index = parts.Length - 2;
        while (GenericExeNames.Contains(name) && index >= 0)
        {
            name = parts[index--];
        }

        return name.Replace('_', ' ').Trim();
    }

    private static string? DirectoryOf(string path)
    {
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? path[..cut] : null;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>Et installert spill slik en butikk ser det.</summary>
/// <param name="Id">Butikkens ID, for eksempel Steam-appid.</param>
public sealed record StoreEntry(string Id, string Title, string? InstallDirectory);

public sealed record StoreSyncResult(IReadOnlyList<Game> Added, int Updated, int MarkedMissing)
{
    public bool Changed => Added.Count > 0 || Updated > 0 || MarkedMissing > 0;
}
