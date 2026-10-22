#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath, [switch]$RenderPreviews,
      [switch]$ReuseAuthored, [string]$MaterialCacheRoot, [switch]$NativeBatch)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if ((Test-Path -LiteralPath $output) -and -not $ReuseAuthored) { throw 'Use fresh output.' }
if ($MaterialCacheRoot) { $MaterialCacheRoot = [IO.Path]::GetFullPath($MaterialCacheRoot, $repo) }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
function Read-Tree([string]$Path) {
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$before = Read-Tree (Join-Path $repo 'assets/source')
$fixture = Join-Path $PSScriptRoot 'fixture.json'
$canonicalRoot = [IO.Path]::GetFullPath('assets/source', $repo)
if (-not $ReuseAuthored -and $NativeBatch) {
    $batchArguments = @()
    if ($RenderPreviews) { $batchArguments += '--render' }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author_batch.py') -InputRoot assets/source -OutputRoot $output -ScriptArguments $batchArguments -BlenderPath $BlenderPath -TimeoutSeconds 1800
}
if (-not $ReuseAuthored -and -not $NativeBatch) {
foreach ($family in @('ferrous_ore','silicates','volatiles','rare_elements')) {
    $familyRoot = Join-Path $output $family
    foreach ($variant in @('first','second','reopened')) {
        $arguments = @('--fixtures', $fixture, '--family', $family)
        if ($variant -eq 'first') { $arguments += '--inspect' }
        if ($variant -eq 'reopened') { $arguments += @('--blend-file', (Join-Path $familyRoot 'first/authoring.blend')) }
        if ($RenderPreviews -and $variant -eq 'first') { $arguments += '--render' }
        & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author.py') -InputRoot assets/source -OutputRoot (Join-Path $familyRoot $variant) -ScriptArguments $arguments -BlenderPath $BlenderPath -TimeoutSeconds 480
    }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'native_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $familyRoot 'native-contracts') -ScriptArguments @((Join-Path $familyRoot 'first/authoring.blend'), $fixture, "mesh.world.resource.$family") -BlenderPath $BlenderPath -TimeoutSeconds 480
}
}
foreach ($family in @('ferrous_ore','silicates','volatiles','rare_elements')) {
    $familyRoot = Join-Path $output $family
    $native = Get-Content -LiteralPath (Join-Path $familyRoot 'native-contracts/native-contracts.json') -Raw | ConvertFrom-Json
    if (-not $native.success -or -not $native.unchangedReopenAccepted -or $native.root -ne "mesh.world.resource.$family" -or
        (Compare-Object @('animation','changed envelope','material slots','modifier','unbaked transform','unexpected object') @($native.rejected | Sort-Object))) {
        throw "Incomplete native rejection evidence: $family"
    }
    foreach ($variant in @('first','second','reopened')) {
        $report = Get-Content -LiteralPath (Join-Path $familyRoot "$variant/roundtrip-report.json") -Raw | ConvertFrom-Json
        $expectedMeshes = @((Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json).families |
            Where-Object root -eq "mesh.world.resource.$family" | ForEach-Object assets | Sort-Object)
        if (-not $report.success -or -not $report.productionSourcesUnchanged -or $report.meshes.Count -ne $expectedMeshes.Count -or
            (Compare-Object $expectedMeshes @($report.meshes.id | Sort-Object))) { throw "Incomplete authored output: $family/$variant" }
        foreach ($file in $report.sourceHashes.PSObject.Properties) {
            $sourcePath = [IO.Path]::GetFullPath($file.Name, $canonicalRoot)
            if (-not $sourcePath.StartsWith($canonicalRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $file.Value) { throw "Canonical source drift: $family/$variant/$($file.Name)" }
        }
    }
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build Release before compiling the preserved exports.' }
foreach ($family in @('ferrous_ore','silicates','volatiles','rare_elements')) {
    $familyRoot = Join-Path $output $family
    foreach ($variant in @('baseline','first','second','reopened')) {
        $source = if ($variant -eq 'baseline') { Join-Path $familyRoot 'first/baseline-source' } else { Join-Path $familyRoot "$variant/source" }
        $runtime = Join-Path $familyRoot "$variant/runtime"
        if (Test-Path -LiteralPath $runtime) { throw "Use fresh runtime output: $family/$variant" }
        if ($MaterialCacheRoot) {
            $seed = Join-Path $MaterialCacheRoot $family
            $seedManifest = Get-Content -LiteralPath (Join-Path $seed 'manifest.json') -Raw | ConvertFrom-Json
            if (@($seedManifest.assets).Count -ne 4 -or @($seedManifest.assets | Where-Object type -eq 'mesh').Count -ne 0) { throw 'Material cache must contain exactly four nonmesh records.' }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($runtime))
            Copy-Item -LiteralPath $seed -Destination $runtime -Recurse
        }
        & dotnet $compiler --mode development --source $source --runtime $runtime --qualification-output (Join-Path $familyRoot "$variant-compile.json") 2>&1 | Out-File (Join-Path $familyRoot "$variant-compile.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Compile failed: $family/$variant" }
        & dotnet $compiler --mode validate --source (Join-Path $output 'absent-source') --runtime $runtime --qualification-output (Join-Path $familyRoot "$variant-source-less.json") 2>&1 | Out-File (Join-Path $familyRoot "$variant-source-less.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $family/$variant" }
    }
    foreach ($variant in @('second','reopened')) {
        foreach ($kind in @('source','runtime')) {
            if (Compare-Object (Read-Tree (Join-Path $familyRoot "first/$kind")) (Read-Tree (Join-Path $familyRoot "$variant/$kind"))) { throw "Non-deterministic $family/$variant/$kind" }
        }
    }
}
if (Compare-Object $before (Read-Tree (Join-Path $repo 'assets/source'))) { throw 'Canonical source bytes changed.' }
$previous = $env:FORGELINE_RESOURCE_MASTERS
try {
    $env:FORGELINE_RESOURCE_MASTERS = $output
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ResourceDepositBlenderMasterTests 2>&1 | Out-File (Join-Path $output 'typed-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Typed runtime comparison failed.' }
}
finally { $env:FORGELINE_RESOURCE_MASTERS = $previous }
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); success = $true;
   deterministicSourceAndRuntime = $true; sourceLessIntegrity = $true; typedCompiledComparison = $true;
   canonicalSourcesUnchanged = $true; productionPromotion = 'blocked-pending-render-qualification';
   physicalPerformance = 'pending'; humanArtApproval = 'pending' } |
    ConvertTo-Json | Set-Content (Join-Path $output 'qualification.json') -Encoding utf8NoBOM
