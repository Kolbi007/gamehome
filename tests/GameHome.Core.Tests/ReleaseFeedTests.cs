// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Sources.Updates;

namespace GameHome.Core.Tests;

public class ReleaseFeedTests
{
    private const string Feed = """
        [
          { "tag_name": "v0.2.3", "draft": false, "assets": [
              { "name": "GameHome.App_0.2.3.0_x64.msix", "browser_download_url": "https://github.com/o/r/releases/download/v0.2.3/GameHome.App_0.2.3.0_x64.msix", "size": 100 },
              { "name": "GameHome.App_0.2.3.0_arm64.msix", "browser_download_url": "https://github.com/o/r/releases/download/v0.2.3/GameHome.App_0.2.3.0_arm64.msix", "size": 90 } ] },
          { "tag_name": "v0.2.10", "draft": false, "assets": [
              { "name": "GameHome.App_0.2.10.0_x64.msix", "browser_download_url": "https://github.com/o/r/releases/download/v0.2.10/GameHome.App_0.2.10.0_x64.msix", "size": 120 } ] },
          { "tag_name": "v0.3.0", "draft": true, "assets": [
              { "name": "GameHome.App_0.3.0.0_x64.msix", "browser_download_url": "https://github.com/o/r/releases/download/v0.3.0/x_x64.msix", "size": 1 } ] },
          { "tag_name": "nightly", "assets": [] }
        ]
        """;

    [Fact]
    public void NewestNonDraftForArchitectureWins()
    {
        var newest = ReleaseFeed.Newest(Feed, "x64");
        Assert.NotNull(newest);
        Assert.Equal(new Version(0, 2, 10, 0), newest.Version);
        Assert.Equal(120, newest.Size);
    }

    [Fact]
    public void OtherArchitectureIsRespected() =>
        Assert.Equal(new Version(0, 2, 3, 0), ReleaseFeed.Newest(Feed, "arm64")?.Version);

    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("0.2.15", "0.2.15.0")]
    [InlineData("nightly", null)]
    [InlineData("v1.2", null)]
    public void TagsAreParsed(string tag, string? expected) =>
        Assert.Equal(expected, ReleaseFeed.ParseTag(tag)?.ToString());

    [Fact]
    public void EmptyOrOddFeedGivesNothing()
    {
        Assert.Null(ReleaseFeed.Newest("[]", "x64"));
        Assert.Null(ReleaseFeed.Newest("{}", "x64"));
    }
}
