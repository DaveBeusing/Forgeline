#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts/blender-rigid-clips',
    [string]$CompilerPath = 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = [IO.Path]::GetFullPath($OutputRoot, $repo)
$allowed = [IO.Path]::GetFullPath('artifacts', $repo) + [IO.Path]::DirectorySeparatorChar
if (-not ($root + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain below artifacts.' }
if (Test-Path -LiteralPath $root) { throw 'Use a fresh evidence directory.' }
$compiler = [IO.Path]::GetFullPath($CompilerPath, $repo)
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release compiler first.' }
$fixture = Join-Path $repo 'tools/fixtures/rigid-clips'
function Get-PayloadHash([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $metadata = [BitConverter]::ToInt32($bytes, 12)
    $length = [BitConverter]::ToInt32($bytes, 16)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes[(20 + $metadata)..(19 + $metadata + $length)]))
}
$hashes = @()
foreach ($name in @('committed','export-a','export-b')) {
    $run = Join-Path $root $name
    $source = Join-Path $run 'source'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    foreach ($file in @('arm.asset.json','frame.asset.json','surface.asset.json','surface.json')) {
        Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $source
    }
    if ($name -eq 'committed') { Copy-Item -LiteralPath (Join-Path $fixture 'rigid_clip.glb') -Destination $source }
    else {
        & "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script (Join-Path $PSScriptRoot 'rigid_clip.py') -OutputRoot (Join-Path $run 'export')
        Copy-Item -LiteralPath (Join-Path $run 'export/rigid_clip.glb') -Destination $source
    }
    $runtime = Join-Path $run 'runtime'
    & dotnet $compiler --mode build --source $source --runtime $runtime --qualification-output (Join-Path $run 'qualification.json') *> (Join-Path $run 'compile.log')
    if ($LASTEXITCODE -ne 0) { throw "Clip compilation failed: $run/compile.log" }
    $manifest = Get-Content -LiteralPath (Join-Path $runtime 'manifest.json') -Raw | ConvertFrom-Json
    $record = @($manifest.assets | Where-Object id -eq 'animation.test.arm_cycle')[0]
    $hashes += Get-PayloadHash (Join-Path $runtime $record.runtimePath)
    # A player only receives compiled files. Prove reading needs no authoring tree.
    $hidden = Join-Path $run 'source-held'
    Move-Item -LiteralPath $source -Destination $hidden
    try {
        & dotnet $compiler --mode validate --runtime $runtime *> (Join-Path $run 'source-less.log')
        if ($LASTEXITCODE -ne 0) { throw "Source-less clip validation failed: $run" }
    }
    finally { Move-Item -LiteralPath $hidden -Destination $source }
}
if (@($hashes | Select-Object -Unique).Count -ne 1) { throw 'Committed and repeated Blender exports differ in compiled clip payload; retain evidence and inspect the source.' }
@{ success = $true; payloadSha256 = $hashes[0]; comparedExports = 3 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding utf8NoBOM
Write-Host "Blender clip payload parity and source-less qualification passed: $root"
