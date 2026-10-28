[CmdletBinding()]
param(
    [ValidateSet('Quick', 'Reference')][string]$Mode = 'Quick',
    [string]$OutputRoot = 'artifacts/pacing-benchmarks',
    [string]$BaselineRoot,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pacing/fixtures.json') -Raw | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 1 -or $manifest.Fixtures.Count -gt 8) { throw 'Invalid pacing fixture manifest.' }
$output = [System.IO.Path]::GetFullPath($OutputRoot, $repoRoot)
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        & dotnet build src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release
        if ($LASTEXITCODE -ne 0) { throw 'Headless build failed.' }
    }
    foreach ($fixture in $manifest.Fixtures) {
        if ($fixture.Name -notmatch '^[a-z][a-z0-9-]{0,63}$') { throw 'Invalid fixture name.' }
        $reportPath = Join-Path $output ($fixture.Name + '.json')
        if (Test-Path -LiteralPath $reportPath) { throw "Report already exists: $reportPath. Choose a fresh OutputRoot." }
        $ticks = if ($Mode -eq 'Quick') { $fixture.QuickTicks } else { $fixture.ReferenceTicks }
        $arguments = @('run', '--project', 'src/ForgeLine.Headless/ForgeLine.Headless.csproj', '--configuration', 'Release', '--no-build', '--',
            '--scenario', $fixture.Scenario, '--profile', $fixture.Profile, '--seed', [string]$fixture.Seed,
            '--matches', [string]$fixture.Matches, '--ticks', [string]$ticks, '--telemetry-output', $reportPath,
            '--pacing-thresholds', (Join-Path $PSScriptRoot ('pacing/' + $fixture.Thresholds)))
        if ($BaselineRoot) {
            $baseline = Join-Path ([System.IO.Path]::GetFullPath($BaselineRoot, $repoRoot)) ($fixture.Name + '.json')
            if (-not (Test-Path -LiteralPath $baseline)) { throw "Missing baseline: $baseline" }
            $arguments += @('--telemetry-baseline', $baseline)
        }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Pacing fixture failed: $($fixture.Name), exit $LASTEXITCODE" }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.PacingBenchmarks.SchemaVersion -ne 1) { throw 'Missing pacing benchmark schema.' }
        if ($BaselineRoot -and $report.PacingBenchmarks.BaselineCompatibility -ne 'Comparable') {
            throw "Pacing cohort is incompatible: $($report.PacingBenchmarks.BaselineCompatibility)"
        }
        Write-Host "Pacing analysis: $reportPath.pacing.md"
    }
} finally { Pop-Location }
