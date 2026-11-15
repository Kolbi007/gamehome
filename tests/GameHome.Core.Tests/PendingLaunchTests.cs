// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Runners;

namespace GameHome.Core.Tests;

public class PendingLaunchTests
{
    [Fact]
    public void Cancel_releases_home_to_start_again_before_the_original_attempt_finishes()
    {
        var launches = new PendingLaunch();
        using var first = launches.Begin();

        Assert.True(launches.Cancel());
        Assert.True(first.IsCancellationRequested);
        Assert.False(launches.Owns(first.Token));

        using var second = launches.Begin();
        launches.Complete(first);

        Assert.True(launches.Owns(second.Token));
        Assert.False(second.IsCancellationRequested);
        launches.Complete(second);
        Assert.False(launches.IsActive);
    }

    [Fact]
    public void Only_one_attempt_can_be_active()
    {
        var launches = new PendingLaunch();
        using var first = launches.Begin();

        Assert.Throws<InvalidOperationException>(() => launches.Begin());
        Assert.True(launches.Owns(first.Token));
    }
}
