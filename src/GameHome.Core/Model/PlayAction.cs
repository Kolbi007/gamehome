// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Model;

public enum PlayKind { Store, Executable, Stream, Emulator }

public enum LaunchMode { Direct, ClientOnDemand, ClientRequired }

/// <summary>
/// Om en spillemåte finnes. Gjelder installasjonen, ikke om en host er påslått; det er <see cref="HostState"/>.
/// </summary>
public enum Availability { Unknown, Installed, Missing }

/// <summary>Én måte å spille et spill på, for eksempel Steam lokalt eller stream fra gaming-PC-en.</summary>
/// <param name="RunnerId">steam, xbox, exe, moonlight, retroarch ...</param>
/// <param name="Target">AppID, exe-sti, app-navn på hosten eller ROM-sti.</param>
/// <param name="HostId">Satt når spillemåten kjører på en annen maskin; null betyr lokalt på Legion Go.</param>
public sealed record PlayAction(
    string Id,
    string GameId,
    PlayKind Kind,
    string RunnerId,
    string Target,
    string? Arguments,
    string? WorkingDirectory,
    LaunchMode LaunchMode,
    string? ProfileId,
    Availability Availability,
    string? HostId = null)
{
    public bool IsRemote => HostId is not null;
}

/// <summary>Et funn fra en kilde. Høy konfidens kan legge til en PlayAction; to spill slås aldri sammen automatisk.</summary>
public sealed record DiscoveryResult(
    string SourceId,
    PlayAction Candidate,
    string? SuggestedTitle,
    IReadOnlyList<ExternalIdentity> Identities,
    double Confidence,
    IReadOnlyList<string> Evidence);
