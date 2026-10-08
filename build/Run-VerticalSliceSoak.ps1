# Compatibility entry point for existing automation; use Run-MatchSoak.ps1 for new calls.
[CmdletBinding()]
param(
    [ValidateSet("validation", "gameplay")][string]$Profile = "validation",
    [ValidateRange(1, 100)][int]$Matches = 5,
    [ValidateRange(1, [long]::MaxValue)][long]$TicksPerMatch = 80000,
    [ValidateRange(0, [long]::MaxValue)][long]$Seed = 2026,
    [string]$Output = "artifacts/vertical-slice-soak.json",
    [string]$TelemetryOutput = "artifacts/vertical-slice-soak.telemetry.json",
    [switch]$NoBuild
)
& (Join-Path $PSScriptRoot 'Run-MatchSoak.ps1') -Profile $Profile -Matches $Matches `
    -TicksPerMatch $TicksPerMatch -Seed $Seed -Output $Output -TelemetryOutput $TelemetryOutput -NoBuild:$NoBuild
