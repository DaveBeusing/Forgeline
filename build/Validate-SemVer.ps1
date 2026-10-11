[CmdletBinding()]
param([string]$BaseRef, [string]$HeadRef = 'HEAD')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
function Read-VersionAt([string]$Ref) {
    $content = & git show "${Ref}:Directory.Build.props"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read version at $Ref." }
    return Get-ProductVersion -Content ($content -join "`n")
}
$current = Read-VersionAt $HeadRef
if ($BaseRef -and $BaseRef -notmatch '^0+$') {
    $previous = Read-VersionAt $BaseRef
    Assert-PrVersion -BaseVersion $previous -HeadVersion $current
}
if ($env:FORGELINE_BUILD_VERSION) {
    Assert-BuildVersion -Version $env:FORGELINE_BUILD_VERSION -ProductVersion $current
}
Write-Host "PR version validated: $current (one PATCH per PR, no tags)."
