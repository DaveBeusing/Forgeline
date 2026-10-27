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
$baselineCompatibility = @()

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
    Invoke-Checked git @('worktree', 'add', '--detach', '--no-checkout', $baseline, $BaseRef)
    $created = $true
    Push-Location $baseline
    try {
        Invoke-Checked git @('sparse-checkout', 'set', 'src', 'benchmarks', 'assets/maps')
        # This uniquely created, initially empty checkout contains no user work.
        Invoke-Checked git @('reset', '--hard', $BaseRef)
        # Older regional sources fail the current CA1512 analyzer before measurement. Apply only
        # the same mechanically equivalent guards as the candidate, keeping every analyzer active.
        # Record the normalization rather than representing the cohort as an unmodified checkout.
        foreach ($relative in @(
            'src/ForgeLine.Navigation/Regional/RegionalChangeJournal.cs',
            'src/ForgeLine.Navigation/Regional/RegionalUpdateCoordinator.cs')) {
            $path = Join-Path $baseline $relative
            if (!(Test-Path -LiteralPath $path)) { continue }
            $beforeHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            $source = [System.IO.File]::ReadAllText($path)
            $pattern = 'if \(tick < (LastTick|_tick)\) throw new ArgumentOutOfRangeException\(nameof\(tick\)\);'
            $guardMatches = [regex]::Matches($source, $pattern)
            if ($guardMatches.Count -eq 0) { continue }
            $normalized = [regex]::Replace($source, $pattern,
                'ArgumentOutOfRangeException.ThrowIfLessThan(tick, $1);')
            [System.IO.File]::WriteAllText($path, $normalized, [System.Text.UTF8Encoding]::new($false))
            $baselineCompatibility += [ordered]@{
                Path = $relative
                Rule = 'CA1512 equivalent monotonic tick argument guard'
                Replacements = $guardMatches.Count
                OriginalSha256 = $beforeHash
                MeasuredSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            }
        }
        # Normalize only JSON constructor metadata in the historical cohort. The route search
        # and measured workload remain unchanged; retain hashes alongside the existing guards.
        $routeRelative = 'src/ForgeLine.Navigation/Contracts/NavigationSearchContracts.cs'
        $routePath = Join-Path $baseline $routeRelative
        if (Test-Path -LiteralPath $routePath) {
            $source = [System.IO.File]::ReadAllText($routePath)
            $marker = '    public NavigationSearchSpecification Request { get; }'
            if ($source.Contains('public sealed class NavigationRouteProposal') -and
                !$source.Contains('[System.Text.Json.Serialization.JsonConstructor]')) {
                if (!$source.Contains($marker)) { throw 'Cannot normalize historical route JSON metadata.' }
                $beforeHash = (Get-FileHash -LiteralPath $routePath -Algorithm SHA256).Hash
                $constructor = @'
    // JSON metadata must not bind a ref-struct constructor parameter.
    [System.Text.Json.Serialization.JsonConstructor]
    public NavigationRouteProposal(NavigationSearchSpecification request, Vector3 resolvedDestination,
        IReadOnlyList<Vector3> waypoints, IReadOnlyList<NavigationRegionRevision> regions,
        NavigationPathDiagnostics diagnostics)
        : this(request, resolvedDestination,
            (waypoints ?? throw new ArgumentNullException(nameof(waypoints))).ToArray(),
            (regions ?? throw new ArgumentNullException(nameof(regions))).ToArray(),
            diagnostics, Math.Max(1, waypoints.Count))
    {
    }

'@
                $normalized = $source.Insert($source.IndexOf($marker, [System.StringComparison]::Ordinal), $constructor + [Environment]::NewLine)
                [System.IO.File]::WriteAllText($routePath, $normalized, [System.Text.UTF8Encoding]::new($false))
                $baselineCompatibility += [ordered]@{
                    Path = $routeRelative
                    Rule = 'JSON collection constructor metadata; route search unchanged'
                    Replacements = 1
                    OriginalSha256 = $beforeHash
                    MeasuredSha256 = (Get-FileHash -LiteralPath $routePath -Algorithm SHA256).Hash
                }
            }
        }
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
        BaselineCompatibility = $baselineCompatibility
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
