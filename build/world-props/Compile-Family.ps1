#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Family, [Parameter(Mandatory)][string]$OutputRoot, [string]$MaterialCacheRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = $OutputRoot
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
function Read-Tree([string]$Path) {
    @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$familyRoot = Join-Path $output $family
    foreach ($variant in @('baseline','first','second','reopened')) {
        $source = if ($variant -eq 'baseline') { Join-Path $familyRoot 'first/baseline-source' } else { Join-Path $familyRoot "$variant/source" }
        $runtime = Join-Path $familyRoot "$variant/runtime"
        if (Test-Path -LiteralPath $runtime) { throw "Use fresh runtime output: $family/$variant" }
        if ($MaterialCacheRoot) {
            $seed = $MaterialCacheRoot
            $seedManifest = Get-Content -LiteralPath (Join-Path $seed 'manifest.json') -Raw | ConvertFrom-Json
            if (@($seedManifest.assets).Count -ne 4 -or @($seedManifest.assets | Where-Object type -eq 'mesh').Count -ne 0) { throw 'Material cache must contain exactly four nonmesh records.' }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($runtime))
            Copy-Item -LiteralPath $seed -Destination $runtime -Recurse
        }
        & dotnet $compiler --mode development --source $source --runtime $runtime --qualification-output (Join-Path $familyRoot "$variant-compile.json") 2>&1 | Out-File (Join-Path $familyRoot "$variant-compile.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Compile failed: $family/$variant" }
        & dotnet $compiler --mode validate --source (Join-Path $output 'absent-source') --runtime $runtime --qualification-output (Join-Path $familyRoot "$variant-source-less.json") 2>&1 | Out-File (Join-Path $familyRoot "$variant-source-less.log") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $family/$variant" }
    }
    foreach ($variant in @('second','reopened')) {
        foreach ($kind in @('source','runtime')) {
            if (Compare-Object (Read-Tree (Join-Path $familyRoot "first/$kind")) (Read-Tree (Join-Path $familyRoot "$variant/$kind"))) { throw "Non-deterministic $family/$variant/$kind" }
        }
    }
Write-Output "Qualified ${Family}: four independent source-less catalogs and deterministic reopened bytes."
