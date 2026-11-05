# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 The GameHome contributors
#
# Flytter API-nøkler fra utviklermaskinen til GameHome på Legion Go uten at de vises noe sted. Legg nøkkelen i en fil i
# %USERPROFILE%\.gamehome-secrets med navnet GameHome bruker (se Secrets.cs), for eksempel steamgriddb.txt. Skriptet
# kopierer filene til appens LocalState over SSH; GameHome flytter dem inn i Windows' sikre lager ved neste bruk og sletter
# filen. Filene her slettes også etter kopiering, med mindre -Keep er satt.
[CmdletBinding()]
param(
    [string] $HostName = 'legion-go',
    [string] $Folder = (Join-Path $env:USERPROFILE '.gamehome-secrets'),
    [switch] $Keep
)

$ErrorActionPreference = 'Stop'
$names = 'steamgriddb', 'igdb-client-id', 'igdb-client-secret', 'steam-webapi-key', 'steam-id'
$target = 'AppData/Local/Packages/GameHome_80y3cyz35a8xa/LocalState/'
$sent = @()
foreach ($name in $names) {
    $file = Join-Path $Folder "$name.txt"
    if (-not (Test-Path $file) -or -not (Get-Content $file -Raw).Trim()) { continue }
    scp -q $file "${HostName}:$target$name.txt"
    if ($LASTEXITCODE -ne 0) { throw "Fikk ikke kopiert $name til $HostName" }
    $sent += $name
    if (-not $Keep) { Remove-Item $file }
}

if ($sent) { Write-Host "Kopiert til GameHome på ${HostName}: $($sent -join ', '). Start GameHome på nytt for å ta dem i bruk." }
else { Write-Host "Fant ingen nøkkelfiler i $Folder ($($names -join ', ') + .txt)." }
