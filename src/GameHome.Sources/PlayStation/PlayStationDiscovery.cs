// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Net;

namespace GameHome.Sources.PlayStation;

/// <summary>Svar på søket etter PlayStation på nettverket.</summary>
public sealed record PlayStationFound(string Nickname, IPAddress Address, bool Awake);

/// <summary>
/// Tolker svaret en PlayStation gir på søket «SRCH * HTTP/1.1» (UDP 9302 for PS5, 987 for PS4). Formatet er lest fra
/// chiaki-ngs åpne kildekode og bekreftes med egne tester: første linje «HTTP/1.1 200 Ok» (våken) eller
/// «HTTP/1.1 620 Server Standby», deretter linjer «nøkkel:verdi», blant dem <c>host-name</c>.
/// </summary>
public static class PlayStationDiscovery
{
    public const string Search = "SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00030010\n";

    public static PlayStationFound? Parse(string reply, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(address);
        var lines = reply.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.1 ", StringComparison.Ordinal))
        {
            return null;
        }

        string? name = null;
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0] == "host-name")
            {
                name = parts[1].Trim();
            }
        }

        return string.IsNullOrEmpty(name) ? null : new PlayStationFound(name, address, lines[0].Contains(" 200 ", StringComparison.Ordinal));
    }
}
