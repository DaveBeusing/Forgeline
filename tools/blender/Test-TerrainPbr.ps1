#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath,
      [ValidateRange(1,86400)][int]$TimeoutSeconds = 3600)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (-not $root.StartsWith((Join-Path $repo 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) {
    throw 'Choose a fresh contained artifacts output root.'
}
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release asset compiler first.' }
$families = @('rocky_scrub','dry_dirt','cracked_earth','bedrock','crushed_gravel','dark_ash','concrete_base')
foreach ($family in $families) {
    foreach ($run in @('a','b')) {
        & "$PSScriptRoot/Invoke-TerrainPbr.ps1" -Family $family -OutputRoot (Join-Path $root "$run/$family") -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    }
    $a = Join-Path $root "a/$family/source"
    $b = Join-Path $root "b/$family/source"
    $files = @(Get-ChildItem -LiteralPath $a -Recurse -File)
    if ($files.Count -ne @(Get-ChildItem -LiteralPath $b -Recurse -File).Count) { throw 'Exported file sets differ.' }
    foreach ($file in $files) {
        $second = Join-Path $b ([IO.Path]::GetRelativePath($a,$file.FullName))
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $second).Hash) { throw "Independent saved-source exports differ: $family" }
    }
    # Merge only this family's known texture definitions and baked image folder.
    $destination = Join-Path $root 'source/world/textures/terrain'
    [void][IO.Directory]::CreateDirectory($destination)
    Get-ChildItem -LiteralPath (Join-Path $a 'world/textures/terrain') -File | Copy-Item -Destination $destination
    [void][IO.Directory]::CreateDirectory((Join-Path $destination 'blender'))
    Copy-Item -LiteralPath (Join-Path $a "world/textures/terrain/blender/$family") -Destination (Join-Path $destination 'blender') -Recurse
}
$materials = Join-Path $root 'source/world/materials/terrain'
[void][IO.Directory]::CreateDirectory($materials)
foreach ($slot in @('grass_ground','dirt','mud','rock','gravel','industrial_ground','concrete','scorched')) {
    foreach ($suffix in @('asset.json','material.json')) {
        Copy-Item -LiteralPath (Join-Path $repo "assets/source/world/materials/terrain/$slot.$suffix") -Destination $materials
    }
}
& "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script tools/blender/tests/native_terrain_pbr.py -InputRoot assets/source -OutputRoot (Join-Path $root 'native') -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
& dotnet $compiler --mode build --source (Join-Path $root 'source') --runtime (Join-Path $root 'runtime') --qualification-output (Join-Path $root 'compiled.json')
if ($LASTEXITCODE -ne 0) { throw 'Scoped terrain compile failed.' }
& dotnet $compiler --mode validate --source (Join-Path $root 'absent') --runtime (Join-Path $root 'runtime') --qualification-output (Join-Path $root 'source-less.json')
if ($LASTEXITCODE -ne 0) { throw 'Scoped source-less terrain qualification failed.' }
$report = Get-Content -LiteralPath (Join-Path $root 'compiled.json') -Raw | ConvertFrom-Json
if ($report.assetCount -ne 29 -or $report.textureCount -ne 21 -or $report.materialCount -ne 8) { throw 'Terrain closure must contain 29 assets: 21 textures and eight materials.' }
Write-Host 'Seven independently reopened masters, deterministic bakes, native edit/rejection controls and 29 source-less assets qualified. Art/performance acceptance remains separate.'
