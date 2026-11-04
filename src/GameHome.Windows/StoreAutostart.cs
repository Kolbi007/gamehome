// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace GameHome.Windows;

/// <summary>Butikkklientene autostart-sjekken kjenner igjen.</summary>
public enum StoreClient { Steam, Epic, GogGalaxy, BattleNet, EaApp, UbisoftConnect, Xbox }

/// <summary>Én oppføring som starter med Windows: navnet, kommandoen, hvor den står og om den er slått på.</summary>
/// <param name="Location">For eksempel <c>HKCU\...\Run</c> eller Oppstart-mappen.</param>
public sealed record AutostartEntry(string Name, string Command, string Location, bool Enabled);

/// <summary>Status for én butikkklient: om den starter med Windows, om den kjører nå og hvor mye minne den bruker.</summary>
/// <param name="Autostarts">Klienten har minst én autostart-oppføring.</param>
/// <param name="Enabled">Minst én av oppføringene er slått på (ikke slått av i Oppgavebehandling eller Innstillinger).</param>
/// <param name="WorkingSetMegabytes">Summen av arbeidssettet til alle klientens prosesser, i MB.</param>
public sealed record StoreClientStatus(
    StoreClient Client,
    string DisplayName,
    bool Autostarts,
    bool Enabled,
    bool Running,
    long WorkingSetMegabytes,
    IReadOnlyList<AutostartEntry> Entries);

/// <summary>
/// Leser hvilke butikkklienter som starter med Windows og hvor mye minne de bruker nå. Bare lesing; GameHome endrer
/// aldri autostart, brukeren slår det av selv.
/// <para>
/// Kilder: <c>Run</c>-nøklene i HKCU og HKLM (også <c>WOW6432Node</c>) og Oppstart-mappene for brukeren og alle brukere.
/// Om en oppføring er slått av, står i <c>...\Explorer\StartupApproved\Run</c>, <c>Run32</c> og <c>StartupFolder</c>:
/// første byte med laveste bit satt (0x03) betyr av, 0x02/0x06 eller ingen verdi betyr på. Formatet er ikke dokumentert
/// av Microsoft; det følger åpen kildekode som leser samme data som Oppgavebehandling (for eksempel start-sequencer,
/// <c>src/StartSequencer/Discovery/StartupApproved.cs</c>).
/// </para>
/// <para>
/// Xbox-appen er en pakket app og bruker en pakket oppstartsoppgave, ikke disse listene. Den vises derfor som «starter
/// ikke med Windows» selv om den gjør det.
/// </para>
/// </summary>
public static class StoreAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private static readonly (StoreClient Client, string DisplayName, string[] Executables)[] Clients =
    [
        (StoreClient.Steam, "Steam", ["steam.exe"]),
        (StoreClient.Epic, "Epic Games Launcher", ["EpicGamesLauncher.exe"]),
        (StoreClient.GogGalaxy, "GOG Galaxy", ["GalaxyClient.exe"]),
        (StoreClient.BattleNet, "Battle.net", ["Battle.net.exe"]),
        (StoreClient.EaApp, "EA app", ["EADesktop.exe"]),
        (StoreClient.UbisoftConnect, "Ubisoft Connect", ["upc.exe", "UbisoftConnect.exe"]),
        (StoreClient.Xbox, "Xbox-appen", ["XboxPcApp.exe"]),
    ];

    /// <summary>Status for alle kjente butikkklienter, også de som verken er installert eller kjører.</summary>
    public static IReadOnlyList<StoreClientStatus> Check()
    {
        var entries = Entries();
        return
        [
            .. Clients.Select(c =>
            {
                var own = entries.Where(e => Matches(e, c.Executables)).ToList();
                var (running, megabytes) = Usage(c.Executables);
                return new StoreClientStatus(c.Client, c.DisplayName, own.Count > 0, own.Exists(e => e.Enabled), running, megabytes, own);
            }),
        ];
    }

    /// <summary>Alle autostart-oppføringer fra Run-nøklene og Oppstart-mappene. En nøkkel som ikke kan leses, hoppes over.</summary>
    public static IReadOnlyList<AutostartEntry> Entries()
    {
        var entries = new List<AutostartEntry>();
        ReadRun(entries, RegistryHive.CurrentUser, RunKey, "Run", @"HKCU\" + RunKey);
        ReadRun(entries, RegistryHive.LocalMachine, RunKey, "Run", @"HKLM\" + RunKey);
        ReadRun(entries, RegistryHive.LocalMachine, Run32Key, "Run32", @"HKLM\" + Run32Key);
        ReadFolder(entries, Environment.SpecialFolder.Startup, RegistryHive.CurrentUser);
        ReadFolder(entries, Environment.SpecialFolder.CommonStartup, RegistryHive.LocalMachine);
        return entries;
    }

    /// <summary>Om StartupApproved-verdien betyr «på». Mangler verdien, er oppføringen på.</summary>
    public static bool IsApproved(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    private static void ReadRun(List<AutostartEntry> entries, RegistryHive hive, string path, string approvedList, string location)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            using var approved = root.OpenSubKey(ApprovedKey + approvedList);
            foreach (var name in key.GetValueNames())
            {
                try
                {
                    if (key.GetValue(name) is string command)
                    {
                        entries.Add(new AutostartEntry(name, command, location, IsApproved(approved?.GetValue(name) as byte[])));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // En verdi som ikke kan leses, hoppes over.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Nøkkelen kan ikke leses; de andre leses likevel.
        }
    }

    private static void ReadFolder(List<AutostartEntry> entries, Environment.SpecialFolder folder, RegistryHive hive)
    {
        try
        {
            var directory = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var approved = root.OpenSubKey(ApprovedKey + "StartupFolder");
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new AutostartEntry(name, ShortcutText(file), directory, IsApproved(approved?.GetValue(name) as byte[])));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Mappen kan ikke leses; de andre kildene leses likevel.
        }
    }

    // En snarvei (.lnk) lagrer målets sti som tekst (ANSI eller UTF-16). Uten COM: les filen og behold teksten, så
    // programfilnavnet kan kjennes igjen. Store filer er ikke snarveier og leses ikke.
    private static string ShortcutText(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!string.Equals(info.Extension, ".lnk", StringComparison.OrdinalIgnoreCase) || info.Length > 64 * 1024)
            {
                return file;
            }

            var bytes = File.ReadAllBytes(file);
            return file + "\n" + Encoding.Latin1.GetString(bytes) + "\n" + Encoding.Unicode.GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return file;
        }
    }

    private static bool Matches(AutostartEntry entry, string[] executables) =>
        executables.Any(exe =>
            ContainsExecutable(entry.Command, exe)
            || string.Equals(Path.GetFileNameWithoutExtension(entry.Name), Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase));

    // Programfilnavnet som eget ord: etter \, /, " eller starten, og ikke som del av et lengre navn.
    private static bool ContainsExecutable(string text, string exe)
    {
        var index = 0;
        while ((index = text.IndexOf(exe, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 ? '\\' : text[index - 1];
            if (before is '\\' or '/' or '"' or ' ' or '\0' or '\n')
            {
                return true;
            }

            index += exe.Length;
        }

        return false;
    }

    private static (bool Running, long Megabytes) Usage(string[] executables)
    {
        var running = false;
        long bytes = 0;
        foreach (var exe in executables)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        bytes += process.WorkingSet64;
                        running = true;
                    }
                    catch (InvalidOperationException)
                    {
                        // Prosessen avsluttet underveis.
                    }
                }
            }
        }

        return (running, bytes / (1024 * 1024));
    }
}
