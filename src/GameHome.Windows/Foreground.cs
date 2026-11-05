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

    /// <summary>
    /// Gir et spill forgrunnen igjen. Mange spill (for eksempel Unity i fullskjerm) minimerer seg når de mister fokus,
    /// og et minimert vindu blir ikke synlig av <c>SetForegroundWindow</c> alene; det gjenopprettes først.
    /// </summary>
    public static (bool Ok, bool WasMinimized) BringGame(nint windowHandle)
    {
        var minimized = NativeMethods.IsIconic(windowHandle);
        if (minimized)
        {
            NativeMethods.ShowWindow(windowHandle, NativeMethods.SwRestore);
        }

        return (NativeMethods.SetForegroundWindow(windowHandle), minimized);
    }

    /// <summary>
    /// Holder et vindu over andre vinduer uten å ta forgrunnen fra dem. Brukes av lasteskjermen mens Moonlight kobler
    /// til, og slås alltid av igjen etterpå.
    /// </summary>
    public static void SetTopmost(nint windowHandle, bool topmost) =>
        NativeMethods.SetWindowPos(windowHandle, topmost ? -1 : -2, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

    /// <summary>
    /// Legger vinduet bakerst uten å aktivere noe. Når «alltid øverst» slås av, legger Windows vinduet øverst blant de
    /// vanlige vinduene, altså foran spillet (målt 2026-11-02 med Ball x Pit); dette flytter det bak igjen.
    /// </summary>
    public static void SendToBack(nint windowHandle) =>
        NativeMethods.SetWindowPos(windowHandle, 1, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

    /// <summary>
    /// Minimerer et vindu (som Alt+Tab). Når forgrunnsvinduet minimeres, aktiverer Windows neste vindu; brukes når et spill
    /// ikke slipper forgrunnen for M2. <see cref="BringGame"/> gjenoppretter det.
    /// </summary>
    public static void Minimize(nint windowHandle) => NativeMethods.ShowWindow(windowHandle, 6); // SW_MINIMIZE

    /// <summary>Om vinduet er minimert.</summary>
    public static bool IsMinimized(nint windowHandle) => NativeMethods.IsIconic(windowHandle);

    /// <summary>Om vinduet er forgrunnsvinduet.</summary>
    public static bool IsForeground(nint windowHandle) => NativeMethods.GetForegroundWindow() == windowHandle;

    /// <summary>Om prosessen har et synlig toppnivåvindu av klassen, for eksempel Moonlights streamvindu.</summary>
    public static bool HasVisibleWindow(int processId, string className)
    {
        nint window = 0;
        while ((window = NativeMethods.FindWindowExW(0, window, className, null)) != 0)
        {
            if (NativeMethods.GetWindowThreadProcessId(window, out var owner) != 0 && owner == processId
                && NativeMethods.IsWindowVisible(window))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Første synlige toppnivåvindu av klassen som prosessen eier (for eksempel Moonlights streamvindu), eller 0.</summary>
    public static nint FindVisibleWindow(int processId, string className)
    {
        nint window = 0;
        while ((window = NativeMethods.FindWindowExW(0, window, className, null)) != 0)
        {
            if (NativeMethods.GetWindowThreadProcessId(window, out var owner) != 0 && owner == processId
                && NativeMethods.IsWindowVisible(window))
            {
                return window;
            }
        }

        return 0;
    }

    /// <summary>Navnet på prosessen som eier forgrunnsvinduet, for loggen. Null hvis det ikke kan leses.</summary>
    public static string? CurrentProcessName()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0 || NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return null;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

internal static unsafe partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    internal const int SwRestore = 9;

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowExW(nint parent, nint after, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint windowHandle, int command);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowLongW(nint window, int index);

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

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWindowsHookExW(int hookId, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll")]
    internal static partial nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint window, nuint id);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, nint deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, nint deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFileW(
        string fileName, uint access, uint share, nint securityAttributes, uint creation, uint flags, nint template);
}
