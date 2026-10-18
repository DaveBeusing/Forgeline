#requires -Version 7.4
[CmdletBinding()]
param([string]$OutputRoot='artifacts/blender-infantry', [string]$CompilerPath='tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath('../..',$PSScriptRoot)
$root=[IO.Path]::GetFullPath($OutputRoot,$repo)
$allowed=[IO.Path]::GetFullPath('artifacts',$repo)+[IO.Path]::DirectorySeparatorChar
if (-not ($root+[IO.Path]::DirectorySeparatorChar).StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) {throw 'Outputs must remain within artifacts.'}
if(Test-Path -LiteralPath $root){throw 'Use a fresh evidence directory.'}
$compiler=[IO.Path]::GetFullPath($CompilerPath,$repo)
New-Item -ItemType Directory -Path $root | Out-Null
$actions=@('idle','walk','run','aim','fire','reload','hit','suppression','death')
$results=@()
foreach($run in @('committed','export-a','export-b','edited')) {
    if($run -in @('export-a','export-b')) {
        & "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script "$PSScriptRoot/infantry_skin.py" -InputRoot assets/source -OutputRoot (Join-Path $root "$run/export") -ScriptArguments '--reopen'
    }
    foreach($family in @('rifle_squad','combat_engineer')) {
        $work=Join-Path $root "$run/$family"
        $source=Join-Path $work 'source'
        New-Item -ItemType Directory -Path $source -Force | Out-Null
        $inputFile=if($run -eq 'committed'){Join-Path $repo "assets/source/units/directorate/authoring/infantry/$family.glb"}elseif($run -eq 'edited'){Join-Path $root "export-a/export/$family-edited.glb"}else{Join-Path $root "$run/export/$family.glb"}
        Copy-Item -LiteralPath $inputFile -Destination (Join-Path $source 'infantry.glb')
        '{}' | Set-Content -LiteralPath (Join-Path $source 'surface.json') -Encoding utf8NoBOM
        @{id='material.test.infantry';type='material';source='surface.json'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $source 'surface.asset.json') -Encoding utf8NoBOM
        foreach($action in $actions){@{id="animation.test.$action";type='animation';source='infantry.glb';skeletalAnimation=$true;animationClip=$action} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $source "$action.asset.json") -Encoding utf8NoBOM}
        @{id='skin.test.infantry';type='skin';source='infantry.glb';materialReferences=@('material.test.infantry');animationReferences=@($actions|ForEach-Object{"animation.test.$_"});sockets=@(@{name='weapon_muzzle';x=.05;y=.05;z=.42});skinSockets=@(@{name='weapon_muzzle';joint='member_0_forearm_right'})} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $source 'skin.asset.json') -Encoding utf8NoBOM
        $runtime=Join-Path $work 'runtime'
        & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $work 'qualification.json') *> (Join-Path $work 'compile.log')
        if($LASTEXITCODE -ne 0){throw "Infantry compilation failed: $work/compile.log"}
        # Validate with no authoring tree at its recorded location.
        $held=Join-Path $work 'source-held'
        if (-not $source.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or -not $held.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected source move target.'}
        Move-Item -LiteralPath $source -Destination $held
        try{& dotnet $compiler --mode validate --runtime $runtime *> (Join-Path $work 'source-less.log');if($LASTEXITCODE -ne 0){throw 'Source-less infantry validation failed.'}}
        finally{Move-Item -LiteralPath $held -Destination $source}
        $manifest=Get-Content -LiteralPath (Join-Path $runtime 'manifest.json') -Raw | ConvertFrom-Json
        $payloads=@{}
        foreach($record in $manifest.assets|Where-Object{$_.type -in @('animation','skin')}){
            $bytes=[IO.File]::ReadAllBytes((Join-Path $runtime $record.runtimePath));$offset=20+[BitConverter]::ToInt32($bytes,12);$length=[BitConverter]::ToInt32($bytes,16)
            $payloads[$record.id]=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes[$offset..($offset+$length-1)]))
        }
        $results+=@{run=$run;family=$family;payloads=$payloads}
    }
}
foreach($family in @('rifle_squad','combat_engineer')){
    $reference=@($results|Where-Object{$_.run -eq 'committed' -and $_.family -eq $family})[0]
    foreach($run in @('export-a','export-b')){
        $current=@($results|Where-Object{$_.run -eq $run -and $_.family -eq $family})[0]
        foreach($id in $reference.payloads.Keys){if($reference.payloads[$id] -ne $current.payloads[$id]){throw "Saved master payload mismatch: $family $run $id"}}
    }
    $edited=@($results|Where-Object{$_.run -eq 'edited' -and $_.family -eq $family})[0]
    if($edited.payloads['animation.test.idle'] -eq $reference.payloads['animation.test.idle']){throw 'An actual key edit did not change the runtime clip.'}
}
@{success=$true;exports=$results} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding utf8NoBOM
Write-Host "Infantry saved-master parity, edit/export and source-less validation passed: $root"
