# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 The GameHome contributors
#
# Setter opp hurtigtastene i Handheld Companion (HC) som GameHome bruker (ADR 0006 og 0007):
#   M2              -> Ctrl+Alt+Shift+F12 (meny under spill)
#   venstre Legion  -> Ctrl+Alt+Shift+F11 (Home)
#   høyre Legion    -> QuickTools (HCs standard, endres ikke)
#   Ctrl+Alt+Shift+F9 -> QuickTools (knappen «TDP, lysstyrke og mer» i GameHome)
#
# Kjøres på Legion Go mens HC er avsluttet. HC-formatet er internt (JSON-filer i %LOCALAPPDATA%\HandheldCompanion\hotkeys)
# og kan endres i nye HC-versjoner; skriptet er laget for HC 1.3 og tar sikkerhetskopi før det skriver. Ingen kode fra HC
# er brukt; feltnavnene er lest fra HCs egne filer og offentlige kildekode.
[CmdletBinding()]
param(
    [string] $Root = (Join-Path $env:LOCALAPPDATA 'HandheldCompanion'),

    # Bare for test mot en kopi av mappen.
    [switch] $IgnoreRunning
)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $Root 'hotkeys'

if (-not $IgnoreRunning -and (Get-Process HandheldCompanion -ErrorAction SilentlyContinue)) {
    throw 'Handheld Companion kjører. Avslutt den fra ikonet ved klokken (Exit) og kjør skriptet igjen.'
}
if (-not (Test-Path $dir)) {
    throw "Fant ikke $dir. Start Handheld Companion én gang først."
}

$templateFile = Join-Path $dir 'HOTKEY_RESERVED1.json' # QuickTools; gir oss formen på knappelisten
$template = Get-Content $templateFile -Raw | ConvertFrom-Json
if ($template.Version -notlike '1.3.*') {
    Write-Warning "Laget for HC 1.3; filene er fra $($template.Version). Sjekk resultatet i HC etterpå."
}

$backup = Join-Path $Root ("hotkeys.backup-{0:yyyyMMdd-HHmmss}" -f (Get-Date))
Copy-Item $dir $backup -Recurse
Write-Host "Sikkerhetskopi: $backup"

# HCs knappenavn (ButtonFlags) og tallverdier for egne hurtigtaster.
$hotkeyUser0 = 80
$keys = @{ LControlKey = @(162, 29); LMenu = @(164, 56); LShiftKey = @(160, 42); F9 = @(120, 67); F11 = @(122, 87); F12 = @(123, 88) }

function Copy-Json($value) { $value | ConvertTo-Json -Depth 30 | ConvertFrom-Json }

function New-ButtonState([string[]] $pressed) {
    $state = Copy-Json $template.inputsChord.ButtonState
    foreach ($p in $state.State.PSObject.Properties) {
        if ($p.Name -ne '$type') { $p.Value = $false }
    }
    foreach ($name in $pressed) { $state.State.$name = $true }
    $state
}

# Ned i rekkefølge, opp i motsatt rekkefølge, slik GameHome (Keyboard.SendChord) og HC sender dem.
function New-KeyList([string[]] $names) {
    $list = [ordered]@{ '$type' = $template.inputsChord.KeyState.'$type'; '$values' = @() }
    $t = 1
    $events = @()
    foreach ($n in $names) { $events += , @($n, $true) }
    $reversed = $names.Clone(); [array]::Reverse($reversed)
    foreach ($n in $reversed) { $events += , @($n, $false) }
    foreach ($e in $events) {
        $k = $keys[$e[0]]
        $list.'$values' += [ordered]@{
            '$type' = 'HandheldCompanion.Inputs.InputsKey, HandheldCompanion'
            KeyValue = $k[0]; ScanCode = $k[1]; Timestamp = $t++
            IsKeyDown = $e[1]; IsKeyUp = -not $e[1]; IsExtendedKey = $false
        }
    }
    $list
}

function New-Chord([string[]] $buttons, [string[]] $keyNames, [int] $target) {
    [ordered]@{
        '$type' = 'HandheldCompanion.Inputs.InputsChord, HandheldCompanion'
        chordTarget = $target
        ButtonState = New-ButtonState $buttons
        KeyState = $(if ($keyNames) { New-KeyList $keyNames } else { [ordered]@{ '$type' = $template.inputsChord.KeyState.'$type'; '$values' = @() } })
        chordType = 1
    }
}

function Save-Hotkey([int] $slot, [string] $name, $command, $chord) {
    $flag = $hotkeyUser0 + $slot
    $hotkey = [ordered]@{
        '$type' = 'HandheldCompanion.Hotkey, HandheldCompanion'
        ButtonFlags = $flag
        command = $command
        inputsChord = $chord
        IsPinned = $false
        PinIndex = -1
        IsInternal = $false
        Name = $name
        Version = $template.Version
        InputsChordType = 1
    }
    $file = Join-Path $dir "HOTKEY_USER$slot.json"
    $hotkey | ConvertTo-Json -Depth 30 | Set-Content $file -Encoding UTF8
    Write-Host "Skrev $name -> $file"
}

function Keystroke([string[]] $keyNames) {
    [ordered]@{
        '$type' = 'HandheldCompanion.Commands.KeyboardCommands, HandheldCompanion'
        commandType = 2
        deviceType = $null
        outputChord = New-Chord @() $keyNames 1
        KeyPressDelay = 0
    }
}

# Fjern GameHome-hurtigtaster fra en tidligere kjøring.
Get-ChildItem $dir -Filter 'HOTKEY_USER*.json' | Where-Object {
    (Get-Content $_.FullName -Raw | ConvertFrom-Json).Name -like 'GameHome*'
} | Remove-Item

# Venstre Legion-knapp åpnet HC-vinduet; den blir Home i GameHome. HC-vinduet nås fortsatt fra ikonet ved klokken.
$mainWindowFile = Join-Path $dir 'HOTKEY_RESERVED2.json'
$mainWindow = Get-Content $mainWindowFile -Raw | ConvertFrom-Json
$mainWindow.inputsChord.ButtonState.State.OEM2 = $false
$mainWindow | ConvertTo-Json -Depth 30 | Set-Content $mainWindowFile -Encoding UTF8

$used = Get-ChildItem $dir -Filter 'HOTKEY_USER*.json' | ForEach-Object { [int]($_.BaseName -replace 'HOTKEY_USER', '') }
$free = 0..59 | Where-Object { $used -notcontains $_ } | Select-Object -First 3

Save-Hotkey $free[0] 'GameHome: meny (M2)' (Keystroke @('LControlKey', 'LMenu', 'LShiftKey', 'F12')) (New-Chord @('B5') @() 0)
Save-Hotkey $free[1] 'GameHome: Home (venstre Legion)' (Keystroke @('LControlKey', 'LMenu', 'LShiftKey', 'F11')) (New-Chord @('OEM2') @() 0)
Save-Hotkey $free[2] 'GameHome: QuickTools' ([ordered]@{
        '$type' = $template.command.'$type'; commandType = 1; deviceType = $null; PageIndex = 0
    }) (New-Chord @() @('LControlKey', 'LMenu', 'LShiftKey', 'F9') 0)

Write-Host 'Ferdig. Start Handheld Companion igjen.'
