[CmdletBinding()]
param([string]$OutputRoot = 'artifacts/runtime-pipeline-contracts')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$root = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$allowed = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not ($root + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Contract outputs must remain inside repository artifacts.'
}
$root = Join-Path $root ([Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$runtime = Join-Path $root 'runtime'
New-Item -ItemType Directory -Force -Path $source | Out-Null
$compiler = Join-Path $repoRoot 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build the Release asset compiler first.' }

function Write-Texture([int]$Dimension) {
    $bytes = [byte[]]::new(18 + $Dimension * $Dimension * 4)
    $bytes[2] = 2
    [BitConverter]::GetBytes([uint16]$Dimension).CopyTo($bytes, 12)
    [BitConverter]::GetBytes([uint16]$Dimension).CopyTo($bytes, 14)
    $bytes[16] = 32; $bytes[17] = 32
    [IO.File]::WriteAllBytes((Join-Path $source 'a.tga'), $bytes)
}
function Write-Definition([string]$Format = 'rgba8Unorm') {
    @{ id = 'texture.test.a'; type = 'texture'; source = 'a.tga'; textureFormat = $Format } |
        ConvertTo-Json | Set-Content (Join-Path $source 'a.asset.json')
}
function Start-Compiler([string]$Name, [string]$Mode = 'build', [switch]$Clean) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WorkingDirectory = $repoRoot
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @($compiler, '--mode', $Mode, '--source', $source, '--runtime', $runtime,
        '--progress-log', (Join-Path $root "$Name.jsonl"))) { $start.ArgumentList.Add($argument) }
    if ($Clean) { $start.ArgumentList.Add('--clean') }
    $process = [Diagnostics.Process]::Start($start)
    return @{ process = $process; stdout = $process.StandardOutput.ReadToEndAsync();
        stderr = $process.StandardError.ReadToEndAsync(); name = $Name }
}
function Finish-Compiler($Run, [int]$ExpectedExit = 0) {
    try {
        if (-not $Run.process.WaitForExit(30000)) { throw "Compiler $($Run.name) timed out." }
        $out = $Run.stdout.GetAwaiter().GetResult(); $err = $Run.stderr.GetAwaiter().GetResult()
        $out | Set-Content (Join-Path $root "$($Run.name).stdout.log")
        $err | Set-Content (Join-Path $root "$($Run.name).stderr.log")
        if ($Run.process.ExitCode -ne $ExpectedExit) {
            throw "Compiler $($Run.name) exited $($Run.process.ExitCode): $err $out"
        }
    }
    finally {
        if (-not $Run.process.HasExited) { $Run.process.Kill($true); $Run.process.WaitForExit() }
        $Run.process.Dispose()
    }
}
Write-Texture 4
Write-Definition
# An independently held OS lock must exclude both child processes, including --clean.
$lease = [IO.File]::Open("$runtime.compile.lock", [IO.FileMode]::OpenOrCreate,
    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$first = $null; $second = $null
try {
    $first = Start-Compiler 'parallel-clean' -Clean
    $second = Start-Compiler 'parallel-incremental'
    Start-Sleep -Milliseconds 300
    if ($first.process.HasExited -or $second.process.HasExited -or (Test-Path -LiteralPath $runtime)) {
        throw 'Compiler bypassed the held cache lock.'
    }
}
finally { $lease.Dispose() }
Finish-Compiler $first
Finish-Compiler $second
Finish-Compiler (Start-Compiler 'parallel-validation' -Mode validate)
# Kill a real writer after lock acquisition, before its expensive import completes.
Write-Texture 2048
Write-Definition 'bc7Unorm'
$interrupted = Start-Compiler 'interrupted' -Clean
try {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $ownsLock = $false
    while ($deadline.Elapsed.TotalSeconds -lt 15 -and -not $interrupted.process.HasExited) {
        $log = Join-Path $root 'interrupted.jsonl'
        if (Test-Path -LiteralPath $log) {
            $ownsLock = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '"Stage":"cache-lock"' -and $_ -match '"State":"completed"' }).Count -gt 0
            if ($ownsLock) { break }
        }
        Start-Sleep -Milliseconds 10
    }
    if (-not $ownsLock -or $interrupted.process.HasExited) { throw 'Could not observe a live cache-owning writer.' }
    $interrupted.process.Kill($true); $interrupted.process.WaitForExit()
}
finally {
    if (-not $interrupted.process.HasExited) { $interrupted.process.Kill($true); $interrupted.process.WaitForExit() }
    $interrupted.stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $root 'interrupted.stdout.log')
    $interrupted.stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $root 'interrupted.stderr.log')
    $interrupted.process.Dispose()
}
Write-Texture 4
Write-Definition
Finish-Compiler (Start-Compiler 'recovery')
# Validation must work after source removal, and must not repair a damaged deployment.
if (-not [IO.Path]::GetFullPath($source).StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Source fixture escaped contract outputs." }
Remove-Item -LiteralPath $source -Recurse -Force
Finish-Compiler (Start-Compiler 'source-less-validation' -Mode validate)
$manifest = Get-Content (Join-Path $runtime 'manifest.json') -Raw | ConvertFrom-Json
$path = Join-Path $runtime $manifest.assets[0].runtimePath
$bytes = [IO.File]::ReadAllBytes($path); $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
[IO.File]::WriteAllBytes($path, $bytes)
Finish-Compiler (Start-Compiler 'damaged-validation' -Mode validate) -ExpectedExit 1
if (-not ([Linq.Enumerable]::SequenceEqual([byte[]]$bytes, [byte[]][IO.File]::ReadAllBytes($path)))) {
    throw 'Validation silently changed a damaged payload.'
}
Write-Output "Runtime asset process contracts passed: $root"