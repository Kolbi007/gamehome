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
$logFile = $null
$logStart = 0
$logStartInitialized = $false
$results = New-Object System.Collections.Generic.List[string]
function Note([string] $s) { $results.Add("$(Get-Date -Format HH:mm:ss) $s") }
function GameHome { Get-Process GameHome -ErrorAction SilentlyContinue | Select-Object -First 1 }
function InFront { $g = GameHome; $g -and [GhStress]::ForegroundPid() -eq $g.Id }
function NewLog([int] $from = $logStart) { Get-Content $logFile -Encoding UTF8 | Select-Object -Skip $from }
function HangLines { @(NewLog | Select-String '\[heng\]' | ForEach-Object Line) }
function Get-PhaseStatus([string] $phase, [bool] $stopped = $false) {
    $g = GameHome
    $hangs = HangLines
    if (-not $g) { Note "${phase}: GameHome-prosess mangler, heng-linjer=$($hangs.Count)" }
    else { Note "${phase}: GameHome svarer=$($g.Responding), CPU totalt=$([int]$g.CPU) s, minne=$([int]($g.WorkingSet64 / 1MB)) MB, heng-linjer=$($hangs.Count)" }
    $hangs | ForEach-Object { Note "   $_" }
    if ($stopped -or -not $g) { return 'FEILET' }
    if ($hangs.Count -gt 0 -or -not $g.Responding) { return 'HENG' }
    return 'OK'
}

$testGame = $null
$runFailed = $true
$status1 = 'IKKE KJØRT'
$status2 = 'IKKE KJØRT'
$status3 = 'IKKE KJØRT'
try {
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
    $logStart = (Get-Content $logFile -Encoding UTF8).Count
    $logStartInitialized = $true
    if (-not (GameHome)) { throw 'GameHome kjører ikke' }
    if ((Get-CimInstance Win32_Battery).BatteryStatus -ne 2) { Note 'MERK: ikke i lader; trailere spilles ikke, så del 1 og 3 tester lite' }
    [GhStress]::Chord(0x7A) # Home fram
    Start-Sleep 2
    if (-not (InFront)) { throw 'GameHome kom ikke fram (et annet vindu ligger øverst)' }

    # ---- 1. Trailere av og på ----
    $status1 = 'FEILET'
    $end = (Get-Date).AddMinutes($TrailerMinutes)
    $i = 0
    $part1Stopped = $false
    while ((Get-Date) -lt $end) {
        if (-not (InFront)) { Note 'GameHome mistet forgrunnen; del 1 stoppes'; $part1Stopped = $true; break }
        [GhStress]::Tap($(if ($i % 4 -lt 2) { 0x27 } else { 0x25 })) # høyre, høyre, venstre, venstre
        $i++
        Start-Sleep -Milliseconds $(if ($i % 3 -eq 0) { 9000 } else { 5000 }) # av og til lenge nok til at traileren spiller en stund
    }
    $trailers = @(NewLog | Select-String '\[trailer\] (?:(?:Steam|YouTube)-)?trailer spiller').Count
    Note "Del 1: $i fokusbytter, $trailers trailere startet"
    $status1 = Get-PhaseStatus 'Del 1' $part1Stopped

    # ---- 2. Spill avsluttes, skjermen byttes tilbake, traileren starter ----
    $status2 = 'FEILET'
    $part2Stopped = $false
    for ($round = 1; $round -le $GameRounds; $round++) {
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, (GameHome).Id)))
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Game)),
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
        $tile = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | Where-Object { $_.Current.AutomationId -like 'game:*' } | Select-Object -First 1
        if (-not $tile) { Note "Fant ikke flisen «$Game»; del 2 stoppes"; $part2Stopped = $true; break }
        $tile.SetFocus(); Start-Sleep 1
        $roundLogStart = (Get-Content $logFile -Encoding UTF8).Count
        $tile.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 20
        $line = NewLog $roundLogStart | Select-String "Startet «$([regex]::Escape($Game))».*PID (\d+)" | Select-Object -Last 1
        $testGame = if ($line) { Get-Process -Id ([int]$line.Matches[0].Groups[1].Value) -ErrorAction SilentlyContinue }
        if (-not $testGame) { Note "Runde ${round}: spillet startet ikke; del 2 stoppes"; $part2Stopped = $true; break }
        [void]$testGame.CloseMainWindow()
        $closed = $testGame.WaitForExit(20000)
        Note "Runde ${round}: $Game startet og ble lukket pent ($closed)"
        if (-not $closed) { $part2Stopped = $true; Note "Runde ${round}: spillet lukket seg ikke; del 2 stoppes"; break }
        $testGame = $null
        Start-Sleep 30 # Home tilbake, skjermen byttet tilbake, traileren starter etter 3 s og spiller en stund
    }
    $status2 = Get-PhaseStatus 'Del 2' $part2Stopped

    # ---- 3. UI Automation gjennom vinduet mens traileren spiller ----
    $status3 = 'FEILET'
    for ($k = 1; $k -le 5; $k++) {
        [GhStress]::Tap(0x27); Start-Sleep 6 # ny flis, traileren starter
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, (GameHome).Id)))
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $n = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition).Count
        Note "Del 3, runde ${k}: $n elementer på $($sw.ElapsedMilliseconds) ms"
    }
    $status3 = Get-PhaseStatus 'Del 3'
    $runFailed = $status1 -ne 'OK' -or $status2 -ne 'OK' -or $status3 -ne 'OK'
    Note "Resultat: del 1 $status1, del 2 $status2, del 3 $status3"
}
catch {
    $runFailed = $true
    Note "Stresstesten stoppet: $($_.Exception.Message)"
    Note "Delstatus ved avbrudd: del 1 $status1, del 2 $status2, del 3 $status3"
}
finally {
    try {
        if ($testGame -and -not $testGame.HasExited) { [void]$testGame.CloseMainWindow(); Note "Opprydding: lukket $($testGame.ProcessName) pent" }
    }
    catch { Note "Opprydding feilet: $($_.Exception.Message)"; $runFailed = $true }
    $logLines = @()
    if ($logStartInitialized) {
        try { $logLines = @(NewLog | Select-String '\[(heng|trailer|forgrunn)\]' | ForEach-Object Line) }
        catch { Note "Kunne ikke hente logglinjer til rapporten: $($_.Exception.Message)"; $runFailed = $true }
    }
    else { Note 'Loggvedlegg hoppet over fordi kjøringen ikke fikk et gyldig startpunkt' }
    if ($runFailed) { Note 'Resultat: FEILET' }
    else { Note 'Resultat: OK' }
    $results + @('', '---- Trailer-, heng- og forgrunnslinjer ----') + $logLines |
        Set-Content $Report -Encoding UTF8
}

if ($runFailed) { exit 1 }
