[CmdletBinding()]
param([string]$BaseRef, [string]$HeadRef = "HEAD")
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/Versioning.ps1"
$current = Get-ProductVersion
if ($BaseRef) {
    $content = & git show "${BaseRef}:Directory.Build.props"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read version at $BaseRef." }
    $previous = Get-ProductVersion -Content ($content -join "`n")
    if ([version]$current -lt [version]$previous) { throw "Version decreases from $previous to $current." }
}
# Historical commits retain their original policy; commits do not consume PATCH.
if ($env:FORGELINE_BUILD_VERSION) { Assert-BuildVersion -Version $env:FORGELINE_BUILD_VERSION }
Write-Host "Release configuration validated: $current (no per-commit increment)."
