#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts/rigid-clip-native',
    [string]$CompilerPath = 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll',
    [string]$ProductionRuntimeRoot = 'assets/runtime'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath('..', $PSScriptRoot)
$root = [IO.Path]::GetFullPath($OutputRoot, $repo)
$allowed = [IO.Path]::GetFullPath('artifacts', $repo) + [IO.Path]::DirectorySeparatorChar
if (-not ($root + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain below artifacts.' }
if (Test-Path -LiteralPath $root) { throw 'Use a new directory; qualification evidence is never overwritten.' }
$compiler = [IO.Path]::GetFullPath($CompilerPath, $repo)
$productionRuntime = [IO.Path]::GetFullPath($ProductionRuntimeRoot, $repo)
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release compiler first.' }
New-Item -ItemType Directory -Path $root | Out-Null
$source = Join-Path $root 'source'
Copy-Item -LiteralPath (Join-Path $repo 'assets/source') -Destination $source -Recurse
$fixture = Join-Path $repo 'tools/fixtures/rigid-clips'
$tankPath = @(Get-ChildItem -LiteralPath (Join-Path $source 'units/directorate') -Recurse -Filter '*.asset.json' |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).id -eq 'unit.directorate.main_battle_tank' })
if ($tankPath.Count -ne 1) { throw 'Expected exactly one production tank definition.' }
$tank = Get-Content -LiteralPath $tankPath[0].FullName -Raw | ConvertFrom-Json
$clipMesh = Get-Content -LiteralPath (Join-Path $fixture 'frame.asset.json') -Raw | ConvertFrom-Json
$tank.source = 'rigid_clip.glb'
$tank.mechanicalParts = $clipMesh.mechanicalParts
$tank.sockets = @($tank.sockets | Where-Object name -ne 'pivot') + $clipMesh.sockets
$tank | Add-Member -NotePropertyName animationReferences -NotePropertyValue @('animation.test.arm_cycle') -Force
Copy-Item -LiteralPath (Join-Path $fixture 'rigid_clip.glb') -Destination $tankPath[0].DirectoryName
Copy-Item -LiteralPath (Join-Path $fixture 'arm.asset.json') -Destination $tankPath[0].DirectoryName
$tank | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $tankPath[0].FullName -Encoding utf8NoBOM
foreach ($variant in @('clip','neutral')) {
    $runtime = Join-Path $root "$variant-runtime"
    Copy-Item -LiteralPath $productionRuntime -Destination $runtime -Recurse
    if ($variant -eq 'neutral') { $tank.animationReferences = @(); $tank | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $tankPath[0].FullName -Encoding utf8NoBOM }
    & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $root "$variant-qualification.json") --progress-log (Join-Path $root "$variant-progress.jsonl") *> (Join-Path $root "$variant-build.log")
    if ($LASTEXITCODE -ne 0) { throw "Isolated $variant compilation failed; inspect retained logs." }
    & dotnet $compiler --mode validate --runtime $runtime *> (Join-Path $root "$variant-integrity.log")
    if ($LASTEXITCODE -ne 0) { throw "Source-less $variant validation failed." }
}
Write-Host "Qualified clip/neutral source-less runtime roots: $root"
