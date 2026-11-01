// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;

namespace GameHome.Core.Runners;

[Flags]
public enum RunnerCapabilities
{
    None = 0,
    /// <summary>Kan oppdage at spillet er ferdig.</summary>
    ObserveExit = 1,
    /// <summary>Kan be om normal avslutning.</summary>
    RequestClose = 2,
    /// <summary>Kan tvangsavslutte, kun etter bekreftelse.</summary>
    ForceTerminate = 4,
    /// <summary>Spillet kjører i GameHomes Job Object.</summary>
    OwnsProcessGroup = 8,
    /// <summary>Kan koble fra uten å stoppe spillet (stream).</summary>
    Detach = 16,
}

public interface IRunner
{
    string RunnerId { get; }

    RunnerCapabilities Capabilities { get; }

    Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct);
}

public enum SessionKind { Local, Stream }

/// <summary>Hvordan et livsløp endte.</summary>
public enum SessionEndReason { Exited, ClosedByUser, Terminated, Detached, LaunchFailed, Unknown }

public sealed record SessionEnd(SessionEndReason Reason, int? ExitCode = null, string? Detail = null);

/// <summary>En aktiv økt startet av GameHome.</summary>
/// <remarks>
/// For lokale spill er <see cref="Completion"/> spillets slutt. For stream er det bare den lokale klienten
/// (Moonlight); spillet på hosten har eget livsløp i <see cref="IStreamSession"/>.
/// </remarks>
public interface ISession
{
    string SessionId { get; }

    string PlayActionId { get; }

    SessionKind Kind { get; }

    Task<SessionEnd> Completion { get; }

    Task RequestCloseAsync();

    /// <summary>Kun etter eksplisitt bekreftelse fra brukeren.</summary>
    Task ForceTerminateAsync();
}

public enum StreamConnectionState { Connecting, Connected, Disconnected, Ended }

public enum RemoteAppState { Unknown, Starting, Running, Exited }

/// <summary>
/// En stream-økt. Klienten (Moonlight), forbindelsen og appen på hosten er separate livsløp,
/// og ingen av dem er sannhet for de andre.
/// </summary>
public interface IStreamSession : ISession
{
    string HostId { get; }

    StreamConnectionState Connection { get; }

    RemoteAppState RemoteApp { get; }

    event EventHandler? StateChanged;

    /// <summary>Lukker strømmen lokalt. Spillet fortsetter på hosten.</summary>
    Task DisconnectAsync();

    /// <summary>Kobler til den eksisterende appen på hosten igjen.</summary>
    Task ReconnectAsync(CancellationToken ct);

    /// <summary>Ber appen på hosten avslutte normalt (Vibepollo). Kun etter bekreftelse.</summary>
    Task EndRemoteAppAsync(CancellationToken ct);
}

/// <summary>Innstillinger for én oppstart, for eksempel oppløsning og bitrate for stream.</summary>
public sealed record Profile(string Id, string Name, IReadOnlyDictionary<string, string> Settings)
{
    public static Profile Default { get; } = new("default", "Standard", new Dictionary<string, string>());
}
