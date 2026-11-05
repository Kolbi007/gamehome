// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace GameHome.Windows;

/// <summary>
/// Skjermtastaturet på Legion Go: vis det når et felt skal fylles ut med kontrolleren, og skjul det igjen etterpå (det ble
/// ellers liggende over Home, 2026-11-05). Synlighet leses med <c>IFrameworkInputPane</c> (dokumentert, shobjidl);
/// av/på gjøres med tastaturets <c>ITipInvocation.Toggle</c> (fra TabTip, brukt bredt men lite dokumentert). Kaster aldri.
/// </summary>
public static partial class TouchKeyboard
{
    private static readonly Guid FrameworkInputPaneClsid = new("D5120AA3-46BA-44C5-822D-CA8092C1FC72");
    private static readonly Guid TipBandClsid = new("4ce576fa-83dc-4F88-951c-9d0782b4e376");
    private const string TabTipExe = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";

    public static bool IsVisible()
    {
        try
        {
            if (Create<IFrameworkInputPane>(FrameworkInputPaneClsid) is { } pane && pane.Location(out var rect) == 0)
            {
                return rect.Bottom - rect.Top > 0;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
        }

        return false;
    }

    /// <summary>Viser tastaturet. Returnerer false hvis det ikke lot seg vise.</summary>
    public static bool Show()
    {
        if (IsVisible())
        {
            return true;
        }

        if (Toggle())
        {
            return true;
        }

        // Tastaturtjenesten kjører ikke ennå: start den (viser tastaturet).
        try
        {
            if (File.Exists(TabTipExe))
            {
                using var _ = Process.Start(new ProcessStartInfo(TabTipExe) { UseShellExecute = true });
                return true;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }

        return false;
    }

    /// <summary>
    /// Skjuler tastaturet og passer på i 1,5 s at det forblir skjult. Tastaturet som er på vei opp, meldes ikke som synlig
    /// før animasjonen er ferdig, så én sjekk rett etter at feltet lukkes, kunne la det bli liggende (2026-11-05, over
    /// startmenyen). Høyst to av/på-trykk, så det aldri blinker av og på.
    /// </summary>
    public static async Task<bool> HideAsync()
    {
        var deadline = Environment.TickCount64 + 1500;
        var toggles = 0;
        while (Environment.TickCount64 < deadline)
        {
            if (IsVisible() && toggles < 2)
            {
                Toggle();
                toggles++;
                await Task.Delay(400);
                continue;
            }

            await Task.Delay(150);
        }

        return !IsVisible();
    }

    private static bool Toggle()
    {
        try
        {
            if (Create<ITipInvocation>(TipBandClsid) is { } tip)
            {
                tip.Toggle(GetDesktopWindow());
                return true;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
        }

        return false;
    }

    private static T? Create<T>(Guid clsid)
        where T : class
    {
        var iid = typeof(T).GUID;
        if (CoCreateInstance(clsid, 0, 0x4 /* CLSCTX_LOCAL_SERVER */ | 0x1 /* INPROC_SERVER */, iid, out var pointer) != 0 || pointer == 0)
        {
            return null;
        }

        try
        {
            return (T)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [GeneratedComInterface]
    [Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
    internal partial interface ITipInvocation
    {
        void Toggle(nint window);
    }

    [GeneratedComInterface]
    [Guid("5752238B-24F0-495A-82F1-2FD593056796")]
    internal partial interface IFrameworkInputPane
    {
        [PreserveSig] int Advise(nint window, nint handler, out uint cookie);
        [PreserveSig] int AdviseWithHWND(nint window, nint handler, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int Location(out Rect rect);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint pointer);

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();
}
