// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>En skjermmodus slik brukeren ser den: bredde og høyde i nåværende retning, og oppdateringsfrekvens.</summary>
public readonly record struct DisplayMode(int Width, int Height, int Hz);

/// <summary>
/// Oppløsning og oppdateringsfrekvens for hovedskjermen med de dokumenterte API-ene <c>EnumDisplaySettingsW</c> og
/// <c>ChangeDisplaySettingsExW</c>. Krever ikke administrator. Endringen lagres i registeret, slik Windows' egne
/// skjerminnstillinger gjør.
/// </summary>
public static class Display
{
    private const int CurrentSettings = -1; // ENUM_CURRENT_SETTINGS
    private const uint FieldsMode = 0x40000 | 0x80000 | 0x100000 | 0x400000; // BITSPERPEL, PELSWIDTH, PELSHEIGHT, DISPLAYFREQUENCY

    public static DisplayMode? Current() =>
        Read(CurrentSettings, out var mode) ? Normal(mode) : null;

    /// <summary>Alle modi skjermen støtter med 32-bits farger, i nåværende retning, størst først.</summary>
    public static IReadOnlyList<DisplayMode> Modes()
    {
        var current = Current();
        var modes = new HashSet<DisplayMode>();
        for (var i = 0; Read(i, out var mode); i++)
        {
            if (mode.BitsPerPel != 32 || mode.DisplayFrequency <= 1)
            {
                continue;
            }

            var m = Normal(mode);
            // EnumDisplaySettings kan gi modi i skjermens egen retning (Legion Go-skjermen er stående); vi viser dem
            // i retningen brukeren har.
            if (current is { } c && (c.Width > c.Height) != (m.Width > m.Height))
            {
                m = new DisplayMode(m.Height, m.Width, m.Hz);
            }

            modes.Add(m);
        }

        return [.. modes.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Hz)];
    }

    /// <summary>Tester og setter modusen. Returnerer feiltekst, eller null hvis det gikk.</summary>
    public static string? Apply(DisplayMode target)
    {
        if (!Read(CurrentSettings, out var mode))
        {
            return "Fant ikke skjermen";
        }

        // Gjeldende modus har bredde og høyde i dreid retning, og retningen beholdes; målet er i samme retning.
        mode.PelsWidth = (uint)target.Width;
        mode.PelsHeight = (uint)target.Height;
        mode.DisplayFrequency = (uint)target.Hz;
        mode.BitsPerPel = 32;
        mode.Fields |= FieldsMode;
        mode.Size = (ushort)Marshal.SizeOf<DevMode>();

        var test = DisplayNative.ChangeDisplaySettingsExW(null, ref mode, 0, 0x2 /* CDS_TEST */, 0);
        if (test != 0)
        {
            return $"Skjermen støtter ikke {target.Width}×{target.Height} {target.Hz} Hz (kode {test})";
        }

        var result = DisplayNative.ChangeDisplaySettingsExW(null, ref mode, 0, 0x1 /* CDS_UPDATEREGISTRY */, 0);
        return result switch
        {
            0 => null,
            1 => "Endringen krever omstart",
            _ => $"Windows avslo endringen (kode {result})",
        };
    }

    private static bool Read(int index, out DevMode mode)
    {
        mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
        return DisplayNative.EnumDisplaySettingsW(null, index, ref mode);
    }

    private static DisplayMode Normal(DevMode mode) =>
        new((int)mode.PelsWidth, (int)mode.PelsHeight, (int)mode.DisplayFrequency);
}

/// <summary>DEVMODEW (220 byte) med bare feltene vi bruker; resten av minnet beholdes slik Windows fylte det inn.</summary>
[StructLayout(LayoutKind.Explicit, Size = 220)]
internal struct DevMode
{
    [FieldOffset(68)] public ushort Size;
    [FieldOffset(72)] public uint Fields;
    [FieldOffset(168)] public uint BitsPerPel;
    [FieldOffset(172)] public uint PelsWidth;
    [FieldOffset(176)] public uint PelsHeight;
    [FieldOffset(184)] public uint DisplayFrequency;
}

internal static partial class DisplayNative
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DevMode devMode);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ChangeDisplaySettingsExW(string? deviceName, ref DevMode devMode, nint hwnd, uint flags, nint param);
}
