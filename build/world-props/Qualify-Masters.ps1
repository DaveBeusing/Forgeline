#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath, [switch]$RenderPreviews,
      [switch]$ReuseAuthored, [string]$MaterialCacheRoot, [switch]$NativeBatch, [string]$SourceRoot = 'assets/source', [ValidateRange(1,4)][int]$MaxParallel = 3)
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
$families = @((Get-Content $fixture -Raw | ConvertFrom-Json).families | ForEach-Object { $_.root -replace '^mesh.world.prop.', '' })
$canonicalRoot = [IO.Path]::GetFullPath('assets/source', $repo)
if (-not $ReuseAuthored -and $NativeBatch) {
    $batchArguments = @()
    if ($RenderPreviews) { $batchArguments += '--render' }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author_batch.py') -InputRoot $SourceRoot -OutputRoot $output -ScriptArguments $batchArguments -BlenderPath $BlenderPath -TimeoutSeconds 3600
}
if (-not $ReuseAuthored -and -not $NativeBatch) {
foreach ($family in $families) {
    $familyRoot = Join-Path $output $family
    foreach ($variant in @('first','second','reopened')) {
        $arguments = @('--fixtures', $fixture, '--family', $family)
        if ($variant -eq 'first') { $arguments += '--inspect' }
        if ($variant -eq 'reopened') { $arguments += @('--blend-file', (Join-Path $familyRoot 'first/authoring.blend')) }
        if ($RenderPreviews -and $variant -eq 'first') { $arguments += '--render' }
        & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author.py') -InputRoot $SourceRoot -OutputRoot (Join-Path $familyRoot $variant) -ScriptArguments $arguments -BlenderPath $BlenderPath -TimeoutSeconds 480
    }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'native_contract.py') -InputRoot $SourceRoot -OutputRoot (Join-Path $familyRoot 'native-contracts') -ScriptArguments @((Join-Path $familyRoot 'first/authoring.blend'), $fixture, "mesh.world.prop.$family") -BlenderPath $BlenderPath -TimeoutSeconds 480
}
}
foreach ($family in $families) {
    $familyRoot = Join-Path $output $family
    $native = Get-Content -LiteralPath (Join-Path $familyRoot 'native-contracts/native-contracts.json') -Raw | ConvertFrom-Json
    if (-not $native.success -or -not $native.unchangedReopenAccepted -or $native.root -ne "mesh.world.prop.$family" -or
        (Compare-Object @('animation','changed envelope','material slots','modifier','unbaked transform','unexpected object') @($native.rejected | Sort-Object))) {
        throw "Incomplete native rejection evidence: $family"
    }
    foreach ($variant in @('first','second','reopened')) {
        $report = Get-Content -LiteralPath (Join-Path $familyRoot "$variant/roundtrip-report.json") -Raw | ConvertFrom-Json
        if (-not $report.success -or -not $report.productionSourcesUnchanged -or @($report.meshes | Where-Object id -like 'mesh.world.prop.*').Count -ne 2 -or
            (Compare-Object @("mesh.world.prop.$family", "mesh.world.prop.${family}_lod1") @($report.meshes | Where-Object id -like 'mesh.world.prop.*' | ForEach-Object id | Sort-Object))) { throw "Incomplete authored output: $family/$variant" }
        foreach ($file in $report.sourceHashes.PSObject.Properties) {
            $sourcePath = [IO.Path]::GetFullPath($file.Name, $canonicalRoot)
            if (-not $sourcePath.StartsWith($canonicalRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $file.Value) { throw "Canonical source drift: $family/$variant/$($file.Name)" }
        }
    }
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build Release before compiling the preserved exports.' }
$worker = Join-Path $PSScriptRoot 'Compile-Family.ps1'
$results = $families | ForEach-Object -Parallel {
    $family = $_
    try {
        $ErrorActionPreference = 'Stop'
        $messages = @(& $using:worker -Family $family -OutputRoot $using:output -MaterialCacheRoot $using:MaterialCacheRoot)
        [pscustomobject]@{ Family = $family; Success = $true; Message = ($messages -join '; ') }
    }
    catch { [pscustomobject]@{ Family = $family; Success = $false; Message = $_.Exception.Message } }
} -ThrottleLimit $MaxParallel
if (@($results).Count -ne $families.Count) { throw 'Incomplete family worker results.' }
foreach ($result in $results) {
    if (-not $result.Success) { throw "Family compiler failed: $($result.Family): $($result.Message)" }
    Write-Output $result.Message
}
if (Compare-Object $before (Read-Tree (Join-Path $repo 'assets/source'))) { throw 'Canonical source bytes changed.' }
$previous = $env:FORGELINE_PROP_MASTERS
try {
    $env:FORGELINE_PROP_MASTERS = $output
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.WorldPropBlenderMasterTests 2>&1 | Out-File (Join-Path $output 'typed-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Typed runtime comparison failed.' }
}
finally { $env:FORGELINE_PROP_MASTERS = $previous }
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); success = $true;
   parallelFamilies = $MaxParallel; deterministicSourceAndRuntime = $true; sourceLessIntegrity = $true; typedCompiledComparison = $true;
   canonicalSourcesUnchanged = $true; productionPromotion = 'blocked-pending-render-qualification';
   physicalPerformance = 'pending'; humanArtApproval = 'pending' } |
    ConvertTo-Json | Set-Content (Join-Path $output 'qualification.json') -Encoding utf8NoBOM
