// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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

internal static unsafe partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint windowHandle, int id);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(
        nint windowHandle,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> subclassProc,
        nuint subclassId,
        nuint refData);

    [LibraryImport("comctl32.dll")]
    internal static partial nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, nint deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, nint deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFileW(
        string fileName, uint access, uint share, nint securityAttributes, uint creation, uint flags, nint template);
}
