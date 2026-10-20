[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$TestExecutable,
    [Parameter(Mandatory)][string]$RuntimeAssets,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$RenderingBenchmark
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path -LiteralPath $Repository).Path
$testPath = (Resolve-Path -LiteralPath $TestExecutable).Path
$runtimePath = (Resolve-Path -LiteralPath $RuntimeAssets).Path
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a fresh measurement output directory.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$previousProfile = [Environment]::GetEnvironmentVariable('FORGELINE_SNAPSHOT_PROFILE_OUTPUT')
$previousRuntime = [Environment]::GetEnvironmentVariable('FORGELINE_RUNTIME_ASSETS')
$source = (& git -C $repositoryPath rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source.' }
$harnessPath = Join-Path $repositoryPath 'tests/ForgeLine.Client.Tests/SnapshotPublication/GameplayCaptureProfileTests.cs'
$manifestPath = Join-Path $runtimePath 'manifest.json'
$metadata = [ordered]@{
    repositorySource = $source
    harnessSha256 = (Get-FileHash -LiteralPath $harnessPath -Algorithm SHA256).Hash
    runtimeManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    runtimeContentSignature = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).contentSignature
    tieredCompilation = [Environment]::GetEnvironmentVariable('DOTNET_TieredCompilation')
    startedUtc = [DateTime]::UtcNow.ToString('O')
    profileExit = $null
    frameHotpathsExit = $null
    combatGroupHotpathsExit = $null
}
Push-Location $repositoryPath
try {
    $env:FORGELINE_SNAPSHOT_PROFILE_OUTPUT = Join-Path $outputPath 'gameplay'
    $env:FORGELINE_RUNTIME_ASSETS = $runtimePath
    & $testPath -class '*SnapshotPublication.GameplayCaptureProfileTests*' *> (Join-Path $outputPath 'gameplay.txt')
    $metadata.profileExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw 'Actual gameplay capture profile failed.' }
    if ($RenderingBenchmark) {
        $benchmarkPath = (Resolve-Path -LiteralPath $RenderingBenchmark).Path
        & dotnet $benchmarkPath --frame-hotpaths (Join-Path $outputPath 'frame-hotpaths.json') *> (Join-Path $outputPath 'frame-hotpaths.txt')
        $metadata.frameHotpathsExit = $LASTEXITCODE
        if ($LASTEXITCODE -ne 0) { throw 'Existing frame hotpaths failed.' }
        & dotnet $benchmarkPath --combat-group-hotpaths (Join-Path $outputPath 'combat-group-hotpaths.json') *> (Join-Path $outputPath 'combat-group-hotpaths.txt')
        $metadata.combatGroupHotpathsExit = $LASTEXITCODE
        if ($LASTEXITCODE -ne 0) { throw 'Existing combat-group hotpaths failed.' }
    }
}
finally {
    $metadata.completedUtc = [DateTime]::UtcNow.ToString('O')
    $metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'run.json') -Encoding utf8
    [Environment]::SetEnvironmentVariable('FORGELINE_SNAPSHOT_PROFILE_OUTPUT', $previousProfile, 'Process')
    [Environment]::SetEnvironmentVariable('FORGELINE_RUNTIME_ASSETS', $previousRuntime, 'Process')
    Pop-Location
}
