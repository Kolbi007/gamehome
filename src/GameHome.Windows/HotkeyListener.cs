// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// Globale hurtigtaster via <c>RegisterHotKey</c> (dokumentert). Windows sender en registrert hurtigtast til oss
/// og ikke til vinduet i forgrunnen, og mottakeren får lov til å ta forgrunnen.
/// Registreres bare mens en økt er aktiv (CLAUDE.md). Én lytter per prosess.
/// </summary>
public sealed class HotkeyListener
{
    private const uint WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static HotkeyListener? s_current;
    private readonly nint _window;
    private readonly HashSet<int> _registered = [];

    public unsafe HotkeyListener(nint windowHandle)
    {
        _window = windowHandle;
        s_current = this;
        NativeMethods.SetWindowSubclass(windowHandle, &SubclassProc, 1, 0);
    }

    /// <summary>Kalles på vinduets tråd med id-en til hurtigtasten.</summary>
    public event EventHandler<int>? Pressed;

    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        var ok = NativeMethods.RegisterHotKey(_window, id, (uint)modifiers | ModNoRepeat, virtualKey);
        if (ok)
        {
            _registered.Add(id);
        }

        return ok;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered)
        {
            NativeMethods.UnregisterHotKey(_window, id);
        }

        _registered.Clear();
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint subclassId, nuint refData)
    {
        if (message == WmHotkey && s_current is { } listener)
        {
            listener.Pressed?.Invoke(listener, (int)wParam);
        }

        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
