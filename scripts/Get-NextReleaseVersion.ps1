# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [string[]] $Tags = @(),
    [string] $MinimumVersion = '0.2.0',
    [string] $RequestedTag = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($MinimumVersion)) { $MinimumVersion = '0.2.0' }
function Parse-Version([string] $text) {
    if ($text -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Invalid package version: $text" }
    $parts = @($text.Split('.') | ForEach-Object { [int]$_ })
    if (@($parts | Where-Object { $_ -gt 65535 }).Count) { throw "Package version exceeds MSIX bounds: $text" }
    if ($parts.Count -eq 3) { $parts += 0 }
    return [version]::new($parts[0], $parts[1], $parts[2], $parts[3])
}
$highest = Parse-Version $MinimumVersion
foreach ($tag in $Tags) {
    if ($tag -eq $RequestedTag -or $tag -notmatch '^v\d+\.\d+\.\d+$') { continue }
    $candidate = Parse-Version $tag.Substring(1)
    if ($candidate -gt $highest) { $highest = $candidate }
}
if ($RequestedTag) {
    if ($RequestedTag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Stable tags must be vX.Y.Z' }
    if ((Parse-Version $RequestedTag.Substring(1)) -le $highest) {
        throw "Stable version must be newer than $highest"
    }
    return $RequestedTag
}
$major = $highest.Major
$minor = $highest.Minor
$build = $highest.Build + 1
if ($build -gt 65535) { $build = 0; $minor++ }
if ($minor -gt 65535) { $minor = 0; $major++ }
if ($major -gt 65535) { throw 'No higher MSIX version is available' }
"v$major.$minor.$build"
