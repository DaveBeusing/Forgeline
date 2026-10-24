param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $client = Join-Path $repoRoot "src/ForgeLine.Client/ForgeLine.Client.csproj"
    & dotnet build $client --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Client build failed ($LASTEXITCODE)." }
    # Generated runtime assets are ignored by Git and can outlive the code that selected them.
    & (Join-Path $PSScriptRoot "Build-RuntimeAssets.ps1") -Mode build
    & dotnet run --project $client --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Client exited with code $LASTEXITCODE." }
}
finally { Pop-Location }
