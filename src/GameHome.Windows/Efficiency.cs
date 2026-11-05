// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Effektivitetsmodus (EcoQoS) for GameHome selv mens et spill kjører: Windows kjører prosessen på de mest
/// strømgjerrige kjernene og laveste frekvens, så kraften går til spillet. Dokumentert API: SetProcessInformation med
/// ProcessPowerThrottling (Windows 11). Feiler stille på eldre Windows.
/// </summary>
public static partial class Efficiency
{
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>true: effektivitetsmodus på (spill kjører). false: tilbake til normal (Home skal svare raskt).</summary>
    public static bool Set(bool efficient)
    {
        var state = new PowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed, StateMask = efficient ? ExecutionSpeed : 0 };
        return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(nint process, int informationClass, ref PowerThrottlingState information, uint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
