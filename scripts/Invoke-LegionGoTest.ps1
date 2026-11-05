# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 The GameHome contributors
#
# Kjører Test-LegionGo.ps1 på Legion Go fra utviklermaskinen over SSH. Testen må kjøre i brukerens skrivebordsøkt, så den
# startes som en planlagt oppgave for den innloggede brukeren (krever at Legion Go er låst opp), og rapporten hentes etterpå.
[CmdletBinding()]
param(
    [string] $HostName = 'legion-go',
    [string] $Game = 'Broforce',
    [int] $TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Test-LegionGo.ps1'
scp -q $script "${HostName}:Test-LegionGo.ps1"
if ($LASTEXITCODE -ne 0) { throw "Fikk ikke kopiert testen til $HostName" }

$remote = @"
`$report = Join-Path `$env:TEMP 'gamehome-test.txt'
Remove-Item `$report -ErrorAction SilentlyContinue
`$arg = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + `$HOME + '\Test-LegionGo.ps1" -Game "$Game"'
`$a = New-ScheduledTaskAction -Execute powershell.exe -Argument `$arg
`$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
`$p = New-ScheduledTaskPrincipal -UserId `$env:USERNAME -LogonType Interactive
Register-ScheduledTask -TaskName GameHomeTest -Action `$a -Settings `$s -Principal `$p -Force | Out-Null
Start-ScheduledTask GameHomeTest
`$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Test-Path `$report) -and (Get-Date) -lt `$deadline) { Start-Sleep 2 }
`$state = (Get-ScheduledTask GameHomeTest).State
Unregister-ScheduledTask GameHomeTest -Confirm:`$false
if (Test-Path `$report) { Get-Content `$report } else { "Ingen rapport etter $TimeoutSeconds s (oppgave: `$state). Er Legion Go låst?" }
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remote))
ssh $HostName "powershell -NoProfile -EncodedCommand $encoded"
