// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Sender en tastekombinasjon til forgrunnsvinduet med <c>SendInput</c>. Brukes for å gi Moonlight snarveier (for
/// eksempel ytelsesoverlegget) og for å sende en tast videre til gaming-PC-en gjennom streamen.
/// </summary>
public static class Keyboard
{
    public const ushort Control = 0x11;
    public const ushort Shift = 0x10;
    public const ushort Alt = 0x12;
    public const ushort S = 0x53;
    public const ushort F9 = 0x78;
    public const ushort F10 = 0x79;

    // Venstre variant av modifikatorene: Handheld Companion sammenligner tastekoden den ser i tastaturkroken.
    public const ushort LeftControl = 0xA2;
    public const ushort LeftShift = 0xA0;
    public const ushort LeftAlt = 0xA4;

    /// <summary>Trykker tastene ned i rekkefølge og slipper dem i motsatt rekkefølge. Returnerer om alt ble sendt.</summary>
    public static bool SendChord(params ushort[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var inputs = new KeyInput[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            inputs[i] = Key(keys[i], up: false);
            inputs[(keys.Length * 2) - 1 - i] = Key(keys[i], up: true);
        }

        return KeyboardNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyInput>()) == inputs.Length;
    }

    // Skannekoden fylles inn også: SDL (Moonlight) leser den fra meldingen for å finne riktig tast.
    private static KeyInput Key(ushort virtualKey, bool up) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        VirtualKey = virtualKey,
        ScanCode = (ushort)KeyboardNative.MapVirtualKeyW(virtualKey, 0),
        Flags = up ? 0x0002u : 0u, // KEYEVENTF_KEYUP
    };
}

/// <summary>INPUT med KEYBDINPUT, slik den ligger i minnet på 64-bit Windows (x64 og arm64).</summary>
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct KeyInput
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public ushort VirtualKey;
    [FieldOffset(10)] public ushort ScanCode;
    [FieldOffset(12)] public uint Flags;
    [FieldOffset(16)] public uint Time;
    [FieldOffset(24)] public nint ExtraInfo;
}

internal static partial class KeyboardNative
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] KeyInput[] inputs, int size);

    [LibraryImport("user32.dll")]
    internal static partial uint MapVirtualKeyW(uint code, uint mapType);
}
