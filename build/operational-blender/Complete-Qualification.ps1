#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [Parameter(Mandatory)][string]$FirstRoot, [Parameter(Mandatory)][string]$BarracksRoot, [Parameter(Mandatory)][string]$RadarRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputRoot, $repo)
& python (Join-Path $PSScriptRoot 'aggregate_qualification.py') $output "vehicle_factory=$(Join-Path $FirstRoot 'vehicle_factory')" "power_plant=$(Join-Path $FirstRoot 'power_plant')" "barracks=$(Join-Path $BarracksRoot 'barracks')" "radar=$(Join-Path $RadarRoot 'radar')"
if ($LASTEXITCODE -ne 0) { throw 'Family evidence aggregation failed.' }
$previous = $env:FORGELINE_OPERATIONAL_BLENDER_ROOT
try {
    $env:FORGELINE_OPERATIONAL_BLENDER_ROOT = $output
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.BlenderRoundTripQualificationTests 2>&1 | Out-File -LiteralPath (Join-Path $output 'typed-runtime.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw 'Typed family comparison failed.' }
    foreach ($family in @('vehicle_factory','power_plant','barracks','radar')) {
        $comparison = Get-Content -Raw -LiteralPath (Join-Path $output "$family/runtime-comparison.json") | ConvertFrom-Json
        if (@($comparison).Count -ne 3) { throw 'Typed variants missing.' }
    }
}
finally { $env:FORGELINE_OPERATIONAL_BLENDER_ROOT = $previous }
$reportPath = Join-Path $output 'qualification.json'
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
$report.typedCompiledContracts = 'passed'
$report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
