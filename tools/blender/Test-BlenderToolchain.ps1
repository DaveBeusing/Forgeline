#requires -Version 7.4
[CmdletBinding()]
param([string]$OutputRoot = 'artifacts/blender-contracts')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repositoryRoot = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repositoryRoot
if (Test-Path -LiteralPath $root) { throw 'Contract outputs must be fresh; use another artifacts path.' }
[void][IO.Directory]::CreateDirectory($root)
$fixtureProject = Join-Path $PSScriptRoot 'tests/BlenderFixture/BlenderFixture.csproj'
$fixtureRoot = Join-Path $root 'fixture with spaces'
& dotnet build $fixtureProject --configuration Release --output $fixtureRoot 2>&1 | Out-File -LiteralPath (Join-Path $root 'fixture-build.log') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw "Cannot build protocol fixture: $root/fixture-build.log" }
$hostName = if ($IsWindows) { 'BlenderFixture.exe' } else { 'BlenderFixture' }
$fixture = Join-Path $fixtureRoot $hostName
$launcher = Join-Path $PSScriptRoot 'Invoke-Blender.ps1'
$policy = Get-BlenderPolicy
$results = [Collections.Generic.List[object]]::new()
$envKeys = @('FORGELINE_BLENDER_PATH','FORGELINE_BLENDER_FIXTURE_MODE','FORGELINE_BLENDER_FIXTURE_PIDFILE','FORGELINE_BLENDER_FIXTURE_CANCELFILE',
    'PYTHONHOME','PYTHONPATH','PYTHONUSERBASE','PYTHONSTARTUP','VIRTUAL_ENV','CONDA_PREFIX','PYTHONOPTIMIZE','PYTHONINSPECT','PATH','ProgramFiles')
$saved = @{}
foreach ($key in $envKeys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key) }
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Check([string]$Name, [scriptblock]$Body) {
    & $Body
    $results.Add(@{ name = $Name; passed = $true })
    Write-Host "Passed: $Name"
}
function Expect-Failure([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body } catch { $message = $_.Exception.Message }
    Assert-Contract ($null -ne $message -and $message -match $Pattern) "Expected failure matching '$Pattern'; got '$message'."
}
function Invoke-Fixture([string]$Mode, [string]$Action = 'Check', [int]$Timeout = 20, [string]$Cancel = '') {
    $env:FORGELINE_BLENDER_FIXTURE_MODE = $Mode
    $arguments = @{ Action = $Action; BlenderPath = $fixture; OutputRoot = (Join-Path $root $Mode); TimeoutSeconds = $Timeout }
    if ($Action -eq 'Script') { $arguments.Script = Join-Path $PSScriptRoot 'smoke.py' }
    if ($Cancel) { $arguments.CancellationFile = $Cancel }
    & $launcher @arguments
}
try {
    Remove-Item Env:FORGELINE_BLENDER_PATH -ErrorAction SilentlyContinue
    Check 'explicit executable with spaces' { Invoke-Fixture 'ok' }
    Check 'environment discovery and explicit precedence' {
        $env:FORGELINE_BLENDER_PATH = $fixture
        Assert-Contract ((Resolve-BlenderExecutable -Policy $policy -RepositoryRoot $repositoryRoot) -eq $fixture) 'Environment path was not selected.'
        $env:FORGELINE_BLENDER_PATH = Join-Path $root 'absent.exe'
        Assert-Contract ((Resolve-BlenderExecutable -BlenderPath $fixture -Policy $policy -RepositoryRoot $repositoryRoot) -eq $fixture) 'Explicit argument did not take precedence.'
        Expect-Failure { Resolve-BlenderExecutable -Policy $policy -RepositoryRoot $repositoryRoot } 'not found'
        Remove-Item Env:FORGELINE_BLENDER_PATH
    }
    Check 'PATH lookup' {
        $name = if ($IsWindows) { 'blender.exe' } else { 'blender' }
        $pathRoot = Join-Path $root 'path lookup'; [void][IO.Directory]::CreateDirectory($pathRoot)
        Copy-Item -LiteralPath $fixture -Destination (Join-Path $pathRoot $name)
        $env:PATH = $pathRoot + [IO.Path]::PathSeparator + $saved.PATH
        Assert-Contract ((Resolve-BlenderExecutable -Policy $policy -RepositoryRoot $repositoryRoot) -eq (Join-Path $pathRoot $name)) 'PATH executable was not selected.'
        $env:PATH = $saved.PATH
    }
    Check 'portable install discovery' {
        $fakeRepo = Join-Path $root 'discovery repository'
        $portable = Join-Path $fakeRepo "artifacts/toolchains/blender-$($policy.version)"
        [void][IO.Directory]::CreateDirectory($portable)
        $name = if ($IsWindows) { 'blender.exe' } else { 'blender' }
        Copy-Item -LiteralPath $fixture -Destination (Join-Path $portable $name)
        $env:PATH = ''
        try { Assert-Contract ((Resolve-BlenderExecutable -Policy $policy -RepositoryRoot $fakeRepo) -eq (Join-Path $portable $name)) 'Portable install was not selected.' }
        finally { $env:PATH = $saved.PATH }
    }
    if ($IsWindows) {
        Check 'supported Windows installation discovery' {
            $fakePrograms = Join-Path $root 'program files'
            $install = Join-Path $fakePrograms 'Blender Foundation/Blender 5.2'
            [void][IO.Directory]::CreateDirectory($install)
            Copy-Item -LiteralPath $fixture -Destination (Join-Path $install 'blender.exe')
            $env:PATH = ''; $env:ProgramFiles = $fakePrograms
            try { Assert-Contract ((Resolve-BlenderExecutable -Policy $policy -RepositoryRoot (Join-Path $root 'no-portable')) -eq (Join-Path $install 'blender.exe')) 'Windows install was not selected.' }
            finally { $env:PATH = $saved.PATH; $env:ProgramFiles = $saved.ProgramFiles }
        }
    }
    Check 'missing executable' { Expect-Failure { & $launcher -BlenderPath (Join-Path $root 'absent.exe') -OutputRoot (Join-Path $root 'absent') } 'not found' }
    Check 'invalid executable preserves actionable start error' {
        $bad = Join-Path $root 'invalid.exe'; Set-Content -LiteralPath $bad -Value 'invalid'
        Expect-Failure { & $launcher -BlenderPath $bad -OutputRoot (Join-Path $root 'invalid') } 'start|executable|format|application|Anwendung|Prozess|Fehler'
        Assert-Contract (-not (Get-Content -LiteralPath (Join-Path $root 'invalid/report.json') -Raw | ConvertFrom-Json).success) 'Invalid executable reported success.'
    }
    foreach ($mode in @('version','bpy','arch','machine','cycle','foreground','probe-version','missing-report','malformed')) {
        $current = $mode
        Check "reject $current" {
            Expect-Failure { Invoke-Fixture $current } 'version|bpy|architecture|capabilities|property|report|schemaVersion'
            $report = Get-Content -LiteralPath (Join-Path $root "$current/report.json") -Raw | ConvertFrom-Json
            Assert-Contract (-not $report.success -and [bool]$report.error) 'Failure report was not retained.'
        }
    }
    Check 'script nonzero exit and UTF-8 stderr retained' {
        Expect-Failure { Invoke-Fixture 'exit' 'Script' } 'exited 17'
        $report = Get-Content -LiteralPath (Join-Path $root 'exit/report.json') -Raw | ConvertFrom-Json
        $stderr = Get-Content -LiteralPath (Join-Path $report.runRoot 'script.stderr.log') -Raw -Encoding utf8
        Assert-Contract ($stderr.Contains('Grüße 日本語')) 'UTF-8 failure log was lost.'
    }
    Check 'literal Unicode paths, argument boundaries and isolated Python environment' {
        foreach ($key in @('PYTHONHOME','PYTHONPATH','PYTHONUSERBASE','PYTHONSTARTUP','VIRTUAL_ENV','CONDA_PREFIX','PYTHONOPTIMIZE','PYTHONINSPECT')) { [Environment]::SetEnvironmentVariable($key, 'fixture-contamination') }
        $env:FORGELINE_BLENDER_FIXTURE_MODE = 'arguments'
        $inputDir = Join-Path $root 'input with spaces Grüße [a]'; [void][IO.Directory]::CreateDirectory($inputDir)
        $outDir = Join-Path $root 'output with spaces 日本語 [b]'
        $values = @('two words','Grüße 日本語','literal "quotes"','--background','x; y & z','')
        Push-Location -LiteralPath $inputDir
        try { & $launcher -Action Script -BlenderPath $fixture -Script tools/blender/smoke.py -InputRoot $inputDir -OutputRoot $outDir -ScriptArguments $values }
        finally { Pop-Location }
        $received = Get-Content -LiteralPath (Join-Path $outDir 'fixture-received.json') -Raw | ConvertFrom-Json
        Assert-Contract ($received.request.arguments.Count -eq $values.Count) 'Argument count changed.'
        for ($i = 0; $i -lt $values.Count; $i++) { Assert-Contract ($received.request.arguments[$i] -ceq $values[$i]) 'Argument content changed.' }
        Assert-Contract ($received.request.inputRoot -ceq $inputDir) 'Input path changed.'
        Assert-Contract ($received.cwd -ceq $received.request.runRoot) 'Working directory was not isolated.'
        Assert-Contract $received.pythonEnvironmentCleared 'External Python environment leaked.'
        $stdout = Get-Content -LiteralPath (Join-Path $received.cwd 'script.stdout.log') -Raw -Encoding utf8
        Assert-Contract ($stdout.Contains('Grüße 日本語')) 'UTF-8 stdout changed.'
        foreach ($key in @('PYTHONHOME','PYTHONPATH','PYTHONUSERBASE','PYTHONSTARTUP','VIRTUAL_ENV','CONDA_PREFIX','PYTHONOPTIMIZE','PYTHONINSPECT')) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
    Check 'timeout kills parent and child' {
        $env:FORGELINE_BLENDER_FIXTURE_PIDFILE = Join-Path $root 'timeout-pids.json'
        Expect-Failure { Invoke-Fixture 'timeout' 'Script' 2 } 'exceeded 2s'
        $ids = Get-Content -LiteralPath $env:FORGELINE_BLENDER_FIXTURE_PIDFILE -Raw | ConvertFrom-Json
        foreach ($id in @($ids.parent, $ids.child)) { Assert-Contract (-not (Get-Process -Id $id -ErrorAction SilentlyContinue)) "Timed-out process $id survived." }
    }
    Check 'cancellation kills parent and child' {
        $env:FORGELINE_BLENDER_FIXTURE_PIDFILE = Join-Path $root 'cancel-pids.json'
        $env:FORGELINE_BLENDER_FIXTURE_CANCELFILE = Join-Path $root 'cancel.signal'
        Expect-Failure { Invoke-Fixture 'cancel' 'Script' 20 $env:FORGELINE_BLENDER_FIXTURE_CANCELFILE } 'cancelled'
        $ids = Get-Content -LiteralPath $env:FORGELINE_BLENDER_FIXTURE_PIDFILE -Raw | ConvertFrom-Json
        foreach ($id in @($ids.parent, $ids.child)) { Assert-Contract (-not (Get-Process -Id $id -ErrorAction SilentlyContinue)) "Cancelled process $id survived." }
    }
    Check 'pre-launch cancellation' {
        $signal = Join-Path $root 'cancel.signal'
        Expect-Failure { Invoke-Fixture 'pre-cancel' 'Check' 20 $signal } 'cancelled before launch'
    }
    Check 'invalid script and input rejected' {
        Expect-Failure { & $launcher -Action Script -BlenderPath $fixture -OutputRoot (Join-Path $root 'no-script') } 'requires -Script'
        Expect-Failure { & $launcher -Action Script -Script missing.py -BlenderPath $fixture -OutputRoot (Join-Path $root 'missing-script') } 'does not exist'
        Expect-Failure { & $launcher -InputRoot (Join-Path $root 'missing-input') -OutputRoot (Join-Path $root 'bad-input') } 'does not exist'
        Expect-Failure { & $launcher -Script tools/blender/smoke.py -OutputRoot (Join-Path $root 'ignored-script') } 'only with'
    }
    Check 'production and traversal output rejected' {
        foreach ($target in @('assets/source','assets/runtime','artifacts','artifacts/../assets/source','../outside-blender')) {
            Expect-Failure { Resolve-BlenderOutput -Path $target -RepositoryRoot $repositoryRoot } 'subdirectory of repository artifacts'
        }
    }
    Check 'output lease excludes concurrent use' {
        $leaseRoot = Join-Path $root 'leased'; [void][IO.Directory]::CreateDirectory($leaseRoot)
        $lease = [IO.File]::Open((Join-Path $leaseRoot '.blender.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { Expect-Failure { & $launcher -BlenderPath $fixture -OutputRoot $leaseRoot } 'process|file|Datei|Zugriff' }
        finally { $lease.Dispose() }
    }
    Check 'junction output rejected' {
        $target = Join-Path $root 'junction-target'; [void][IO.Directory]::CreateDirectory($target)
        $link = Join-Path $root 'junction-output'
        $type = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
        New-Item -ItemType $type -Path $link -Target $target | Out-Null
        Expect-Failure { Resolve-BlenderOutput -Path (Join-Path $link 'child') -RepositoryRoot $repositoryRoot } 'symbolic link or junction'
    }
    [ordered]@{ schemaVersion = 1; success = $true; fixtureOnly = $true; cases = @($results) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'contracts.json') -Encoding utf8NoBOM
    Write-Host "$($results.Count) Blender protocol contracts passed (fixture, not native Blender)."
}
finally { foreach ($key in $envKeys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) } }
