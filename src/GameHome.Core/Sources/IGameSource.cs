// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;

namespace GameHome.Core.Sources;

public enum SourceHealthState { NotScanned, Ok, Degraded, Failed, Disabled }

/// <summary>Synlig helsestatus for en kilde, vist i innstillingene og i diagnostikk.</summary>
/// <param name="LastError">Kort, brukervennlig feiltekst uten tokens, brukernavn eller personlige stier.</param>
public sealed record SourceHealth(
    SourceHealthState State,
    DateTimeOffset? LastScan = null,
    int FoundCount = 0,
    string? LastError = null)
{
    public static SourceHealth NotScanned { get; } = new(SourceHealthState.NotScanned);
}

/// <summary>
/// En kilde til spill, for eksempel Steam, Xbox eller en lokal mappe. Skanner bare på forespørsel, aldri mens
/// et spill kjører, og en kilde som feiler påvirker ikke de andre.
/// </summary>
public interface IGameSource
{
    string SourceId { get; }

    SourceHealth Health { get; }

    IAsyncEnumerable<DiscoveryResult> DiscoverAsync(CancellationToken ct);
}
