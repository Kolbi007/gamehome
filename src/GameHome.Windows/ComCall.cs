// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Små hjelpere for å kalle dokumenterte COM-grensesnitt (Core Audio, WMI) direkte via vtabellen. Plassene
/// (slot-numrene) følger rekkefølgen i Windows SDK-headerne (endpointvolume.h, mmdeviceapi.h, wbemcli.h).
/// </summary>
internal static unsafe partial class ComCall
{
    public const int SOk = 0;

    /// <summary>Funksjonspekeren på plass <paramref name="slot"/> i objektets vtabell.</summary>
    public static void* Slot(nint obj, int slot) => (*(void***)obj)[slot];

    public static void Release(nint obj)
    {
        if (obj != 0)
        {
            Marshal.Release(obj);
        }
    }

    public static nint Create(Guid clsid, Guid iid)
    {
        return CoCreateInstance(clsid, 0, 0x1 /* INPROC_SERVER */ | 0x4 /* LOCAL_SERVER */, iid, out var pointer) == SOk ? pointer : 0;
    }

    /// <summary>VARIANT slik den ligger i minnet på x64 (24 byte): typen først, verdien fra byte 8.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct Variant
    {
        public const ushort VtI4 = 3;
        public const ushort VtBstr = 8;
        public const ushort VtUi1 = 17;

        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public int Int;
        [FieldOffset(8)] public byte Byte;
        [FieldOffset(8)] public nint Pointer;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint pointer);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoSetProxyBlanket(nint proxy, uint authnSvc, uint authzSvc, nint serverPrincName, uint authnLevel,
        uint impLevel, nint authInfo, uint capabilities);

    [LibraryImport("oleaut32.dll")]
    public static partial int VariantClear(ref Variant variant);
}
