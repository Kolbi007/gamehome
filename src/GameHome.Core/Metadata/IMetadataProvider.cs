// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;

namespace GameHome.Core.Metadata;

/// <summary>Hva en metadatakilde kan levere. En kilde kan levere flere.</summary>
[Flags]
public enum MetadataFacets
{
    None = 0,
    /// <summary>Beskrivelse, sjangre, utgivelsesdato, utvikler.</summary>
    Details = 1,
    /// <summary>Cover, hero-bilde og logo.</summary>
    Art = 2,
    /// <summary>Hvor lang tid spillet tar (for eksempel HowLongToBeat eller IGDB time-to-beat).</summary>
    PlayTime = 4,
    /// <summary>Vurderinger fra kritikere eller brukere (for eksempel Metacritic, IGDB, Steam).</summary>
    Ratings = 8,
}

/// <summary>
/// Om kildens vilkår er avklart. Bare <see cref="Approved"/> kan være på som standard;
/// de andre krever at brukeren selv slår dem på under Avansert.
/// </summary>
public enum TermsStatus
{
    /// <summary>Offisielt API med vilkår som tillater bruken.</summary>
    Approved,
    /// <summary>Vilkårene er ikke avklart ennå.</summary>
    PendingReview,
    /// <summary>Ingen offisiell API; kilden leses fra nettsiden. Av som standard og kan slutte å virke.</summary>
    Unofficial,
}

/// <summary>Beskrivelse av en metadatakilde, brukt av innstillingene og av orkestreringen.</summary>
/// <param name="MaxRequestsPerSecond">Øvre grense kilden selv krever eller vi velger; null betyr ingen kjent grense.</param>
/// <param name="AttributionUrl">Lenke som vises der dataene vises, når kilden krever det.</param>
public sealed record MetadataProviderInfo(
    string ProviderId,
    string DisplayName,
    MetadataFacets Facets,
    TermsStatus Terms,
    bool RequiresApiKey,
    double? MaxRequestsPerSecond,
    string? AttributionUrl);

/// <summary>Et oppslag: eksakt ID når den finnes, ellers tittel.</summary>
public sealed record MetadataQuery(
    string? Title,
    IReadOnlyList<ExternalIdentity> Identities,
    MetadataFacets Wanted = MetadataFacets.Details | MetadataFacets.Art);

public sealed record GameDetails(
    string? Description = null,
    IReadOnlyList<string>? Genres = null,
    DateOnly? ReleaseDate = null,
    string? Developer = null,
    string? Publisher = null);

/// <summary>Tid å fullføre. Null betyr at kilden ikke har tallet.</summary>
public sealed record PlayTime(
    string Source,
    TimeSpan? MainStory,
    TimeSpan? MainPlusExtras,
    TimeSpan? Completionist,
    string? Url);

public enum RatingKind { Critic, User }

/// <summary>En vurdering normalisert til 0–100, med lenke til kilden for attribusjon.</summary>
/// <param name="Source">For eksempel "metacritic", "igdb", "steam".</param>
public sealed record Rating(string Source, RatingKind Kind, int Score, int? Count, string? Url);

public sealed record MetadataCandidate(
    string ProviderId,
    string Title,
    IReadOnlyList<ExternalIdentity> Identities,
    double Confidence,
    GameDetails? Details = null,
    ArtSet? Art = null,
    PlayTime? PlayTime = null,
    IReadOnlyList<Rating>? Ratings = null);

/// <summary>
/// En metadatakilde. Kalles aldri mens et spill eller en stream kjører; alt resultat caches lokalt
/// slik at appen virker offline etter første henting.
/// </summary>
public interface IMetadataProvider
{
    MetadataProviderInfo Info { get; }

    Task<IReadOnlyList<MetadataCandidate>> ResolveAsync(MetadataQuery query, CancellationToken ct);
}

/// <summary>Metadata lagret for et spill, sammenslått fra flere kilder. Brukerens overstyringer vinner alltid.</summary>
public sealed record GameMetadata(
    string GameId,
    GameDetails? Details,
    ArtSet Art,
    PlayTime? PlayTime,
    IReadOnlyList<Rating> Ratings,
    IReadOnlyDictionary<MetadataFacets, string> SourceByFacet,
    DateTimeOffset FetchedAt);
