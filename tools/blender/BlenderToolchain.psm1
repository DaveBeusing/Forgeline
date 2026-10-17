#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-BlenderPolicy {
    $policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
    if ($policy.schemaVersion -ne 1 -or $policy.version -notmatch '^\d+\.\d+\.\d+$' -or
        $policy.architecture -ne 'x64' -or $policy.pythonExitCode -notin 1..255) {
        throw 'Invalid Blender toolchain policy.'
    }
    return $policy
}

function Resolve-BlenderExecutable {
    param([string]$BlenderPath, [Parameter(Mandatory)]$Policy, [Parameter(Mandatory)][string]$RepositoryRoot)
    $system = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } else { 'Unsupported' }
    if ($system -notin $Policy.platforms -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
        throw 'Blender authoring supports Windows/Linux x64 with PowerShell 7.4 or later. The player and headless server do not require Blender.'
    }
    $explicit = if ($BlenderPath) { $BlenderPath } else { $env:FORGELINE_BLENDER_PATH }
    if ($explicit) {
        $path = [IO.Path]::GetFullPath($explicit, $RepositoryRoot)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Blender executable not found: $path. Set -BlenderPath or FORGELINE_BLENDER_PATH to the pinned $($Policy.version) executable, not its directory."
        }
        return $path
    }
    $name = if ($IsWindows) { 'blender.exe' } else { 'blender' }
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    $candidates = @((Join-Path $RepositoryRoot "artifacts/toolchains/blender-$($Policy.version)/$name"))
    if ($IsWindows -and $env:ProgramFiles) {
        $series = ($Policy.version.Split('.')[0..1] -join '.')
        $candidates += Join-Path $env:ProgramFiles "Blender Foundation/Blender $series/blender.exe"
    }
    if ($IsLinux) { $candidates += "/opt/blender-$($Policy.version)/blender", "/usr/local/blender-$($Policy.version)/blender" }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    }
    throw "Blender $($Policy.version) x64 was not found. Install that exact version, add its directory to PATH, or set -BlenderPath / FORGELINE_BLENDER_PATH. No automatic install or fallback version is used."
}

function Resolve-BlenderOutput {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$RepositoryRoot)
    $resolved = [IO.Path]::GetFullPath($Path, $RepositoryRoot)
    $artifacts = [IO.Path]::GetFullPath('artifacts', $RepositoryRoot)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $resolved.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'Blender output must be a subdirectory of repository artifacts; production sources and runtime caches are never output targets.'
    }
    $cursor = $resolved
    while ($cursor -and $cursor.StartsWith($artifacts, $comparison)) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Blender output cannot traverse a symbolic link or junction: $cursor"
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $resolved
}

function Invoke-BlenderProcess {
    param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory, [Parameter(Mandatory)][string]$LogPrefix,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120, [string]$CancellationFile)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WorkingDirectory = $WorkingDirectory
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in @('PYTHONHOME','PYTHONPATH','PYTHONUSERBASE','PYTHONSTARTUP','VIRTUAL_ENV','CONDA_PREFIX','PYTHONOPTIMIZE','PYTHONINSPECT')) {
        [void]$start.Environment.Remove($key)
    }
    $start.Environment['PYTHONIOENCODING'] = 'utf-8'
    $start.Environment['PYTHONUTF8'] = '1'
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    $stdout = $null; $stderr = $null; $started = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($CancellationFile -and (Test-Path -LiteralPath $CancellationFile)) { throw [OperationCanceledException]::new('Blender cancelled before launch.') }
        if (-not $process.Start()) { throw 'Blender process did not start.' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(100)) {
            if ($CancellationFile -and (Test-Path -LiteralPath $CancellationFile)) { throw [OperationCanceledException]::new('Blender cancelled; process tree terminated.') }
            if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw [TimeoutException]::new("Blender exceeded ${TimeoutSeconds}s; process tree terminated. Logs: $LogPrefix") }
        }
        if (-not $stdout.Wait(5000) -or -not $stderr.Wait(5000)) { throw 'Blender log streams did not close after process exit.' }
        $result = [pscustomobject]@{ exitCode = $process.ExitCode; stdout = $stdout.Result; stderr = $stderr.Result }
        if ($result.exitCode -ne 0) {
            throw "Blender exited $($result.exitCode). Inspect $LogPrefix.stdout.log and $LogPrefix.stderr.log; Python requires bundled bpy and scripts must match the pinned version."
        }
        return $result
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        foreach ($stream in @(@{ task = $stdout; suffix = 'stdout' }, @{ task = $stderr; suffix = 'stderr' })) {
            if ($stream.task -and -not $stream.task.IsCompleted) { try { [void]$stream.task.Wait(2000) } catch {} }
            $value = if ($stream.task -and $stream.task.IsCompletedSuccessfully) { $stream.task.Result } else { '' }
            [IO.File]::WriteAllText("$LogPrefix.$($stream.suffix).log", $value, [Text.UTF8Encoding]::new($false))
        }
        $process.Dispose()
    }
}

function Test-BlenderInstallation {
    param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)]$Policy,
        [Parameter(Mandatory)][string]$RunRoot, [int]$TimeoutSeconds = 120, [string]$CancellationFile)
    $version = Invoke-BlenderProcess -Executable $Executable -Arguments @('--version') -WorkingDirectory $RunRoot -LogPrefix (Join-Path $RunRoot 'version') -TimeoutSeconds $TimeoutSeconds -CancellationFile $CancellationFile
    if ($version.stdout -notmatch '(?m)^Blender (\d+\.\d+\.\d+)(?:\s|$)' -or $Matches[1] -ne $Policy.version) {
        throw "Unsupported Blender version; exact $($Policy.version) x64 required. Select it with -BlenderPath; no newer/older version is substituted. See version.stdout.log."
    }
    $probePath = Join-Path $RunRoot 'probe.json'
    $arguments = @('--background','--factory-startup','--disable-autoexec','--offline-mode','--threads','1',
        '--python-exit-code', [string]$Policy.pythonExitCode, '--python', (Join-Path $PSScriptRoot 'probe.py'), '--', $probePath)
    $null = Invoke-BlenderProcess -Executable $Executable -Arguments $arguments -WorkingDirectory $RunRoot -LogPrefix (Join-Path $RunRoot 'probe') -TimeoutSeconds $TimeoutSeconds -CancellationFile $CancellationFile
    if (-not (Test-Path -LiteralPath $probePath)) { throw 'Blender did not produce the bundled bpy capability report; inspect probe logs.' }
    $probe = Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json
    if ($probe.schemaVersion -ne 1 -or $probe.version -ne $Policy.version -or $probe.versionCycle -ne 'release' -or
        $probe.pointerBits -ne 64 -or $probe.machine -notin @('AMD64','x86_64') -or
        $probe.system -notin $Policy.platforms -or $probe.background -isnot [bool] -or -not $probe.background -or
        $probe.bpy -isnot [bool] -or -not $probe.bpy) {
        throw 'Unsupported bpy version, release cycle, platform, architecture or background capabilities; inspect probe.json.'
    }
    return [pscustomobject]@{ executable = $Executable; executableSha256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash.ToLowerInvariant(); capabilities = $probe }
}
Export-ModuleMember -Function Get-BlenderPolicy, Resolve-BlenderExecutable, Resolve-BlenderOutput, Invoke-BlenderProcess, Test-BlenderInstallation
