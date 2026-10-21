#requires -Version 7.4
[CmdletBinding()]
param([string]$SourceRoot='assets/source',[string]$OutputRoot='artifacts/decal-native',
      [string]$CompilerPath='artifacts/decal-build/bin/ForgeLine.AssetCompiler/release/ForgeLine.AssetCompiler.dll')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath('../..',$PSScriptRoot)
$source=[IO.Path]::GetFullPath($SourceRoot,$repo);$output=[IO.Path]::GetFullPath($OutputRoot,$repo)
if(-not $output.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Use a fresh artifacts output directory.'}
$compiler=[IO.Path]::GetFullPath($CompilerPath,$repo)
if(-not (Test-Path -LiteralPath $compiler)){throw 'Build the Release compiler first.'}
New-Item -ItemType Directory -Path $output|Out-Null
$families=@('tire_tracks','tracked_vehicle_marks','road_wear','oil_stain','blast_mark','shell_impact','scorch_mark','concrete_crack')
$results=@()
$isolated=Join-Path $output 'isolated-masters'
New-Item -ItemType Directory -Path $isolated|Out-Null
foreach($family in $families) {
    $master=Join-Path $source "world/authoring/decals/$family.blend"
    $expected=(Get-FileHash (Join-Path $source 'world/authoring/decals/decal_quad.glb') -Algorithm SHA256).Hash
    foreach($pass in @('reopen-a','reopen-b')) {
        $target=Join-Path $output "$pass/$family"
        $inputMaster=$master;$inputRoot=$source
        if($pass -eq 'reopen-b'){
            $inputMaster=Join-Path $isolated "$family.blend"
            Copy-Item -LiteralPath $master -Destination $inputMaster
            $inputRoot=$isolated
        }
        & "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script "$PSScriptRoot/decal_bakes.py" -InputRoot $inputRoot -OutputRoot $target -ScriptArguments @('--reopen',$inputMaster) -TimeoutSeconds 240
        if((Get-FileHash (Join-Path $target "$family.glb") -Algorithm SHA256).Hash -ne $expected){throw "Saved carrier export mismatch: $family"}
        $report=Get-Content (Join-Path $target 'reopen.json') -Raw|ConvertFrom-Json
        if(@($report.packed).Count -ne 3){throw 'Expected three packed shared maps.'}
        foreach($map in $report.packed){
            $name='decal_atlas'+$(if($map.channel -eq 'base'){''}else{'_'+$map.channel})+'.png'
            if($map.sha256 -ne (Get-FileHash (Join-Path $source "world/textures/decals/$name") -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Packed shared map differs from canonical bytes.'}
            if($map.colorSpace -ne $(if($map.channel -eq 'base'){'sRGB'}else{'Non-Color'})){throw 'Packed map has wrong color space.'}
            if($map.filepath.Replace('\','/') -ne "//../../textures/decals/$name"){throw 'Packed map path is not canonical-relative.'}
        }
    }
    & "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script "$PSScriptRoot/decal_bakes.py" -InputRoot $source -OutputRoot (Join-Path $output "rebakes/$family") -ScriptArguments @('--reopen',$master,'--rebake') -TimeoutSeconds 600
    $results+=@{family=$family;reopens=2;rebake=$true;carrierSha256=$expected}
}
python "$PSScriptRoot/decal_atlas_contract.py" --source $source --scope (Join-Path $output 'source') --rebakes (Join-Path $output 'rebakes') > (Join-Path $output 'atlas-contract.json')
if($LASTEXITCODE -ne 0){throw 'Saved master rebakes differ from the canonical atlas.'}
$scope=Join-Path $output 'source';$runtime=Join-Path $output 'runtime';$absent=Join-Path $output 'absent-source'
& dotnet $compiler --mode development --source $scope --runtime $runtime --qualification-output (Join-Path $output 'assets.json') *> (Join-Path $output 'compile.log')
if($LASTEXITCODE -ne 0){throw 'Scoped native source compile failed.'}
& dotnet $compiler --mode validate --source $absent --runtime $runtime *> (Join-Path $output 'source-less.log')
if($LASTEXITCODE -ne 0){throw 'Scoped source-less validation failed.'}
$edited=Join-Path $output 'edited-relief'
& "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script "$PSScriptRoot/decal_bakes.py" -InputRoot $source -OutputRoot $edited -ScriptArguments @('--reopen',(Join-Path $source 'world/authoring/decals/tire_tracks.blend'),'--rebake','--edit-relief') -TimeoutSeconds 600
if((Get-FileHash (Join-Path $edited 'tire_tracks-normal.png')).Hash -eq (Get-FileHash (Join-Path $output 'rebakes/tire_tracks/tire_tracks-normal.png')).Hash){throw 'A real relief edit did not change the normal bake.'}
if((Get-FileHash (Join-Path $edited 'tire_tracks.glb')).Hash -ne (Get-FileHash (Join-Path $source 'world/authoring/decals/decal_quad.glb')).Hash){throw 'Relief edit changed runtime plane geometry.'}
$inputs=@()
foreach($directory in @('world/authoring/decals','world/materials/decals','world/textures/decals')){
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $source $directory) -File){
        if($file.Extension -in @('.blend','.glb','.png','.json')){$inputs+=@{path=[IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}}
    }
}
@{success=$true;sourceRevision=(& git -C $repo rev-parse HEAD);sourceTreeClean=(@(& git -C $repo status --porcelain).Count -eq 0);inputs=$inputs;families=$results;exactRebakedChannels=24;editedReliefChangesNormalAndPreservesCarrier=$true;sourceLessCatalog=$true}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'result.json') -Encoding utf8NoBOM
