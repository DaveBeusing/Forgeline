[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CurrentRoot,
    [Parameter(Mandatory)][string]$BaselineRoot,
    [string]$ReportPath,
    [ValidateRange(0,1000)][double]$MaximumRegressionPercent = 15,
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
function Confirm-Distribution($Distribution) {
    if ($Distribution.samples -lt 1) { return $false }
    foreach ($name in @('mean','p50','p95','p99')) {
        $value = $Distribution.$name
        if ($null -eq $value -or -not [double]::IsFinite([double]$value) -or $value -lt 0) { throw 'Nonfinite or negative performance distribution.' }
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
            -not (Same $case.graphics.device $previous.graphics.device)) { $reason = 'hardware-environment-changed' }
        elseif ($case.fingerprint -ne $previous.fingerprint -or -not (Same $case.camera $previous.camera) -or
            -not (Same $case.profile $previous.profile) -or -not (Same $case.lighting $previous.lighting) -or
            $case.rasterDpi -ne $previous.rasterDpi -or $case.uiScale -ne $previous.uiScale -or
            -not (Same $case.requestedDisplay $previous.requestedDisplay) -or
            $case.run.runProfile -ne $previous.run.runProfile -or $case.run.cameraPath -ne $previous.run.cameraPath -or
            $case.run.cameraPathVersion -ne $previous.run.cameraPathVersion -or
            $case.run.warmupFrames -ne $previous.run.warmupFrames -or $current.samples -ne $baseline.samples) { $reason = 'workload-settings-changed' }
    }
    if ($reason) { $unqualified += $case.key; $rows += @{ key=$case.key; status=$reason; deltas=$null }; continue }
    $deltas = @()
    foreach ($metric in @('cpu','gpu')) {
        if (-not (Confirm-Distribution $case.$metric) -or -not (Confirm-Distribution $previous.$metric)) {
            $unqualified += $case.key; $deltas += @{metric=$metric;status='unavailable';percent=$null}; continue
        }
        foreach ($stat in @('mean','p50','p95','p99')) {
            $old = [double]$previous.$metric.$stat; $new = [double]$case.$metric.$stat
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
    gateRequested=[bool]$GatePerformance;regressions=$failures;unqualified=$unqualified;cases=$rows;
    interpretation='Percent changes are observed variance/trends. Paired runs and repeated cohorts are required before attributing a regression. GPU subsets have explicit counts; software and changed settings cannot satisfy a hardware gate.'} |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $report -Encoding utf8
if ($GatePerformance -and ($failures.Count -or $unqualified.Count)) { throw "Performance gate has $($failures.Count) regressions and $($unqualified.Count) unqualified cases; inspect $report." }
Write-Output "Compared $($rows.Count) performance cases; $($failures.Count) observed trends exceed threshold, $($unqualified.Count) unqualified cases."
