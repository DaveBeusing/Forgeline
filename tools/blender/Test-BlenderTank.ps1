#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot,[string]$BlenderPath,[switch]$RenderPreviews,
    [string]$CompilerPath='tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll',
    [string]$SourceRoot='assets/source',
    [string]$BlendFile='assets/source/units/directorate/authoring/main_battle_tank.blend')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo=[IO.Path]::GetFullPath('../..',$PSScriptRoot)
$root=Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if(Test-Path -LiteralPath $root){throw 'Use a fresh tank qualification output.'}
[void][IO.Directory]::CreateDirectory($root)
$blend=[IO.Path]::GetFullPath($BlendFile,$repo)
$inputSource=[IO.Path]::GetFullPath($SourceRoot,$repo)
$compiler=[IO.Path]::GetFullPath($CompilerPath,$repo)
if(-not(Test-Path -LiteralPath $compiler)){throw 'Build the Release asset compiler first.'}
foreach($name in @('first','second')){
    & (Join-Path $PSScriptRoot 'Invoke-BlenderTank.ps1') -Action Export -SourceRoot $inputSource -BlendFile $blend -OutputRoot (Join-Path $root $name) -BlenderPath $BlenderPath -RenderPreviews:($RenderPreviews -and $name -eq 'first')
    if(-not $?){throw "Tank export failed: $name"}
}
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_tank_contract.py') -InputRoot $inputSource -ScriptArguments @('--blend-file',$blend) -OutputRoot (Join-Path $root 'contracts') -BlenderPath $BlenderPath
if(-not $?){throw 'Native saved-project checks failed.'}
function Read-Tree([string]$Path){
    @(Get-ChildItem -LiteralPath $Path -Recurse -File|Sort-Object FullName|ForEach-Object{[IO.Path]::GetRelativePath($Path,$_.FullName)+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash})
}
if(Compare-Object (Read-Tree (Join-Path $root 'first/source')) (Read-Tree (Join-Path $root 'second/source'))){throw 'Tank exports are not byte reproducible.'}
foreach($name in @('first','second','contracts/edited')){
    $source=Join-Path $root "$name/source";$runtime=Join-Path $root "$name/runtime"
    & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $root "$name/asset-qualification.json") 2>&1|Out-File -LiteralPath (Join-Path $root ($name.Replace('/','-')+'-compile.log')) -Encoding utf8NoBOM
    if($LASTEXITCODE -ne 0){throw "Tank compile failed: $name"}
    & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime 2>&1|Out-File -LiteralPath (Join-Path $root ($name.Replace('/','-')+'-integrity.log')) -Encoding utf8NoBOM
    if($LASTEXITCODE -ne 0){throw "Tank source-less validation failed: $name"}
}
if(Compare-Object (Read-Tree (Join-Path $root 'first/runtime')) (Read-Tree (Join-Path $root 'second/runtime'))){throw 'Tank compiled outputs are not byte reproducible.'}
$before=(Get-Content -LiteralPath (Join-Path $root 'first/runtime/manifest.json') -Raw|ConvertFrom-Json).assets
$edited=(Get-Content -LiteralPath (Join-Path $root 'contracts/edited/runtime/manifest.json') -Raw|ConvertFrom-Json).assets
if($before.Count -ne $edited.Count){throw 'Edited tank asset inventory changed.'}
foreach($asset in $before){
    $other=@($edited|Where-Object id -eq $asset.id)
    if($other.Count -ne 1){throw 'Edited tank asset identity changed.'}
    $changed=$asset.runtimeHash -ne $other[0].runtimeHash
    if($changed -ne ($asset.id -eq 'unit.directorate.main_battle_tank.damaged')){throw 'Saved shape edit did not change exactly its intended compiled asset.'}
}
@{editedManifestChangedOnlyDamaged=$true;success=$true;reopenedExportsByteEqual=$true;compiledOutputsByteEqual=$true;editedProjectCompiled=$true;sourceLessIntegrity=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
