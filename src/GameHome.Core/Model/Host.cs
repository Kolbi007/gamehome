// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Model;

/// <summary>En maskin spill kan streames fra, for eksempel gaming-PC-en med Vibepollo. v1 har én host.</summary>
/// <param name="Address">Vertsnavn eller IP-adresse slik Moonlight bruker den.</param>
/// <param name="MacAddress">For Wake-on-LAN, hvis kjent.</param>
public sealed record Host(string Id, string Name, string Address, string? MacAddress = null);

/// <summary>Hostens tilstand. Timeout gir <see cref="Unknown"/>, aldri antatt <see cref="Asleep"/>.</summary>
public enum HostState
{
    Unknown,
    Online,
    /// <summary>Bekreftet i hvile, for eksempel fordi vekking ble sendt og hosten svarte etterpå.</summary>
    Asleep,
    Unreachable,
    Waking,
}
