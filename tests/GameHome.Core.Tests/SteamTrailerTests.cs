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
    [InlineData("not json")]
    public void NoTrailerGivesNull(string json) => Assert.Null(SteamStoreClient.ParseTrailer(json, 10));
}
