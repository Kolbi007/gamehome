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
    private readonly List<(int Id, HotkeyModifiers Modifiers, uint Key)> _combos = [];
    private nint _hook;
    private int _lastId = -1;
    private long _lastTicks;

    public unsafe HotkeyListener(nint windowHandle)
    {
        _window = windowHandle;
        s_current = this;
        NativeMethods.SetWindowSubclass(windowHandle, &SubclassProc, 1, 0);
    }

    /// <summary>Kalles på vinduets tråd med id-en til hurtigtasten.</summary>
    public event EventHandler<int>? Pressed;

    /// <summary>Om reserven (tastaturkroken) er aktiv.</summary>
    public bool HookActive => _hook != 0;

    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        var ok = NativeMethods.RegisterHotKey(_window, id, (uint)modifiers | ModNoRepeat, virtualKey);
        if (ok)
        {
            _registered.Add(id);
        }

        _combos.Add((id, modifiers, virtualKey));
        return ok;
    }

    /// <summary>
    /// Reserve under en økt: en tastaturkrok på lavt nivå (WH_KEYBOARD_LL, dokumentert). Noen spill leser tastaturet med
    /// «raw input» og slår av programmers hurtigtaster mens de har fokus (målt 2026-11-02: Assassin's Creed Rogue; M2 kom
    /// aldri fram). Kroken ser tasten før spillet, sender den videre som hurtigtast til GameHome og holder den unna
    /// spillet. Den gjør bare en sammenligning per tast og fjernes når økten slutter.
    /// </summary>
    public unsafe bool StartHook()
    {
        if (_hook == 0)
        {
            _hook = NativeMethods.SetWindowsHookExW(13, &KeyboardProc, NativeMethods.GetModuleHandleW(null), 0); // WH_KEYBOARD_LL
        }

        return _hook != 0;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered)
        {
            NativeMethods.UnregisterHotKey(_window, id);
        }

        _registered.Clear();
        _combos.Clear();
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint subclassId, nuint refData)
    {
        if (message == WmHotkey && s_current is { } listener)
        {
            // Samme hurtigtast fra både kroken og RegisterHotKey innen 400 ms teller én gang.
            var id = (int)wParam;
            var now = Environment.TickCount64;
            if (id != listener._lastId || now - listener._lastTicks > 400)
            {
                listener._lastId = id;
                listener._lastTicks = now;
                listener.Pressed?.Invoke(listener, id);
            }

            return 0;
        }

        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInfo
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [UnmanagedCallersOnly]
    private static unsafe nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        // Må svare raskt: ingen arbeid her utover å sammenligne og poste en melding.
        if (code >= 0 && s_current is { } listener && (wParam == 0x0100 || wParam == 0x0104)) // WM_KEYDOWN / WM_SYSKEYDOWN
        {
            var key = ((KeyboardInfo*)lParam)->VirtualKey;
            var modifiers = HotkeyModifiers.None;
            if (NativeMethods.GetAsyncKeyState(0x11) < 0)
            {
                modifiers |= HotkeyModifiers.Control;
            }

            if (NativeMethods.GetAsyncKeyState(0x12) < 0)
            {
                modifiers |= HotkeyModifiers.Alt;
            }

            if (NativeMethods.GetAsyncKeyState(0x10) < 0)
            {
                modifiers |= HotkeyModifiers.Shift;
            }

            foreach (var (id, wanted, wantedKey) in listener._combos)
            {
                if (wantedKey == key && wanted == modifiers)
                {
                    NativeMethods.PostMessageW(listener._window, WmHotkey, id, 0);
                    return 1; // Holdes unna spillet.
                }
            }
        }

        return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }
}
