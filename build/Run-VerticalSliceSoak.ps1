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

    [string]$Output = "artifacts/vertical-slice-soak.json"
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
        "--",
        "--scenario", "vertical-slice",
        "--profile", $Profile,
        "--ticks", $TicksPerMatch.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--seed", $Seed.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--matches", $Matches.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--diagnostics-output", $Output
    )

    if ($Profile -eq "validation") {
        $arguments += "--require-terminal"
    }

    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Vertical-slice soak run failed with exit code $LASTEXITCODE."
    }

    Write-Host "Vertical-slice soak completed. Report: $Output"
}
finally {
    Pop-Location
}
