# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 The GameHome contributors
#
# Røyktest av GameHome på Legion Go, kjørt i brukerens skrivebordsøkt (ikke over SSH direkte: SSH ser ikke skrivebordet).
# Bruker samme veier som brukeren: hurtigtaster med SendInput og knappetrykk med UI Automation. Leser forgrunnsvindu,
# skjermmodus, Handheld Companions vinduer og GameHome-loggen.
#
# Tester ikke det som krever fysiske knapper (at HC gjør M2/Legion om til hurtigtaster), kontrollernavigasjon i Home
# eller at TDP faktisk endres. Tvangsavslutter aldri et spill; svarer ikke spillet på «Avslutt», meldes det som feil.
#
# Eksempel (fra utviklermaskinen): scripts/Invoke-LegionGoTest.ps1, eller på Legion Go:
#   powershell -ExecutionPolicy Bypass -File Test-LegionGo.ps1 -Game Broforce
[CmdletBinding()]
param(
    [string] $Game = 'Broforce',
    [string] $Report = (Join-Path $env:TEMP 'gamehome-test.txt')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic; using System.Threading;
public static class GhTest {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc e, IntPtr l);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [StructLayout(LayoutKind.Explicit, Size = 220)] struct DevMode {
        [FieldOffset(68)] public ushort Size; [FieldOffset(172)] public uint W; [FieldOffset(176)] public uint H; [FieldOffset(184)] public uint Hz; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string n, int i, ref DevMode m);

    public static uint ForegroundPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
    public static string Mode() { var m = new DevMode(); m.Size = 220; EnumDisplaySettingsW(null, -1, ref m); return m.W + "x" + m.H + "@" + m.Hz; }
    public static string VisibleTitles(int pid) {
        var r = new List<string>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) { var s = new StringBuilder(256); GetWindowText(h, s, 256); r.Add(s.ToString()); } return true; }, IntPtr.Zero);
        return string.Join("|", r);
    }
    // Ctrl+Alt+Shift+<tast>, holdt 60 ms (som GameHome og HC trenger).
    public static void Chord(byte key) {
        byte[] k = { 0xA2, 0xA4, 0xA0, key };
        foreach (var v in k) keybd_event(v, (byte)MapVirtualKey(v, 0), 0, IntPtr.Zero);
        Thread.Sleep(60);
        for (int i = k.Length - 1; i >= 0; i--) keybd_event(k[i], (byte)MapVirtualKey(k[i], 0), 2, IntPtr.Zero);
    }
}
'@

$results = New-Object System.Collections.Generic.List[string]
$failed = 0
function Pass([string] $what) { $results.Add("OK    $what") }
function Fail([string] $what) { $script:failed++; $results.Add("FEIL  $what") }
function Note([string] $what) { $results.Add("      $what") }
function Check([bool] $ok, [string] $what) { if ($ok) { Pass $what } else { Fail $what } }

function Wait-Until([scriptblock] $condition, [int] $seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $condition) { return $true }; Start-Sleep -Milliseconds 250 } while ((Get-Date) -lt $deadline)
    return $false
}

$AE = [System.Windows.Automation.AutomationElement]
function GameHomeWindow {
    $p = Get-Process GameHome -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}
function Find([string] $automationId) {
    $w = GameHomeWindow; if (-not $w) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)
    $e = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($e -and -not $e.Current.IsOffscreen) { $e } else { $null }
}
function FindByName([string] $name) {
    $w = GameHomeWindow; if (-not $w) { return $null }
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $e = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($e -and -not $e.Current.IsOffscreen) { $e } else { $null }
}
function Press($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function GameHomeInFront { $p = Get-Process GameHome -ErrorAction SilentlyContinue; $p -and [GhTest]::ForegroundPid() -eq $p.Id }
$logFile = Join-Path $env:LOCALAPPDATA 'Packages\GameHome_80y3cyz35a8xa\LocalState\gamehome.log'
$logStart = if (Test-Path $logFile) { (Get-Content $logFile).Count } else { 0 }

$originalMode = [GhTest]::Mode()
$gameProcess = $null
try {
    # ---- Grunnlag ----
    $gh = Get-Process GameHome -ErrorAction SilentlyContinue
    Check ([bool]$gh) 'GameHome kjører'
    if (-not $gh) { throw 'GameHome kjører ikke' }
    $hc = Get-Process HandheldCompanion -ErrorAction SilentlyContinue
    Check ([bool]$hc) 'Handheld Companion kjører'
    $hotkeys = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'HandheldCompanion\hotkeys') -Filter 'HOTKEY_USER*.json' -ErrorAction SilentlyContinue |
        ForEach-Object { (Get-Content $_.FullName -Raw | ConvertFrom-Json).Name } | Where-Object { $_ -like 'GameHome*' }
    Check (@($hotkeys).Count -eq 3) "HC har GameHome-hurtigtastene ($(@($hotkeys) -join ', '))"
    Note "Skjerm før test: $originalMode"

    # Låst skjerm: hurtigtaster og forgrunn virker ikke, og testen ville startet et spill ingen ser.
    if (Get-Process LogonUI -ErrorAction SilentlyContinue) {
        throw 'Legion Go er låst. Lås den opp og kjør testen igjen.'
    }

    # Start fra Home: lukk paneler som står åpne fra før (søk/bibliotek, melding).
    # Bare knapper som alltid betyr «lukk» trykkes. En åpen melding kan ha to valg som begge gjør noe (for eksempel
    # «Spill her» og «Stream fra PC-en», 2026-11-05); da stopper testen i stedet for å gjette.
    if (Find 'MessagePrimary') { throw 'En melding er åpen i GameHome. Lukk den og kjør testen igjen.' }
    foreach ($id in 'FindCloseButton', 'LibraryCloseButton', 'PowerCancelButton') {
        if ($b = Find $id) { Note "Lukker åpent panel ($id)"; Press $b; Start-Sleep 1 }
    }

    # ---- Home-hurtigtasten uten spill ----
    if (-not (Find 'ResumeButton')) {
        [GhTest]::ShowWindow($gh.MainWindowHandle, 6) | Out-Null # SW_MINIMIZE
        Start-Sleep 1
        [GhTest]::Chord(0x7A) # F11
        Check (Wait-Until { GameHomeInFront } 5) 'Ctrl+Alt+Shift+F11 henter Home fram fra minimert'
    } else {
        Note 'Et spill kjørte allerede; hopper over Home-testen'
    }

    # ---- M2 uten spill: GameHome-menyen ----
    [GhTest]::Chord(0x7B) # F12
    Check (Wait-Until { Find 'MenuAccountsButton' } 5) 'M2 uten spill åpner GameHome-menyen'
    if ($b = Find 'MenuAccountsButton') {
        Press $b
        Check (Wait-Until { Find 'AccountsCloseButton' } 3) '«Kontoer og kilder» åpner'
        if ($b = Find 'AccountsCloseButton') { Press $b; Start-Sleep 1 }
    }
    [GhTest]::Chord(0x7B)
    Check (Wait-Until { -not (Find 'MenuAccountsButton') } 5) 'M2 en gang til lukker menyen'

    # ---- Start spillet ----
    # Flisen kan ligge utenfor skjermen lenger ut i raden: finn den blant alle, gi den fokus (WinUI ruller den fram).
    $w = GameHomeWindow
    $tileCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Game)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $tile = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tileCond) |
        Where-Object { $_.Current.AutomationId -like 'game:*' } | Select-Object -First 1
    if (-not $tile) { throw "Fant ikke flisen «$Game» i Home" }
    $tile.SetFocus()
    Start-Sleep 1
    $before = @(Get-Process | ForEach-Object Id)
    Press $tile
    Start-Sleep 1
    if ($primary = Find 'MessagePrimary') { Note "Melding: «$($primary.Current.Name)» trykkes"; Press $primary }
    $started = Wait-Until { -not (GameHomeInFront) -and [GhTest]::ForegroundPid() -notin $before } 90
    Check $started "«$Game» startet og fikk forgrunnen"
    if (-not $started) { throw 'Spillet startet ikke' }
    $gameProcess = Get-Process -Id ([GhTest]::ForegroundPid())
    Note "Spillets prosess: $($gameProcess.ProcessName) ($($gameProcess.Id))"
    Start-Sleep 5

    # ---- M2 (meny under spill) ----
    [GhTest]::Chord(0x7B) # F12
    Check (Wait-Until { (GameHomeInFront) -and (Find 'ResumeButton') } 5) 'Ctrl+Alt+Shift+F12 åpner «Kjører nå» foran spillet'

    # ---- Hz ----
    $mode = [GhTest]::Mode(); $hz = [int]($mode -split '@')[1]
    $otherHz = if ($hz -eq 144) { '60 Hz' } else { '144 Hz' }
    if ($b = FindByName $otherHz) {
        Press $b
        $want = ($otherHz -split ' ')[0]
        Check (Wait-Until { [GhTest]::Mode() -like "*@$want" } 5) "Bytter til $otherHz ($([GhTest]::Mode()))"
        Start-Sleep 3
        Check ((GameHomeInFront) -and (Find 'ResumeButton')) 'Menyen er fortsatt framme etter Hz-bytte'
    } else { Fail "Fant ikke knappen «$otherHz»" }

    # ---- Oppløsning ----
    $mode = [GhTest]::Mode(); $size = ($mode -split '@')[0]
    $target = if ($size -eq '1280x800') { '1920×1200' } else { '1280×800' }
    if ($b = FindByName $target) {
        Press $b
        $want = $target -replace '×', 'x'
        Check (Wait-Until { [GhTest]::Mode() -like "$want@*" } 5) "Bytter til $target ($([GhTest]::Mode()))"
        Start-Sleep 3
        Check ((GameHomeInFront) -and (Find 'ResumeButton')) 'Menyen er fortsatt framme etter oppløsningsbytte'
    } else { Fail "Fant ikke knappen «$target»" }

    # ---- QuickTools (TDP) ----
    if ($hc -and ($b = Find 'QuickToolsButton')) {
        Press $b
        Check (Wait-Until { [GhTest]::VisibleTitles($hc.Id) -match 'QuickTools' } 4) '«TDP, lysstyrke og mer» åpner QuickTools i HC'
        if ([GhTest]::VisibleTitles($hc.Id) -match 'QuickTools') {
            [GhTest]::Chord(0x78) # F9: lukker QuickTools igjen
            Start-Sleep 1
        }
    } else { Fail 'Fant ikke QuickTools-knappen (eller HC kjører ikke)' }

    # ---- Tilbake til spillet ----
    if (-not (Find 'ResumeButton')) { [GhTest]::Chord(0x7B); Start-Sleep 2 }
    if ($b = Find 'ResumeButton') {
        Press $b
        Check (Wait-Until { [GhTest]::ForegroundPid() -eq $gameProcess.Id } 5) '«Tilbake til spillet» gir spillet forgrunnen'
    } else { Fail 'Fant ikke «Tilbake til spillet»' }
    Start-Sleep 2

    # ---- Avslutt spillet ----
    [GhTest]::Chord(0x7B)
    if ((Wait-Until { Find 'EndSessionButton' } 5) -and ($b = Find 'EndSessionButton')) {
        Press $b
        # Avslutning av et lokalt spill bekreftes; «Avbryt» har fokus, testen velger «Avslutt».
        if (Wait-Until { Find 'MessagePrimary' } 3) {
            $confirm = Find 'MessagePrimary'
            Check ($confirm.Current.Name -eq 'Avslutt') "Avslutning bekreftes («$($confirm.Current.Name)»)"
            Press $confirm
        } else { Fail 'Ingen bekreftelse før avslutning' }
        Check (Wait-Until { $gameProcess.Refresh(); $gameProcess.HasExited } 30) '«Avslutt spillet» avslutter spillet'
        Check (Wait-Until { (GameHomeInFront) -and -not (Find 'ResumeButton') } 10) 'Home er framme etter at spillet er avsluttet'
    } else { Fail 'Fant ikke «Avslutt spillet»' }

    # ---- Bilder og Steam-biblioteket (krever nøklene i Windows' sikre lager) ----
    # Loggen leses i sin helhet: nøkler og biblioteket hentes én gang per kjøring av GameHome, ikke per test.
    $allLog = { Get-Content $logFile -Encoding UTF8 }
    Check ([bool](& $allLog | Select-String 'SteamGridDB-nøkkel funnet')) 'SteamGridDB-nøkkelen er i bruk'
    if (Find 'SteamLibraryTile') {
        Press (Find 'SteamLibraryTile')
        $found = Wait-Until { & $allLog | Select-String 'Steam-biblioteket: \d+ spill' } 20
        $line = & $allLog | Select-String 'Steam-biblioteket: \d+ spill' | Select-Object -Last 1
        Check $found "«Installer fra Steam» henter biblioteket ($($line.Line -replace '^.*Steam-biblioteket: ', ''))"
        if ($b = Find 'FindCloseButton') { Press $b }
    } else { Fail '«Installer fra Steam» vises ikke (mangler Steam Web API-nøkkel eller Steam-ID?)' }
    Start-Sleep 1

    # ---- Home («Fortsett») og Biblioteket ----
    # Paneler uten egen rolle vises ikke i UI Automation; flisene skilles i stedet på om de er aktive. Mens biblioteket
    # er åpent, er Home-flisene slått av og bibliotekets fliser på.
    function AllButtons {
        $w = GameHomeWindow
        @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))))
    }
    function ActiveGameTiles { @(AllButtons | Where-Object { $_.Current.AutomationId -like 'game:*' -and $_.Current.IsEnabled }) }
    function TabNames { @(AllButtons | Where-Object { $_.Current.IsEnabled -and $_.Current.AutomationId -notlike 'game:*' -and $_.Current.Name -match '^.+ \(\d+\)$' -and $_.Current.Name -notlike 'Bibliotek*' } | ForEach-Object { $_.Current.Name }) }
    $homeCount = (ActiveGameTiles).Count
    Check ($homeCount -ge 1 -and $homeCount -le 10) "Home viser «Fortsett» med $homeCount spill (høyst 10)"
    if ($b = Find 'LibraryTile') {
        Press $b
        Check (Wait-Until { Find 'LibraryCloseButton' } 5) 'Biblioteket åpner'
        Start-Sleep 1
        $tabs = TabNames
        Note "Faner: $($tabs -join ', ')"
        $all = (ActiveGameTiles).Count
        $allTab = $tabs | Where-Object { $_ -match '^Alle \((\d+)\)$' } | Select-Object -First 1
        Check ($allTab -and [int]($allTab -replace '\D', '') -eq $all -and $all -ge $homeCount) "«Alle» viser alle spillene ($all)"
        if ($retroTab = $tabs | Where-Object { $_ -like 'Retro (*' } | Select-Object -First 1) {
            Press (FindByName $retroTab)
            Start-Sleep 1
            $retro = (ActiveGameTiles).Count
            Check ($retro -eq [int]($retroTab -replace '\D', '')) "Fanen «$retroTab» viser $retro spill"
        }
        $sortBefore = (Find 'LibrarySortButton').Current.Name
        Press (Find 'LibrarySortButton')
        Start-Sleep 1
        $sortAfter = (Find 'LibrarySortButton').Current.Name
        Check ($sortAfter -ne $sortBefore) "Sortering byttes («$sortBefore» → «$sortAfter»)"
        # Tilbake slik brukeren hadde det: sorteringen går rundt (fire valg), og «Alle» velges igjen.
        foreach ($i in 1..3) { Press (Find 'LibrarySortButton'); Start-Sleep -Milliseconds 400 }
        if ($allTab -and ($b = FindByName ($tabs | Where-Object { $_ -like 'Alle (*' } | Select-Object -First 1))) { Press $b; Start-Sleep 1 }
        Press (Find 'LibraryCloseButton')
        Check (Wait-Until { -not (Find 'LibraryCloseButton') -and (Find 'LibraryTile') } 5) 'Biblioteket lukkes, Home er tilbake'
    } else { Fail 'Fant ikke Bibliotek-knappen' }
}
catch {
    Fail "Testen stoppet: $($_.Exception.Message)"
}
finally {
    # Skjermen settes tilbake slik den var, uansett utfall.
    if ([GhTest]::Mode() -ne $originalMode) {
        $parts = $originalMode -split '[x@]'
        Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class GhRestore {
    [StructLayout(LayoutKind.Explicit, Size = 220)] struct DevMode {
        [FieldOffset(68)] public ushort Size; [FieldOffset(72)] public uint Fields; [FieldOffset(168)] public uint Bpp;
        [FieldOffset(172)] public uint W; [FieldOffset(176)] public uint H; [FieldOffset(184)] public uint Hz; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string n, int i, ref DevMode m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string n, ref DevMode m, IntPtr h, uint f, IntPtr p);
    public static int Set(uint w, uint h, uint hz) { var m = new DevMode(); m.Size = 220; EnumDisplaySettingsW(null, -1, ref m);
        m.W = w; m.H = h; m.Hz = hz; m.Bpp = 32; m.Fields |= 0x40000 | 0x80000 | 0x100000 | 0x400000; return ChangeDisplaySettingsExW(null, ref m, IntPtr.Zero, 1, IntPtr.Zero); }
}
'@
        $code = [GhRestore]::Set([uint32]$parts[0], [uint32]$parts[1], [uint32]$parts[2])
        Note "Skjermen satt tilbake til $originalMode (kode $code)"
    }

    if ($gameProcess -and -not $gameProcess.HasExited) {
        Note "MERK: $($gameProcess.ProcessName) kjører fortsatt. Testen tvangsavslutter aldri; avslutt det selv."
    }

    $log = if (Test-Path $logFile) { Get-Content $logFile | Select-Object -Skip $logStart } else { @() }
    $lines = @("GameHome-test $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), spill: $Game", '') + $results +
        @('', "$failed feil", '', '---- GameHome-logg under testen ----') + $log
    $lines | Set-Content $Report -Encoding UTF8
    $lines | Select-Object -First ($results.Count + 4)
}
