// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime;
using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Gir minne tilbake mens et spill kjører: rydder .NET-haugen og ber Windows flytte GameHomes sider ut av arbeidsminnet
/// (de ligger i ventelisten eller sidefilen og hentes inn igjen når Home vises). Spillet får mer RAM; Home tar et
/// øyeblikk lenger første gang etter retur.
/// </summary>
public static class MemoryTrim
{
    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        _ = TrimNative.SetProcessWorkingSetSizeEx(TrimNative.GetCurrentProcess(), -1, -1, 0);
    }
}

internal static partial class TrimNative
{
    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessWorkingSetSizeEx(nint process, nint minimum, nint maximum, uint flags);
}
