// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Metadata;
using GameHome.Metadata;

namespace GameHome.Core.Tests;

/// <summary>Parsere for SteamGridDB, IGDB og Steam Web API, med eksempelsvar formet etter den offentlige dokumentasjonen.</summary>
public class MetadataClientTests
{
    private const string SgdbSearch = """
        {"success":true,"data":[
          {"id":5248878,"name":"Broforce","types":["steam"],"verified":true,"release_date":1444867200},
          {"id":5328430,"name":"Broforce Forever","types":[],"verified":false},
          {"id":0,"name":""}]}
        """;

    private const string SgdbGrids = """
        {"success":true,"page":0,"total":4,"limit":50,"data":[
          {"id":101,"score":3,"style":"alternate","width":600,"height":900,"nsfw":false,"humor":false,
           "url":"https://cdn2.steamgriddb.com/grid/a.png","thumb":"https://cdn2.steamgriddb.com/thumb/a.jpg",
           "tags":[],"author":{"name":"artist","steam64":"76561197960287930","avatar":"https://example.invalid/a.jpg"}},
          {"id":102,"score":1,"style":["blurred"],"width":600,"height":900,"url":"https://cdn2.steamgriddb.com/grid/b.png",
           "thumb":"https://cdn2.steamgriddb.com/thumb/b.jpg","tags":["humor"]},
          {"id":103,"score":0,"style":"material","width":600,"height":900,"nsfw":true,
           "url":"https://cdn2.steamgriddb.com/grid/c.png","thumb":"https://cdn2.steamgriddb.com/thumb/c.jpg","tags":[]},
          {"id":104,"style":"alternate","thumb":"https://cdn2.steamgriddb.com/thumb/d.jpg"}]}
        """;

    [Fact]
    public void SteamGridDb_search_reads_games_and_skips_broken_entries()
    {
        var games = SteamGridDbClient.ParseGames(SgdbSearch);

        Assert.Equal(2, games.Count);
        Assert.Equal(new SteamGridDbGame(5248878, "Broforce", true, new DateTime(2015, 10, 15, 0, 0, 0, DateTimeKind.Utc)), games[0]);
        Assert.Null(games[1].ReleaseDateUtc);
        Assert.Empty(SteamGridDbClient.ParseGames("""{"success":false,"errors":["Game not found"]}"""));
        Assert.Empty(SteamGridDbClient.ParseGames("ikke json"));
    }

    [Fact]
    public void SteamGridDb_game_lookup_reads_a_single_game()
    {
        var game = SteamGridDbClient.ParseGame("""{"success":true,"data":{"id":5248878,"name":"Broforce","types":["steam"],"verified":true}}""");

        Assert.Equal(5248878, game?.Id);
        Assert.Null(SteamGridDbClient.ParseGame("""{"success":false,"errors":["Game not found"]}"""));
    }

    [Fact]
    public void SteamGridDb_images_skip_nsfw_and_humor_by_default()
    {
        var safe = SteamGridDbClient.ParseImages(SgdbGrids);
        var all = SteamGridDbClient.ParseImages(SgdbGrids, includeNsfwAndHumor: true);

        var only = Assert.Single(safe);
        Assert.Equal(new SteamGridDbImage(101, "https://cdn2.steamgriddb.com/grid/a.png", "https://cdn2.steamgriddb.com/thumb/a.jpg",
            600, 900, "alternate", false, false), only);
        Assert.Equal([101, 102, 103], all.Select(i => i.Id));
        Assert.True(all[1].Humor);
        Assert.Equal("blurred", all[1].Style);
        Assert.True(all[2].Nsfw);
    }

    [Fact]
    public void SteamGridDb_grid_shapes_use_documented_dimensions()
    {
        Assert.Equal("600x900", SteamGridDbClient.GridDimensions(SteamGridDbGridShape.Portrait));
        Assert.Equal("920x430,460x215", SteamGridDbClient.GridDimensions(SteamGridDbGridShape.Landscape));
    }

    private const string IgdbGames = """
        [{"id":1942,"name":"The Witcher 3: Wild Hunt",
          "summary":"RPG and sequel to The Witcher 2 (2011).",
          "genres":[{"id":12,"name":"Role-playing (RPG)"},{"id":31,"name":"Adventure"}],
          "first_release_date":1431993600,"total_rating":93.4,"total_rating_count":3200,
          "aggregated_rating":92.6,"aggregated_rating_count":50,
          "cover":{"id":89386,"image_id":"coaarl"},
          "artworks":[{"id":1,"image_id":"ar5l8"},{"id":2,"image_id":"ar5l9"}],
          "screenshots":[{"id":9742,"image_id":"mnljdjtrh44x4snmierh"},{"id":9743,"image_id":"em1y2ugcwy2myuhvb9db"}],
          "url":"https://www.igdb.com/games/the-witcher-3-wild-hunt"},
         {"id":7,"name":"Uten detaljer"},
         {"id":8}]
        """;

    [Fact]
    public void Igdb_games_are_read_with_expanded_fields()
    {
        var games = IgdbClient.ParseGames(IgdbGames);

        Assert.Equal(2, games.Count);
        var witcher = games[0];
        Assert.Equal(1942, witcher.Id);
        Assert.Equal(["Role-playing (RPG)", "Adventure"], witcher.Genres);
        Assert.Equal(new DateTime(2015, 5, 19, 0, 0, 0, DateTimeKind.Utc), witcher.FirstReleaseUtc);
        Assert.Equal(93.4, witcher.TotalRating);
        Assert.Equal("coaarl", witcher.CoverImageId);
        Assert.Equal(["ar5l8", "ar5l9"], witcher.ArtworkImageIds);
        Assert.Equal(["mnljdjtrh44x4snmierh", "em1y2ugcwy2myuhvb9db"], witcher.ScreenshotImageIds);

        var bare = games[1];
        Assert.Null(bare.Summary);
        Assert.Empty(bare.Genres);
        Assert.Null(bare.CoverImageId);
        Assert.Empty(IgdbClient.ParseGames("""{"message":"Authorization Failure"}"""));
    }

    [Fact]
    public void Igdb_game_maps_to_details_ratings_and_image_urls()
    {
        var witcher = IgdbClient.ParseGames(IgdbGames)[0];
        var fetched = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

        var details = IgdbClient.ToDetails(witcher, fetched);
        Assert.Equal("RPG and sequel to The Witcher 2 (2011).", details.Description);
        Assert.Equal("2015-05-19", details.Released);
        Assert.Equal(["Role-playing (RPG)", "Adventure"], details.Genres);
        Assert.Null(details.Metacritic);
        Assert.Equal("IGDB", details.Source);

        var rating = Assert.Single(IgdbClient.ToRatings(witcher));
        Assert.Equal(new Rating("igdb", RatingKind.Critic, 93, 50, "https://www.igdb.com/games/the-witcher-3-wild-hunt"), rating);

        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/coaarl.jpg", IgdbClient.ImageUrl("coaarl"));
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_screenshot_huge/mnljdjtrh44x4snmierh.jpg",
            IgdbClient.ImageUrl("mnljdjtrh44x4snmierh", "screenshot_huge"));
    }

    [Fact]
    public void Igdb_queries_are_apicalypse_and_cannot_be_broken_by_quotes()
    {
        var search = IgdbClient.SearchQuery("Tom \"Clancy's\" \\ Game", 5);

        Assert.StartsWith("search \"Tom Clancy's  Game\"; fields name,", search, StringComparison.Ordinal);
        Assert.EndsWith(" limit 5;", search, StringComparison.Ordinal);
        Assert.Equal("fields game,uid,external_game_source; where uid = \"620\" & external_game_source = 1; limit 1;",
            IgdbClient.SteamLookupQuery(620));
    }

    [Fact]
    public void Igdb_external_game_gives_the_igdb_game_id()
    {
        Assert.Equal(1942, IgdbClient.ParseExternalGameId("""[{"id":12345,"game":1942,"uid":"292030","external_game_source":1}]"""));
        Assert.Null(IgdbClient.ParseExternalGameId("[]"));
    }

    [Fact]
    public void Igdb_time_to_beat_is_read_in_seconds_and_maps_to_play_time()
    {
        var time = IgdbClient.ParseTimeToBeat("""[{"id":3,"game_id":1942,"hastily":180000,"normally":360000,"completely":0,"count":120}]""");

        Assert.Equal(new IgdbTimeToBeat(1942, 180000, 360000, null, 120), time);
        var play = IgdbClient.ToPlayTime(time!);
        Assert.Equal(TimeSpan.FromHours(50), play.MainStory);
        Assert.Equal(TimeSpan.FromHours(100), play.MainPlusExtras);
        Assert.Null(play.Completionist);
        Assert.Null(IgdbClient.ParseTimeToBeat("[]"));
    }

    [Fact]
    public void Igdb_token_response_gives_token_and_expiry()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        var token = IgdbClient.ParseToken("""{"access_token":"access12345token","expires_in":5587808,"token_type":"bearer"}""", now, out var expires);

        Assert.Equal("access12345token", token);
        Assert.Equal(now.AddSeconds(5587808), expires);
        Assert.Null(IgdbClient.ParseToken("""{"status":403,"message":"invalid client secret"}""", now, out _));
    }

    [Fact]
    public void Clients_never_show_their_secrets()
    {
        using var http = new HttpClient();
        using var igdb = new IgdbClient(http, "client-id-secret", "client-secret-value");

        Assert.DoesNotContain("secret", igdb.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key-value", new SteamWebApiClient(http, "key-value").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("key-value", new SteamGridDbClient(http, "key-value").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_owned_games_are_read_with_name_playtime_and_icon()
    {
        var games = SteamWebApiClient.ParseOwnedGames("""
            {"response":{"game_count":2,"games":[
              {"appid":274190,"name":"Broforce","playtime_forever":845,"img_icon_url":"0d9e3b5b0a0ab8e1b5e7b0d6a4a2c8f1e3d5b7a9",
               "has_community_visible_stats":true,"playtime_windows_forever":845},
              {"appid":440,"name":"Team Fortress 2","playtime_forever":0,"img_icon_url":""}]}}
            """);

        Assert.NotNull(games);
        Assert.Equal(2, games.Count);
        Assert.Equal(new SteamOwnedGame(274190, "Broforce", 845, "0d9e3b5b0a0ab8e1b5e7b0d6a4a2c8f1e3d5b7a9"), games[0]);
        Assert.Equal("https://media.steampowered.com/steamcommunity/public/images/apps/274190/0d9e3b5b0a0ab8e1b5e7b0d6a4a2c8f1e3d5b7a9.jpg",
            games[0].IconUrl);
        Assert.Null(games[1].IconUrl);
    }

    [Fact]
    public void Steam_private_profile_gives_an_empty_list_and_garbage_gives_null()
    {
        Assert.Empty(SteamWebApiClient.ParseOwnedGames("""{"response":{}}""")!);
        Assert.Null(SteamWebApiClient.ParseOwnedGames("<html>Forbidden</html>"));
    }

    [Fact]
    public void Steam_vanity_name_resolves_only_on_success()
    {
        Assert.Equal(76561197960287930UL, SteamWebApiClient.ParseVanity("""{"response":{"steamid":"76561197960287930","success":1}}"""));
        Assert.Null(SteamWebApiClient.ParseVanity("""{"response":{"success":42,"message":"No match"}}"""));
        Assert.Equal("gabelogannewell", SteamWebApiClient.VanityName("https://steamcommunity.com/id/gabelogannewell/"));
        Assert.Equal("gabelogannewell", SteamWebApiClient.VanityName(" gabelogannewell "));
        Assert.Null(SteamWebApiClient.VanityName("https://steamcommunity.com/profiles/76561197960287930/x"));
    }
}

/// <summary>Klientene mot en falsk HTTP-handler: ingen nettverk.</summary>
public class MetadataClientHttpTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task Igdb_token_is_fetched_once_and_sent_as_documented()
    {
        var handler = new FakeHandler((request, _) => request.RequestUri!.Host == "id.twitch.tv"
            ? Json("""{"access_token":"tok123","expires_in":5587808,"token_type":"bearer"}""")
            : Json("""[{"id":1942,"name":"The Witcher 3: Wild Hunt"}]"""));
        using var http = new HttpClient(handler);
        using var igdb = new IgdbClient(http, "cid", "csecret");

        Assert.Equal(1942, (await igdb.GetGameAsync(1942, TestContext.Current.CancellationToken))?.Id);
        Assert.Single(await igdb.SearchAsync("witcher", 5, TestContext.Current.CancellationToken));

        var token = Assert.Single(handler.Requests, r => r.Request.RequestUri!.Host == "id.twitch.tv");
        Assert.Equal(HttpMethod.Post, token.Request.Method);
        Assert.Equal("", token.Request.RequestUri!.Query);
        Assert.Equal("client_id=cid&client_secret=csecret&grant_type=client_credentials", token.Body);

        var api = handler.Requests.Where(r => r.Request.RequestUri!.Host == "api.igdb.com").ToList();
        Assert.Equal(2, api.Count);
        Assert.All(api, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Request.Method);
            Assert.Equal("cid", r.Request.Headers.GetValues("Client-ID").Single());
            Assert.Equal("Bearer tok123", r.Request.Headers.Authorization?.ToString());
        });
        Assert.Equal("https://api.igdb.com/v4/games", api[0].Request.RequestUri!.ToString());
        Assert.Contains("where id = 1942;", api[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failures_give_null_or_empty_and_never_leak_the_key()
    {
        var handler = new FakeHandler((request, _) => request.RequestUri!.AbsolutePath.Contains("ResolveVanityURL", StringComparison.Ordinal)
            ? throw new HttpRequestException("Feil for " + request.RequestUri)
            : new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        var steam = new SteamWebApiClient(http, "steamkey42");
        var sgdb = new SteamGridDbClient(http, "sgdbkey42");
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await steam.GetOwnedGamesAsync(76561197960287930UL, ct));
        Assert.Null(await steam.ResolveVanityUrlAsync("someone", ct));
        Assert.Empty(await sgdb.SearchAsync("Broforce", ct));
        Assert.Null(await sgdb.GetGameBySteamAppIdAsync(274190, ct));
        Assert.Empty(await sgdb.GetGridsAsync(5248878, SteamGridDbGridShape.Portrait, ct: ct));

        var owned = handler.Requests[0].Request.RequestUri!;
        Assert.Equal("api.steampowered.com", owned.Host);
        Assert.Equal("/IPlayerService/GetOwnedGames/v1/", owned.AbsolutePath);
        Assert.Contains("include_appinfo=1", owned.Query, StringComparison.Ordinal);
        Assert.Contains("include_played_free_games=1", owned.Query, StringComparison.Ordinal);
        Assert.Contains("steamid=76561197960287930", owned.Query, StringComparison.Ordinal);

        var grids = handler.Requests[^1].Request;
        Assert.Equal("Bearer sgdbkey42", grids.Headers.Authorization?.ToString());
        Assert.Equal("/api/v2/grids/game/5248878", grids.RequestUri!.AbsolutePath);
        Assert.Equal("?nsfw=false&humor=false&dimensions=600x900", grids.RequestUri.Query);
        Assert.DoesNotContain("sgdbkey42", grids.RequestUri.ToString(), StringComparison.Ordinal);
    }
}
