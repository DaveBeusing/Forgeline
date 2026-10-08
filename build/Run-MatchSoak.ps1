[CmdletBinding()]
param(
    [ValidateSet("validation", "gameplay")]
    [string]$Profile = "validation",

    [ValidateRange(1, 100)]
    [int]$Matches = 5,

    [ValidateRange(1, [long]::MaxValue)]
    [long]$TicksPerMatch = 80000,

    [ValidateRange(0, [long]::MaxValue)]
    [long]$Seed = 2026,

    [string]$Output = "artifacts/match-soak.json",

    [string]$TelemetryOutput = "artifacts/match-soak.telemetry.json",

    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repositoryRoot

try {
    $arguments = @(
        "run",
        "--project", "src/ForgeLine.Headless/ForgeLine.Headless.csproj",
        "--configuration", "Release",
        $(if ($NoBuild) { "--no-build" }),
        "--",
        "--scenario", "central-divide",
        "--profile", $Profile,
        "--ticks", $TicksPerMatch.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--seed", $Seed.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--matches", $Matches.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--diagnostics-output", $Output,
        "--telemetry-output", $TelemetryOutput
    )

    if ($Profile -eq "validation") {
        $arguments += "--require-terminal"
    }

    $arguments = @($arguments | Where-Object { $null -ne $_ })
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Match soak run failed with exit code $LASTEXITCODE."
    }

    Write-Host "Match soak completed. Diagnostics: $Output"
    Write-Host "Match soak telemetry: $TelemetryOutput"
}
finally {
    Pop-Location
}
