# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Installerer en test-MSIX fra CI på Legion Go (eller en annen testmaskin).

.DESCRIPTION
  Første gang: stoler på utviklersertifikatet (krever administrator).
  Deretter: installerer eller oppgraderer pakken.
  Merk: Add-AppxPackage virker ikke fra en SSH-økt (0x80070005). Over SSH: kjør skriptet for å stole på
  sertifikatet, og legg .msix-filen på skrivebordet så brukeren installerer den med et trykk.

.PARAMETER MsixPath
  Sti til .msix-filen fra CI-artefaktet "gamehome-msix-x64".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $MsixPath,
    [string] $CerPath
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot er tom i standardverdier for parametere i Windows PowerShell 5.1; derfor settes stien her.
if (-not $CerPath) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $CerPath = Join-Path $scriptDir '..\certs\GameHome-Dev.cer'
}
$cer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $CerPath
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cer.Thumbprint

if (-not $trusted) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw 'Første installasjon må kjøres som administrator for å stole på sertifikatet.' }
    Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host 'Stoler nå på GameHome Dev-sertifikatet.'
}

Add-AppxPackage -Path $MsixPath -ForceUpdateFromAnyVersion
Write-Host "Installert: $((Get-AppxPackage -Name GameHome).PackageFullName)"
