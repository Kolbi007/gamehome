// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Sources.Moonlight;

namespace GameHome.Core.Tests;

public class StreamTests
{
    private const string Host = "F6E4625F-393F-8105-0F0D-46B64BFA3DF6";

    private const string Csv = """
        Name, ID, HDR Support, App Collection Game, Hidden, Direct Launch, Boxart URL
        "Desktop",881448767,false,false,false,false,"qrc:/res/no_app_image.png"
        "Steam Big Picture",1093255277,false,false,false,false,"qrc:/res/no_app_image.png"
        "007 First Light",123456,true,false,false,false,"file:///C:/cache/1.png"
        "The "Quoted" Game",77,false,false,false,false,"qrc:/res/no_app_image.png"
        "Secret",5,false,false,true,false,"qrc:/res/no_app_image.png"
        """;

    [Fact]
    public void Csv_rows_are_parsed_including_unescaped_quotes()
    {
        var apps = MoonlightAppList.ParseCsv(Csv.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(5, apps.Count);
        Assert.Equal(new HostApp("007 First Light", 123456, false), apps[2]);
        Assert.Equal("The \"Quoted\" Game", apps[3].Name);
        Assert.True(apps[4].Hidden);
    }

    [Fact]
    public void Only_games_are_offered_not_host_tools_or_hidden_apps()
    {
        var games = MoonlightAppList.ParseCsv(Csv).Where(a => a.IsGame).Select(a => a.Name);

        Assert.Equal(["007 First Light", "The \"Quoted\" Game"], games);
    }

    [Fact]
    public void Adding_a_host_game_creates_one_stream_tile()
    {
        var library = new GameLibrary();

        var (game, newTile) = library.AddStream(Host, "007 First Light");

        Assert.True(newTile);
        var action = Assert.Single(library.PlayActionsFor(game.Id));
        Assert.Equal(PlayKind.Stream, action.Kind);
        Assert.Equal(Host, action.HostId);
        Assert.True(library.HasStream(Host, "007 first light"));
        Assert.Single(library.VisibleGames);
    }

    [Fact]
    public void Same_title_as_a_local_game_becomes_a_second_way_to_play()
    {
        var library = new GameLibrary();
        var (broforce, _) = library.AddExecutable(@"C:\Games\Broforce\Broforce.exe");

        var (game, newTile) = library.AddStream(Host, "Broforce™");

        Assert.False(newTile);
        Assert.Equal(broforce.Id, game.Id);
        var actions = library.AvailableActionsFor(game.Id);
        Assert.Equal(2, actions.Count);
        Assert.False(actions[0].IsRemote);
        Assert.True(actions[1].IsRemote);
    }

    [Fact]
    public void Adding_the_same_host_game_twice_does_nothing()
    {
        var library = new GameLibrary();
        library.AddStream(Host, "007 First Light");

        var (_, newTile) = library.AddStream(Host, "007 First Light");

        Assert.False(newTile);
        Assert.Single(library.PlayActions);
    }
}
