#requires -Version 7.4
[CmdletBinding()]
param([string]$BlenderPath, [string]$OutputRoot = 'artifacts/blender-smoke', [int]$TimeoutSeconds = 120)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repositoryRoot = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repositoryRoot
if (Test-Path -LiteralPath $root) { throw 'Smoke qualification needs a fresh output directory; previous evidence is never deleted.' }
$compiler = Join-Path $repositoryRoot 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release solution before qualifying the Blender export.' }
function Read-Tree([string]$Directory) {
    return @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Directory, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
$productionTreeBefore = Read-Tree (Join-Path $repositoryRoot 'assets/source')
$sourceBefore = @(& git -C $repositoryRoot status --porcelain --untracked-files=all -- assets/source)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect production sources.' }
[void][IO.Directory]::CreateDirectory($root)
foreach ($name in @('first','second')) {
    $destination = Join-Path $root $name
    & (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Smoke -BlenderPath $BlenderPath -OutputRoot $destination -TimeoutSeconds $TimeoutSeconds
    & dotnet $compiler --mode build --source (Join-Path $destination 'source') --runtime (Join-Path $destination 'runtime') --qualification-output (Join-Path $destination 'asset-qualification.json') > (Join-Path $destination 'compiler.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Blender export failed canonical compilation: $destination/compiler.log" }
    $qualification = Get-Content -LiteralPath (Join-Path $destination 'asset-qualification.json') -Raw | ConvertFrom-Json
    if (-not $qualification.success -or $qualification.assetCount -ne 2 -or $qualification.meshCount -ne 1 -or $qualification.materialCount -ne 1) { throw 'Unexpected smoke catalog content.' }
}
$first = Join-Path $root 'first'; $second = Join-Path $root 'second'
if (Compare-Object (Read-Tree (Join-Path $first 'source')) (Read-Tree (Join-Path $second 'source'))) { throw 'Repeated Blender source exports differ.' }
if (Compare-Object (Read-Tree (Join-Path $first 'runtime')) (Read-Tree (Join-Path $second 'runtime'))) { throw 'Repeated compiled payloads differ.' }
& dotnet $compiler --mode validate --source (Join-Path $root 'absent-sources') --runtime (Join-Path $first 'runtime') --qualification-output (Join-Path $root 'source-less-qualification.json') > (Join-Path $root 'source-less.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Source-less smoke runtime failed integrity validation.' }
$nativeScript = Join-Path $root 'native-contract.py'
@'
import json
import sys
from pathlib import Path
import bpy
request = json.loads(Path(sys.argv[sys.argv.index("--request") + 1]).read_text(encoding="utf-8-sig"))
assert request["arguments"] == ["two words", 'literal "quotes"', "Grüße 日本語", "--background", ""]
assert bpy.app.background and sys.flags.optimize == 0
print("Native contract complete Grüße 日本語", flush=True)
raise RuntimeError("ExpectedBlenderFailure")
'@ | Set-Content -LiteralPath $nativeScript -Encoding utf8NoBOM
$expectedFailure = $null
try {
    & (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script $nativeScript -BlenderPath $BlenderPath -OutputRoot (Join-Path $root 'native-failure') -TimeoutSeconds $TimeoutSeconds -ScriptArguments @('two words','literal "quotes"','Grüße 日本語','--background','')
}
catch { $expectedFailure = $_.Exception.Message }
if (-not $expectedFailure -or $expectedFailure -notmatch 'exited 23') { throw "Expected native Python exception exit 23; got $expectedFailure" }
$failureReport = Get-Content -LiteralPath (Join-Path $root 'native-failure/report.json') -Raw | ConvertFrom-Json
$nativeLogs = (Get-Content -LiteralPath (Join-Path $failureReport.runRoot 'script.stdout.log') -Raw -Encoding utf8) +
    (Get-Content -LiteralPath (Join-Path $failureReport.runRoot 'script.stderr.log') -Raw -Encoding utf8)
if (-not $nativeLogs.Contains('Native contract complete Grüße 日本語') -or -not $nativeLogs.Contains('ExpectedBlenderFailure')) { throw 'Native UTF-8/arguments/exception evidence missing.' }
$sourceAfter = @(& git -C $repositoryRoot status --porcelain --untracked-files=all -- assets/source)
if (($sourceBefore -join "`n") -cne ($sourceAfter -join "`n")) { throw 'Production source status changed during isolated smoke.' }
$productionTreeAfter = Read-Tree (Join-Path $repositoryRoot 'assets/source')
if (Compare-Object $productionTreeBefore $productionTreeAfter) { throw 'Production source bytes changed during isolated smoke.' }
[ordered]@{ schemaVersion = 1; success = $true; reproducibleSources = $true; reproducibleRuntime = $true;
    productionSourceStatusUnchanged = $true; productionSourceBytesUnchanged = $true; sourceLessValidation = $true; nativeExceptionExit23 = $true; nativeUtf8Arguments = $true;
    geometry = (Get-Content -LiteralPath (Join-Path $first 'source/smoke-report.json') -Raw | ConvertFrom-Json);
    toolchain = (Get-Content -LiteralPath (Join-Path $first 'report.json') -Raw | ConvertFrom-Json).toolchain
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
Write-Host "Blender exports, canonical compiler, idempotency and source-less integrity passed: $root/qualification.json"
