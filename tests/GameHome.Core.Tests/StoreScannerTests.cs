// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Sources;

namespace GameHome.Core.Tests;

public class StoreScannerTests
{
    [Fact]
    public void Failed_source_preserves_installations_and_other_sources_are_applied()
    {
        var library = new GameLibrary();
        library.SyncStore("xbox", [new("first", "Existing Xbox Game", null)]);
        var scans = StoreScanner.Scan(
        [
            ("xbox", _ => throw new IOException(@"Cannot read C:\Users\personal\Packages")),
            ("epic", _ => StoreScanResult.Complete([new("second", "New Epic Game", null)])),
        ], TestContext.Current.CancellationToken);

        Assert.Collection(scans,
            scan =>
            {
                Assert.Equal("xbox", scan.RunnerId);
                Assert.Equal(SourceHealthState.Failed, scan.Result.Health.State);
                Assert.Equal("Kunne ikke lese kilden (IOException)", scan.Result.Health.LastError);
                Assert.Null(scan.Result.ApplyTo(library, scan.RunnerId));
            },
            scan =>
            {
                Assert.Equal("epic", scan.RunnerId);
                Assert.Equal(SourceHealthState.Ok, scan.Result.Health.State);
                Assert.True(scan.Result.ApplyTo(library, scan.RunnerId)?.Changed);
            });
        Assert.All(library.PlayActions, action => Assert.Equal(Availability.Installed, action.Availability));
        Assert.Equal(["Existing Xbox Game", "New Epic Game"], library.VisibleGames.Select(GameLibrary.DisplayTitle));
    }

    [Fact]
    public void Partial_scan_adds_confirmed_games_and_only_complete_empty_scan_marks_games_missing()
    {
        var library = new GameLibrary();
        library.SyncStore("xbox", [new("first", "Existing Xbox Game", null)]);

        var partial = StoreScanResult.Degraded([new("second", "New Xbox Game", null)], "Pakke utilgjengelig");
        var updated = partial.ApplyTo(library, "xbox");

        Assert.NotNull(updated);
        Assert.Single(updated.Added);
        Assert.Equal(0, updated.MarkedMissing);
        Assert.All(library.PlayActions, action => Assert.Equal(Availability.Installed, action.Availability));

        var empty = StoreScanResult.Complete([]).ApplyTo(library, "xbox");

        Assert.NotNull(empty);
        Assert.Equal(2, empty.MarkedMissing);
        Assert.All(library.PlayActions, action => Assert.Equal(Availability.Missing, action.Availability));
    }

    [Fact]
    public void Cancellation_during_one_source_skips_remaining_sources()
    {
        using var cancellation = new CancellationTokenSource();
        var laterSourceCalled = false;

        Assert.Throws<OperationCanceledException>(() => StoreScanner.Scan(
        [
            ("steam", _ =>
            {
                cancellation.Cancel();
                return StoreScanResult.Complete([]);
            }),
            ("xbox", _ =>
            {
                laterSourceCalled = true;
                return StoreScanResult.Complete([]);
            }),
        ], cancellation.Token));

        Assert.False(laterSourceCalled);
    }
}
