[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineRepository,
    [Parameter(Mandatory)][string]$BaselineTests,
    [Parameter(Mandatory)][string]$CandidateTests,
    [Parameter(Mandatory)][string]$BaselineBenchmark,
    [Parameter(Mandatory)][string]$CandidateBenchmark,
    [Parameter(Mandatory)][string]$RuntimeAssets,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BaselineClient,
    [string]$CandidateClient
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$baseline = (Resolve-Path -LiteralPath $BaselineRepository).Path
$runtime = (Resolve-Path -LiteralPath $RuntimeAssets).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh comparison directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($label in @('baseline-a','candidate-a','candidate-b','baseline-b')) {
    $before = $label.StartsWith('baseline')
    $source = if ($before) { $baseline } else { $repository }
    $tests = if ($before) { $BaselineTests } else { $CandidateTests }
    $benchmark = if ($before) { $BaselineBenchmark } else { $CandidateBenchmark }
    & "$PSScriptRoot/InvokeMeasurements.ps1" -Repository $source -TestExecutable $tests -RuntimeAssets $runtime -OutputRoot (Join-Path $output $label) -RenderingBenchmark $benchmark
    if ($LASTEXITCODE -ne 0) { throw "CPU cohort failed: $label" }
}
& python "$PSScriptRoot/SummarizeMeasurements.py" $output *> (Join-Path $output 'summary.txt')
if ($LASTEXITCODE -ne 0) { throw 'Equal-work summary failed.' }
if ([bool]$BaselineClient -ne [bool]$CandidateClient) { throw 'Provide both native clients, or neither.' }
if ($BaselineClient) {
    $probeRelative = 'tests/ForgeLine.Client.Tests/SnapshotPublication/GameplayCaptureProfileTests.cs'
    $probe = Join-Path $baseline $probeRelative
    if ((Get-FileHash -LiteralPath $probe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repository $probeRelative)).Hash) { throw 'Baseline probe differs.' }
    & git -C $baseline ls-files --error-unmatch -- $probeRelative *> $null
    if ($LASTEXITCODE -eq 1) {
        # Only the verified copied probe is removed; never a tracked baseline source.
        Remove-Item -LiteralPath $probe
    } elseif ($LASTEXITCODE -ne 0) { throw 'Cannot inspect baseline probe ownership.' }
    $common = @{ Capture=$true; RuntimeAssetRoot=$runtime; Scenes=@('IndustrialBase','MainBattleTankProduction'); Profiles=@('High'); Zooms=@('CloseTactical','NormalGameplay','Strategic'); Displays=@('1920x1080'); UiScenarios=@('World'); RasterDpi=96; Warmups=3; Samples=32; RepeatCaptures=2 }
    $nativeBaseline = Join-Path $output 'native-baseline'
    & "$baseline/build/Invoke-RtsReferenceQualification.ps1" @common -ClientPath $BaselineClient -OutputRoot $nativeBaseline *> (Join-Path $output 'native-baseline.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Native baseline failed.' }
    & "$repository/build/Invoke-RtsReferenceQualification.ps1" @common -ClientPath $CandidateClient -OutputRoot (Join-Path $output 'native-candidate') -BaselineRoot $nativeBaseline *> (Join-Path $output 'native-candidate.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Native candidate failed.' }
}
