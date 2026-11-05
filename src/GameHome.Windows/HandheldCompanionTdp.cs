// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;

namespace GameHome.Windows;

/// <summary>
/// TDP fjernstyres i Handheld Companion (HC), aldri direkte (ADR 0011): GameHome sender HCs hurtigtaster for «TDP ned» og
/// «TDP opp», og HC gjør endringen innenfor sine grenser. Verdien som vises, leses fra HCs «Default»-strømprofil
/// (%LOCALAPPDATA%\HandheldCompanion\powerprofiles\Default.json). Filen leses bare, aldri skrives; formatet er HCs
/// eget (feltnavn fra HCs filer og offentlige kildekode), så alt som ikke ser riktig ut, gir null.
/// </summary>
public static class HandheldCompanionTdp
{
    private static readonly string ProfileFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion", "powerprofiles", "Default.json");

    /// <summary>Hurtigtastene scripts/Set-HandheldCompanion.ps1 setter opp i HC.</summary>
    public static readonly ushort[] DecreaseChord = [Keyboard.LeftControl, Keyboard.LeftAlt, Keyboard.LeftShift, Keyboard.F7];

    public static readonly ushort[] IncreaseChord = [Keyboard.LeftControl, Keyboard.LeftAlt, Keyboard.LeftShift, Keyboard.F8];

    /// <summary>
    /// Vedvarende TDP i watt fra HCs profil, eller null når profilen mangler eller ikke har TDP-overstyring (da virker ikke
    /// HCs TDP-knapper, og oppsettskriptet må kjøres).
    /// </summary>
    public static double? Read()
    {
        try
        {
            using var stream = new FileStream(ProfileFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("TDPOverrideEnabled", out var enabled) || enabled.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("TDPOverrideValues", out var values)
                || !values.TryGetProperty("$values", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0
                || !list[0].TryGetDouble(out var sustained))
            {
                return null;
            }

            return sustained;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
