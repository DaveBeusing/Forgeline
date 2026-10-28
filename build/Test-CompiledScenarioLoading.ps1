[CmdletBinding()]
param([string]$Configuration = 'Release', [string]$OutputRoot = 'artifacts/scenario-loading')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/ForgeLine.Headless/ForgeLine.Headless.csproj'
$output = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Invoke-Headless([string[]]$Arguments) {
    & dotnet run --project $project --configuration $Configuration --no-build -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Compiled scenario command failed: $Arguments" }
}
foreach ($key in @('central-divide.directorate.v1', 'split-basin.directorate.v1', 'practice.command-recon.v1', 'practice.build-production.v1', 'practice.supply-recovery.v1')) {
    $save = Join-Path $output "$key.save.json"
    $replay = Join-Path $output "$key.replay.json"
    Invoke-Headless @('--scenario', $key, '--profile', 'validation', '--ticks', '20', '--seed', '2026', '--save-output', $save, '--replay-output', $replay)
    Invoke-Headless @('--load-input', $save)
    Invoke-Headless @('--replay-input', $replay)
    $savedPayload = (Get-Content -LiteralPath $save -Raw | ConvertFrom-Json).Payload | ConvertFrom-Json
    if ($savedPayload.SchemaVersion -ne 12 -or $savedPayload.Configuration.CompositionKey -ne $key -or
        $savedPayload.Configuration.Identity.MapSha256.Length -ne 64 -or $savedPayload.Configuration.Identity.ContentSha256.Length -ne 64) {
        throw 'The headless save did not preserve the complete registered scenario identity.'
    }
}
$missingRoot = Join-Path $output ([Guid]::NewGuid().ToString('N'))
foreach ($scenario in @('central-divide', 'split-basin')) {
    $failure = & dotnet run --project $project --configuration $Configuration --no-build -- --scenario $scenario --scenario-root $missingRoot --ticks 1 2>&1
    if ($LASTEXITCODE -ne 1 -or ($failure -join ' ') -notmatch 'Scenario loading failed:') { throw "Missing $scenario data must fail explicitly without a fallback." }
}
Write-Output 'Compiled headless startup, save, replay, and missing-content rejection qualified.'
# The expected failing native command must not become this successful script's process exit status.
exit 0
