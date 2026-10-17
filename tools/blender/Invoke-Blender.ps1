#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Check','Smoke','Script')][string]$Action = 'Check',
    [string]$Script,
    [string]$InputRoot = '.',
    [string[]]$ScriptArguments = @(),
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
$inputPath = [IO.Path]::GetFullPath($InputRoot, $repositoryRoot)
if (-not (Test-Path -LiteralPath $inputPath -PathType Container)) { throw "Blender input directory does not exist: $inputPath" }
$scriptPath = $null
if ($Action -eq 'Script') {
    if (-not $Script) { throw 'Action Script requires -Script pointing to a trusted Python file.' }
    $scriptPath = [IO.Path]::GetFullPath($Script, $repositoryRoot)
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf) -or [IO.Path]::GetExtension($scriptPath) -ne '.py') { throw "Python script does not exist: $scriptPath" }
}
elseif ($Script -or $ScriptArguments.Count -gt 0) { throw 'Script and ScriptArguments are accepted only with -Action Script.' }
if ($Action -eq 'Smoke') { $scriptPath = Join-Path $PSScriptRoot 'smoke.py' }
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
        if ($Action -ne 'Check') {
            $requestPath = Join-Path $runRoot 'request.json'
            [ordered]@{ schemaVersion = 1; inputRoot = $inputPath; outputRoot = $output; runRoot = $runRoot; arguments = @($ScriptArguments) } |
                ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
            $arguments = @('--background','--factory-startup','--disable-autoexec','--offline-mode','--threads','1',
                '--python-exit-code', [string]$policy.pythonExitCode, '--python', $scriptPath, '--', '--request', $requestPath)
            $null = Invoke-BlenderProcess -Executable $executable -Arguments $arguments -WorkingDirectory $runRoot -LogPrefix (Join-Path $runRoot 'script') -TimeoutSeconds $TimeoutSeconds -CancellationFile $CancellationFile
        }
        $report.success = $true
        Write-Host "Blender $($policy.version) x64: $Action passed. Report: $output/report.json"
    }
    catch { $report.error = $_.Exception.Message; throw }
    finally {
        $json = $report | ConvertTo-Json -Depth 12
        $json | Set-Content -LiteralPath (Join-Path $runRoot 'report.json') -Encoding utf8NoBOM
        $json | Set-Content -LiteralPath (Join-Path $output 'report.json') -Encoding utf8NoBOM
    }
}
finally { if ($lease) { $lease.Dispose() } }
