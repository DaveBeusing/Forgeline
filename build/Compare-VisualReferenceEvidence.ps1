[CmdletBinding()]
param([Parameter(Mandatory)][string]$CurrentRoot, [string]$BaselineRoot,
    [string]$ReportPath,
    [switch]$GateImages,
    [ValidateRange(0,8)][int]$ChannelTolerance = 0,
    [ValidateRange(0,0.05)][double]$MaximumChangedPixelFraction = 0)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$current = Get-Content -LiteralPath (Join-Path $CurrentRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($current.schemaVersion -ne 1) { throw 'Unsupported evidence schema.' }
if ($current.status -ne 'structural-pass') { throw "Incomplete native capture: $($current.status)." }
$baseline = $null
if ($GateImages -and -not $BaselineRoot) { throw 'Image gate requires a baseline.' }
if ($GateImages -and -not ('ReferenceImageComparison' -as [type])) { Add-Type -Path "$PSScriptRoot/ReferenceImageComparison.cs" }
if ($BaselineRoot) {
    if ([IO.Path]::GetFullPath($CurrentRoot) -eq [IO.Path]::GetFullPath($BaselineRoot)) { throw 'Baseline and current evidence must be distinct.' }
    $baseline = Get-Content -LiteralPath (Join-Path $BaselineRoot 'manifest.json') -Raw | ConvertFrom-Json
    if ($baseline.schemaVersion -ne 1) { throw 'Unsupported baseline schema.' }
    if ($baseline.status -ne 'structural-pass') { throw 'Baseline is not structurally qualified.' }
}
function Confirm-Image([string]$Root, [string]$Image, [string]$Hash, [int]$Width, [int]$Height) {
    if ([IO.Path]::GetFileName($Image) -ne $Image) { throw 'Image path escapes evidence root.' }
    $path = Join-Path $Root $Image
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) { throw "Image checksum mismatch: $Image" }
    $stream = [IO.File]::OpenRead($path)
    try { $header = [byte[]]::new(24); $stream.ReadExactly($header) }
    finally { $stream.Dispose() }
    if ([Convert]::ToHexString($header[0..7]) -ne '89504E470D0A1A0A') { throw "Invalid PNG signature: $Image" }
    $actualWidth = $header[16]*16777216 + $header[17]*65536 + $header[18]*256 + $header[19]
    $actualHeight = $header[20]*16777216 + $header[21]*65536 + $header[22]*256 + $header[23]
    if ($actualWidth -ne $Width -or $actualHeight -ne $Height) { throw "PNG resolution mismatch: $Image" }
}
$seen = @{}
$imageFailures = [Collections.Generic.List[string]]::new()
if (@($current.captures).Count -eq 0) { throw 'Empty native evidence cannot qualify.' }
if ($null -ne $baseline) {
    $baselineKeys = @{}
    foreach ($capture in $baseline.captures) {
        if ($baselineKeys.ContainsKey($capture.key)) { throw 'Duplicate baseline key.' }; $baselineKeys[$capture.key] = $true
        if (-not $capture.repeatPixelsEqual -or $capture.actualSurface.width -ne $capture.requestedDisplay.width -or
            $capture.actualSurface.height -ne $capture.requestedDisplay.height -or $capture.graphics.resources.textureBindingFailureCount -ne 0 -or
            $capture.materials.bindingFailureCount -ne 0 -or $capture.instances.fallbackMeshInstances -ne 0) { throw 'Invalid baseline native contract.' }
        Confirm-Image $BaselineRoot $capture.image $capture.imageSha256 $capture.actualSurface.width $capture.actualSurface.height
    }
    if ($GateImages -and @($baseline.captures).Count -ne @($current.captures).Count) { $imageFailures.Add('matrix cardinality changed') }
}
$rows = foreach ($capture in $current.captures) {
    if ($seen.ContainsKey($capture.key)) { throw "Duplicate capture key: $($capture.key)" }; $seen[$capture.key] = $true
    if ($capture.actualSurface.width -ne $capture.requestedDisplay.width -or $capture.actualSurface.height -ne $capture.requestedDisplay.height) { throw 'Unqualified native dimensions.' }
    if (-not $capture.repeatPixelsEqual -or $capture.graphics.resources.textureBindingFailureCount -ne 0 -or $capture.materials.bindingFailureCount -ne 0 -or $capture.instances.fallbackMeshInstances -ne 0) { throw 'Failed deterministic/resource capture contract.' }
    Confirm-Image $CurrentRoot $capture.image $capture.imageSha256 $capture.actualSurface.width $capture.actualSurface.height
    foreach ($motion in $capture.motionImages) { Confirm-Image $CurrentRoot $motion.image $motion.imageSha256 $capture.actualSurface.width $capture.actualSurface.height }
    $previous = $null
    if ($null -ne $baseline) { $previous = @($baseline.captures | Where-Object key -eq $capture.key); if ($previous.Count -ne 1) { $previous = $null } else { $previous = $previous[0] } }
    if ($null -ne $previous) { Confirm-Image $BaselineRoot $previous.image $previous.imageSha256 $previous.actualSurface.width $previous.actualSurface.height }
    $pixelComparison = $null
    $equivalentHardware = ($null -ne $previous -and $baseline.driver -eq $current.driver -and $previous.graphics.device.adapterName -eq $capture.graphics.device.adapterName -and $baseline.software -eq $current.software)
    if ($GateImages) {
        $sameInputs = $null -ne $previous -and $previous.fingerprint -eq $capture.fingerprint -and $equivalentHardware
        foreach ($property in @('camera','requestedDisplay','profile','lighting','rasterDpi','uiScale')) {
            if ($sameInputs) { $sameInputs = ($capture.$property | ConvertTo-Json -Depth 20 -Compress) -ceq ($previous.$property | ConvertTo-Json -Depth 20 -Compress) }
        }
        if (-not $sameInputs) { $imageFailures.Add("$($capture.key): changed or missing fixture/device/settings require human review") }
        else {
            $pixelComparison = [ReferenceImageComparison]::Compare((Join-Path $CurrentRoot $capture.image), (Join-Path $BaselineRoot $previous.image),
                $capture.actualSurface.width, $capture.actualSurface.height, $ChannelTolerance, $MaximumChangedPixelFraction)
            if (-not $pixelComparison.WithinTolerance) { $imageFailures.Add("$($capture.key): native pixel delta exceeds declared tolerance") }
        }
    }
    @{
        key = $capture.key; structural = 'pass'; visualReview = 'pending'
        comparison = $(if ($null -eq $previous) {'no-matched-baseline'} elseif ($previous.fingerprint -ne $capture.fingerprint) {'fixture-changed'} elseif ($previous.imageSha256 -eq $capture.imageSha256) {'exact-file-match'} else {'pixels-require-human-review'})
        equivalentHardware = $equivalentHardware; pixelComparison = $pixelComparison
        cpuMeanMs = $capture.cpu.mean; gpuMeanMs = $capture.gpu.mean
        cpuMeanDeltaMs = $(if ($null -ne $previous) {$capture.cpu.mean-$previous.cpu.mean} else {$null})
    }
}
if (-not $ReportPath) { $ReportPath = Join-Path $CurrentRoot 'comparison.json' }
if ($BaselineRoot -and [IO.Path]::GetFullPath($ReportPath).StartsWith([IO.Path]::GetFullPath($BaselineRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Comparison report may not write into baseline evidence.' }
if (Test-Path -LiteralPath $ReportPath) { throw 'Comparison report already exists; select a new report path.' }
@{schemaVersion=1; commit=$current.commit; structural='pass'; visualAcceptance='pending'; baselineCommit=$(if ($baseline) {$baseline.commit} else {$null}); captures=@($rows);
    imageGateRequested=[bool]$GateImages;channelTolerance=$ChannelTolerance;maximumChangedPixelFraction=$MaximumChangedPixelFraction;imageFailures=@($imageFailures);
    policy='Checksums and native dimensions are structural checks. No accepted goldens, similarity threshold, art-quality acceptance or portable timing gate is inferred.'} |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding utf8
if ($GateImages -and $imageFailures.Count) { throw "Image gate failed for $($imageFailures.Count) cases; inspect $ReportPath. Human visual acceptance remains pending." }
Write-Output "Qualified $(@($rows).Count) native images; visual acceptance remains human review."
