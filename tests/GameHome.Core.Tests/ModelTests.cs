// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Home;
using GameHome.Core.Model;

namespace GameHome.Core.Tests;

public class ModelTests
{
    [Fact]
    public void ExternalIdentity_compares_by_value()
    {
        Assert.Equal(new ExternalIdentity("steam", "1091500"), new ExternalIdentity("steam", "1091500"));
        Assert.NotEqual(new ExternalIdentity("steam", "1091500"), new ExternalIdentity("gog", "1091500"));
    }

    [Fact]
    public void PlayAction_is_remote_only_when_it_points_to_a_host()
    {
        var local = new PlayAction("a1", "g1", PlayKind.Store, "steam", "1091500", null, null,
            LaunchMode.ClientOnDemand, null, Availability.Installed);
        var remote = local with { Id = "a2", Kind = PlayKind.Stream, RunnerId = "moonlight", HostId = "gaming-pc" };

        Assert.False(local.IsRemote);
        Assert.True(remote.IsRemote);
    }

    [Fact]
    public void Missing_home_integration_reports_unavailable()
    {
        var home = new NoHomeIntegration();

        Assert.False(home.IsAvailable);
        Assert.False(home.IsFullScreenExperienceActive());
    }
}
