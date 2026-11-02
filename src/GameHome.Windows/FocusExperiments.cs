// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Windows;

/// <summary>
/// Brytere for fokusendringene fra 2026-11-02 (M2 i Assassin's Creed Rogue og det som fulgte). Alle er av som
/// standard, så GameHome oppfører seg som 0.1.95.0 (som virket med strøm og lokale spill). Slås på én om gangen
/// under Feilsøking for å finne hvilken som ødela fokus ved strømstart.
/// </summary>
public static class FocusExperiments
{
    /// <summary>Tastaturkrok på lavt nivå under økter (reserve for M2 i spill som slår av hurtigtaster).</summary>
    public static bool KeyboardHook { get; set; }

    /// <summary>Følg en ny prosess fra spillmappen når en oppstarter blir liggende (gjelder også Moonlight-økten).</summary>
    public static bool FollowGameProcess { get; set; }

    /// <summary>Gi forgrunnen tilbake til spillet når GameHome blir aktiv uten at noen ba om det.</summary>
    public static bool HandForegroundBack { get; set; }

    /// <summary>Endret dekke ved strømstart og vindusrekkefølge (GameHome rett bak spillet, Legion Space bakerst).</summary>
    public static bool WindowOrder { get; set; }

    public static string Describe() =>
        $"krok={On(KeyboardHook)}, følg spillprosess={On(FollowGameProcess)}, gi forgrunnen tilbake={On(HandForegroundBack)}, vindusrekkefølge={On(WindowOrder)}";

    private static string On(bool value) => value ? "på" : "av";
}
