// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Metadata;

namespace GameHome.Metadata;

/// <summary>
/// Kildene GameHome kjenner til, med vilkårsstatus. Se docs/adr/0003-metadata-providers.md.
/// Selve klientene legges til i fase 4.
/// </summary>
public static class KnownProviders
{
    public static MetadataProviderInfo Igdb { get; } = new(
        "igdb", "IGDB",
        MetadataFacets.Details | MetadataFacets.Art | MetadataFacets.PlayTime | MetadataFacets.Ratings,
        TermsStatus.Approved, RequiresApiKey: true, MaxRequestsPerSecond: 4, "https://www.igdb.com");

    public static MetadataProviderInfo SteamGridDb { get; } = new(
        "steamgriddb", "SteamGridDB",
        MetadataFacets.Art,
        TermsStatus.Approved, RequiresApiKey: true, MaxRequestsPerSecond: null, "https://www.steamgriddb.com");

    /// <summary>Steams egen bildecache på disk. Ingen nettverk.</summary>
    public static MetadataProviderInfo SteamLocalCache { get; } = new(
        "steam-local", "Steam (lokal bildecache)",
        MetadataFacets.Art,
        TermsStatus.Approved, RequiresApiKey: false, MaxRequestsPerSecond: null, AttributionUrl: null);

    /// <summary>Steam Store sitt appdetails-endepunkt: beskrivelse, bilder og Metacritic-score der Steam har den.</summary>
    public static MetadataProviderInfo SteamStore { get; } = new(
        "steam-store", "Steam Store",
        MetadataFacets.Details | MetadataFacets.Art | MetadataFacets.Ratings,
        TermsStatus.Unofficial, RequiresApiKey: false, MaxRequestsPerSecond: 1, "https://store.steampowered.com");

    public static MetadataProviderInfo HowLongToBeat { get; } = new(
        "howlongtobeat", "HowLongToBeat",
        MetadataFacets.PlayTime,
        TermsStatus.Unofficial, RequiresApiKey: false, MaxRequestsPerSecond: 0.5, "https://howlongtobeat.com");

    public static MetadataProviderInfo Metacritic { get; } = new(
        "metacritic", "Metacritic",
        MetadataFacets.Ratings,
        TermsStatus.Unofficial, RequiresApiKey: false, MaxRequestsPerSecond: 0.5, "https://www.metacritic.com");

    public static MetadataProviderInfo ScreenScraper { get; } = new(
        "screenscraper", "ScreenScraper",
        MetadataFacets.Details | MetadataFacets.Art,
        TermsStatus.PendingReview, RequiresApiKey: true, MaxRequestsPerSecond: 1, "https://www.screenscraper.fr");

    public static IReadOnlyList<MetadataProviderInfo> All { get; } =
        [Igdb, SteamGridDb, SteamLocalCache, SteamStore, HowLongToBeat, Metacritic, ScreenScraper];

    /// <summary>
    /// Uoffisielle kilder produkteier har valgt å slå på som standard. Steam Store (butikksøk og offentlige butikkbilder)
    /// ble slått på 2026-11-02 (ADR 0003, tillegg). Brukeren skal kunne slå den av.
    /// </summary>
    public static IReadOnlySet<string> EnabledByProductOwner { get; } = new HashSet<string>(StringComparer.Ordinal) { "steam-store" };

    /// <summary>Kilder med avklarte vilkår og uten nøkkel, og kilder produkteier har valgt, er på uten oppsett.</summary>
    public static bool IsEnabledByDefault(MetadataProviderInfo provider) =>
        (provider.Terms == TermsStatus.Approved && !provider.RequiresApiKey)
        || EnabledByProductOwner.Contains(provider.ProviderId);
}
