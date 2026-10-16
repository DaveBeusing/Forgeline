[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$ClientPath = 'src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64/ForgeLine.Client.exe',
    [string]$RuntimeAssetRoot = 'assets/runtime',
    [switch]$Capture,
    [switch]$Resume
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiQualificationMatrix.ps1"
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputRoot, $repository)
$client = [IO.Path]::GetFullPath($ClientPath, $repository)
$runtime = [IO.Path]::GetFullPath($RuntimeAssetRoot, $repository)
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source identity.' }
if ($Capture -and (& git -C $repository status --porcelain)) { throw 'Commit and build before capturing.' }
$plan = [ordered]@{
    schemaVersion = 1; commit = $commit
    clientSha256 = (Get-FileHash -LiteralPath $client).Hash
    runtimeManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash
    client = $client; runtimeRoot = $runtime
    scenarios = @(Get-UiQualificationScenarios); uiScales = @(.75,1,2)
    cohorts = @(Get-UiQualificationCohorts)
    expectedCases = @(Get-UiQualificationScenarios).Count * 3 * 15
    policy = 'Complete Medium cinematic UI matrix; three warmups/eight samples/two exact readbacks. Human and physical-display acceptance pending. Timing is whole-frame observation.'
}
$planPath = Join-Path $output 'plan.json'
if ($Resume) {
    $previous = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    foreach ($field in @('commit','clientSha256','runtimeManifestSha256','client','runtimeRoot')) {
        if ($previous.$field -cne $plan[$field]) { throw "Resume identity changed: $field" }
    }
    if (($previous.scenarios -join ',') -cne ($plan.scenarios -join ',') -or
        ($previous.cohorts.id -join ',') -cne ($plan.cohorts.id -join ',')) { throw 'Resume matrix changed.' }
} else {
    if (Test-Path -LiteralPath $output) { throw 'Use a new root, or explicitly resume the same identity.' }
    [void](New-Item -ItemType Directory -Path $output)
    $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $planPath -Encoding utf8
}
if (-not $Capture) { Write-Output "Planned $($plan.expectedCases) cases; no captures executed."; return }
& "$PSScriptRoot/Build-RuntimeAssets.ps1" -Mode validate -RuntimeRoot $runtime -NoBuild
if ($LASTEXITCODE -ne 0) { throw 'Runtime payload validation failed.' }
$clientAssembly = (Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($client)) 'ForgeLine.Client.dll')).Hash
$presentationAssembly = (Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($client)) 'ForgeLine.Presentation.dll')).Hash
$completed = @()
foreach ($cohort in $plan.cohorts) {
    # A failed/partial attempt is retained forever; resume creates a separate attempt.
    $attempt = 1
    while (Test-Path -LiteralPath (Join-Path $output "$($cohort.id)-attempt$attempt")) {
        $root = Join-Path $output "$($cohort.id)-attempt$attempt"
        $manifestPath = Join-Path $root 'manifest.json'
        if ((Test-Path -LiteralPath $manifestPath) -and -not (Test-Path -LiteralPath (Join-Path $root 'failure.json'))) {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($manifest.status -ceq 'structural-pass') { break }
        }
        $attempt++
    }
    $root = Join-Path $output "$($cohort.id)-attempt$attempt"
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Output "Capturing $($cohort.id): $($cohort.scenarios.Count * 3) cases."
        & "$PSScriptRoot/Invoke-RtsReferenceQualification.ps1" -Capture -ClientPath $client -RuntimeAssetRoot $runtime `
            -Scenes BattlefieldCombat -Profiles Medium -Zooms NormalGameplay -Displays $cohort.display `
            -RasterDpi $cohort.dpi -UiScales .75,1,2 -UiScenarios $cohort.scenarios -CinematicFrontend `
            -Warmups 3 -Samples 8 -RepeatCaptures 2 -OutputRoot $root
    }
    $manifestPath = Join-Path $root 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-UiQualificationCoverage -Manifest $manifest -Cohort $cohort -Commit $commit
    if ($manifest.clientAssemblySha256 -cne $clientAssembly -or $manifest.presentationAssemblySha256 -cne $presentationAssembly) {
        throw 'Completed cohort has different binary hashes.'
    }
    if ($completed.Count -and $manifest.runtimeFingerprint -cne $completed[0].runtimeFingerprint) { throw 'Runtime content changed between cohorts.' }
    # Revalidate originals on resume, retaining previous diagnostic reports.
    $validation = Join-Path $output "$($cohort.id)-attempt$attempt.validation-$([DateTime]::UtcNow.Ticks).json"
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $root -ReportPath $validation
    $review = Join-Path $output "$($cohort.id)-attempt$attempt.review.pending.json"
    if (-not (Test-Path -LiteralPath $review)) {
        & "$PSScriptRoot/Export-VisualReferenceReview.ps1" -EvidenceRoot $root -OutputPath $review
    }
    $completed += [ordered]@{ id=$cohort.id; root=$root; cases=@($manifest.captures).Count
        manifestSha256=(Get-FileHash -LiteralPath $manifestPath).Hash
        runtimeFingerprint=$manifest.runtimeFingerprint; hostWindowDpi=$manifest.hostWindowDpi
        keys=@($manifest.captures.key); review=$review }
    $checkpoint = Join-Path $output "$($cohort.id)-attempt$attempt.complete.json"
    if (Test-Path -LiteralPath $checkpoint) {
        $saved = Get-Content -LiteralPath $checkpoint -Raw | ConvertFrom-Json
        if ($saved.manifestSha256 -cne $completed[-1].manifestSha256) { throw 'Completed original manifest changed.' }
    } else {
        $completed[-1] | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $checkpoint -Encoding utf8
    }
}
Assert-UiQualificationUnion -Cohorts $completed -ExpectedCases $plan.expectedCases
$summary = Join-Path $output 'complete.json'
if (Test-Path -LiteralPath $summary) { throw 'This matrix already has a completion record.' }
[ordered]@{ schemaVersion=1; commit=$commit; status='structural-pass'; cases=$plan.expectedCases
    cohorts=$completed; visualAcceptance='pending'; physicalMonitor='pending'; liveMotion='pending' } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summary -Encoding utf8
Write-Output "Completed $($plan.expectedCases) distinct native cases; human acceptance pending."
