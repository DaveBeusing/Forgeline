param(
    [ValidateSet("development", "build", "validate")]
    [string]$Mode = "development",
    [string]$SourceRoot = "assets/source",
    [string]$RuntimeRoot = "assets/runtime",
    [string]$QualificationOutput = "artifacts/asset-qualification.json",
    [string]$ProgressLog = "artifacts/asset-compilation.jsonl",
    [switch]$ForceRebuild,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$Mode = $Mode.ToLowerInvariant()
$repoRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $repoRoot "tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj"
if ($Mode -eq "validate" -and $ForceRebuild) { throw "Validation cannot rebuild assets." }
if (-not $NoBuild) {
    & dotnet build $compiler --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Asset compiler build failed ($LASTEXITCODE)." }
}
$compilerArguments = @("run", "--project", $compiler, "--configuration", "Release", "--no-build", "--",
    "--mode", $Mode, "--source", $SourceRoot, "--runtime", $RuntimeRoot,
    "--qualification-output", $QualificationOutput, "--progress-log", $ProgressLog)
if ($ForceRebuild) { $compilerArguments += "--clean" }
& dotnet @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Runtime asset $Mode failed ($LASTEXITCODE). No package may consume this output." }
