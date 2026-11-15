# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Tag,
    [string] $PreviousTag = '',
    [string] $TargetCommit = 'HEAD'
)

$ErrorActionPreference = 'Stop'
$range = if ($PreviousTag) { "$PreviousTag..$TargetCommit" } else { $TargetCommit }
$changes = @(git log $range --no-merges --pretty=format:'%s' |
    ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw "Could not read changes for $Tag" }
if ($changes.Count -eq 0) { $changes = @('Maintenance and release updates.') }

$highlights = @($changes | Where-Object {
    $_ -match '(?i)menu|library|\bgame\b|wow|stream|launch|controller|display|performance|update|recover|scan|session|settings|retro|playstation|trailer|changelog'
} | Select-Object -First 3)
if ($highlights.Count -eq 0) { $highlights = @($changes | Select-Object -First 3) }

$compare = if ($PreviousTag) {
    "https://github.com/$Repository/compare/$PreviousTag...$Tag"
} else {
    "https://github.com/$Repository/commits/$Tag"
}
$body = @(
    '## Highlights'
    ''
    ($highlights | ForEach-Object { "- $_" })
    ''
    '## Changelog'
    ''
    ($changes | ForEach-Object { "- $_" })
    ''
    "[View full changes]($compare)"
    ''
    '## Download'
    ''
    'Choose the x64 package for Legion Go. Choose ARM64 for an ARM Windows device.'
    ''
    'On Legion Go, open the power menu in GameHome, choose System, select the matching update channel, then check for updates.'
) -join "`n"

$body | Set-Content -LiteralPath 'release-notes.md' -Encoding utf8
