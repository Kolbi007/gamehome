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

    [Fact]
    public void ProgressIsClampedWithoutOverflow()
    {
        var download = new SteamDownload("1", "Game", long.MaxValue - 1, long.MaxValue);
        Assert.Equal(99, download.Percent);
        Assert.Equal(100, (download with { BytesToDownload = 1 }).Percent);
        Assert.Equal(0, (download with { BytesDownloaded = long.MinValue }).Percent);
    }

    [Fact]
    public void ScanHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SteamDownloads.Scan("unused", cancellation.Token));
    }

    [Fact]
    public void ScanContinuesPastUnreadableManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gamehome-downloads-{Guid.NewGuid():N}");
        var steamapps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamapps);
        try
        {
            var broken = Path.Combine(steamapps, "appmanifest_1.acf");
            try
            {
                File.CreateSymbolicLink(broken, Path.Combine(root, "missing"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                // Symlinks can require privileges on Windows; the valid-file check still applies.
            }

            File.WriteAllText(Path.Combine(steamapps, "appmanifest_2.acf"), Manifest(1026, 100, 50));
            var downloads = SteamDownloads.Scan(root, TestContext.Current.CancellationToken);
            Assert.Single(downloads);
            Assert.Equal("1580180", downloads[0].AppId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
