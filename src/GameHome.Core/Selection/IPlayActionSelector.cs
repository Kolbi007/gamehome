// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;

namespace GameHome.Core.Selection;

/// <summary>Tilstanden enheten er i når et standardvalg skal foreslås.</summary>
public sealed record DeviceContext(bool OnBattery, bool NetworkAvailable, IReadOnlyDictionary<string, HostState> Hosts);

/// <summary>Foreslår spillemåte. Sist valgte vinner; batteri og hoststatus gir bare forslag.</summary>
public interface IPlayActionSelector
{
    PlayAction? SelectDefault(Game game, IReadOnlyList<PlayAction> actions, DeviceContext context);
}
