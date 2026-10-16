Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiQualificationMatrix.ps1"
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid UI matrix was accepted.' }
}
$source = Get-Content -LiteralPath "$PSScriptRoot/../src/ForgeLine.Client/UiReferenceScenario.cs" -Raw
$enum = [regex]::Match($source, 'enum UiReferenceScenario\s*\{([^}]+)\}').Groups[1].Value
$scenarios = @($enum.Split(',').Trim() | Where-Object { $_ -and $_ -cne 'World' })
if (@(Compare-Object $scenarios @(Get-UiQualificationScenarios) -CaseSensitive).Count) {
    throw 'Qualification matrix does not cover every named UI fixture.'
}
$cohorts = @(Get-UiQualificationCohorts)
if ($cohorts.Count -ne 15 -or @($cohorts.id | Select-Object -Unique).Count -ne 15) { throw 'Incomplete display/DPI matrix.' }
$commit = 'a' * 40
foreach ($cohort in $cohorts) {
    $dimensions = $cohort.display.Split('x')
    $captures = @(foreach ($scenario in $scenarios) {
        foreach ($scale in @(.75,1,2)) {
            [pscustomobject]@{ key="$($cohort.id)/$scenario/$scale"; ui=[pscustomobject]@{scenario=$scenario};
                uiScale=$scale; rasterDpi=$cohort.dpi;
                requestedDisplay=[pscustomobject]@{width=[int]$dimensions[0];height=[int]$dimensions[1]};
                actualSurface=[pscustomobject]@{width=[int]$dimensions[0];height=[int]$dimensions[1]} }
        }
    })
    $manifest = [pscustomobject]@{ status='structural-pass'; commit=$commit; captures=$captures }
    Assert-UiQualificationCoverage $manifest $cohort $commit
    $manifest.captures = $captures[1..($captures.Count-1)]
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $manifest.captures = $captures + $captures[0]
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $manifest.captures = $captures
    $manifest.status = 'capturing'
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $manifest.status = 'structural-pass'
    Reject { Assert-UiQualificationCoverage $manifest $cohort ('b' * 40) }
    $captures[0].actualSurface.width++
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $captures[0].actualSurface.width--
    $captures[0].rasterDpi++
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $captures[0].rasterDpi--
    $captures[0].ui.scenario = 'Unknown'
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
}
Write-Output "UI coverage contracts passed: $($scenarios.Count) states x 45 display/DPI/scale combinations; incomplete, duplicate, changed-source, wrong-output and unexpected cases rejected. Synthetic metadata is not native evidence."
