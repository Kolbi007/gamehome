// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Sources.Retro;

namespace GameHome.Core.Tests;

public sealed class RetroLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gh-retro-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Super Mario World (USA)", "Super Mario World")]
    [InlineData("Sonic & Knuckles + Sonic The Hedgehog 3 (USA) (Lock-On)", "Sonic & Knuckles + Sonic The Hedgehog 3")]
    [InlineData("Some_Game [!] (Europe) (Rev 1)", "Some Game")]
    [InlineData("(USA)", "(USA)")]
    public void TitleIsCleaned(string fileName, string expected) =>
        Assert.Equal(expected, RetroLibrary.CleanTitle(fileName));

    [Fact]
    public void ScanFindsGamesForAvailableSystemsOnly()
    {
        Touch("snes", "Super Mario World (USA).zip");
        Touch("snes", "notes.txt");
        Touch("megadrive", "Sonic (USA).md");
        Touch("gba", "Game (USA).gba");

        var games = RetroLibrary.Scan(_root, s => s.Folder is "snes" or "megadrive");

        Assert.Equal(["Super Mario World", "Sonic"], games.Select(g => g.Title).Order().Reverse());
        Assert.Equal("snes", RetroLibrary.SystemFor(games.Single(g => g.Title == "Super Mario World").Path)?.Folder);
    }

    [Fact]
    public void MissingRootGivesNoGames() =>
        Assert.Empty(RetroLibrary.Scan(Path.Combine(_root, "missing"), _ => true));

    private void Touch(string system, string file)
    {
        Directory.CreateDirectory(Path.Combine(_root, system));
        File.WriteAllBytes(Path.Combine(_root, system, file), [0]);
    }
}
