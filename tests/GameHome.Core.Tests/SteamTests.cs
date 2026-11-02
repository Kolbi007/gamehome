// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Sources.Steam;

namespace GameHome.Core.Tests;

public class SteamTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"apps"
        		{
        			"2062430"		"508358799"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        }
        """;

    private const string Manifest = """
        "AppState"
        {
        	"appid"		"2062430"
        	"name"		"BALL x PIT"
        	"StateFlags"		"4"
        	"installdir"		"BALLxPIT"
        	// kommentar
        	"InstalledDepots"
        	{
        		"2062431" { "manifest" "123" }
        	}
        }
        """;

    [Fact]
    public void KeyValues_reads_nested_groups_escapes_and_comments()
    {
        var root = KeyValues.Parse(Manifest)["AppState"];

        Assert.NotNull(root);
        Assert.Equal("BALL x PIT", root.GetString("name"));
        Assert.Equal("123", root["InstalledDepots"]?["2062431"]?.GetString("manifest"));
        Assert.Equal(@"C:\Program Files (x86)\Steam", KeyValues.Parse(LibraryFolders)["libraryfolders"]?["0"]?.GetString("path"));
    }

    [Fact]
    public void KeyValues_rejects_unbalanced_braces() =>
        Assert.Throws<FormatException>(() => KeyValues.Parse("\"AppState\" { \"appid\" \"1\""));

    [Fact]
    public void Library_folders_include_steam_root_once_and_extra_libraries()
    {
        var folders = SteamLibraryReader.LibraryFolders(@"C:\Program Files (x86)\Steam", LibraryFolders);

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], folders);
    }

    [Fact]
    public void App_manifest_gives_store_entry()
    {
        var entry = SteamLibraryReader.ParseAppManifest(Manifest, "/steam");

        Assert.NotNull(entry);
        Assert.Equal("2062430", entry.Id);
        Assert.Equal("BALL x PIT", entry.Title);
        Assert.Equal(Path.Combine("/steam", "steamapps", "common", "BALLxPIT"), entry.InstallDirectory);
    }

    [Theory]
    [InlineData("\"AppState\" { \"appid\" \"10\" \"name\" \"Half\" \"StateFlags\" \"1026\" }")] // oppdateres, ikke ferdig
    [InlineData("\"AppState\" { \"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"StateFlags\" \"4\" }")]
    [InlineData("ikke en manifest {")]
    public void Unfinished_tools_and_broken_manifests_are_skipped(string acf) =>
        Assert.Null(SteamLibraryReader.ParseAppManifest(acf, "/steam"));

    [Fact]
    public void Scan_reads_manifests_from_disk()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gamehome-steam-{Guid.NewGuid():N}");
        try
        {
            var steamapps = Directory.CreateDirectory(Path.Combine(root, "steamapps")).FullName;
            File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"), "\"libraryfolders\" { }");
            File.WriteAllText(Path.Combine(steamapps, "appmanifest_2062430.acf"), Manifest);

            var entry = Assert.Single(SteamLibraryReader.Scan(root));
            Assert.Equal("BALL x PIT", entry.Title);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Sync_adds_new_games_and_keeps_existing_ones()
    {
        var library = new GameLibrary();
        var ball = new StoreEntry("2062430", "BALL x PIT", @"C:\Steam\steamapps\common\BALLxPIT");

        var first = library.SyncStore(GameLibrary.SteamRunnerId, [ball]);
        var second = library.SyncStore(GameLibrary.SteamRunnerId, [ball]);

        var game = Assert.Single(first.Added);
        Assert.Equal(new ExternalIdentity("steam", "2062430"), Assert.Single(game.Identities));
        var action = Assert.Single(library.PlayActionsFor(game.Id));
        Assert.Equal(PlayKind.Store, action.Kind);
        Assert.Equal(LaunchMode.ClientOnDemand, action.LaunchMode);
        Assert.False(second.Changed);
        Assert.Single(library.Games);
    }

    [Fact]
    public void Uninstalled_games_are_hidden_but_kept_and_come_back()
    {
        var library = new GameLibrary();
        var ball = new StoreEntry("2062430", "BALL x PIT", null);
        library.SyncStore(GameLibrary.SteamRunnerId, [ball]);

        var removed = library.SyncStore(GameLibrary.SteamRunnerId, []);
        Assert.Equal(1, removed.MarkedMissing);
        Assert.Empty(library.VisibleGames);
        Assert.Single(library.Games);

        var back = library.SyncStore(GameLibrary.SteamRunnerId, [ball]);
        Assert.True(back.Changed);
        Assert.Single(library.VisibleGames);
    }

    [Fact]
    public void Sync_for_steam_does_not_touch_manual_games()
    {
        var library = new GameLibrary();
        library.AddExecutable(@"C:\Games\Broforce\Broforce.exe");

        library.SyncStore(GameLibrary.SteamRunnerId, []);

        Assert.Single(library.VisibleGames);
    }
}
