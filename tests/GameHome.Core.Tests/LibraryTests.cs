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
