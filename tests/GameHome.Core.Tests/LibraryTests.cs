// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Data;

namespace GameHome.Core.Tests;

public class LibraryTests
{
    [Theory]
    [InlineData(@"C:\Games\Broforce\Broforce.exe", "Broforce")]
    [InlineData(@"D:\Spill\Hollow_Knight\hollow_knight.exe", "hollow knight")]
    [InlineData(@"C:\Games\Some Game\Binaries\Win64\Game.exe", "Some Game")]
    [InlineData("/home/user/games/Celeste/Celeste.exe", "Celeste")]
    [InlineData(@"C:\Games\Cool Game\Launcher.exe", "Cool Game")]
    public void Title_is_suggested_from_the_path(string path, string expected) =>
        Assert.Equal(expected, GameLibrary.TitleFromPath(path));

    [Fact]
    public void Adding_an_executable_creates_a_game_with_one_direct_play_action()
    {
        var library = new GameLibrary();
        var (game, added) = library.AddExecutable(@"C:\Games\Broforce\Broforce.exe");

        Assert.True(added);
        var action = Assert.Single(library.PlayActionsFor(game.Id));
        Assert.Equal(PlayKind.Executable, action.Kind);
        Assert.Equal(GameLibrary.ExecutableRunnerId, action.RunnerId);
        Assert.Equal(@"C:\Games\Broforce", action.WorkingDirectory);
        Assert.Same(action, library.DefaultPlayAction(game.Id));
    }

    [Fact]
    public void Adding_the_same_file_twice_returns_the_existing_game()
    {
        var library = new GameLibrary();
        var (first, _) = library.AddExecutable(@"C:\Games\Broforce\Broforce.exe");
        var (second, added) = library.AddExecutable(@"c:\games\broforce\BROFORCE.EXE");

        Assert.False(added);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(library.Games);
    }

    [Fact]
    public void Removing_a_game_removes_its_play_actions()
    {
        var library = new GameLibrary();
        var (game, _) = library.AddExecutable(@"C:\Games\Broforce\Broforce.exe");

        Assert.True(library.Remove(game.Id));
        Assert.Empty(library.Games);
        Assert.Empty(library.PlayActions);
    }

    [Fact]
    public void Hidden_games_are_not_visible_and_titles_are_sorted()
    {
        var library = new GameLibrary();
        library.AddExecutable(@"C:\Games\Zeta\Zeta.exe");
        library.AddExecutable(@"C:\Games\Alpha\Alpha.exe");

        Assert.Equal(["Alpha", "Zeta"], library.VisibleGames.Select(GameLibrary.DisplayTitle));
    }

    [Fact]
    public void Visible_games_require_a_nonmissing_action_and_respect_hidden_overrides()
    {
        var games = new[]
        {
            new Game("visible", "Visible", [], ArtSet.Empty, UserOverrides.None),
            new Game("missing", "Missing", [], ArtSet.Empty, UserOverrides.None),
            new Game("hidden", "Hidden", [], ArtSet.Empty, UserOverrides.None with { Hidden = true }),
        };
        var actions = new[]
        {
            new PlayAction("a1", "visible", PlayKind.Store, "runner", "1", null, null, LaunchMode.Direct, null, Availability.Unknown),
            new PlayAction("a2", "missing", PlayKind.Store, "runner", "2", null, null, LaunchMode.Direct, null, Availability.Missing),
            new PlayAction("a3", "hidden", PlayKind.Store, "runner", "3", null, null, LaunchMode.Direct, null, Availability.Installed),
        };

        var library = new GameLibrary(games, actions);

        Assert.Equal(["Visible"], library.VisibleGames.Select(GameLibrary.DisplayTitle));
    }

    [Theory]
    [InlineData("  The   Game  ", "THE GAME")]
    [InlineData("A ™  B®", "A B")]
    [InlineData("A\tB  C", "A\tB C")]
    public void Title_key_removes_marks_and_collapses_only_ascii_spaces(string title, string expected) =>
        Assert.Equal(expected, GameLibrary.TitleKey(title));

    [Fact]
    public void Title_key_uses_invariant_uppercase_for_unmodified_unicode_titles() =>
        Assert.Equal("Straße Ω".ToUpperInvariant(), GameLibrary.TitleKey("Straße Ω"));

    [Fact]
    public void Sync_updates_only_the_first_persisted_action_for_a_duplicate_target()
    {
        var firstGame = new Game("first", "First", [], ArtSet.Empty, UserOverrides.None);
        var secondGame = new Game("second", "Second", [], ArtSet.Empty, UserOverrides.None);
        var firstAction = new PlayAction("a1", firstGame.Id, PlayKind.Store, "runner", "same", null, "old-first", LaunchMode.Direct, null, Availability.Missing);
        var duplicateAction = new PlayAction("a2", secondGame.Id, PlayKind.Store, "runner", "same", null, "old-second", LaunchMode.Direct, null, Availability.Missing);
        var library = new GameLibrary([firstGame, secondGame], [firstAction, duplicateAction]);

        var result = library.SyncStore("runner", [new StoreEntry("same", "Title", "new")]);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.MarkedMissing);
        Assert.Equal(Availability.Installed, library.PlayActions[0].Availability);
        Assert.Equal("new", library.PlayActions[0].WorkingDirectory);
        Assert.Equal(Availability.Missing, library.PlayActions[1].Availability);
        Assert.Equal("old-second", library.PlayActions[1].WorkingDirectory);
    }

    [Fact]
    public void Sync_reuses_a_target_added_during_the_scan_and_keeps_case_distinctions()
    {
        var library = new GameLibrary();

        var result = library.SyncStore("runner",
        [
            new StoreEntry("game", "First title", "old"),
            new StoreEntry("game", "Duplicate title", "new"),
            new StoreEntry("GAME", "Different target", null),
        ]);

        Assert.Equal(["First title", "Different target"], result.Added.Select(g => g.Title));
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.MarkedMissing);
        Assert.Equal("new", library.PlayActions[0].WorkingDirectory);
        Assert.Equal("GAME", library.PlayActions[1].Target);
    }

    [Fact]
    public void Library_survives_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gamehome-test-{Guid.NewGuid():N}", "library.json");
        try
        {
            var library = new GameLibrary();
            var (game, _) = library.AddExecutable(@"C:\Games\Broforce\Broforce.exe");
            LibraryStore.Save(library, path);

            var loaded = LibraryStore.Load(path);

            var loadedGame = Assert.Single(loaded.Games);
            Assert.Equal(game.Id, loadedGame.Id);
            Assert.Equal("Broforce", loadedGame.Title);
            Assert.Equal(@"C:\Games\Broforce\Broforce.exe", Assert.Single(loaded.PlayActions).Target);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Missing_library_file_gives_an_empty_library() =>
        Assert.Empty(LibraryStore.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")).Games);
}
