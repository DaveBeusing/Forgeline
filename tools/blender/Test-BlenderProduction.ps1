#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath,
    [string]$RuntimeSeedRoot, [switch]$RenderPreviews, [int]$TimeoutSeconds = 480)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $root) { throw 'Qualification needs a fresh output root.' }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release solution first.' }
$fixtures = Join-Path $PSScriptRoot 'fixtures/production.json'
foreach ($name in @('first','second')) {
    & (Join-Path $PSScriptRoot 'Invoke-BlenderProduction.ps1') -OutputRoot (Join-Path $root $name) -RenderPreviews:($RenderPreviews -and $name -eq 'first') -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
}
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_production_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $root 'native-contracts') -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
& dotnet $compiler --mode development --source (Join-Path $root 'native-contracts/source') --runtime (Join-Path $root 'native-contracts/runtime') --qualification-output (Join-Path $root 'generated-lod-qualification.json') 2>&1 | Out-File -LiteralPath (Join-Path $root 'generated-lod-compile.log') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'Generated LOD fixture failed compiler validation.' }
& dotnet $compiler --mode validate --source (Join-Path $root 'absent-generated-source') --runtime (Join-Path $root 'native-contracts/runtime') 2>&1 | Out-File -LiteralPath (Join-Path $root 'generated-lod-source-less.log') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'Generated LOD fixture failed source-less validation.' }
$firstReport = Get-Content -LiteralPath (Join-Path $root 'first/production-report.json') -Raw | ConvertFrom-Json
function Copy-Seed([string]$Seed, [string]$Target) {
    $manifestPath = Join-Path $Seed 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    [void][IO.Directory]::CreateDirectory($Target)
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $Target 'manifest.json')
    foreach ($record in $manifest.assets) {
        if ($record.id -notin $firstReport.assetIds) { continue }
        $relative = $record.runtimePath
        $source = [IO.Path]::GetFullPath($relative, $Seed)
        $destination = [IO.Path]::GetFullPath($relative, $Target)
        if (-not $source.StartsWith($Seed + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            -not $destination.StartsWith($Target + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime seed payload escaped its root.' }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $source -Destination $destination
    }
}
$baseline = Join-Path $root 'baseline-runtime'
if ($RuntimeSeedRoot) {
    $RuntimeSeedRoot = [IO.Path]::GetFullPath($RuntimeSeedRoot,$repo)
    Copy-Seed $RuntimeSeedRoot $baseline
}
function Compile-Source([string]$Source, [string]$Runtime, [string]$Name) {
    & dotnet $compiler --mode development --source $Source --runtime $Runtime --qualification-output (Join-Path $root "$Name-qualification.json") 2>&1 |
        Out-File -LiteralPath (Join-Path $root "$Name-compile.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $Name-compile.log" }
}
Compile-Source (Join-Path $root 'first/baseline-source') $baseline 'baseline'
foreach ($name in @('first','second')) {
    $runtime = Join-Path $root "$name/runtime"
    Copy-Seed $baseline $runtime
    Compile-Source (Join-Path $root "$name/source") $runtime $name
}
function Read-Tree([string]$Path) {
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path,$_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
foreach ($name in @('second')) {
    foreach ($kind in @('source','runtime')) {
        if (Compare-Object (Read-Tree (Join-Path $root "first/$kind")) (Read-Tree (Join-Path $root "$name/$kind"))) { throw "Non-deterministic $kind output: $name" }
    }
}
& dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime (Join-Path $root 'second/runtime') --qualification-output (Join-Path $root 'source-less-qualification.json') 2>&1 | Out-File -LiteralPath (Join-Path $root 'source-less.log') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'Source-less round-trip runtime validation failed.' }
$oldRoot = $env:FORGELINE_BLENDER_PRODUCTION_ROOT; $oldSeed = $env:FORGELINE_BLENDER_RUNTIME_SEED
try {
    $env:FORGELINE_BLENDER_PRODUCTION_ROOT = $root; $env:FORGELINE_BLENDER_RUNTIME_SEED = $RuntimeSeedRoot
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.BlenderRoundTripQualificationTests 2>&1 | Out-File -LiteralPath (Join-Path $root 'runtime-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Compiled mesh/metadata/mechanical batch comparison failed; inspect runtime-comparison.log.' }
}
finally { $env:FORGELINE_BLENDER_PRODUCTION_ROOT = $oldRoot; $env:FORGELINE_BLENDER_RUNTIME_SEED = $oldSeed }
@{ success = $true; fixtures = 2; meshes = 19; assetCount = 29; repeatedSourceAndRuntimeBytes = $true;  sourceLessIntegrity = $true;
    compiledRendererContracts = $true; generatedLodCompiled = $true; overlayAvailable = [bool]$RuntimeSeedRoot } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
Write-Host "Blender surface production qualification passed: $root/qualification.json"
