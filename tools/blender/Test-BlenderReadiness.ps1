#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 480)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $root) { throw 'Readiness qualification needs fresh output.' }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release compiler first.' }
$fixture = Join-Path $PSScriptRoot 'fixtures/readiness.json'
function Read-Tree([string]$Path) {
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$sourceBefore = Read-Tree (Join-Path $repo 'assets/source')
foreach ($name in @('first','second')) {
    & (Join-Path $PSScriptRoot 'Invoke-BlenderRoundTrip.ps1') -OutputRoot (Join-Path $root $name) -FixtureContract $fixture -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
}
& (Join-Path $PSScriptRoot 'Invoke-BlenderRoundTrip.ps1') -Action Export -BlendFile (Join-Path $root 'first/authoring.blend') -OutputRoot (Join-Path $root 'reopened') -FixtureContract $fixture -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_asset_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $root 'native-contracts') -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds -ScriptArguments @((Join-Path $root 'first/authoring.blend'),$fixture)
foreach ($name in @('baseline','first','second','reopened')) {
    $source = if ($name -eq 'baseline') { Join-Path $root 'first/baseline-source' } else { Join-Path $root "$name/source" }
    $runtime = Join-Path $root "$name/runtime"
    & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $root "$name-compile.json") 2>&1 | Out-File -LiteralPath (Join-Path $root "$name-compile.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $name-compile.log" }
    & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime --qualification-output (Join-Path $root "$name-source-less.json") 2>&1 | Out-File -LiteralPath (Join-Path $root "$name-source-less.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $name" }
}
foreach ($name in @('second','reopened')) {
    foreach ($kind in @('source','runtime')) {
        if (Compare-Object (Read-Tree (Join-Path $root "first/$kind")) (Read-Tree (Join-Path $root "$name/$kind"))) {
            throw "Non-deterministic ${kind}: $name"
        }
    }
}
if (Compare-Object $sourceBefore (Read-Tree (Join-Path $repo 'assets/source'))) { throw 'Production source bytes changed.' }
$previousRoot = $env:FORGELINE_BLENDER_READINESS_ROOT
try {
    $env:FORGELINE_BLENDER_READINESS_ROOT = $root
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.BlenderRoundTripQualificationTests 2>&1 | Out-File -LiteralPath (Join-Path $root 'runtime-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Compiled geometry/material/socket/LOD/batching comparison failed.' }
}
finally { $env:FORGELINE_BLENDER_READINESS_ROOT = $previousRoot }
@{
    schemaVersion = 1
    fixtureSha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceSha = (& git -C $repo rev-parse HEAD)
    staticRoundTrip = 'passed'
    repeatedSourceAndRuntime = 'passed'
    reopenedSavedMaster = 'passed'
    sourceLessIntegrity = 'passed'
    typedCompiledContracts = 'passed'
    nativeRenderer = 'pending'
    physicalPerformance = 'pending'
    installedPackage = 'pending'
    humanVisualApproval = 'pending'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
Write-Host "Staged source/compile qualification passed: $root/qualification.json"
