[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineProbe,
    [Parameter(Mandatory)][string]$CandidateProbe,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$BaselineSource,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$CandidateSource,
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateSet('cpu','native')][string]$Mode = 'cpu'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$baseline = (Resolve-Path -LiteralPath $BaselineProbe).Path
$candidate = (Resolve-Path -LiteralPath $CandidateProbe).Path
$assets = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh measurement directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$metadata = [ordered]@{
    baselineSource = $BaselineSource; candidateSource = $CandidateSource; mode = $Mode
    order = @('baseline-a','candidate-a','candidate-b','baseline-b')
    startedUtc = [DateTime]::UtcNow.ToString('O')
    scriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash
    probeSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'TerrainProbe.cs')).Hash
    runtimeManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $assets 'manifest.json')).Hash
    exits = [ordered]@{}
}
try {
    foreach ($label in $metadata.order) {
        $reference = $label.StartsWith('baseline')
        $probe = if ($reference) { $baseline } else { $candidate }
        $source = if ($reference) { $BaselineSource } else { $CandidateSource }
        & dotnet $probe $Mode (Join-Path $output "$label.json") $assets *> (Join-Path $output "$label.log")
        $metadata.exits[$label] = $LASTEXITCODE
        if ($LASTEXITCODE -ne 0) { throw "Terrain measurement failed: $label" }
        $report = Get-Content -LiteralPath (Join-Path $output "$label.json") -Raw | ConvertFrom-Json
        if (!$report.SourceVersion.EndsWith($source, [StringComparison]::OrdinalIgnoreCase)) { throw "Wrong product source: $label" }
        if ($report.AssetManifestSha256 -ne $metadata.runtimeManifestSha256) { throw 'Runtime manifest changed during measurement.' }
    }
} finally {
    $metadata.completedUtc = [DateTime]::UtcNow.ToString('O')
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'protocol.json') -Encoding utf8
}
