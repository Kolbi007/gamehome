// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Runners;

namespace GameHome.Core.Tests;

public class ExecutableLocationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "games");
    private static readonly string Game = Path.Combine(Root, "Game");

    [Fact]
    public void Matches_nested_and_normalized_game_executable_paths()
    {
        Assert.True(ExecutableLocation.IsInDirectory(Game, Path.Combine(Game, "Game.exe")));
        Assert.True(ExecutableLocation.IsInDirectory(Game + Path.DirectorySeparatorChar, Path.Combine(Game, "bin", ".", "Game.exe")));
        Assert.True(ExecutableLocation.IsInDirectory(Game, Path.Combine(Game, "..support", "Game.exe")));
    }

    [Fact]
    public void Excludes_sibling_folders_shared_prefix_and_parent_paths()
    {
        Assert.False(ExecutableLocation.IsInDirectory(Game, Path.Combine(Root, "GameBackup", "Other.exe")));
        Assert.False(ExecutableLocation.IsInDirectory(Game, Path.Combine(Game, "bin", "..", "..", "Other", "Other.exe")));
        Assert.False(ExecutableLocation.IsInDirectory(Game, Path.Combine(Root, "Other.exe")));
        Assert.False(ExecutableLocation.IsInDirectory(Game, "Game.exe"));
        Assert.False(ExecutableLocation.IsInDirectory(Game, Game));
    }
}
