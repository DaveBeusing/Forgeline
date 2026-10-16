[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $BaseRef,
    [string] $OutputRoot = 'artifacts/movement-quality/navigation',
    [ValidateRange(32, 4096)]
    [int] $Samples = 128,
    [ValidateRange(1, 8)]
    [int] $Rounds = 3
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [System.IO.Path]::GetFullPath($OutputRoot, $root)
$baseline = Join-Path ([System.IO.Path]::GetTempPath()) ('navigation-baseline-' + [guid]::NewGuid().ToString('N'))
$project = 'benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj'
$assembly = 'benchmarks/ForgeLine.Navigation.Benchmarks/bin/Release/net10.0/ForgeLine.Navigation.Benchmarks.dll'
$created = $false

function Invoke-Checked {
    param([string] $Executable, [string[]] $Parameters)
    & $Executable @Parameters
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable exited with code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $root
try {
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve candidate revision.' }
    Invoke-Checked git @('worktree', 'add', '--detach', $baseline, $BaseRef)
    $created = $true
    Push-Location $baseline
    try {
        Invoke-Checked dotnet @('restore', $project)
        Invoke-Checked dotnet @('build', $project, '--configuration', 'Release', '--no-restore')
    }
    finally { Pop-Location }

    $cohorts = @(
        @{ Name = 'baseline'; Root = $baseline },
        @{ Name = 'candidate'; Root = $root }
    )
    for ($round = 1; $round -le $Rounds; $round++) {
        # Alternate process order to expose, rather than hide, runner drift.
        $order = if ($round % 2 -eq 0) { @(1, 0) } else { @(0, 1) }
        foreach ($index in $order) {
            $cohort = $cohorts[$index]
            $report = Join-Path $output ($cohort.Name + '-' + $round + '.json')
            Push-Location $cohort.Root
            try {
                Invoke-Checked dotnet @($assembly, '--scalability', $report, $Samples.ToString([System.Globalization.CultureInfo]::InvariantCulture))
                if (!(Test-Path $report)) { throw "Missing navigation report: $report" }
                Write-Host ('Navigation measurement: ' + $cohort.Name + ', round ' + $round)
                Get-Content $report -Raw | Write-Host
            }
            finally { Pop-Location }
        }
    }

    [ordered]@{
        BaselineRevision = $BaseRef
        CandidateRevision = $head
        Rounds = $Rounds
        SamplesPerWorkload = $Samples
        Scope = 'Existing warmed Central Divide navigation CPU workloads; no graphics, collision or full-match throughput claim.'
        Interpretation = 'Retain each cohort, allocations and variance. Hosted runner timings are not a target-hardware frame-rate qualification.'
    } | ConvertTo-Json | Set-Content (Join-Path $output 'comparison.json') -Encoding utf8
}
finally {
    if ($created) {
        & git worktree remove --force $baseline
        if ($LASTEXITCODE -ne 0) { Write-Warning "Temporary baseline worktree cleanup failed: $baseline" }
    }
    Pop-Location
}
