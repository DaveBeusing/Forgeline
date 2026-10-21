#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot,[string]$BlenderPath,
    [string]$CompilerPath='tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo=[IO.Path]::GetFullPath('../..',$PSScriptRoot)
$root=Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if(Test-Path -LiteralPath $root){throw 'Use a fresh truck qualification output.'}
[void][IO.Directory]::CreateDirectory($root)
$compiler=[IO.Path]::GetFullPath($CompilerPath,$repo)
if(-not(Test-Path -LiteralPath $compiler)){throw 'Build the Release asset compiler first.'}
function Read-Tree([string]$Path){
    @(Get-ChildItem -LiteralPath $Path -Recurse -File|Sort-Object FullName|ForEach-Object{[IO.Path]::GetRelativePath($Path,$_.FullName)+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash})
}
foreach($family in @('cargo_truck','supply_truck')){
    $blend=Join-Path $repo "assets/source/units/directorate/authoring/$family.blend"
    foreach($name in @('first','second')){
        & (Join-Path $PSScriptRoot 'Invoke-BlenderLogisticsTruck.ps1') -Action Export -Family $family -BlendFile $blend -OutputRoot (Join-Path $root "$family/$name") -BlenderPath $BlenderPath
        if(-not $?){throw "Truck export failed: $family/$name"}
    }
    if(Compare-Object (Read-Tree (Join-Path $root "$family/first/source")) (Read-Tree (Join-Path $root "$family/second/source"))){throw 'Truck source exports are not byte reproducible.'}
    foreach($name in @('first','second')){
        $source=Join-Path $root "$family/$name/source";$runtime=Join-Path $root "$family/$name/runtime"
        & dotnet $compiler --mode build --source $source --runtime $runtime 2>&1|Out-File -LiteralPath (Join-Path $root "$family/$name/compile.log") -Encoding utf8NoBOM
        if($LASTEXITCODE -ne 0){throw "Truck compile failed: $family/$name"}
        & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime 2>&1|Out-File -LiteralPath (Join-Path $root "$family/$name/integrity.log") -Encoding utf8NoBOM
        if($LASTEXITCODE -ne 0){throw 'Truck source-less integrity failed.'}
    }
    if(Compare-Object (Read-Tree (Join-Path $root "$family/first/runtime")) (Read-Tree (Join-Path $root "$family/second/runtime"))){throw 'Truck compiled outputs are not byte reproducible.'}
}
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_logistics_truck_contract.py') -InputRoot (Join-Path $repo 'assets/source') -OutputRoot (Join-Path $root 'contracts') -BlenderPath $BlenderPath -TimeoutSeconds 900
if(-not $?){throw 'Native saved truck contract checks failed.'}
foreach($family in @('cargo_truck','supply_truck')){
    $source=Join-Path $root "contracts/$family/edited/source";$runtime=Join-Path $root "contracts/$family/edited/runtime"
    & dotnet $compiler --mode build --source $source --runtime $runtime 2>&1|Out-File -LiteralPath (Join-Path $root "contracts/$family/edited/compile.log") -Encoding utf8NoBOM
    if($LASTEXITCODE -ne 0){throw 'Edited truck compile failed.'}
    & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime 2>&1|Out-File -LiteralPath (Join-Path $root "contracts/$family/edited/integrity.log") -Encoding utf8NoBOM
    if($LASTEXITCODE -ne 0){throw 'Edited truck source-less integrity failed.'}
    $before=(Get-Content (Join-Path $root "$family/first/runtime/manifest.json") -Raw|ConvertFrom-Json).assets
    $edited=(Get-Content (Join-Path $runtime 'manifest.json') -Raw|ConvertFrom-Json).assets
    if($before.Count -ne $edited.Count){throw 'Edited truck asset inventory changed.'}
    foreach($asset in $before){
        $other=@($edited|Where-Object id -eq $asset.id)
        if($other.Count -ne 1 -or (($asset.runtimeHash -ne $other[0].runtimeHash) -ne ($asset.id -eq "unit.directorate.$family"))){throw 'Saved component edit did not change exactly its intended runtime asset.'}
    }
}
@{success=$true;reopenedSourcesByteEqual=$true;compiledOutputsByteEqual=$true;sourceLessIntegrity=$true;editedRuntimeChangesOnlyLod0=$true}|ConvertTo-Json|Set-Content (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
