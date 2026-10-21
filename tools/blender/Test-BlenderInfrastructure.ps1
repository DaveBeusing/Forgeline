#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo=[IO.Path]::GetFullPath('../..',$PSScriptRoot)
$root=Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if(Test-Path -LiteralPath $root){throw 'Use a fresh infrastructure qualification directory.'}
[void][IO.Directory]::CreateDirectory($root)
$compiler=Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if(-not(Test-Path -LiteralPath $compiler)){throw 'Build the Release compiler first.'}
$fixture=Join-Path $PSScriptRoot 'fixtures/infrastructure.json'
$families=(Get-Content -LiteralPath $fixture -Raw|ConvertFrom-Json).families
function Read-Tree([string]$Path){
    @(Get-ChildItem -LiteralPath $Path -Recurse -File|Sort-Object FullName|ForEach-Object{
        [IO.Path]::GetRelativePath($Path,$_.FullName)+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
foreach($family in $families){
    $name=$family.name
    $blend=Join-Path $repo "assets/source/infrastructure/directorate/authoring/$name.blend"
    foreach($pass in @('first','second')){
        $output=Join-Path $root "$name/$pass"
        & (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'infrastructure.py') `
            -InputRoot (Join-Path $repo 'assets/source') -OutputRoot $output -BlenderPath $BlenderPath `
            -ScriptArguments @('--action','Export','--family',$name,'--fixtures',$fixture,'--blend-file',$blend)
        & dotnet $compiler --mode build --source "$output/source" --runtime "$output/runtime" 2>&1|
            Out-File -LiteralPath "$output/compile.log" -Encoding utf8NoBOM
        if($LASTEXITCODE -ne 0){throw "Infrastructure compile failed: $name/$pass"}
        & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime "$output/runtime" 2>&1|
            Out-File -LiteralPath "$output/integrity.log" -Encoding utf8NoBOM
        if($LASTEXITCODE -ne 0){throw 'Infrastructure source-less validation failed.'}
    }
    foreach($kind in @('source','runtime')){
        if(Compare-Object (Read-Tree "$root/$name/first/$kind") (Read-Tree "$root/$name/second/$kind")){
            throw "Infrastructure $kind outputs are not byte reproducible: $name"
        }
    }
    $native=Join-Path $root "$name/contracts"
    & (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_infrastructure_contract.py') `
        -InputRoot (Join-Path $repo 'assets/source') -OutputRoot $native -BlenderPath $BlenderPath `
        -ScriptArguments @($blend,$fixture,$family.root)
    & dotnet $compiler --mode build --source "$native/edit-source" --runtime "$native/edit-runtime" 2>&1|
        Out-File -LiteralPath "$native/edit-compile.log" -Encoding utf8NoBOM
    if($LASTEXITCODE -ne 0){throw 'Saved infrastructure edit did not compile.'}
    & dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime "$native/edit-runtime" 2>&1|
        Out-File -LiteralPath "$native/edit-integrity.log" -Encoding utf8NoBOM
    if($LASTEXITCODE -ne 0){throw 'Edited infrastructure source-less validation failed.'}
    $before=(Get-Content -LiteralPath "$root/$name/first/runtime/manifest.json" -Raw|ConvertFrom-Json).assets
    $after=(Get-Content -LiteralPath "$native/edit-runtime/manifest.json" -Raw|ConvertFrom-Json).assets
    if($before.Count -ne $after.Count){throw 'Saved edit changed the infrastructure inventory.'}
    foreach($asset in $before){
        $other=@($after|Where-Object id -eq $asset.id)
        if($other.Count -ne 1 -or (($asset.runtimeHash -ne $other[0].runtimeHash) -ne ($asset.id -eq $family.root))){
            throw 'Saved edit must change exactly its intended runtime mesh.'
        }
    }
}
@{success=$true;families=3;repeatedSourceAndRuntimeBytes=$true;sourceLessIntegrity=$true;nativeInvalidEditsRejected=$true;savedEditsReachOnlyIntendedMeshes=$true}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
