[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [object]$Metrics
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Terrain submits one draw per visible chunk; viewport size changes that count.
if ($Metrics.visibleTerrainChunks -le 0 -or
    $Metrics.visibleTerrainChunks -gt $Metrics.totalTerrainChunks -or
    $Metrics.terrainDrawCalls -ne $Metrics.visibleTerrainChunks) {
    throw 'Terrain draw count must equal the valid visible chunk count.'
}

# Retain eleven canonical base batches; each additional LOD can split those same families.
# A LOD with fewer instances cannot introduce more batches than it has instances.
$instanceBatchBudget = 11
$lodProperties = @('lod1Instances', 'lod2Instances')
$presentLods = @($lodProperties | Where-Object { $null -ne $Metrics.PSObject.Properties[$_] })
if ($presentLods.Count -ne 0 -and $presentLods.Count -ne 2) {
    throw 'LOD draw qualification requires both instance counts.'
}
$additionalLodInstances = 0
foreach ($name in $presentLods) {
    $count = $Metrics.$name
    if ($null -eq $count -or -not [double]::IsFinite([double]$count) -or
        $count -lt 0 -or [Math]::Floor([double]$count) -ne $count) {
        throw 'LOD instance counts must be finite nonnegative integers.'
    }
    $additionalLodInstances += $count
    $instanceBatchBudget += [Math]::Min(11, $count)
}
if ($presentLods.Count -gt 0 -and $additionalLodInstances -gt $Metrics.visibleInstances) {
    throw 'LOD instances exceeded the visible instance count.'
}
if ($Metrics.instanceDrawCalls -le 0 -or $Metrics.instanceDrawCalls -gt $instanceBatchBudget) {
    throw "Instance draw calls exceeded the canonical LOD-aware batch budget of $instanceBatchBudget."
}

$compositeDrawCalls = 0
if ($null -ne $Metrics.PSObject.Properties['framePasses'] -and $null -ne $Metrics.framePasses -and
    $null -ne $Metrics.framePasses.PSObject.Properties['compositeDrawCalls']) {
    $compositeDrawCalls = $Metrics.framePasses.compositeDrawCalls
    if ($compositeDrawCalls -lt 0 -or $compositeDrawCalls -gt 1) {
        throw 'Scene composition must submit at most one fullscreen draw.'
    }
}

$shadowDrawCalls = 0
if ($null -ne $Metrics.PSObject.Properties['directionalShadows'] -and $null -ne $Metrics.directionalShadows) {
    $shadowDrawCalls = $Metrics.directionalShadows.drawCalls
    if ($shadowDrawCalls -lt 0 -or $shadowDrawCalls -gt 256) {
        throw 'Directional shadow draws exceeded the bounded quality budget of 256.'
    }
}

if ($Metrics.totalMeasuredDrawCalls -ne ($Metrics.terrainDrawCalls + $Metrics.instanceDrawCalls + $compositeDrawCalls + $shadowDrawCalls) -or
    $Metrics.totalMeasuredDrawCalls -gt ($Metrics.visibleTerrainChunks + $instanceBatchBudget + $compositeDrawCalls + $shadowDrawCalls)) {
    throw 'Measured draw calls exceeded the visible-terrain plus instance-batch budget.'
}
