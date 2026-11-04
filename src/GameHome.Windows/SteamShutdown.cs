// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;

namespace GameHome.Windows;

/// <summary>
/// Ber Steam lukke seg pent med Steams dokumenterte kommandolinje <c>steam.exe -shutdown</c> (Valve Developer
/// Community, «Command line options (Steam)»: «-shutdown: Shutdown and exit Steam»). Steam avslutter selv, så
/// skylagring og nedlastinger håndteres av Steam. Tenkt for et valgfritt «lukk klienten etter spillet». Tvangslukker aldri.
/// </summary>
public static class SteamShutdown
{
    /// <summary>Om Steam kjører nå.</summary>
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>
    /// Ber Steam lukke seg. Gjør ingenting når Steam ikke kjører (ellers kunne kallet starte Steam). Returnerer true når
    /// forespørselen ble sendt; Steam lukker seg etter hvert, og kan vente på skylagring.
    /// </summary>
    public static bool RequestExit()
    {
        if (!IsRunning() || SteamClient.FindRoot() is not { } root)
        {
            return false;
        }

        var steam = Path.Combine(root, "steam.exe");
        if (!File.Exists(steam))
        {
            return false;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(steam)
            {
                UseShellExecute = false,
                Arguments = "-shutdown",
                WorkingDirectory = root,
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
