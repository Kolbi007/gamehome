// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameHome.Windows;

public enum PowerAction { Sleep, Restart, Shutdown }

/// <summary>
/// Dvale, omstart og avslutning. Omstart og avslutning går via Windows' vanlige avslutning (<c>shutdown.exe</c> uten
/// <c>/f</c>), så spill, Steam og andre programmer får lukke normalt. Aldri tvang.
/// </summary>
public static partial class Power
{
    /// <summary>Utfører valget. Returnerer en feilmelding, eller null hvis Windows tok imot.</summary>
    public static string? Run(PowerAction action)
    {
        try
        {
            switch (action)
            {
                case PowerAction.Sleep:
                    return SetSuspendState(hibernate: false, force: false, disableWakeEvent: false)
                        ? null
                        : $"Dvale ble avslått (feil {Marshal.GetLastPInvokeError()})";
                case PowerAction.Restart:
                    return StartShutdown("/r /t 0");
                case PowerAction.Shutdown:
                    return StartShutdown("/s /t 0");
                default:
                    return "Ukjent valg";
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    private static string? StartShutdown(string arguments)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(system, "shutdown.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        return process is null ? "shutdown.exe startet ikke" : null;
    }

    [LibraryImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
