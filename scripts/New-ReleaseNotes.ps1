# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Tag,
    [string] $PreviousTag = '',
    [string[]] $Subjects = @()
)

$ErrorActionPreference = 'Stop'
$changes = @($Subjects | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($changes.Count -eq 0) {
    $range = if ($PreviousTag) { "$PreviousTag..$Tag" } else { $Tag }
    $changes = @(git log $range --no-merges --pretty=format:'%s' -- . ':!docs' ':!README.md' |
        ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw "Could not read changes for $Tag" }
}
if ($changes.Count -eq 0) { $changes = @('Maintenance and release updates.') }

$highlights = @($changes | Where-Object {
    $_ -match '(?i)menu|library|game|stream|launch|controller|display|performance|update|recover|scan|session|settings|retro|playstation|trailer'
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
    'Beta releases can be installed from GameHome by selecting Beta in the power menu and checking for updates.'
) -join "`n"

$body | Set-Content -LiteralPath 'release-notes.md' -Encoding utf8
