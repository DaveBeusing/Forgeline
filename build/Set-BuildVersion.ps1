[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
$product = Get-ProductVersion
$sha = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Full source SHA required.' }
$version = "$product-dev"
if ($env:GITHUB_EVENT_NAME -eq 'pull_request') {
    $event = Get-Content $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
    $version = "$product-pr.$($event.number).$env:GITHUB_RUN_ID.$env:GITHUB_RUN_ATTEMPT"
}
Assert-BuildVersion -Version $version
if ($env:GITHUB_ENV) { "FORGELINE_BUILD_VERSION=$version" >> $env:GITHUB_ENV }
Write-Host "Validation build identity: $version+$sha (no remote writes)."
