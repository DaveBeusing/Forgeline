#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $output) { throw 'Qualification requires fresh output.' }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release compiler first.' }
function Read-Tree([string]$Path) {
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$sourceBefore = Read-Tree (Join-Path $repo 'assets/source')
foreach ($family in @('fuel_refinery', 'ammunition_plant')) {
    $root = Join-Path $output $family
    $fixture = Join-Path $PSScriptRoot "$family.json"
    foreach ($name in @('first', 'second')) {
        & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author_master.py') -InputRoot assets/source -OutputRoot (Join-Path $root $name) -ScriptArguments @('--fixtures', $fixture) -BlenderPath $BlenderPath -TimeoutSeconds 480
    }
    & (Join-Path $repo 'tools/blender/Invoke-BlenderRoundTrip.ps1') -Action Export -BlendFile (Join-Path $root 'first/authoring.blend') -OutputRoot (Join-Path $root 'reopened') -FixtureContract $fixture -BlenderPath $BlenderPath -TimeoutSeconds 480
    foreach ($name in @('baseline', 'first', 'second', 'reopened')) {
        $source = if ($name -eq 'baseline') { Join-Path $root 'first/baseline-source' } else { Join-Path $root "$name/source" }
        $runtime = Join-Path $root "$name/runtime"
        & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $root "$name-compile.json") 2>&1 | Out-File (Join-Path $root "$name-compile.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $family/$name" }
        & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime --qualification-output (Join-Path $root "$name-source-less.json") 2>&1 | Out-File (Join-Path $root "$name-source-less.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $family/$name" }
    }
    foreach ($name in @('second', 'reopened')) {
        foreach ($kind in @('source', 'runtime')) {
            if (Compare-Object (Read-Tree (Join-Path $root "first/$kind")) (Read-Tree (Join-Path $root "$name/$kind"))) { throw "Non-deterministic $family/$name/$kind" }
        }
    }
}
if (Compare-Object $sourceBefore (Read-Tree (Join-Path $repo 'assets/source'))) { throw 'Production source bytes changed.' }
$previous = $env:FORGELINE_INDUSTRIAL_PLANT_MASTERS
try {
    $env:FORGELINE_INDUSTRIAL_PLANT_MASTERS = $output
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.IndustrialPlantMasterTests 2>&1 | Out-File (Join-Path $output 'compiled-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Typed compiled comparison failed.' }
}
finally { $env:FORGELINE_INDUSTRIAL_PLANT_MASTERS = $previous }
@{
    schemaVersion = 1
    sourceSha = (& git -C $repo rev-parse HEAD)
    savedAndReopenedBlenderMasters = 'passed'
    deterministicSourceAndRuntime = 'passed'
    sourceLessIntegrity = 'passed'
    typedCompiledComparison = 'passed'
    productionSourcesUnchanged = $true
    productionPromotion = 'blocked-pending-render-qualification'
    nativeRenderer = 'pending'
    physicalPerformance = 'pending'
    installedPackage = 'pending'
    humanVisualApproval = 'pending'
} | ConvertTo-Json | Set-Content (Join-Path $output 'qualification.json') -Encoding utf8NoBOM
