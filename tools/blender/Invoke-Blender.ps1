#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Check')][string]$Action = 'Check',
    [string]$BlenderPath,
    [string]$OutputRoot = 'artifacts/blender/check',
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120,
    [string]$CancellationFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repositoryRoot = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
if ($CancellationFile) { $CancellationFile = [IO.Path]::GetFullPath($CancellationFile, $repositoryRoot) }
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repositoryRoot
[void][IO.Directory]::CreateDirectory($output)
$lease = $null
try {
    $lease = [IO.File]::Open((Join-Path $output '.blender.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $runRoot = Join-Path $output ('runs/' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($runRoot)
    $report = [ordered]@{ schemaVersion = 1; action = $Action; success = $false; runRoot = $runRoot; toolchain = $null; error = $null }
    try {
        $policy = Get-BlenderPolicy
        $executable = Resolve-BlenderExecutable -BlenderPath $BlenderPath -Policy $policy -RepositoryRoot $repositoryRoot
        $report.toolchain = Test-BlenderInstallation -Executable $executable -Policy $policy -RunRoot $runRoot -TimeoutSeconds $TimeoutSeconds -CancellationFile $CancellationFile
        $report.success = $true
        Write-Host "Blender $($policy.version) x64: bundled bpy/background checks passed. Report: $output/report.json"
    }
    catch { $report.error = $_.Exception.Message; throw }
    finally { $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'report.json') -Encoding utf8NoBOM }
}
finally { if ($lease) { $lease.Dispose() } }
