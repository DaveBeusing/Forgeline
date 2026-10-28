param([string]$Configuration = "Release", [string]$OutputRoot = "artifacts/debrief-contracts")
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputRoot, $repoRoot)
$owned = [IO.Path]::GetFullPath("artifacts", $repoRoot) + [IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($owned, [StringComparison]::OrdinalIgnoreCase)) { throw "Contract outputs must stay inside artifacts." }
if (Test-Path -LiteralPath $destination) { throw "Use a fresh contract output directory." }
New-Item -ItemType Directory -Path $destination | Out-Null
$headless = Join-Path $repoRoot "src/ForgeLine.Headless/ForgeLine.Headless.csproj"
function Invoke-DebriefContract([string[]]$Arguments, [int]$Expected = 0) {
    & dotnet run --project $headless --configuration $Configuration --no-build -- @Arguments
    if ($LASTEXITCODE -ne $Expected) { throw "Unexpected CLI result: $LASTEXITCODE, expected $Expected." }
    # An asserted negative case succeeded as a contract; do not leak its process code
    # to callers such as the Actions PowerShell wrapper. Unexpected codes throw above.
    $global:LASTEXITCODE = 0
}
$first = Join-Path $destination "first.json"
$repeat = Join-Path $destination "repeat.json"
Invoke-DebriefContract @('--scenario','central-divide','--profile','validation','--ticks','12','--seed','7511','--debrief-output',$first)
Invoke-DebriefContract @('--scenario','central-divide','--profile','validation','--ticks','12','--seed','7511','--debrief-output',$repeat)
$report = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json -Depth 8
if ($report.SchemaVersion -ne 1 -or $report.Player -ne 1 -or $report.Outcome -ne 0 -or $null -ne $report.ResultAtTick -or $report.Events.Count -gt 128) { throw "Invalid incomplete player report." }
if ((Get-FileHash -LiteralPath $first).Hash -ne (Get-FileHash -LiteralPath $repeat).Hash) { throw "Repeated seeded summaries differ." }
$text = Get-Content -LiteralPath $first -Raw
foreach ($forbidden in @('Actor','Target','OrderIdentity','Position','Debug','Metrics','Opponent')) {
    if ($text.Contains('"' + $forbidden + '"')) { throw "Private or diagnostic property leaked: $forbidden" }
}
Invoke-DebriefContract @('--scenario','central-divide','--ticks','1','--debrief-player','1') 1
Invoke-DebriefContract @('--scenario','central-divide','--ticks','1','--matches','2','--debrief-output',(Join-Path $destination 'invalid.json')) 1
Invoke-DebriefContract @('--scenario','central-divide','--ticks','1','--debrief-player','999','--debrief-output',(Join-Path $destination 'foreign.json')) 1
Invoke-DebriefContract @('--scenario','central-divide','--ticks','1','--save-output',(Join-Path $destination 'shared.json'),'--debrief-output',(Join-Path $destination 'shared.json')) 1
$thresholds = Join-Path $destination 'thresholds.json'
[IO.File]::WriteAllText($thresholds, '{}')
$thresholdHash = (Get-FileHash -LiteralPath $thresholds).Hash
Invoke-DebriefContract @('--scenario','central-divide','--ticks','1','--telemetry-output',(Join-Path $destination 'telemetry.json'),'--pacing-thresholds',(Join-Path $destination 'thresholds.json'),'--debrief-output',(Join-Path $destination 'thresholds.json')) 1
if ((Get-FileHash -LiteralPath $thresholds).Hash -ne $thresholdHash) { throw 'Debrief export modified pacing threshold input.' }
Write-Host "Player debrief CLI contracts passed."
