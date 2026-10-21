#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $output) { throw 'Use fresh retained-master output.' }
& python (Join-Path $PSScriptRoot 'validate_retained.py')
if ($LASTEXITCODE -ne 0) { throw 'Retained source lineage failed.' }
$records = @()
foreach ($family in @('vehicle_factory','power_plant','barracks','radar')) {
    $folder = Join-Path $repo "assets/staged/operational-buildings/$family"
    $lineage = Get-Content -Raw -LiteralPath (Join-Path $folder 'lineage.json') | ConvertFrom-Json
    $target = Join-Path $output $family
    & (Join-Path $repo 'tools/blender/Invoke-BlenderRoundTrip.ps1') -Action Export -BlendFile (Join-Path $folder $lineage.master) -FixtureContract (Join-Path $PSScriptRoot "$family.json") -OutputRoot $target -BlenderPath $BlenderPath -TimeoutSeconds 480
    foreach ($entry in $lineage.exports) {
        $definitionFile = Join-Path $target "source/$($entry.canonicalDefinition)"
        $definition = Get-Content -Raw -LiteralPath $definitionFile | ConvertFrom-Json
        $glb = Join-Path (Split-Path -Parent $definitionFile) $definition.source
        if ((Get-FileHash -LiteralPath $glb -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Retained master export changed: $($entry.id)" }
    }
    $records += @{ family = $family; masterSha256 = $lineage.masterSha256; exactGlbExports = 10 }
}
@{ schemaVersion = 1; success = $true; families = $records } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'retained-exports.json') -Encoding utf8NoBOM
