// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Sources.Steam;

namespace GameHome.Core.Tests;

public class SteamDownloadsTests
{
    private static string Manifest(int flags, long total, long done) => $$"""
        "AppState"
        {
            "appid"		"1580180"
            "name"		"Gunbrella"
            "StateFlags"		"{{flags}}"
            "BytesToDownload"		"{{total}}"
            "BytesDownloaded"		"{{done}}"
        }
        """;

    [Fact]
    public void DownloadingGameShowsPercent()
    {
        var download = SteamDownloads.Parse(Manifest(1026, 320_000_000, 160_000_000));
        Assert.NotNull(download);
        Assert.Equal("Gunbrella", download.Name);
        Assert.Equal(50, download.Percent);
    }

    [Fact]
    public void FinishedGameIsNotADownload() =>
        Assert.Null(SteamDownloads.Parse(Manifest(4, 320_000_000, 320_000_000)));

    [Fact]
    public void UpdateOfInstalledGameIsADownload() =>
        Assert.Equal(25, SteamDownloads.Parse(Manifest(1028, 400, 100))?.Percent);

    [Fact]
    public void BrokenManifestIsIgnored() => Assert.Null(SteamDownloads.Parse("not a manifest"));
}
