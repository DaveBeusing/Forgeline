[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$TestExecutable,
    [Parameter(Mandatory)][string]$OutputRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path -LiteralPath $Repository).Path
$testPath = (Resolve-Path -LiteralPath $TestExecutable).Path
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a fresh host measurement directory.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$source = (& git -C $repositoryPath rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source.' }
$metadata = [ordered]@{
    repositorySource = $source
    harnessSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryPath 'tests/ForgeLine.Client.Tests/SnapshotPublication/HostPublicationProfileTests.cs')).Hash
    tieredCompilation = [Environment]::GetEnvironmentVariable('DOTNET_TieredCompilation')
    startedUtc = [DateTime]::UtcNow.ToString('O')
    exit = $null
}
$previous = [Environment]::GetEnvironmentVariable('FORGELINE_HOST_PROFILE_OUTPUT')
try {
    $env:FORGELINE_HOST_PROFILE_OUTPUT = Join-Path $outputPath 'host'
    & $testPath -class '*SnapshotPublication.HostPublicationProfileTests*' *> (Join-Path $outputPath 'host.txt')
    $metadata.exit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw 'Host publication profile failed.' }
}
finally {
    $metadata.completedUtc = [DateTime]::UtcNow.ToString('O')
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'run.json') -Encoding utf8
    [Environment]::SetEnvironmentVariable('FORGELINE_HOST_PROFILE_OUTPUT', $previous, 'Process')
}
