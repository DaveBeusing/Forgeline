#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 1800)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $root) { throw 'Use a fresh qualification directory.' }
$master = Join-Path $repo 'assets/source/buildings/directorate/authoring/command_core.blend'
$fixture = Join-Path $PSScriptRoot 'fixtures/command_core.json'
$masterHash = (Get-FileHash -LiteralPath $master -Algorithm SHA256).Hash
foreach ($name in @('first','second')) {
    & (Join-Path $PSScriptRoot 'Invoke-BlenderCommandCore.ps1') -OutputRoot (Join-Path $root $name) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
}
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_command_core_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $root 'native') -ScriptArguments @($master,$fixture) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
$first = Join-Path $root 'first/source'
$files = @(Get-ChildItem -LiteralPath $first -File -Recurse)
foreach ($variant in @('second/source','native/control/source')) {
    $other = Join-Path $root $variant
    if (@(Get-ChildItem -LiteralPath $other -File -Recurse).Count -ne $files.Count) { throw 'Exported file sets differ.' }
    foreach ($file in $files) {
        $target = Join-Path $other ([IO.Path]::GetRelativePath($first, $file.FullName))
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Reopened export differs: $target" }
    }
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $first 'buildings/directorate/sources/command_core/blender') -Filter '*.glb')) {
    $production = Join-Path $repo ('assets/source/buildings/directorate/sources/command_core/blender/' + $file.Name)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $production).Hash) { throw "Review and promote saved edits before qualification: $production" }
}
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
& dotnet $compiler --mode build --source $first --runtime (Join-Path $root 'runtime') --qualification-output (Join-Path $root 'runtime-qualification.json') *> (Join-Path $root 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Command Core source compilation failed.' }
& dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime (Join-Path $root 'runtime') *> (Join-Path $root 'source-less.log')
if ($LASTEXITCODE -ne 0) { throw 'Command Core source-less validation failed.' }
if ((Get-FileHash -LiteralPath $master).Hash -ne $masterHash) { throw 'Production master changed.' }
@{ success = $true; repeatedSavedExports = $true; canonicalGeometryMatches = $true; sourceLessIntegrity = $true; productionMasterUnchanged = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8NoBOM
