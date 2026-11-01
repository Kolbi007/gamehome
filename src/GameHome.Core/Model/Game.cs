// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Model;

/// <summary>Ett spill, uansett hvor mange måter det kan spilles på.</summary>
/// <remarks>Utkast fra docs/architecture.md. Overstyringer vinner alltid over skanning og metadata.</remarks>
public sealed record Game(
    string Id,
    string Title,
    IReadOnlyList<ExternalIdentity> Identities,
    ArtSet Art,
    UserOverrides Overrides);

/// <summary>En ekstern ID for spillet, for eksempel ("steam", "1091500").</summary>
public sealed record ExternalIdentity(string Namespace, string Value);

/// <summary>Bilder for et spill. Stier er lokale filer i art-cachen.</summary>
public sealed record ArtSet(string? Cover = null, string? Hero = null, string? Logo = null)
{
    public static ArtSet Empty { get; } = new();
}

/// <summary>Brukerens rettelser. Overskrives aldri av skanning eller metadata.</summary>
public sealed record UserOverrides(
    string? Title = null,
    string? ProcessName = null,
    string? DefaultPlayActionId = null,
    bool Hidden = false)
{
    public static UserOverrides None { get; } = new();
}
