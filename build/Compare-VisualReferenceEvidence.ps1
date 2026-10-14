[CmdletBinding()]
param([Parameter(Mandatory)][string]$CurrentRoot, [string]$BaselineRoot,
    [string]$ReportPath,
    [switch]$GateImages,
    [string]$ApprovalPath,
    [ValidateRange(0,8)][int]$ChannelTolerance = 0,
    [ValidateRange(0,0.05)][double]$MaximumChangedPixelFraction = 0)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$current = Get-Content -LiteralPath (Join-Path $CurrentRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($current.schemaVersion -ne 1) { throw 'Unsupported evidence schema.' }
if ($current.status -ne 'structural-pass') { throw "Incomplete native capture: $($current.status)." }
function Confirm-Identity($Manifest) {
    if ($Manifest.commit -notmatch '^[a-fA-F0-9]{40}$' -or
        -not $Manifest.buildIdentity.EndsWith($Manifest.commit,[StringComparison]::OrdinalIgnoreCase) -or
        $Manifest.runtimeFingerprint -notmatch '^[a-fA-F0-9]{64}$') { throw 'Source/build/runtime identity mismatch or missing identity.' }
}
Confirm-Identity $current
$baseline = $null
if ($GateImages -and -not $BaselineRoot) { throw 'Image gate requires a baseline.' }
if ($GateImages -and -not ('ReferenceImageComparison' -as [type])) { Add-Type -Path "$PSScriptRoot/ReferenceImageComparison.cs" }
if ($BaselineRoot) {
    if ([IO.Path]::GetFullPath($CurrentRoot) -eq [IO.Path]::GetFullPath($BaselineRoot)) { throw 'Baseline and current evidence must be distinct.' }
    $baseline = Get-Content -LiteralPath (Join-Path $BaselineRoot 'manifest.json') -Raw | ConvertFrom-Json
    if ($baseline.schemaVersion -ne 1) { throw 'Unsupported baseline schema.' }
    if ($baseline.status -ne 'structural-pass') { throw 'Baseline is not structurally qualified.' }
    Confirm-Identity $baseline
}
if ($GateImages) {
    if (-not $ApprovalPath) { throw 'Image gate requires an explicit human baseline review.' }
    $approval = Get-Content -LiteralPath $ApprovalPath -Raw | ConvertFrom-Json
    try { $reviewTime = [DateTimeOffset]$approval.reviewedAtUtc }
    catch { throw 'Review requires a valid UTC decision time.' }
    if ($approval.schemaVersion -ne 1 -or $approval.manifestSha256 -ne (Get-FileHash -LiteralPath (Join-Path $BaselineRoot 'manifest.json')).Hash -or
        $approval.commit -ne $baseline.commit -or [string]::IsNullOrWhiteSpace($approval.reviewer) -or
        [string]::IsNullOrWhiteSpace($approval.rationale) -or $reviewTime.Year -lt 2000 -or $reviewTime.Offset -ne [TimeSpan]::Zero -or
        @($approval.captures).Count -ne @($baseline.captures).Count) { throw 'Baseline review is incomplete or bound to different evidence.' }
    $reviewKeys = @{}
    foreach ($decision in $approval.captures) {
        if ($reviewKeys.ContainsKey($decision.key)) { throw 'Duplicate reviewed key.' }; $reviewKeys[$decision.key] = $true
        $image = @($baseline.captures | Where-Object key -CEQ $decision.key)
        if ($image.Count -ne 1 -or $decision.imageSha256 -ne $image[0].imageSha256 -or $decision.decision -cne 'accepted') { throw 'Unaccepted or mismatched baseline image.' }
        foreach ($item in @('typography','hierarchy','contrast','focus','hitAlignment','feedbackSeverity','occlusion','intelligence')) {
            if ($decision.rubric.$item -cne 'accepted') { throw "Incomplete human rubric: $item" }
        }
    }
}
function Confirm-Image([string]$Root, [string]$Image, [string]$Hash, [int]$Width, [int]$Height) {
    if ([IO.Path]::GetFileName($Image) -ne $Image) { throw 'Image path escapes evidence root.' }
    $path = Join-Path $Root $Image
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hash) { throw "Image checksum mismatch: $Image" }
    $stream = [IO.File]::OpenRead($path)
    try { $header = [byte[]]::new(29); $stream.ReadExactly($header) }
    finally { $stream.Dispose() }
    if ([Convert]::ToHexString($header[0..7]) -ne '89504E470D0A1A0A') { throw "Invalid PNG signature: $Image" }
    if ($header[24] -ne 8 -or $header[25] -ne 6 -or $header[26] -ne 0 -or $header[27] -ne 0 -or $header[28] -ne 0) { throw 'Expected unscaled native RGBA8 PNG.' }
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
    if ($capture.PSObject.Properties.Name -contains 'ui') {
        if ($capture.ui.scenario -ne 'World' -and ($capture.ui.vertices -le 0 -or $capture.ui.vertices -gt 1048576 -or
            -not $capture.ui.text.atlasReady -or $capture.ui.text.missingGlyphs -ne 0 -or $capture.ui.text.droppedGlyphs -ne 0)) { throw 'Incomplete or unbounded UI text/geometry.' }
        $regionKeys = @{}
        foreach ($region in $capture.ui.regions) {
            if ($regionKeys.ContainsKey($region.name) -or $region.x -lt 0 -or $region.y -lt 0 -or $region.width -le 0 -or $region.height -le 0 -or
                $region.x+$region.width -gt $capture.actualSurface.width -or $region.y+$region.height -gt $capture.actualSurface.height -or
                $region.pixelSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid or duplicate UI region.' }
            $regionKeys[$region.name]=$true
        }
    }
    Confirm-Image $CurrentRoot $capture.image $capture.imageSha256 $capture.actualSurface.width $capture.actualSurface.height
    foreach ($motion in $capture.motionImages) { Confirm-Image $CurrentRoot $motion.image $motion.imageSha256 $capture.actualSurface.width $capture.actualSurface.height }
    $previous = $null
    if ($null -ne $baseline) { $previous = @($baseline.captures | Where-Object key -eq $capture.key); if ($previous.Count -ne 1) { $previous = $null } else { $previous = $previous[0] } }
    if ($null -ne $previous) { Confirm-Image $BaselineRoot $previous.image $previous.imageSha256 $previous.actualSurface.width $previous.actualSurface.height }
    $pixelComparison = $null
    $equivalentHardware = ($null -ne $previous -and -not [string]::IsNullOrWhiteSpace($current.driver) -and $baseline.driver -eq $current.driver -and $previous.graphics.device.adapterName -eq $capture.graphics.device.adapterName -and $baseline.software -eq $current.software)
    if ($GateImages) {
        $sameInputs = $null -ne $previous -and $previous.fingerprint -eq $capture.fingerprint -and $equivalentHardware -and
            $baseline.commit -eq $current.commit -and $baseline.buildIdentity -eq $current.buildIdentity -and $baseline.runtimeFingerprint -eq $current.runtimeFingerprint
        foreach ($assembly in @('clientAssemblySha256','presentationAssemblySha256')) {
            if ($sameInputs -and ($current.PSObject.Properties.Name -contains $assembly -or $baseline.PSObject.Properties.Name -contains $assembly)) {
                $sameInputs = $current.PSObject.Properties.Name -contains $assembly -and $baseline.PSObject.Properties.Name -contains $assembly -and
                    $current.$assembly -eq $baseline.$assembly
            }
        }
        foreach ($property in @('camera','requestedDisplay','profile','lighting','rasterDpi','uiScale')) {
            if ($sameInputs) { $sameInputs = ($capture.$property | ConvertTo-Json -Depth 20 -Compress) -ceq ($previous.$property | ConvertTo-Json -Depth 20 -Compress) }
        }
        if ($sameInputs -and $capture.PSObject.Properties.Name -contains 'ui') {
            $sameInputs = $previous.PSObject.Properties.Name -contains 'ui' -and
                (($capture.ui | Select-Object scenario,fixtureVersion,layoutId,captureOffset | ConvertTo-Json -Compress) -ceq
                 ($previous.ui | Select-Object scenario,fixtureVersion,layoutId,captureOffset | ConvertTo-Json -Compress))
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
        sameSource = ($null -ne $baseline -and $baseline.commit -eq $current.commit)
        sameRuntime = ($null -ne $baseline -and $baseline.runtimeFingerprint -eq $current.runtimeFingerprint)
        regionChanges = $(if ($null -ne $previous -and $capture.PSObject.Properties.Name -contains 'ui' -and $previous.PSObject.Properties.Name -contains 'ui') {
            @($capture.ui.regions | ForEach-Object { $region=$_; $old=@($previous.ui.regions | Where-Object name -CEQ $region.name)
                @{name=$region.name;unchanged=($old.Count -eq 1 -and $old[0].pixelSha256 -eq $region.pixelSha256);nonuniform=$region.nonuniform} })
        } else {@()})
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
