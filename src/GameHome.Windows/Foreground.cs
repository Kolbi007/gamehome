// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Forgrunnsvindu etter Windows' dokumenterte regler: en prosess som har mottatt siste input eller er
/// gitt lov med <c>AllowSetForegroundWindow</c>, kan ta forgrunnen. Ingen omveier som AttachThreadInput.
/// </summary>
public static class Foreground
{
    /// <summary>Gir en annen prosess lov til å ta forgrunnen. Kalles av prosessen som har retten.</summary>
    public static bool AllowFor(uint processId) => NativeMethods.AllowSetForegroundWindow(processId);

    /// <summary>Ber om å få vinduet i forgrunnen. Returnerer false hvis Windows avslår.</summary>
    public static bool Bring(nint windowHandle) => NativeMethods.SetForegroundWindow(windowHandle);
}

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);
}
