// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Data;
using GameHome.Metadata;

namespace GameHome.Core.Tests;

public class GameDetailsTests
{
    private const string Sample = """
        {"274190":{"success":true,"data":{
          "short_description":"Når ondskapen truer, svarer &quot;Bros&quot; med <b>eksplosjoner</b>.",
          "developers":["Free Lives"],"publishers":["Devolver Digital"],
          "release_date":{"coming_soon":false,"date":"15. okt. 2015"},
          "genres":[{"id":"1","description":"Action"},{"id":"23","description":"Indie"}],
          "metacritic":{"score":83,"url":"https://example.invalid"}}}}
        """;

    [Fact]
    public void Steam_appdetails_become_plain_details()
    {
        var details = SteamStoreClient.ParseDetails(Sample, 274190, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(details);
        Assert.Equal("Når ondskapen truer, svarer \"Bros\" med eksplosjoner .", details.Description);
        Assert.Equal("Free Lives", details.Developer);
        Assert.Equal("Devolver Digital", details.Publisher);
        Assert.Equal("15. okt. 2015", details.Released);
        Assert.Equal(["Action", "Indie"], details.Genres);
        Assert.Equal(83, details.Metacritic);
        Assert.Equal("Steam Store", details.Source);
    }

    [Fact]
    public void Missing_or_failed_appdetails_give_null()
    {
        Assert.Null(SteamStoreClient.ParseDetails("""{"1":{"success":false}}""", 1, DateTime.UtcNow));
        Assert.Null(SteamStoreClient.ParseDetails("""{"2":{"success":true,"data":{}}}""", 1, DateTime.UtcNow));
        Assert.Null(SteamStoreClient.ParseDetails("ikke json", 1, DateTime.UtcNow));
    }

    [Fact]
    public void Details_survive_a_round_trip_on_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gamehome-details-{Guid.NewGuid():N}.json");
        try
        {
            var details = new Dictionary<string, GameDetails>(StringComparer.Ordinal)
            {
                ["a"] = new("Tekst", "Dev", null, "2015", ["Action"], 80, "Steam Store", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)),
            };
            GameDetailsStore.Save(path, details);

            var loaded = GameDetailsStore.Load(path);
            Assert.Equal("Tekst", loaded["a"].Description);
            Assert.Equal(80, loaded["a"].Metacritic);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Play_time_adds_up_and_ignores_sessions_under_a_minute()
    {
        var ended = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        var first = PlayStatsStore.Add(null, ended, 30);
        var second = PlayStatsStore.Add(first, ended.AddHours(1), 15);
        var failed = PlayStatsStore.Add(second, ended.AddHours(2), 0.2);

        Assert.Equal(30, first.Minutes);
        Assert.Equal(45, second.Minutes);
        Assert.Equal(ended.AddHours(1), second.LastPlayedUtc);
        Assert.Same(second, failed);
    }
}
