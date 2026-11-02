// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Network;

namespace GameHome.Core.Tests;

public class WakeOnLanTests
{
    private static readonly byte[] Mac = [0x04, 0x42, 0x1A, 0x89, 0xA1, 0x3B];

    [Fact]
    public void Magic_packet_is_six_ff_then_mac_sixteen_times()
    {
        var packet = WakeOnLan.BuildMagicPacket(Mac);

        Assert.Equal(102, packet.Length);
        Assert.All(packet[..6], b => Assert.Equal(0xFF, b));
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(Mac, packet[(6 + (i * 6))..(12 + (i * 6))]);
        }
    }

    [Fact]
    public void Moonlight_qt_byte_array_is_parsed()
    {
        var stored = "@ByteArray(" + new string([.. Mac.Select(b => (char)b)]) + ")";

        Assert.Equal(Mac, WakeOnLan.ParseQtByteArray(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@ByteArray()")]
    [InlineData("@ByteArray(abc)")]
    [InlineData("04-42-1A-89-A1-3B")]
    public void Invalid_mac_values_give_null(string? value) => Assert.Null(WakeOnLan.ParseQtByteArray(value));

    [Theory]
    [InlineData("192.168.1.165", "192.168.1.255")]
    [InlineData("10.0.0.7", "10.0.0.255")]
    [InlineData("not an ip", null)]
    [InlineData(null, null)]
    public void Subnet_broadcast_for_slash_24(string? ip, string? expected) => Assert.Equal(expected, WakeOnLan.SubnetBroadcast(ip));
}
