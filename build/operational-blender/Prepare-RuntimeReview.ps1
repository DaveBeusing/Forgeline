#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$QualifiedRoot, [Parameter(Mandatory)][string]$OutputRoot, [string]$RuntimeSeed = 'assets/runtime')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$inputRoot = [IO.Path]::GetFullPath($QualifiedRoot, $repo)
$output = [IO.Path]::GetFullPath($OutputRoot, $repo)
$allowed = [IO.Path]::GetFullPath('artifacts', $repo) + [IO.Path]::DirectorySeparatorChar
if (-not ($output + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Use fresh contained runtime-review output.' }
foreach ($family in @('vehicle_factory','power_plant','barracks','radar')) {
    foreach ($variant in @('baseline','first','second','reopened')) {
        $target = Join-Path $output "$family/$variant"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $inputRoot "$family/$variant/runtime") -Destination (Join-Path $target 'runtime') -Recurse
    }
}
$previousRoot = $env:FORGELINE_OPERATIONAL_BLENDER_ROOT
$previousSeed = $env:FORGELINE_BLENDER_RUNTIME_SEED
try {
    $env:FORGELINE_OPERATIONAL_BLENDER_ROOT = $output
    $env:FORGELINE_BLENDER_RUNTIME_SEED = [IO.Path]::GetFullPath($RuntimeSeed, $repo)
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.BlenderRoundTripQualificationTests 2>&1 | Out-File -LiteralPath (Join-Path $output 'typed-runtime.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Full-catalog candidate comparison failed.' }
}
finally {
    $env:FORGELINE_OPERATIONAL_BLENDER_ROOT = $previousRoot
    $env:FORGELINE_BLENDER_RUNTIME_SEED = $previousSeed
}
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
& dotnet $compiler --mode validate --source (Join-Path $output 'absent-source') --runtime (Join-Path $output 'combined/runtime') --qualification-output (Join-Path $output 'combined-source-less.json') 2>&1 | Out-File -LiteralPath (Join-Path $output 'combined-source-less.log') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'Combined runtime source-less qualification failed.' }
Write-Host "Operational runtime review candidate ready: $output/combined/runtime"
