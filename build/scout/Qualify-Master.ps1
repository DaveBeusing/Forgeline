#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath, [switch]$RenderPreviews, [string]$QualifiedRuntimeSeed)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $output) { throw 'Qualification requires fresh output.' }
if ($IsWindows -and (Join-Path $output 'reopened/source/units/directorate/vehicles/_roundtrip/unit.directorate.scout_vehicle.critical.glb').Length -ge 260) {
    throw 'Use a shorter OutputRoot for native Windows paths.'
}
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build Release first.' }
function Read-Tree([string]$Path) {
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$seed = if ($QualifiedRuntimeSeed) { [IO.Path]::GetFullPath($QualifiedRuntimeSeed, $repo) } else { $null }
if ($seed) {
    $seedQualification = Get-Content -LiteralPath (Join-Path $seed 'qualification.json') -Raw | ConvertFrom-Json
    if (-not $seedQualification.success -or -not $seedQualification.sourceLessIntegrity) { throw 'Runtime seed is not qualified.' }
}
$before = Read-Tree (Join-Path $repo 'assets/source')
$fixture = Join-Path $PSScriptRoot 'fixture.json'
foreach ($variant in @('first', 'second', 'reopened')) {
    $arguments = @('--fixtures', $fixture)
    if ($variant -eq 'reopened') { $arguments += @('--action', 'Export', '--blend-file', (Join-Path $output 'first/authoring.blend')) }
    if ($RenderPreviews -and $variant -eq 'first') { $arguments += '--render' }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author.py') -InputRoot assets/source -OutputRoot (Join-Path $output $variant) -ScriptArguments $arguments -BlenderPath $BlenderPath -TimeoutSeconds 480
}
& (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'native_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $output 'native-contracts') -ScriptArguments @((Join-Path $output 'first/authoring.blend'), $fixture) -BlenderPath $BlenderPath -TimeoutSeconds 480

& (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $repo 'build/blender/industrial/master.py') -InputRoot assets/source -OutputRoot (Join-Path $output 'edit-check') -ScriptArguments @((Join-Path $output 'first/authoring.blend'), $fixture) -BlenderPath $BlenderPath -TimeoutSeconds 480
foreach ($variant in @('baseline', 'first', 'second', 'reopened', 'edited')) {
    $source = if ($variant -eq 'baseline') { Join-Path $output 'first/baseline-source' } elseif ($variant -eq 'edited') { Join-Path $output 'edit-check/edit-source' } else { Join-Path $output "$variant/source" }
    $runtime = Join-Path $output "$variant/runtime"
    $mode = if ($seed) { 'development' } else { 'build' }
    if ($seed) {
        New-Item -ItemType Directory -Path (Join-Path $output $variant) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $seed "$variant/runtime") -Destination (Join-Path $output $variant) -Recurse
    }
    & dotnet $compiler --mode $mode --source $source --runtime $runtime --qualification-output (Join-Path $output "$variant-compile.json") 2>&1 | Out-File (Join-Path $output "$variant-compile.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $variant" }
    & dotnet $compiler --mode validate --source (Join-Path $output 'absent-source') --runtime $runtime --qualification-output (Join-Path $output "$variant-source-less.json") 2>&1 | Out-File (Join-Path $output "$variant-source-less.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $variant" }
}
foreach ($variant in @('second', 'reopened')) {
    foreach ($kind in @('source', 'runtime')) {
        if (Compare-Object (Read-Tree (Join-Path $output "first/$kind")) (Read-Tree (Join-Path $output "$variant/$kind"))) { throw "Non-deterministic $variant/$kind" }
    }
}
if (Compare-Object $before (Read-Tree (Join-Path $repo 'assets/source'))) { throw 'Canonical source bytes changed.' }
$previous = $env:FORGELINE_SCOUT_MASTER
try {
    $env:FORGELINE_SCOUT_MASTER = $output
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ScoutBlenderMasterTests ForgeLine.Presentation.Tests.ScoutBlenderPoseTests 2>&1 | Out-File (Join-Path $output 'typed-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Typed compiled/pose comparison failed.' }
}
finally { $env:FORGELINE_SCOUT_MASTER = $previous }
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); success = $true;
   deterministicSourceAndRuntime = $true; sourceLessIntegrity = $true; typedCompiledComparison = $true;
   canonicalSourcesUnchanged = $true; productionPromotion = 'blocked-pending-render-qualification';
   physicalPerformance = 'pending'; humanArtApproval = 'pending'; qualifiedRuntimeSeed = $seed } |
    ConvertTo-Json | Set-Content (Join-Path $output 'qualification.json') -Encoding utf8NoBOM
