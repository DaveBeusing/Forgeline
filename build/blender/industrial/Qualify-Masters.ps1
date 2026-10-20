#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 480
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$root = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $root) { throw 'Qualification requires a fresh artifacts directory.' }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Build the Release asset compiler first.' }
$contract = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'families.json') | ConvertFrom-Json
New-Item -ItemType Directory -Path $root | Out-Null
function Tree([string]$Path) {
    @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path,$_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$before = Tree (Join-Path $repo 'assets/source')
$reports = @()
foreach ($family in $contract.families) {
    $name = $family.root.Substring('building.directorate.'.Length)
    $directory = Join-Path $root $name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $fixture = Join-Path $directory 'family.json'
    @{ schemaVersion = 1; families = @($family) } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $fixture -Encoding utf8NoBOM
    $roundTrip = Join-Path $repo 'tools/blender/Invoke-BlenderRoundTrip.ps1'
    foreach ($variant in @('first','second')) {
        & $roundTrip -OutputRoot (Join-Path $directory $variant) -FixtureContract $fixture -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'master.py') -InputRoot assets/source -OutputRoot (Join-Path $directory 'master') -ScriptArguments @((Join-Path $directory 'first/authoring.blend'),$fixture) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    & $roundTrip -Action Export -BlendFile (Join-Path $directory 'master/authoring.blend') -OutputRoot (Join-Path $directory 'reopened') -FixtureContract $fixture -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $repo 'tools/blender/tests/native_asset_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $directory 'native-contracts') -ScriptArguments @((Join-Path $directory 'first/authoring.blend'),$fixture) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    foreach ($variant in @('baseline','first','second','reopened','edited')) {
        $source = if ($variant -eq 'baseline') { Join-Path $directory 'first/baseline-source' } elseif ($variant -eq 'edited') { Join-Path $directory 'master/edit-source' } else { Join-Path $directory "$variant/source" }
        $runtime = Join-Path $directory "$variant/runtime"
        & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $directory "$variant-compile.json") 2>&1 | Out-File -LiteralPath (Join-Path $directory "$variant-compile.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Compile failed: $name/$variant" }
        & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime --qualification-output (Join-Path $directory "$variant-source-less.json") 2>&1 | Out-File -LiteralPath (Join-Path $directory "$variant-source-less.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $name/$variant" }
    }
    foreach ($variant in @('second','reopened')) {
        foreach ($kind in @('source','runtime')) {
            if (Compare-Object (Tree (Join-Path $directory "first/$kind")) (Tree (Join-Path $directory "$variant/$kind"))) { throw "Changed $kind bytes: $name/$variant" }
        }
    }
    $report = Get-Content -Raw -LiteralPath (Join-Path $directory 'first/roundtrip-report.json') | ConvertFrom-Json
    $reports += @{
        family = $name
        sourceSignature = $report.sourceSignature
        assetIds = $report.assetIds
        meshes = $report.meshes
        repeatedAndReopenedExports = 'passed'
        sourceLessCompiler = 'passed'
        nativeSceneRejections = 'passed'
        savedVertexEdit = 'passed'
        masterSha256 = (Get-FileHash -LiteralPath (Join-Path $directory 'master/authoring.blend') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
if (Compare-Object $before (Tree (Join-Path $repo 'assets/source'))) { throw 'Production source tree changed.' }
@{
    schemaVersion = 1
    sourceSha = (& git -C $repo rev-parse HEAD)
    families = $reports
    productionSourceUnchanged = $true
    productionPromotion = 'blocked-render-qualification'
    nativeRenderer = 'pending'
    liveLogisticsAndRotations = 'pending'
    physicalPerformance = 'pending'
    installedPackage = 'pending'
    humanVisualApproval = 'pending'
} | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
Write-Host "Isolated master qualification passed: $root/qualification.json"
