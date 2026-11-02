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
    public const string MoonlightRunnerId = "moonlight";

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

    /// <summary>Setter bildene for et spill (for eksempel omslag hentet fra hosten). Returnerer false hvis spillet mangler.</summary>
    public bool SetArt(string gameId, ArtSet art)
    {
        var index = _games.FindIndex(g => g.Id == gameId);
        if (index < 0)
        {
            return false;
        }

        _games[index] = _games[index] with { Art = art };
        return true;
    }

    /// <summary>Spillet som har en stream-spillemåte for appen på hosten, eller null.</summary>
    public Game? FindStreamGame(string hostId, string appName) =>
        _actions.Find(a => IsStream(a, hostId, appName)) is { } action ? Find(action.GameId) : null;

    /// <summary>Om en app på en host allerede er lagt til som spillemåte.</summary>
    public bool HasStream(string hostId, string appName) =>
        _actions.Exists(a => IsStream(a, hostId, appName));

    /// <summary>
    /// Legger en app på en host til i Home, valgt av brukeren. Finnes et spill med samme tittel, blir streamen en ekstra
    /// spillemåte på samme flis (ett spill, ikke to kopier). Ellers lages et nytt spill.
    /// </summary>
    public (Game Game, bool NewTile) AddStream(string hostId, string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        var existing = _actions.Find(a => IsStream(a, hostId, appName));
        if (existing is not null && Find(existing.GameId) is { } known)
        {
            return (known, false);
        }

        var key = TitleKey(appName);
        var sameTitle = _games.Find(g => !g.Overrides.Hidden && TitleKey(DisplayTitle(g)) == key);
        var game = sameTitle ?? new Game(NewId(), appName.Trim(), [], ArtSet.Empty, UserOverrides.None);
        if (sameTitle is null)
        {
            _games.Add(game);
        }

        _actions.Add(new PlayAction(
            NewId(), game.Id, PlayKind.Stream, MoonlightRunnerId, appName,
            Arguments: null, WorkingDirectory: null, LaunchMode.ClientRequired,
            ProfileId: null, Availability.Installed, HostId: hostId));
        return (game, sameTitle is null);
    }

    /// <summary>Spillemåtene som kan brukes nå, lokale først.</summary>
    public IReadOnlyList<PlayAction> AvailableActionsFor(string gameId) =>
        [.. _actions.Where(a => a.GameId == gameId && a.Availability != Availability.Missing).OrderBy(a => a.IsRemote)];

    /// <summary>Tittel for sammenligning: uten store bokstaver, varemerketegn og ekstra mellomrom.</summary>
    public static string TitleKey(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var cleaned = new string([.. title.Where(c => c is not ('™' or '®' or '©'))]);
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    private static bool IsStream(PlayAction action, string hostId, string appName) =>
        action.RunnerId == MoonlightRunnerId && action.HostId == hostId
        && string.Equals(action.Target, appName, StringComparison.OrdinalIgnoreCase);

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
