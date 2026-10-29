[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CurrentRoot,
    [Parameter(Mandatory)][string]$BaselineRoot,
    [string]$ReportPath,
    [ValidateRange(0,1000)][double]$MaximumRegressionPercent = 15,
    [ValidateSet('Matched','StaticMemory')][string]$ComparisonMode = 'Matched',
    [switch]$GatePerformance
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Read-Evidence([string]$Root) {
    $manifest = Get-Content -LiteralPath (Join-Path $Root 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.status -ne 'structural-pass' -or @($manifest.captures).Count -eq 0) { throw 'Performance requires complete qualified native evidence.' }
    $keys = @{}
    foreach ($case in $manifest.captures) {
        if ($keys.ContainsKey($case.key)) { throw 'Duplicate performance key.' }
        $keys[$case.key] = $true
        if (-not $case.repeatPixelsEqual -or $case.instances.fallbackMeshInstances -ne 0 -or
            $case.materials.bindingFailureCount -ne 0 -or $case.graphics.resources.textureBindingFailureCount -ne 0 -or
            $case.graphics.health.submissionFaultCount -ne 0 -or $case.actualSurface.width -ne $case.requestedDisplay.width -or
            $case.actualSurface.height -ne $case.requestedDisplay.height) { throw 'Invalid native performance contract.' }
    }
    return $manifest
}
function Same($Left, $Right) { return ($Left | ConvertTo-Json -Depth 20 -Compress) -ceq ($Right | ConvertTo-Json -Depth 20 -Compress) }
function Device-Identity($Device) {
    # Optional support probes are informational; no advanced render path is active.
    # Active features remain part of Effective-Profile and must match independently.
    $identity = [ordered]@{}
    foreach ($property in $Device.PSObject.Properties | Sort-Object Name) {
        if ($property.Name -ne 'advanced') { $identity[$property.Name] = $property.Value }
    }
    return [pscustomobject]$identity
}
function Effective-Profile($Profile) {
    $values = @{}
    foreach ($property in $Profile.PSObject.Properties) {
        if ($property.Name -notin @('requestedSettings','terrainResidency')) { $values[$property.Name] = $property.Value }
    }
    if (-not $values.ContainsKey('environment')) { $values['environment'] = [pscustomobject][ordered]@{daylight=1;cloudCover=0;wetness=0} }
    if (-not $values.ContainsKey('vegetation')) { $values['vegetation'] = [pscustomobject][ordered]@{density=1;maximumDistanceMeters=4000} }
    foreach ($default in @(@('resolutionScale',1),@('textures','Full'),@('vegetationMotion',$true))) {
        if (-not $values.ContainsKey($default[0])) { $values[$default[0]] = $default[1] }
    }
    $ordered = [ordered]@{}
    foreach ($key in @($values.Keys | Sort-Object)) { $ordered[$key] = $values[$key] }
    return [pscustomobject]$ordered
}
function Surface-Inputs($Case) {
    $haze = $Case.PSObject.Properties['localHaze']
    $marks = $Case.PSObject.Properties['surfaceMarks']
    return [pscustomobject][ordered]@{localHaze=$(if($haze){$haze.Value}else{$null});surfaceMarks=$(if($marks -and $marks.Value.Count){@($marks.Value)}else{@()})}
}
function Other-Controls($Controls) {
    $values = [ordered]@{}
    foreach ($property in $Controls.PSObject.Properties | Sort-Object Name) {
        if ($property.Name -ne 'staticBufferUploadFallback') { $values[$property.Name] = $property.Value }
    }
    return [pscustomobject]$values
}
function Confirm-Distribution($Distribution) {
    if ($Distribution.samples -lt 1) { return $false }
    if ($Distribution.samples -gt 8192 -or $Distribution.samples -ne [math]::Floor($Distribution.samples)) { throw 'Invalid performance sample count.' }
    foreach ($name in @('mean','p50','p95','p99')) {
        $value = $Distribution.$name
        if ($null -eq $value -or -not [double]::IsFinite([double]$value) -or $value -le 0) { throw 'Nonfinite or negative performance distribution.' }
    }
    return $true
}
$current = Read-Evidence $CurrentRoot
$baseline = Read-Evidence $BaselineRoot
if ([IO.Path]::GetFullPath($CurrentRoot) -eq [IO.Path]::GetFullPath($BaselineRoot)) { throw 'Comparison needs independent runs.' }
if (-not $ReportPath) { $ReportPath = Join-Path $CurrentRoot 'performance-comparison.json' }
$baselinePath = [IO.Path]::GetFullPath($BaselineRoot).TrimEnd('\','/')
$report = [IO.Path]::GetFullPath($ReportPath)
if ($report -eq $baselinePath -or $report.StartsWith($baselinePath + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Report cannot modify baseline evidence.' }
if (Test-Path -LiteralPath $report) { throw 'Performance report already exists.' }
$rows = @(); $failures = @(); $unqualified = @()
foreach ($case in $current.captures) {
    $previous = @($baseline.captures | Where-Object key -eq $case.key)
    $reason = $null
    if ($previous.Count -ne 1) { $reason = 'missing-baseline' }
    else {
        $previous = $previous[0]
        if ($current.software -or $baseline.software) { $reason = 'software-functional-only' }
        elseif (-not (Same $current.environment $baseline.environment) -or $current.driver -ne $baseline.driver -or
            -not (Same (Device-Identity $case.graphics.device) (Device-Identity $previous.graphics.device))) { $reason = 'hardware-environment-changed' }
        elseif ($current.runtimeFingerprint -ne $baseline.runtimeFingerprint) { $reason = 'runtime-payload-changed' }
        elseif ($case.fingerprint -ne $previous.fingerprint -or -not (Same $case.camera $previous.camera) -or
            -not (Same (Effective-Profile $case.profile) (Effective-Profile $previous.profile)) -or -not (Same $case.lighting $previous.lighting) -or
            -not (Same (Surface-Inputs $case) (Surface-Inputs $previous)) -or
            $case.rasterDpi -ne $previous.rasterDpi -or $case.uiScale -ne $previous.uiScale -or
            -not (Same $case.requestedDisplay $previous.requestedDisplay) -or
            $case.run.runProfile -ne $previous.run.runProfile -or $case.run.cameraPath -ne $previous.run.cameraPath -or
            $case.run.cameraPathVersion -ne $previous.run.cameraPathVersion -or
            $case.run.warmupFrames -ne $previous.run.warmupFrames -or $current.samples -ne $baseline.samples) { $reason = 'workload-settings-changed' }
    }
    $cp = $case.PSObject.Properties['performance']
    $bp = if ($previous -and @($previous).Count -eq 1) { $previous.PSObject.Properties['performance'] } else { $null }
    if (-not $reason -and $ComparisonMode -eq 'Matched') {
        $cc = if ($cp) { $cp.Value.PSObject.Properties['controls'] } else { $null }
        $bc = if ($bp) { $bp.Value.PSObject.Properties['controls'] } else { $null }
        if (($null -ne $cc) -ne ($null -ne $bc) -or ($cc -and -not (Same $cc.Value $bc.Value))) { $reason = 'runtime-controls-changed-or-unrecorded' }
    }
    if (-not $reason -and $ComparisonMode -eq 'StaticMemory' -and (-not $cp -or -not $bp -or
        -not $cp.Value.PSObject.Properties['controls'] -or -not $bp.Value.PSObject.Properties['controls'])) { $reason = 'unrecorded-static-memory-control' }
    if (-not $reason -and $ComparisonMode -eq 'StaticMemory' -and
        (-not (Same (Other-Controls $cp.Value.controls) (Other-Controls $bp.Value.controls)))) { $reason = 'other-runtime-controls-changed' }
    if ($reason) { $unqualified += $case.key; $rows += @{ key=$case.key; status=$reason; deltas=$null }; continue }
    $deltas = @()
    foreach ($metric in @('cpu','gpu')) {
        $now = if ($metric -eq 'gpu' -and $cp) { $cp.Value.gpu } else { $case.$metric }
        $before = if ($metric -eq 'gpu' -and $bp) { $bp.Value.gpu } else { $previous.$metric }
        if (-not (Confirm-Distribution $now) -or -not (Confirm-Distribution $before)) {
            $unqualified += $case.key; $deltas += @{metric=$metric;status='unavailable';percent=$null}; continue
        }
        foreach ($stat in @('mean','p50','p95','p99')) {
            $old = [double]$before.$stat; $new = [double]$now.$stat
            $percent = if ($old -gt 0) { 100 * ($new - $old) / $old } else { $null }
            $regressed = $null -ne $percent -and $percent -gt $MaximumRegressionPercent
            if ($regressed) { $failures += "$($case.key)/$metric/$stat" }
            if ($null -eq $percent) { $unqualified += $case.key }
            $deltas += @{metric=$metric;statistic=$stat;baselineMs=$old;currentMs=$new;deltaMs=$new-$old;percent=$percent;regressed=$regressed}
        }
    }
    $rows += @{key=$case.key;status='comparable';deltas=$deltas;assetPayloadChanged=$current.runtimeFingerprint -ne $baseline.runtimeFingerprint}
}
if (@($baseline.captures).Count -ne @($current.captures).Count) { $unqualified += 'matrix-cardinality' }
@{schemaVersion=1;currentCommit=$current.commit;baselineCommit=$baseline.commit;maximumRegressionPercent=$MaximumRegressionPercent;
    gateRequested=[bool]$GatePerformance;comparisonMode=$ComparisonMode;regressions=$failures;unqualified=$unqualified;cases=$rows;
    interpretation='Percent changes are observed variance/trends. Paired runs and repeated cohorts are required before attributing a regression. GPU subsets have explicit counts; software and changed settings cannot satisfy a hardware gate.'} |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $report -Encoding utf8
if ($GatePerformance -and ($failures.Count -or $unqualified.Count)) { throw "Performance gate has $($failures.Count) regressions and $($unqualified.Count) unqualified cases; inspect $report." }
Write-Output "Compared $($rows.Count) performance cases; $($failures.Count) observed trends exceed threshold, $($unqualified.Count) unqualified cases."
