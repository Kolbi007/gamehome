# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Installerer en test-MSIX fra CI på Legion Go (eller en annen testmaskin).

.DESCRIPTION
  Første gang: stoler på utviklersertifikatet (krever administrator).
  Deretter: installerer eller oppgraderer pakken. Kan kjøres over SSH fra Mac:
    ssh legion-go "pwsh -File C:\gamehome\scripts\Install-DevBuild.ps1 -MsixPath C:\gamehome\drop\GameHome.msix"

.PARAMETER MsixPath
  Sti til .msix-filen fra CI-artefaktet "gamehome-msix-x64".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $MsixPath,
    [string] $CerPath = (Join-Path $PSScriptRoot '..\certs\GameHome-Dev.cer')
)

$ErrorActionPreference = 'Stop'
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
