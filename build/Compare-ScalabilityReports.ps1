[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$Output
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$before = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json -Depth 64
$after = Get-Content -LiteralPath $Candidate -Raw | ConvertFrom-Json -Depth 64
foreach ($required in @('HardwareReference', 'CpuModel', 'ClockAndPowerSettings')) {
    if ([string]::IsNullOrWhiteSpace($before.$required) -or [string]::IsNullOrWhiteSpace($after.$required)) {
        throw "Comparison requires recorded $required; unknown hardware cannot qualify a regression."
    }
}
foreach ($key in @('SchemaVersion', 'Backend', 'Runtime', 'OS', 'Architecture', 'ProcessorCount', 'Processor', 'CpuModel', 'HardwareReference', 'DriverMetadata', 'ClockAndPowerSettings')) {
    if ($before.$key -ne $after.$key) { throw "Incomparable reports: $key differs." }
}
function Get-Cases($report) {
    $cases = @{}
    foreach ($entry in $report.Results) {
        $measurement = if ($entry.PSObject.Properties['Measurement']) { $entry.Measurement } else { $entry }
        if ($measurement.PSObject.Properties['Name']) { $cases[$measurement.Name] = $measurement }
    }
    return $cases
}
$oldCases = Get-Cases $before
$newCases = Get-Cases $after
if ($oldCases.Count -ne $newCases.Count) { throw 'Workload matrix differs.' }
$rows = foreach ($name in $oldCases.Keys | Sort-Object) {
    if (-not $newCases.ContainsKey($name)) { throw "Candidate lacks $name." }
    $old = $oldCases[$name]; $new = $newCases[$name]
    foreach ($key in @('Scope', 'Samples', 'Warmup', 'BudgetMilliseconds')) {
        if ($old.$key -ne $new.$key) { throw "Workload $name differs in $key." }
    }
    foreach ($key in @('Scene', 'Viewport', 'Alpha', 'PhaseTimingEnabled', 'Seed', 'TickRate', 'ActualEntities')) {
        $oldValue = if ($old.Counters -and $old.Counters.PSObject.Properties[$key]) { $old.Counters.$key | ConvertTo-Json -Depth 8 -Compress } else { $null }
        $newValue = if ($new.Counters -and $new.Counters.PSObject.Properties[$key]) { $new.Counters.$key | ConvertTo-Json -Depth 8 -Compress } else { $null }
        if ($oldValue -ne $newValue) { throw "Workload $name counter $key differs." }
    }
    [ordered]@{ Name = $name; BaselineTiming = $old.Timing; CandidateTiming = $new.Timing
        BaselineBytesPerOperation = $old.AllocatedBytesPerOperation; CandidateBytesPerOperation = $new.AllocatedBytesPerOperation
        P95ChangePercent = if ($old.Timing.P95Milliseconds -gt 0) { 100 * ($new.Timing.P95Milliseconds / $old.Timing.P95Milliseconds - 1) } else { $null } }
}
$fullOutput = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force ([System.IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
[ordered]@{ SchemaVersion = 1; Policy = 'Advisory paired reports. Repeat ABBA comparisons on controlled reference hardware; a percentile delta is not a statistical regression gate.'
    BaselineRevision = $before.Revision; CandidateRevision = $after.Revision; HardwareReference = $after.HardwareReference; Results = @($rows) } |
    ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $fullOutput -Encoding utf8
