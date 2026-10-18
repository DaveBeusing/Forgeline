#requires -Version 7.4
[CmdletBinding()]
param([string]$OutputRoot='artifacts/infantry-skin-review',[string]$CompilerPath='tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll',[string]$ProductionRuntimeRoot='assets/runtime')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath('..',$PSScriptRoot)
$root=[IO.Path]::GetFullPath($OutputRoot,$repo)
$allowed=[IO.Path]::GetFullPath('artifacts',$repo)+[IO.Path]::DirectorySeparatorChar
if(-not ($root+[IO.Path]::DirectorySeparatorChar).StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Outputs must remain within artifacts.'}
if(Test-Path -LiteralPath $root){throw 'Use fresh evidence directory.'}
New-Item -ItemType Directory -Path $root | Out-Null
$source=Join-Path $root 'source'
Copy-Item -LiteralPath (Join-Path $repo 'assets/source') -Destination $source -Recurse
$compiler=[IO.Path]::GetFullPath($CompilerPath,$repo)
foreach($variant in @('skin','bind')) {
    if($variant -eq 'bind') {
        foreach($family in @('rifle_squad','combat_engineer')) {
            $path=Join-Path $source "units/directorate/infantry/$family.asset.json"
            $definition=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            $inputPath=[IO.Path]::GetFullPath($definition.source,[IO.Path]::GetDirectoryName($path))
            $bytes=[IO.File]::ReadAllBytes($inputPath);$length=[BitConverter]::ToInt32($bytes,12)
            $json=[Text.Encoding]::UTF8.GetString($bytes,20,$length)|ConvertFrom-Json
            $json.PSObject.Properties.Remove('skins');$json.PSObject.Properties.Remove('animations')
            foreach($node in $json.nodes){$node.PSObject.Properties.Remove('skin')}
            foreach($mesh in $json.meshes){foreach($primitive in $mesh.primitives){$primitive.attributes.PSObject.Properties.Remove('JOINTS_0');$primitive.attributes.PSObject.Properties.Remove('WEIGHTS_0')}}
            $encoded=[Text.Encoding]::UTF8.GetBytes(($json|ConvertTo-Json -Depth 40 -Compress))
            $padded=($encoded.Length+3)-band -4
            $newLength=12+8+$padded+($bytes.Length-20-$length)
            $output=[byte[]]::new($newLength)
            [Array]::Copy($bytes,0,$output,0,12);[BitConverter]::GetBytes($newLength).CopyTo($output,8)
            [BitConverter]::GetBytes($padded).CopyTo($output,12);[BitConverter]::GetBytes([uint32]0x4E4F534A).CopyTo($output,16)
            [Array]::Copy($encoded,0,$output,20,$encoded.Length)
            for($i=$encoded.Length;$i -lt $padded;$i++){$output[20+$i]=32}
            [Array]::Copy($bytes,20+$length,$output,20+$padded,$bytes.Length-20-$length)
            $bindPath=Join-Path ([IO.Path]::GetDirectoryName($path)) "$family-bind.glb"
            [IO.File]::WriteAllBytes($bindPath,$output)
            $definition.source="$family-bind.glb";$definition.PSObject.Properties.Remove('skinReference')
            $definition|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $path -Encoding utf8NoBOM
        }
    }
    $runtime=Join-Path $root "$variant-runtime"
    Copy-Item -LiteralPath ([IO.Path]::GetFullPath($ProductionRuntimeRoot,$repo)) -Destination $runtime -Recurse
    & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $root "$variant-qualification.json") *> (Join-Path $root "$variant-build.log")
    if($LASTEXITCODE -ne 0){throw "Skin/bind preparation failed: $root/$variant-build.log"}
    & dotnet $compiler --mode validate --runtime $runtime *> (Join-Path $root "$variant-source-less.log")
    if($LASTEXITCODE -ne 0){throw 'Source-less qualification failed.'}
}
Write-Host "Qualified matched skin/bind catalogs: $root"
