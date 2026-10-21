#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $output) { throw 'Retained verification requires fresh output.' }
$target = Join-Path $repo 'assets/staged/scout'
$lineage = Get-Content -LiteralPath (Join-Path $target 'lineage.json') -Raw | ConvertFrom-Json
& (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author.py') -InputRoot assets/source -OutputRoot $output -ScriptArguments @('--fixtures', (Join-Path $PSScriptRoot 'fixture.json'), '--action', 'Export', '--blend-file', (Join-Path $target 'scout.blend')) -BlenderPath $BlenderPath -TimeoutSeconds 480
foreach ($entry in $lineage.exports) {
    $definitionPath = Join-Path (Join-Path $output 'source') $entry.canonicalDefinition
    $definition = Get-Content -LiteralPath $definitionPath -Raw | ConvertFrom-Json
    $export = [IO.Path]::GetFullPath($definition.source, [IO.Path]::GetDirectoryName($definitionPath))
    if ((Get-FileHash -LiteralPath $export -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw ('Retained Scout export differs: ' + $entry.id) }
}
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); success = $true;
   savedMasterReopened = $true; exactExports = $lineage.exports.Count;
   masterSha256 = (Get-FileHash -LiteralPath (Join-Path $target 'scout.blend') -Algorithm SHA256).Hash.ToLowerInvariant() } |
   ConvertTo-Json | Set-Content (Join-Path $output 'retained-export.json') -Encoding utf8NoBOM
