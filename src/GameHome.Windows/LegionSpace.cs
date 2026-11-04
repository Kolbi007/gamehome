// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;

namespace GameHome.Windows;

/// <summary>
/// Ser om hovedappen til Legion Space (LegionSpace.exe) kjører. Mens den kjører (også i bakgrunnen etter at vinduet er
/// lukket), tar den forgrunnen når Moonlight eller et spill bytter vindu (målt 2026-11-02 og 2026-11-04).
/// GameHome lukker den aldri selv: å avslutte prosessen med makt etterlot kontrollerne uten data til Legion Space ble
/// startet igjen (målt 2026-11-04). Bare «Avslutt» fra Legion Space-ikonet i oppgavelinjen er trygt.
/// </summary>
public static class LegionSpace
{
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("LegionSpace");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
