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
$expectedCohorts = 15 * [int][Math]::Ceiling($scenarios.Count / 29.0)
if ($cohorts.Count -ne $expectedCohorts -or @($cohorts.id | Select-Object -Unique).Count -ne $expectedCohorts) { throw 'Incomplete display/DPI matrix.' }
foreach ($group in $cohorts | Group-Object dpi,display) {
    $union = @($group.Group | ForEach-Object scenarios)
    if ($union.Count -ne $scenarios.Count -or @($union | Select-Object -Unique).Count -ne $scenarios.Count -or
        @(Compare-Object $scenarios $union -CaseSensitive).Count) { throw 'State banks overlap or omit fixtures.' }
}
$commit = 'a' * 40
$complete = @()
foreach ($cohort in $cohorts) {
    $dimensions = $cohort.display.Split('x')
    if ($cohort.scenarios.Count -gt 30) { throw 'Native per-request state limit exceeded.' }
    $captures = @(foreach ($scenario in $cohort.scenarios) {
        foreach ($scale in @(.75,1,2)) {
            [pscustomobject]@{ key="$($cohort.id)/$scenario/$scale";
                ui=[pscustomobject]@{scenario=$scenario;skin=[pscustomobject]@{missingAssets=0;droppedQuads=0}};
                graphics=[pscustomobject]@{health=[pscustomobject]@{submissionFaultCount=0}};
                uiScale=$scale; rasterDpi=$cohort.dpi;
                requestedDisplay=[pscustomobject]@{width=[int]$dimensions[0];height=[int]$dimensions[1]};
                actualSurface=[pscustomobject]@{width=[int]$dimensions[0];height=[int]$dimensions[1]} }
        }
    })
    $manifest = [pscustomobject]@{ status='structural-pass'; commit=$commit; captures=$captures }
    $complete += [ordered]@{keys=@($captures.key)}
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
    $skin = $captures[0].ui.skin
    $captures[0].ui.skin = $null
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $captures[0].ui.skin = $skin
    $captures[0].ui.skin.missingAssets++
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $captures[0].ui.skin.missingAssets--
    $captures[0].ui.skin.droppedQuads++
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $captures[0].ui.skin.droppedQuads--
    $captures[0].graphics.health.submissionFaultCount++
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
    $captures[0].graphics.health.submissionFaultCount--
    $captures[0].ui.scenario = 'Unknown'
    Reject { Assert-UiQualificationCoverage $manifest $cohort $commit }
}
$expectedCases = $scenarios.Count * 45
Assert-UiQualificationUnion $complete $expectedCases
Reject { Assert-UiQualificationUnion $complete[1..($complete.Count-1)] $expectedCases }
Reject { Assert-UiQualificationUnion ($complete + $complete[0]) $expectedCases }
# Keep every existing distinct-state pair together so native comparison still runs.
$distinct = [regex]::Matches($source, '\(UiReferenceScenario\.(\w+), UiReferenceScenario\.(\w+)\)')
foreach ($pair in $distinct) {
    if (-not @($cohorts | Where-Object { $_.scenarios -ccontains $pair.Groups[1].Value -and $_.scenarios -ccontains $pair.Groups[2].Value }).Count) {
        throw 'State banking split an existing native distinctness contract.'
    }
}
Write-Output "UI coverage contracts passed: $($scenarios.Count) states x 45 display/DPI/scale combinations; incomplete, duplicate, changed-source, wrong-output and unexpected cases rejected. Synthetic metadata is not native evidence."

# A future state beyond two banks must neither disappear nor duplicate an existing state.
$extended = @($scenarios) + 'AdditionalFixture'
foreach ($group in @(Get-UiQualificationCohorts -Scenarios $extended) | Group-Object dpi,display) {
    $union = @($group.Group | ForEach-Object scenarios)
    if ($union.Count -ne $extended.Count -or @(Compare-Object $extended $union -CaseSensitive).Count -or
        @($group.Group | Where-Object { $_.scenarios.Count -gt 29 }).Count) { throw 'Extended state banking lost coverage.' }
}
# Runtime playback sidecars participate even when the sealed manifest is unchanged.
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('ui-runtime-fingerprint-' + [Guid]::NewGuid().ToString('N'))
$first = Join-Path $temporary 'first'
$second = Join-Path $temporary 'second'
try {
    [void][IO.Directory]::CreateDirectory($first)
    [void][IO.Directory]::CreateDirectory($second)
    [IO.File]::WriteAllText((Join-Path $first 'manifest.json'), '{}')
    [IO.File]::WriteAllText((Join-Path $second 'manifest.json'), '{}')
    $initial = Get-UiRuntimeFingerprint $first
    if ($initial -cne (Get-UiRuntimeFingerprint $second)) { throw 'Fingerprint includes absolute root.' }
    [IO.File]::WriteAllBytes((Join-Path $first 'cue.wav'), [byte[]]@(1,2,3))
    $withSidecar = Get-UiRuntimeFingerprint $first
    if ($withSidecar -ceq $initial) { throw 'Sidecar addition was ignored.' }
    [IO.File]::WriteAllBytes((Join-Path $first 'cue.wav'), [byte[]]@(1,2,4))
    if ((Get-UiRuntimeFingerprint $first) -ceq $withSidecar) { throw 'Sidecar mutation was ignored.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Temporary path escaped its root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output 'Extended state banking and whole-runtime fingerprint contracts passed.'
