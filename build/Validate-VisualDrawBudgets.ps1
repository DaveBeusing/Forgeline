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

# Retain the canonical roster's eleven mesh/material batches independently of terrain visibility.
if ($Metrics.instanceDrawCalls -le 0 -or $Metrics.instanceDrawCalls -gt 11) {
    throw 'Instance draw calls exceeded the canonical mesh/material batch budget of 11.'
}

if ($Metrics.totalMeasuredDrawCalls -ne ($Metrics.terrainDrawCalls + $Metrics.instanceDrawCalls) -or
    $Metrics.totalMeasuredDrawCalls -gt ($Metrics.visibleTerrainChunks + 11)) {
    throw 'Measured draw calls exceeded the visible-terrain plus instance-batch budget.'
}
