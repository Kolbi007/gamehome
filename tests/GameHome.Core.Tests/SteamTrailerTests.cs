// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Metadata;

namespace GameHome.Core.Tests;

public class SteamTrailerTests
{
    [Fact]
    public void Mp4480IsPreferred() =>
        Assert.Equal("https://video/480.mp4", SteamStoreClient.ParseTrailer("""
            {"10":{"success":true,"data":{"movies":[{"mp4":{"480":"https://video/480.mp4","max":"https://video/max.mp4"}}]}}}
            """, 10));

    [Fact]
    public void HlsIsUsedWhenNoMp4() =>
        Assert.Equal("https://video/x.m3u8", SteamStoreClient.ParseTrailer("""
            {"10":{"success":true,"data":{"movies":[{"hls_h264":"https://video/x.m3u8"}]}}}
            """, 10));

    [Theory]
    [InlineData("""{"10":{"success":true,"data":{"movies":[]}}}""")]
    [InlineData("""{"10":{"success":false}}""")]
    [InlineData("[]")]
    [InlineData("""{"10":null}""")]
    [InlineData("""{"10":{"data":{"movies":[null]}}}""")]
    [InlineData("not json")]
    public void NoTrailerGivesNull(string json) => Assert.Null(SteamStoreClient.ParseTrailer(json, 10));

    [Fact]
    public void InvalidPreferredFormatStillAllowsAnotherFormat() =>
        Assert.Equal("https://video/max.mp4", SteamStoreClient.ParseTrailer("""
            {"10":{"data":{"movies":[{"mp4":{"480":false,"max":"https://video/max.mp4"}}]}}}
            """, 10));

    [Fact]
    public void InvalidScreenshotRowsAreSkipped() =>
        Assert.Equal([("preview", "full")], SteamStoreClient.ParseScreenshots("""
            {"10":{"data":{"screenshots":[null,{"path_thumbnail":3,"path_full":"unusable"},
                {"path_thumbnail":"preview","path_full":"full"}]}}}
            """, 10));

    [Fact]
    public void InvalidOptionalDetailsFieldsAreIgnored()
    {
        Assert.Null(SteamStoreClient.ParseDetails("""{"10":null}""", 10, DateTime.UnixEpoch));
        var details = SteamStoreClient.ParseDetails("""
            {"10":{"success":true,"data":{"genres":[null,{"description":"Action"}],
                "release_date":"unavailable","metacritic":{"score":"unavailable"}}}}
            """, 10, DateTime.UnixEpoch);
        Assert.NotNull(details);
        Assert.Equal(["Action"], details.Genres);
    }

    [Fact]
    public void MissingSearchRowsAreSkipped() =>
        Assert.Equal(10, Assert.Single(SteamStoreClient.ParseSearch("""
            {"items":[null,{"type":"app","name":"Game","id":10}]}
            """)).Id);
}
