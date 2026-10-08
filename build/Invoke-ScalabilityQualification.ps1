[CmdletBinding()]
param(
    [ValidateRange(16, 65536)][int]$Samples = 1024,
    [string]$OutputDirectory = 'artifacts/scalability',
    [switch]$NoBuild,
    [switch]$Gpu,
    [switch]$Soak,
    [ValidateRange(16, 65536)][int]$SoakSamples = 16384
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $repositoryRoot
$previousRevision = $env:FORGELINE_QUALIFICATION_REVISION
try {
    $env:FORGELINE_QUALIFICATION_REVISION = (& git rev-parse HEAD).Trim()
    if ((& git status --porcelain --untracked-files=no)) {
        $env:FORGELINE_QUALIFICATION_REVISION += '-working-tree'
    }
    if (-not $NoBuild) {
        dotnet build ForgeLine.sln --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
    $outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    foreach ($hostName in @('Simulation', 'Navigation', 'Rendering')) {
        $project = "benchmarks/ForgeLine.$hostName.Benchmarks/ForgeLine.$hostName.Benchmarks.csproj"
        dotnet run --project $project --configuration Release --no-build -- --scalability (Join-Path $outputRoot "$($hostName.ToLowerInvariant()).json") $Samples
        if ($LASTEXITCODE -ne 0) { throw "$hostName scalability qualification failed." }
    }
    dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release --no-build -- --frame-hotpaths (Join-Path $outputRoot 'extraction-publication.json')
    if ($LASTEXITCODE -ne 0) { throw 'Extraction/publication qualification failed.' }
    if ($Soak) {
        foreach ($hostName in @('Simulation', 'Rendering')) {
            dotnet run --project "benchmarks/ForgeLine.$hostName.Benchmarks/ForgeLine.$hostName.Benchmarks.csproj" --configuration Release --no-build -- --scalability (Join-Path $outputRoot "$($hostName.ToLowerInvariant())-soak.json") $SoakSamples
            if ($LASTEXITCODE -ne 0) { throw "$hostName scalability soak failed." }
        }
    }
    if ($Gpu) {
        dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release --no-build -- --gpu-scalability (Join-Path $outputRoot 'gpu.json') $Samples
        if ($LASTEXITCODE -ne 0) { throw 'Native GPU scalability qualification failed; CPU reports remain available.' }
    }
} finally {
    $env:FORGELINE_QUALIFICATION_REVISION = $previousRevision
    Pop-Location
}
