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
    if ($current -ne $previous) {
        $next = [version]$current
        $prior = [version]$previous
        if (($next.Major -gt $prior.Major -and ($next.Minor -ne 0 -or $next.Build -ne 0)) -or
            ($next.Major -eq $prior.Major -and $next.Minor -gt $prior.Minor -and $next.Build -ne 0)) {
            throw 'Advancing MAJOR/MINOR requires resetting lower SemVer components.'
        }
        $notes = "$PSScriptRoot/../docs/releases/$current.md"
        if (-not (Test-Path -LiteralPath $notes) -or [string]::IsNullOrWhiteSpace((Get-Content $notes -Raw))) {
            throw "A new product target requires reviewed scope/compatibility notes: $notes"
        }
    }
}
# Historical commits retain their original policy; commits do not consume PATCH.
if ($env:FORGELINE_BUILD_VERSION) { Assert-BuildVersion -Version $env:FORGELINE_BUILD_VERSION }
Write-Host "Release configuration validated: $current (no per-commit increment)."
