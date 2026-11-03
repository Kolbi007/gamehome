// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.ComponentModel;
using System.Diagnostics;

namespace GameHome.Windows;

/// <summary>
/// Lukker hovedappen til Legion Space (LegionSpace.exe). Mens den kjører, tar den forgrunnen hver gang Moonlight eller et
/// spill bytter vindu, og kontrolleren går til den (målt 2026-11-02: alt virket til Legion Space ble åpnet). Kjører som
/// vanlig bruker (asInvoker), så GameHome har lov. LegionSettingMenu (Legion-knappene og hurtigmenyen) røres ikke.
/// Innstillingene til Legion Space (TDP, kontroller) lagres utenfor appen og blir værende.
/// </summary>
public static class LegionSpaceCloser
{
    private const string ProcessName = "LegionSpace";

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>Avslutter alle LegionSpace-prosessene. Gir antallet som ble avsluttet; feil meldes til <paramref name="log"/>.</summary>
    public static int Close(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var closed = 0;
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    closed++;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    log($"Legion Space (PID {process.Id}) kunne ikke lukkes: {ex.Message}");
                }
            }
        }

        return closed;
    }
}
