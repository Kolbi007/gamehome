// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Network;

/// <summary>
/// Wake-on-LAN: en «magisk pakke» er 6 × 0xFF fulgt av maskinens MAC-adresse 16 ganger, sendt som UDP-kringkasting.
/// Offentlig standard (AMD Magic Packet). Sending ligger i GameHome.Windows; her er bare formatet, så det kan testes.
/// </summary>
public static class WakeOnLan
{
    public const int MacLength = 6;

    public static byte[] BuildMagicPacket(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != MacLength)
        {
            throw new ArgumentException("En MAC-adresse er 6 byte.", nameof(mac));
        }

        var packet = new byte[6 + (16 * MacLength)];
        packet.AsSpan(0, 6).Fill(0xFF);
        for (var i = 0; i < 16; i++)
        {
            mac.CopyTo(packet.AsSpan(6 + (i * MacLength)));
        }

        return packet;
    }

    /// <summary>
    /// Leser MAC-adressen slik Moonlight lagrer den i registeret: Qt skriver binærdata som teksten
    /// <c>@ByteArray(...)</c> med ett tegn per byte. Returnerer null hvis verdien ikke er en gyldig MAC.
    /// </summary>
    public static byte[]? ParseQtByteArray(string? value)
    {
        const string prefix = "@ByteArray(";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(')'))
        {
            return null;
        }

        var body = value.AsSpan(prefix.Length, value.Length - prefix.Length - 1);
        if (body.Length != MacLength)
        {
            return null;
        }

        var mac = new byte[MacLength];
        for (var i = 0; i < MacLength; i++)
        {
            if (body[i] > 0xFF)
            {
                return null;
            }

            mac[i] = (byte)body[i];
        }

        return mac.All(b => b == 0) ? null : mac;
    }

    /// <summary>Kringkastingsadressen for et /24-nett, for eksempel 192.168.1.165 → 192.168.1.255.</summary>
    public static string? SubnetBroadcast(string? ipv4)
    {
        var parts = ipv4?.Split('.');
        return parts is { Length: 4 } && parts.All(p => byte.TryParse(p, out _))
            ? $"{parts[0]}.{parts[1]}.{parts[2]}.255"
            : null;
    }
}
