# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 The GameHome contributors
#
# Prøver å gjenskape hengene fra 2026-11-05 (GameHome brukte én kjerne for fullt fra en trailer startet). Kjøres på
# Legion Go i brukerens økt (som Test-LegionGo.ps1), i lader (trailere spilles bare da). Tre deler, hver etterfulgt av
# en sjekk av vakthundens logglinjer ([heng]):
#   1. Trailere av og på: fokus flyttes mellom flisene hvert 5. s i 3 minutter.
#   2. Spill avsluttes mens traileren er på vei: spillet startes og lukkes pent tre ganger, med pause mellom.
#   3. UI Automation går gjennom vinduet mens traileren spiller (den første hengen).
# Spillet lukkes alltid pent (som Alt+F4), aldri med tvang.
[CmdletBinding()]
param(
    [string] $Game = 'Broforce',
    [int] $TrailerMinutes = 3,
    [int] $GameRounds = 3,
    [string] $Report = (Join-Path $env:TEMP 'gamehome-stress.txt')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class GhStress {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    public static uint ForegroundPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
    public static void Tap(byte key) { keybd_event(key, (byte)MapVirtualKey(key, 0), 0, IntPtr.Zero); Thread.Sleep(60); keybd_event(key, (byte)MapVirtualKey(key, 0), 2, IntPtr.Zero); }
    public static void Chord(byte key) {
        byte[] k = { 0xA2, 0xA4, 0xA0, key };
        foreach (var v in k) keybd_event(v, (byte)MapVirtualKey(v, 0), 0, IntPtr.Zero);
        Thread.Sleep(60);
        for (int i = k.Length - 1; i >= 0; i--) keybd_event(k[i], (byte)MapVirtualKey(k[i], 0), 2, IntPtr.Zero);
    }
}
'@
$AE = [System.Windows.Automation.AutomationElement]
$logFile = Join-Path $env:LOCALAPPDATA 'Packages\GameHome_80y3cyz35a8xa\LocalState\gamehome.log'
$logStart = (Get-Content $logFile).Count
$results = New-Object System.Collections.Generic.List[string]
function Note([string] $s) { $results.Add("$(Get-Date -Format HH:mm:ss) $s") }
function GameHome { Get-Process GameHome -ErrorAction SilentlyContinue | Select-Object -First 1 }
function InFront { $g = GameHome; $g -and [GhStress]::ForegroundPid() -eq $g.Id }
function NewLog { Get-Content $logFile -Encoding UTF8 | Select-Object -Skip $logStart }
function HangLines { @(NewLog | Select-String '\[heng\]' | ForEach-Object Line) }
function Check([string] $phase) {
    $g = GameHome
    $hangs = HangLines
    Note "${phase}: GameHome svarer=$($g.Responding), CPU totalt=$([int]$g.CPU) s, minne=$([int]($g.WorkingSet64 / 1MB)) MB, heng-linjer=$($hangs.Count)"
    $hangs | ForEach-Object { Note "   $_" }
    return $hangs.Count -eq 0 -and $g.Responding
}

$testGame = $null
try {
    if (-not (GameHome)) { throw 'GameHome kjører ikke' }
    if ((Get-CimInstance Win32_Battery).BatteryStatus -ne 2) { Note 'MERK: ikke i lader; trailere spilles ikke, så del 1 og 3 tester lite' }
    [GhStress]::Chord(0x7A) # Home fram
    Start-Sleep 2
    if (-not (InFront)) { throw 'GameHome kom ikke fram (et annet vindu ligger øverst)' }

    # ---- 1. Trailere av og på ----
    $end = (Get-Date).AddMinutes($TrailerMinutes)
    $i = 0
    while ((Get-Date) -lt $end) {
        if (-not (InFront)) { Note 'GameHome mistet forgrunnen; del 1 stoppes'; break }
        [GhStress]::Tap($(if ($i % 4 -lt 2) { 0x27 } else { 0x25 })) # høyre, høyre, venstre, venstre
        $i++
        Start-Sleep -Milliseconds $(if ($i % 3 -eq 0) { 9000 } else { 5000 }) # av og til lenge nok til at traileren spiller en stund
    }
    $trailers = @(NewLog | Select-String '\[trailer\] Trailer spiller').Count
    Note "Del 1: $i fokusbytter, $trailers trailere startet"
    $ok1 = Check 'Del 1'

    # ---- 2. Spill avsluttes, skjermen byttes tilbake, traileren starter ----
    for ($round = 1; $round -le $GameRounds; $round++) {
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, (GameHome).Id)))
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Game)),
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
        $tile = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | Where-Object { $_.Current.AutomationId -like 'game:*' } | Select-Object -First 1
        if (-not $tile) { Note "Fant ikke flisen «$Game»; del 2 stoppes"; break }
        $tile.SetFocus(); Start-Sleep 1
        $tile.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 20
        $line = NewLog | Select-String "Startet «$([regex]::Escape($Game))».*PID (\d+)" | Select-Object -Last 1
        $testGame = if ($line) { Get-Process -Id ([int]$line.Matches[0].Groups[1].Value) -ErrorAction SilentlyContinue }
        if (-not $testGame) { Note "Runde ${round}: spillet startet ikke; del 2 stoppes"; break }
        [void]$testGame.CloseMainWindow()
        $closed = $testGame.WaitForExit(20000)
        Note "Runde ${round}: $Game startet og ble lukket pent ($closed)"
        $testGame = $null
        Start-Sleep 30 # Home tilbake, skjermen byttet tilbake, traileren starter etter 3 s og spiller en stund
    }
    $ok2 = Check 'Del 2'

    # ---- 3. UI Automation gjennom vinduet mens traileren spiller ----
    for ($k = 1; $k -le 5; $k++) {
        [GhStress]::Tap(0x27); Start-Sleep 6 # ny flis, traileren starter
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, (GameHome).Id)))
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $n = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition).Count
        Note "Del 3, runde ${k}: $n elementer på $($sw.ElapsedMilliseconds) ms"
    }
    $ok3 = Check 'Del 3'
    Note "Resultat: del 1 $(if ($ok1) { 'ok' } else { 'HENG' }), del 2 $(if ($ok2) { 'ok' } else { 'HENG' }), del 3 $(if ($ok3) { 'ok' } else { 'HENG' })"
}
catch {
    Note "Stresstesten stoppet: $($_.Exception.Message)"
}
finally {
    if ($testGame -and -not $testGame.HasExited) { [void]$testGame.CloseMainWindow(); Note "Opprydding: lukket $($testGame.ProcessName) pent" }
    $results + @('', '---- Trailer-, heng- og forgrunnslinjer ----') + @(NewLog | Select-String '\[(heng|trailer|forgrunn)\]' | ForEach-Object Line) |
        Set-Content $Report -Encoding UTF8
}
