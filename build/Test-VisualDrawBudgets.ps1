[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Metrics([int]$Draws, $Lod1 = $null, $Lod2 = $null) {
    $result = [ordered]@{
        visibleTerrainChunks = 15; totalTerrainChunks = 144; terrainDrawCalls = 15
        visibleInstances = 1000; instanceDrawCalls = $Draws; totalMeasuredDrawCalls = 15 + $Draws + 1 + 48
        framePasses = [pscustomobject]@{ compositeDrawCalls = 1 }; directionalShadows = [pscustomobject]@{ drawCalls = 48 }
    }
    if ($null -ne $Lod1) { $result.lod1Instances = $Lod1 }
    if ($null -ne $Lod2) { $result.lod2Instances = $Lod2 }
    return [pscustomobject]$result
}
function Check($Value) { & "$PSScriptRoot/Validate-VisualDrawBudgets.ps1" -Metrics $Value }
function Reject($Value) {
    $rejected = $false
    try { Check $Value } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid draw-budget report was accepted.' }
}

# Legacy reports and zero additional LODs retain the original eleven-batch ceiling.
Check (Metrics 11)
Reject (Metrics 12)
Check (Metrics 11 0 0)
Reject (Metrics 12 0 0)
# One instance in each extra LOD permits only one extra batch in each.
Check (Metrics 13 1 1)
Reject (Metrics 14 1 1)
# The recorded wide borderless view has five LOD1 and one LOD2 instances.
Check (Metrics 13 5 1)
Check (Metrics 17 5 1)
Reject (Metrics 18 5 1)
# More instances do not lift the three-LOD ceiling above thirty-three batches.
Check (Metrics 33 500 500)
Reject (Metrics 34 500 500)
Reject (Metrics 13 -1 1)
Reject (Metrics 13 1.5 1)
Reject (Metrics 13 ([double]::NaN) 1)
Reject (Metrics 13 1)
Reject (Metrics 13 1000 1)
$invalid = Metrics 13 1 1
$invalid.totalMeasuredDrawCalls++
Reject $invalid
$invalid = Metrics 13 1 1
$invalid.terrainDrawCalls++
Reject $invalid
Write-Output 'LOD-aware visual draw-budget contracts passed.'
