# SPDX-License-Identifier: GPL-3.0-or-later
<#
.SYNOPSIS
  Lager det faste utviklersertifikatet som signerer test-MSIX fra CI.

.DESCRIPTION
  Kjøres én gang. Den offentlige delen (certs/GameHome-Dev.cer) sjekkes inn i repoet.
  Den private delen (PFX) legges som GitHub-secret og skal ALDRI sjekkes inn:
    gh secret set DEV_CERT_PFX_BASE64 < (base64 av PFX)
    gh secret set DEV_CERT_PASSWORD
  Emnet må være lik Publisher i src/GameHome.App/Package.appxmanifest.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PfxPath,
    [Parameter(Mandatory)] [securestring] $Password,
    [string] $CerPath = (Join-Path $PSScriptRoot '..\certs\GameHome-Dev.cer'),
    [int] $Years = 5
)

$ErrorActionPreference = 'Stop'
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject 'CN=GameHome Dev' `
    -FriendlyName 'GameHome Dev (test signing)' `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA -KeyLength 3072 `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears($Years) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

New-Item -ItemType Directory -Force (Split-Path $CerPath) | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $Password | Out-Null
Export-Certificate -Cert $cert -FilePath $CerPath | Out-Null
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"

Write-Host "Laget $PfxPath (privat, ikke sjekk inn) og $CerPath (offentlig). Thumbprint: $($cert.Thumbprint)"
