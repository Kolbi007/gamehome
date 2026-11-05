// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Net;
using GameHome.Sources.PlayStation;

namespace GameHome.Core.Tests;

public class PlayStationDiscoveryTests
{
    private static readonly IPAddress Address = IPAddress.Parse("192.0.2.10");

    [Fact]
    public void AwakeConsoleIsParsed()
    {
        const string reply = "HTTP/1.1 200 Ok\nhost-id:0123456789AB\nhost-type:PS5\nhost-name:PS5-123\nhost-request-port:997\n";
        var found = PlayStationDiscovery.Parse(reply, Address);
        Assert.NotNull(found);
        Assert.Equal("PS5-123", found.Nickname);
        Assert.True(found.Awake);
        Assert.Equal(Address, found.Address);
    }

    [Fact]
    public void StandbyConsoleIsAsleep()
    {
        const string reply = "HTTP/1.1 620 Server Standby\r\nhost-type:PS5\r\nhost-name:PS5-123\r\n";
        var found = PlayStationDiscovery.Parse(reply, Address);
        Assert.NotNull(found);
        Assert.False(found.Awake);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00030010\n")]
    [InlineData("HTTP/1.1 200 Ok\nhost-type:PS5\n")]
    public void OtherRepliesAreIgnored(string reply) =>
        Assert.Null(PlayStationDiscovery.Parse(reply, Address));
}
