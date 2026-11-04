// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Sources.Epic;
using GameHome.Sources.Gog;

namespace GameHome.Core.Tests;

public class StoreManifestTests
{
    // Typisk goggame-*.info: en primær spilleoppgave, en ekstra oppgave, en lenke og et verktøy.
    private const string GogInfo = """
        {
          "buildId": "56489219104384627",
          "clientId": "53051446606509405",
          "gameId": "1207658924",
          "language": "English",
          "languages": ["en-US"],
          "name": "Unreal Tournament 2004 Editor's Choice Edition",
          "playTasks": [
            {
              "category": "game",
              "isPrimary": true,
              "languages": ["en-US"],
              "name": "Unreal Tournament 2004 Editor's Choice Edition",
              "path": "System\\UT2004.exe",
              "type": "FileTask",
              "workingDir": "System"
            },
            {
              "category": "game",
              "name": "Dedicated server",
              "arguments": "-server",
              "path": "System/UCC.exe",
              "type": "FileTask"
            },
            {
              "category": "document",
              "link": "https://www.gog.com/support/unreal_tournament_2004_ece",
              "name": "Support",
              "type": "URLTask"
            },
            {
              "category": "tool",
              "isHidden": true,
              "name": "Settings",
              "path": "Tools\\Settings.exe",
              "type": "FileTask"
            }
          ],
          "rootGameId": "1207658924",
          "version": 1
        }
        """;

    [Fact]
    public void Gog_info_picks_the_primary_game_task()
    {
        var info = GogInfoReader.Parse(GogInfo);

        Assert.NotNull(info);
        Assert.Equal("1207658924", info.GameId);
        Assert.Equal("Unreal Tournament 2004 Editor's Choice Edition", info.Name);
        Assert.Equal(3, info.PlayTasks.Count); // Lenken er ikke med.
        var primary = info.PrimaryGameTask;
        Assert.NotNull(primary);
        Assert.True(primary.IsPrimary);
        Assert.Equal(@"System\UT2004.exe", primary.Path);
        Assert.Equal("System", primary.WorkingDirectory);
        Assert.Null(primary.Arguments);
        Assert.Equal("-server", info.PlayTasks[1].Arguments);
    }

    [Fact]
    public void Gog_info_without_a_primary_task_uses_the_first_visible_game_task()
    {
        const string json = """
            {
              "gameId": "1",
              "name": "X",
              "playTasks": [
                { "category": "launcher", "isPrimary": false, "path": "Launcher.exe", "type": "FileTask" },
                { "category": "game", "isHidden": true, "path": "Hidden.exe", "type": "FileTask" },
                { "category": "game", "path": "bin/Game.exe", "arguments": "-windowed", "type": "FileTask" }
              ]
            }
            """;

        var task = GogInfoReader.Parse(json)?.PrimaryGameTask;

        Assert.NotNull(task);
        Assert.Equal("bin/Game.exe", task.Path);
        Assert.Equal("-windowed", task.Arguments);
    }

    [Fact]
    public void Gog_info_with_only_links_or_tools_has_no_game_task()
    {
        const string json = """
            {
              "gameId": "2",
              "playTasks": [
                { "category": "document", "link": "https://example.org", "type": "URLTask", "isPrimary": true },
                { "category": "tool", "path": "Tool.exe", "type": "FileTask" }
              ]
            }
            """;

        Assert.Null(GogInfoReader.Parse(json)?.PrimaryGameTask);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ ikke json")]
    [InlineData("[]")]
    [InlineData("""{ "gameId": "3" }""")]
    public void Gog_info_that_is_broken_or_lacks_play_tasks_is_ignored(string json) =>
        Assert.Null(GogInfoReader.Parse(json));

    [Theory]
    [InlineData(@"C:\GOG Games\UT2004", @"System\UT2004.exe", @"C:\GOG Games\UT2004\System\UT2004.exe")]
    [InlineData(@"C:\GOG Games\X\", "bin/Game.exe", @"C:\GOG Games\X\bin\Game.exe")]
    [InlineData(@"C:\GOG Games\X", @".\Game.exe", @"C:\GOG Games\X\Game.exe")]
    [InlineData(@"C:\GOG Games\X", @"D:\Other\Game.exe", @"D:\Other\Game.exe")]
    public void Gog_paths_are_relative_to_the_install_directory(string directory, string relative, string expected) =>
        Assert.Equal(expected, GogInfoReader.Combine(directory, relative));

    private const string EpicItem = """
        {
          "FormatVersion": 0,
          "bIsIncompleteInstall": false,
          "LaunchCommand": "",
          "LaunchExecutable": "retail/gotg.exe",
          "ManifestLocation": "C:\\Games\\MarvelGOTG/.egstore",
          "bIsApplication": true,
          "bIsExecutable": true,
          "AppCategories": ["public", "games", "applications"],
          "DisplayName": "Marvel's Guardians of the Galaxy",
          "InstallLocation": "C:\\Games\\MarvelGOTG",
          "CatalogNamespace": "2a09fb19b47f46dfb11ebd382f132a8f",
          "CatalogItemId": "88f4bb0bb06e4962a2042d5e20fb6ace",
          "AppName": "63a665088eb1480298f1e57943b225d8",
          "MainGameCatalogNamespace": "2a09fb19b47f46dfb11ebd382f132a8f",
          "MainGameCatalogItemId": "88f4bb0bb06e4962a2042d5e20fb6ace",
          "MainGameAppName": "63a665088eb1480298f1e57943b225d8"
        }
        """;

    [Fact]
    public void Epic_item_for_an_installed_game_is_read()
    {
        var game = EpicManifestReader.Parse(EpicItem);

        Assert.NotNull(game);
        Assert.Equal("63a665088eb1480298f1e57943b225d8", game.AppName);
        Assert.Equal("Marvel's Guardians of the Galaxy", game.DisplayName);
        Assert.Equal(@"C:\Games\MarvelGOTG\retail\gotg.exe", game.ExecutablePath);
        Assert.Equal(
            "com.epicgames.launcher://apps/2a09fb19b47f46dfb11ebd382f132a8f%3A88f4bb0bb06e4962a2042d5e20fb6ace"
            + "%3A63a665088eb1480298f1e57943b225d8?action=launch&silent=true",
            game.LaunchUri);
    }

    [Fact]
    public void Epic_item_for_an_incomplete_install_is_skipped() =>
        Assert.Null(EpicManifestReader.Parse(EpicItem.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true", StringComparison.Ordinal)));

    [Fact]
    public void Epic_item_that_is_not_a_game_is_skipped() =>
        Assert.Null(EpicManifestReader.Parse(EpicItem.Replace("\"public\", \"games\", \"applications\"", "\"public\", \"applications\"", StringComparison.Ordinal)));

    [Fact]
    public void Epic_item_for_an_addon_is_skipped() =>
        Assert.Null(EpicManifestReader.Parse(EpicItem.Replace(
            "\"MainGameAppName\": \"63a665088eb1480298f1e57943b225d8\"", "\"MainGameAppName\": \"someothergame\"", StringComparison.Ordinal)));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "AppName": "x", "AppCategories": ["games"] }""")]
    public void Epic_item_that_is_broken_or_incomplete_is_skipped(string json) =>
        Assert.Null(EpicManifestReader.Parse(json));

    [Fact]
    public void Epic_manifests_folder_skips_bad_files_and_duplicates()
    {
        var directory = Directory.CreateTempSubdirectory("gamehome-epic-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "A.item"), EpicItem);
            File.WriteAllText(Path.Combine(directory.FullName, "B.item"), "{ ødelagt");
            File.WriteAllText(Path.Combine(directory.FullName, "C.item"), EpicItem);
            File.WriteAllText(Path.Combine(directory.FullName, "D.item"),
                EpicItem.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(directory.FullName, "ignored.txt"), EpicItem);

            var games = EpicManifestReader.ReadAll(directory.FullName);

            Assert.Single(games);
            Assert.Empty(EpicManifestReader.ReadAll(Path.Combine(directory.FullName, "finnes-ikke")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
