[CmdletBinding()]
param([string]$OutputRoot = 'artifacts/pacing-contracts')
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
$output = [System.IO.Path]::GetFullPath($OutputRoot, $repoRoot)
if (Test-Path -LiteralPath $output) { throw 'Pacing contract output must be a fresh directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$program = Join-Path $repoRoot 'src/ForgeLine.Headless/bin/Release/net10.0/ForgeLine.Headless.dll'
$common = @($program, '--scenario', 'central-divide', '--profile', 'gameplay', '--ticks', '4', '--seed', '17')
$cases = [System.Collections.Generic.List[object]]::new()
function Invoke-Case([string]$Name, [string[]]$Arguments, [int]$Expected) {
    & dotnet @Arguments *> (Join-Path $output ($Name + '.log'))
    $actual = $LASTEXITCODE
    $cases.Add([pscustomobject]@{ Name = $Name; Expected = $Expected; Actual = $actual })
    if ($actual -ne $Expected) { throw "Unexpected exit for $Name : $actual (expected $Expected)." }
}
$baseline = Join-Path $output 'baseline.json'
$current = Join-Path $output 'current.json'
Invoke-Case 'baseline' ($common + @('--telemetry-output', $baseline)) 0
Invoke-Case 'repeat' ($common + @('--telemetry-output', $current, '--telemetry-baseline', $baseline)) 0
$a = Get-Content -LiteralPath $baseline -Raw | ConvertFrom-Json
$b = Get-Content -LiteralPath $current -Raw | ConvertFrom-Json
if ($b.PacingBenchmarks.BaselineCompatibility -ne 'Comparable') { throw 'Identical cohort rejected.' }
if (($a.PacingBenchmarks.Runs | ConvertTo-Json -Depth 32 -Compress) -cne ($b.PacingBenchmarks.Runs | ConvertTo-Json -Depth 32 -Compress)) { throw 'Seeded pacing metrics changed.' }
foreach ($delta in $b.PacingBenchmarks.Differences) {
    if ($null -ne $delta.Delta -and $delta.Delta -ne 0) { throw 'Repeat produced a nonzero pacing delta.' }
}
$incompatible = Join-Path $output 'incompatible.json'
Invoke-Case 'incompatible' ($common + @('--seed', '18', '--telemetry-output', $incompatible, '--telemetry-baseline', $baseline)) 0
$c = Get-Content -LiteralPath $incompatible -Raw | ConvertFrom-Json
if ($c.PacingBenchmarks.BaselineCompatibility -ne 'IncompatibleContext' -or $c.PacingBenchmarks.Differences.Count -ne 0 -or $c.Comparison.Count -ne 0) { throw 'Incompatible cohorts were compared.' }
$bad = Join-Path $output 'bad-thresholds.json'
Set-Content -LiteralPath $bad -Value '{"FirstContactMaximumSeconds":-1}' -NoNewline
Invoke-Case 'invalid-thresholds' ($common + @('--telemetry-output', (Join-Path $output 'bad.json'), '--pacing-thresholds', $bad)) 1
Set-Content -LiteralPath $bad -Value '{"UnrecognizedLimit":1}' -NoNewline
Invoke-Case 'unknown-threshold' ($common + @('--telemetry-output', (Join-Path $output 'bad.json'), '--pacing-thresholds', $bad)) 1
Invoke-Case 'threshold-without-output' ($common + @('--pacing-thresholds', $bad)) 1
Invoke-Case 'missing-threshold-file' ($common + @('--telemetry-output', (Join-Path $output 'bad.json'), '--pacing-thresholds', (Join-Path $output 'missing.json'))) 1
Invoke-Case 'output-is-directory' ($common + @('--telemetry-output', $output)) 1
$policy = Join-Path $output 'warning-policy.json'
$warningReport = Join-Path $output 'warnings.json'
Set-Content -LiteralPath $policy -Value '{"FirstContactMaximumSeconds":0.05,"SupplyPressureMaximumSeconds":0.05}' -NoNewline
Invoke-Case 'observed-warning-paths' ($common + @('--telemetry-output', $warningReport, '--pacing-thresholds', $policy)) 0
$warnings = (Get-Content -LiteralPath $warningReport -Raw | ConvertFrom-Json).PacingBenchmarks.Runs[0].Warnings.Code
if ('late_contact' -notin $warnings -or 'sustained_supply_pressure' -notin $warnings) { throw 'Controlled advisory limits did not report observed delays/shortages.' }
$cases | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json')
Write-Host "Pacing command-line contracts passed: $($cases.Count)."
exit 0
