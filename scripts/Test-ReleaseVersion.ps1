# SPDX-License-Identifier: GPL-3.0-or-later
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Get-NextReleaseVersion.ps1'
function Equal($expected, $actual) {
    if ($expected -ne $actual) { throw "Expected $expected, got $actual" }
}
Equal 'v0.2.23' (& $script -Tags @('v0.2.22', 'v0.2.19', 'other'))
Equal 'v0.3.1' (& $script -Tags @('v0.2.22', 'v0.3.0'))
Equal 'v0.2.31' (& $script -Tags @('v0.2.22') -MinimumVersion '0.2.30.1')
Equal 'v0.3.0' (& $script -Tags @('v0.2.65535'))
Equal 'v1.0.0' (& $script -Tags @('v0.65535.65535'))
Equal 'v0.3.0' (& $script -Tags @('v0.2.22', 'v0.3.0') -RequestedTag 'v0.3.0')
$rejected = $false
try { & $script -Tags @('v0.3.0') -RequestedTag 'v0.2.23' } catch { $rejected = $true }
if (-not $rejected) { throw 'Accepted a Stable downgrade' }
$rejected = $false
try { & $script -MinimumVersion '0.2.65536' } catch { $rejected = $true }
if (-not $rejected) { throw 'Accepted an invalid MSIX component' }
'Release version tests passed (8 cases)'
