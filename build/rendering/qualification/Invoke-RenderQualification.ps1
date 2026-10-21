#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineSource,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CandidateSource,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$AnalysisMasterSource,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BaselineRoot, [string]$CandidateRoot,
    [string]$BaselinePackageRoot, [string]$CandidatePackageRoot,
    [ValidateRange(5,1000)][int]$Repetitions = 5,
    [ValidateRange(512,8192)][int]$WarmSamples = 512,
    [ValidateRange(8,8192)][int]$ColdSamples = 128,
    [ValidateRange(64,4096)][int]$Warmups = 64,
    [ValidateRange(1,4320)][int]$CapacityMinutes = 720,
    [ValidateSet('optimization','visual-candidate')][string]$ComparisonKind = 'optimization',
    [string]$Workstream = 'RenderPerformanceAndVisualQualification',
    [ValidatePattern('^[0-9a-f]{40}$')][string]$OwnershipBaseSource,
    [string]$HardwareQualificationEvidence, [string]$GateEvidenceRoot,
    [switch]$Software, [switch]$PlanOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Qualification.Commands.psm1') -Force
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = [IO.Path]::GetFullPath($OutputRoot, $repository)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory. No prior attempts are overwritten or resumed in place.' }
foreach ($inputRoot in @($BaselineRoot, $CandidateRoot, $BaselinePackageRoot, $CandidatePackageRoot)) {
    if ($inputRoot -and ((Test-QualificationPathWithin $output $inputRoot) -or (Test-QualificationPathWithin $inputRoot $output))) {
        throw 'Evidence must be separate from both source checkouts and both player packages.'
    }
}
$ownershipPath = Join-Path $repository 'docs/rendering/parallelization/ParallelRenderOwnership.json'
$ownership = Get-Content -LiteralPath $ownershipPath -Raw | ConvertFrom-Json
if ($ownership.parallelReleased -isnot [bool] -or $ownership.parallelReleased -ne $true -or $ownership.status -cne 'accepted-for-parallel-implementation' -or
    $ownership.integratedFoundationSha -notmatch '^[0-9a-f]{40}$' -or $ownership.integratedRepairSha -notmatch '^[0-9a-f]{40}$') {
    throw 'The trusted ownership record has not released this rendering foundation.'
}
[void][IO.Directory]::CreateDirectory($output)
[void][IO.Directory]::CreateDirectory((Join-Path $output 'commands'))
$plan = New-QualificationPlan -Repetitions $Repetitions -WarmSamples $WarmSamples -ColdSamples $ColdSamples -Warmups $Warmups
$planPath = Join-Path $output 'plan.json'
Write-QualificationJson -Path $planPath -Value $plan
$runs = [Collections.Generic.List[object]]::new()
$commands = [Collections.Generic.List[object]]::new()
$preflight = [Collections.Generic.List[object]]::new()
$evidence = [Collections.Generic.List[object]]::new()
$comparisons = [Collections.Generic.List[object]]::new()
$comparisonFailures = [Collections.Generic.List[string]]::new()
$cohort = [ordered]@{
    schemaVersion = 1; workstream = $Workstream; comparisonKind = $ComparisonKind
    baselineSha = $BaselineSource; candidateSha = $CandidateSource; analysisMasterSha = $AnalysisMasterSource
    integratedFoundationSha = $ownership.integratedFoundationSha; integratedRepairSha = $ownership.integratedRepairSha
    plan = $plan; planEvidence = @{ path = 'plan.json'; sha256 = Get-QualificationHash $planPath }
    hardware = @{ qualified = $false; qualificationEvidence = $null }
    runs = $runs; commands = $commands; preflight = $preflight; supplemental = @(); validationEvidence = @()
    inventory = $evidence; comparisons = $comparisons
    collectorStatus = @{ status = 'failed'; reason = 'Collector did not complete.' }
    sourceCohorts = @(@{ role = 'current-master'; sourceSha = $AnalysisMasterSource; measurementStatus = 'descriptor-only' },
        @{ role = 'foundation'; sourceSha = $ownership.integratedFoundationSha; measurementStatus = 'descriptor-only' },
        @{ role = 'foundation-repair'; sourceSha = $ownership.integratedRepairSha; measurementStatus = 'descriptor-only' })
}
$script:qualificationCommandNumber = 0
$pwshPath = 'pwsh'
$pythonPath = 'python'
$mutex = $null; $ownsMutex = $false; $failure = $null
$sessionWatch = [Diagnostics.Stopwatch]::StartNew()

function Invoke-JournalledCommand {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory = $repository, [switch]$NativeCapture)
    $script:qualificationCommandNumber++
    $id = 'command-{0:D4}' -f $script:qualificationCommandNumber
    $remaining = [Math]::Max(1, [Math]::Floor($CapacityMinutes * 60 - $sessionWatch.Elapsed.TotalSeconds))
    if ($NativeCapture -and $remaining -le 1) { throw 'Declared runner capacity is exhausted; remaining cases stay open.' }
    $record = Invoke-QualificationProcess -Executable $Executable -Arguments $Arguments -WorkingDirectory $WorkingDirectory `
        -JournalRoot (Join-Path $output 'commands') -Id $id -TimeoutSeconds ([int]$remaining) -MonitorCompetition:$NativeCapture
    $commands.Add(@{ path = "commands/$id.json"; sha256 = Get-QualificationHash (Join-Path $output "commands/$id.json") })
    return $record
}

function Invoke-OwnedScript {
    param([string]$ScriptPath, [hashtable]$Parameters, [string]$WorkingDirectory = $repository, [switch]$NativeCapture)
    $parametersPath = Join-Path $output ('commands/parameters-{0:D4}.json' -f ($script:qualificationCommandNumber + 1))
    Write-QualificationJson -Path $parametersPath -Value $Parameters
    $evidence.Add(@{ path = [IO.Path]::GetRelativePath($output, $parametersPath).Replace('\', '/'); sha256 = Get-QualificationHash $parametersPath })
    $sourceInputsPath = Join-Path $output ('commands/script-inputs-{0:D4}.json' -f ($script:qualificationCommandNumber + 1))
    Write-QualificationJson -Path $sourceInputsPath -Value @{ scriptPath = $ScriptPath; scriptSha256 = Get-QualificationHash $ScriptPath
        parameters = @{ path = [IO.Path]::GetFileName($parametersPath); sha256 = Get-QualificationHash $parametersPath } }
    $evidence.Add(@{ path = [IO.Path]::GetRelativePath($output, $sourceInputsPath).Replace('\', '/'); sha256 = Get-QualificationHash $sourceInputsPath })
    return Invoke-JournalledCommand -Executable $pwshPath -Arguments @('-NoProfile', '-NonInteractive', '-File',
        (Join-Path $PSScriptRoot 'Invoke-QualificationChild.ps1'), '-Script', $ScriptPath, '-ParametersJson', $parametersPath) `
        -WorkingDirectory $WorkingDirectory -NativeCapture:$NativeCapture
}

function Get-CommandOutput {
    param($Command)
    Assert-QualificationCommand $Command
    return [IO.File]::ReadAllText((Join-Path $output "commands/$($Command.stdout.path)")).Trim()
}

function Assert-InputUnchanged {
    param($InputState)
    $current = Assert-QualificationPackage -Root $InputState.packageRoot -Source $InputState.source
    if ($current.package.sha256 -cne $InputState.identity.package.sha256) { throw 'A player package or runtime payload changed during qualification.' }
    $status = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $InputState.root, 'status', '--porcelain', '--untracked-files=normal')
    if (Get-CommandOutput $status) { throw 'The source checkout changed during qualification.' }
    $head = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $InputState.root, 'rev-parse', 'HEAD')
    if ((Get-CommandOutput $head) -cne $InputState.source) { throw 'The source checkout moved during qualification.' }
}

try {
    $pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
    $pythonPath = (Get-Command python -ErrorAction Stop).Source
    $ownershipCommand = Invoke-JournalledCommand -Executable $pythonPath -Arguments @(
        (Join-Path $repository 'build/rendering/parallelization/ValidateOwnership.py'), '--manifest', $ownershipPath)
    Assert-QualificationCommand $ownershipCommand
    if ($GateEvidenceRoot) {
        $gateIndexPath = (Resolve-Path -LiteralPath (Join-Path $GateEvidenceRoot 'index.json')).Path
        Assert-QualificationCommand (Invoke-JournalledCommand -Executable $pythonPath -Arguments @(
            (Join-Path $PSScriptRoot 'qualification.py'), '--validate-json', $gateIndexPath))
        $index = Copy-QualificationEvidence -Root $GateEvidenceRoot -Destination (Join-Path $output 'imports')
        $evidence.Add(@{ path = 'imports/index.json'; sha256 = Get-QualificationHash (Join-Path $output 'imports/index.json') })
        foreach ($group in @('validationEvidence','supplemental')) {
            $cohort[$group] = @($index[$group] | ForEach-Object { @{ kind = $_.kind; path = "imports/$($_.path)"; sha256 = $_.sha256 } })
        }
    }
    if ($PlanOnly) {
        $cohort.collectorStatus = @{ status = 'plan-only'; reason = 'No product measurements or hardware qualification were executed.' }
    } else {
        if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
            throw 'Native collection requires Windows x64. The declared full matrix remains open.'
        }
        foreach ($required in @($BaselineRoot, $CandidateRoot, $BaselinePackageRoot, $CandidatePackageRoot)) {
            if ([string]::IsNullOrWhiteSpace($required)) { throw 'Capture requires both clean source roots and both prebuilt source-less package roots.' }
        }
        $mutex = [Threading.Mutex]::new($false, 'Global\ForgeLine.RenderQualification.Gpu')
        try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
        if (-not $ownsMutex) { throw 'Another render qualification owns the physical GPU lane.' }
        $inputs = @{}
        foreach ($role in @('baseline','candidate')) {
            $source = if ($role -eq 'baseline') { $BaselineSource } else { $CandidateSource }
            $root = (Resolve-Path -LiteralPath $(if ($role -eq 'baseline') { $BaselineRoot } else { $CandidateRoot })).Path
            $packageRoot = (Resolve-Path -LiteralPath $(if ($role -eq 'baseline') { $BaselinePackageRoot } else { $CandidatePackageRoot })).Path
            $head = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $root, 'rev-parse', 'HEAD')
            if ((Get-CommandOutput $head) -cne $source) { throw 'Checkout HEAD differs from its declared immutable source.' }
            $status = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $root, 'status', '--porcelain', '--untracked-files=normal')
            if (Get-CommandOutput $status) { throw 'Source checkouts must be clean before qualification.' }
            foreach ($ancestor in @($ownership.integratedFoundationSha, $ownership.integratedRepairSha)) {
                Assert-QualificationCommand (Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $root, 'merge-base', '--is-ancestor', $ancestor, $source))
            }
            Assert-QualificationCommand (Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $root, 'cat-file', '-e', "$AnalysisMasterSource^{commit}"))
            $sdk = Get-CommandOutput (Invoke-JournalledCommand -Executable 'dotnet' -Arguments @('--version') -WorkingDirectory $root)
            Assert-QualificationCommand (Invoke-JournalledCommand -Executable $pythonPath -Arguments @((Join-Path $PSScriptRoot 'qualification.py'),
                '--validate-json', (Join-Path $root 'global.json'), (Join-Path $packageRoot 'build-provenance.json'), (Join-Path $packageRoot 'assets/runtime/manifest.json')))
            $pinned = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version
            if ($sdk -cne $pinned) { throw "Use the repository-pinned SDK $pinned for this cohort; observed $sdk." }
            $identity = Assert-QualificationPackage -Root $packageRoot -Source $source
            if ($identity.provenance.sdk -cne $sdk) { throw 'The package was produced with a different SDK.' }
            $inputs[$role] = @{ root = $root; packageRoot = $packageRoot; source = $source; identity = $identity; sdk = $sdk }
            Write-QualificationJson -Path (Join-Path $output "$role-inputs.json") -Value @{ sourceSha = $source; sdk = $sdk
                package = $identity.package; runtime = $identity.runtime; provenance = $identity.provenance }
            $evidence.Add(@{ path = "$role-inputs.json"; sha256 = Get-QualificationHash (Join-Path $output "$role-inputs.json") })
        }
        if ($ComparisonKind -eq 'optimization' -and $inputs.baseline.identity.runtime.sha256 -cne $inputs.candidate.identity.runtime.sha256) {
            throw 'Optimization cohorts require byte-identical packaged runtime assets.'
        }
        if ($OwnershipBaseSource) {
            $diff = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $inputs.candidate.root, 'diff', '--name-only', '--no-renames', "$OwnershipBaseSource...$CandidateSource")
            $paths = (Get-CommandOutput $diff) -split '\r?\n' | Where-Object { $_ -and $_ -cne 'Directory.Build.props' }
            $diffPath = Join-Path $output 'owned-paths.txt'
            $stream = [IO.File]::Open($diffPath, [IO.FileMode]::CreateNew)
            try { $stream.Write([Text.Encoding]::UTF8.GetBytes(($paths -join "`n") + "`n")) } finally { $stream.Dispose() }
            Assert-QualificationCommand (Invoke-JournalledCommand -Executable $pythonPath -Arguments @(
                (Join-Path $repository 'build/rendering/parallelization/ValidateOwnership.py'), '--manifest', $ownershipPath,
                '--package', $Workstream, '--diff-file', $diffPath))
            $beforeVersion = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $inputs.candidate.root, 'show', "${OwnershipBaseSource}:Directory.Build.props")
            $afterVersion = Invoke-JournalledCommand -Executable 'git' -Arguments @('-C', $inputs.candidate.root, 'show', "${CandidateSource}:Directory.Build.props")
            Assert-QualificationCommand $beforeVersion; Assert-QualificationCommand $afterVersion
            Assert-QualificationVersionDelta -Before ([IO.File]::ReadAllText((Join-Path $output "commands/$($beforeVersion.stdout.path)"))) `
                -After ([IO.File]::ReadAllText((Join-Path $output "commands/$($afterVersion.stdout.path)")))
            Assert-QualificationCommand (Invoke-OwnedScript -ScriptPath (Join-Path $inputs.candidate.root 'build/Validate-SemVer.ps1') `
                -Parameters @{ BaseRef = $OwnershipBaseSource } -WorkingDirectory $inputs.candidate.root)
        }
        if ($HardwareQualificationEvidence) {
            $evidencePath = (Resolve-Path -LiteralPath $HardwareQualificationEvidence).Path
            $hardwareRecord = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
            $destination = Join-Path $output 'hardware'
            [void][IO.Directory]::CreateDirectory($destination)
            [IO.File]::Copy($evidencePath, (Join-Path $destination 'qualification.json'), $false)
            foreach ($reference in $hardwareRecord.qualificationRuns) {
                if ($reference.path -notmatch '^[A-Za-z0-9_. /-]+$' -or $reference.path.StartsWith('/') -or
                    '..' -in ($reference.path -split '/')) { throw 'External hardware evidence paths must remain within their own directory.' }
                $origin = Join-Path ([IO.Path]::GetDirectoryName($evidencePath)) $reference.path
                if ((Get-Item -LiteralPath $origin).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked calibration evidence is not accepted.' }
                if ((Get-QualificationHash $origin) -cne $reference.sha256.ToLowerInvariant()) { throw 'Hardware calibration evidence hash mismatch.' }
                $target = Join-Path $destination $reference.path
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
                [IO.File]::Copy($origin, $target, $false)
            }
            $cohort.hardware = @{ qualified = $true; qualificationEvidence = @{ path = 'hardware/qualification.json'; sha256 = Get-QualificationHash (Join-Path $destination 'qualification.json') } }
        }
        $jsonInputs = [Collections.Generic.List[string]]::new()
        $jsonInputs.Add($ownershipPath)
        foreach ($directory in @('imports','hardware')) {
            $root = Join-Path $output $directory
            if (Test-Path -LiteralPath $root) {
                foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.json') { $jsonInputs.Add($file.FullName) }
            }
        }
        Assert-QualificationCommand (Invoke-JournalledCommand -Executable $pythonPath -Arguments (@((Join-Path $PSScriptRoot 'qualification.py'), '--validate-json') + $jsonInputs.ToArray()))
        $maximumP99 = 0.0; $maximumWallMillisecondsPerCase = 0.0
        foreach ($role in @('baseline','candidate')) {
            $inputState = $inputs[$role]
            Assert-InputUnchanged $inputState
            $root = Join-Path $output "preflight/$role"
            $command = Invoke-OwnedScript -ScriptPath (Join-Path $inputState.root 'build/Invoke-ReferencePerformance.ps1') `
                -WorkingDirectory $inputState.root -NativeCapture -Parameters @{
                    OutputRoot = $root; RunProfile = 'Warm'; CameraPath = 'Sweep'; Preset = 'Representative'; Warmups = $Warmups
                    Samples = $WarmSamples; Profiles = $plan.profiles; Displays = $plan.displays; Software = [bool]$Software
                    ClientPath = (Join-Path $inputState.packageRoot 'ForgeLine.Client.exe'); RuntimeAssetRoot = (Join-Path $inputState.packageRoot 'assets/runtime') }
            $control = @{ role = $role; command = $command; root = "preflight/$role"; varianceGate = $null; hardwareStatus = 'not-qualified' }
            $preflight.Add($control)
            Assert-QualificationCommand $command
            $varianceParameters = @{ CurrentRoot = (Join-Path $root 'run-2'); BaselineRoot = (Join-Path $root 'run-1')
                ReportPath = (Join-Path $root 'variance-gate.json'); GatePerformance = -not [bool]$Software }
            $varianceCommand = Invoke-OwnedScript -ScriptPath (Join-Path $repository 'build/Compare-ReferencePerformance.ps1') -Parameters $varianceParameters
            $control.varianceGate = $varianceCommand
            $control.hardwareStatus = if ($Software) { 'software-functional-only' } else { 'variance-control-only' }
            $controlIdentity = Get-QualificationTreeIdentity $root
            foreach ($path in $controlIdentity.files.Keys) { $evidence.Add(@{ path = "preflight/$role/$path"; sha256 = $controlIdentity.files[$path].sha256 }) }
            Assert-QualificationCommand $varianceCommand
            $count = 0
            foreach ($runNumber in @(1,2)) {
                $manifest = Get-Content -LiteralPath (Join-Path $root "run-$runNumber/manifest.json") -Raw | ConvertFrom-Json
                foreach ($capture in $manifest.captures) { $count++; $maximumP99 = [Math]::Max($maximumP99, [double]$capture.cpu.p99) }
            }
            $maximumWallMillisecondsPerCase = [Math]::Max($maximumWallMillisecondsPerCase, $command.elapsedMilliseconds / $count)
            Assert-InputUnchanged $inputState
        }
        $casesPerVariant = $plan.scenes.Count * $plan.profiles.Count * $plan.zooms.Count * $plan.displays.Count
        $frameCount = 2 * $Repetitions * $casesPerVariant * (3 * ($WarmSamples + $Warmups) + $ColdSamples)
        $captureCount = 2 * $Repetitions * $casesPerVariant * $plan.variants.Count
        $estimate = 1.5 * [Math]::Max($frameCount * $maximumP99, $captureCount * $maximumWallMillisecondsPerCase) / 60000
        $capacity = @{ declaredMinutes = $CapacityMinutes; elapsedMinutes = $sessionWatch.Elapsed.TotalMinutes
            estimatedRemainingMinutes = $estimate; plannedCapturedCases = $captureCount; plannedRenderedFramesIncludingWarmup = $frameCount
            completedPreflight = $true; method = '1.5 times the larger of maximum observed preflight p99 times planned frames and slowest preflight wall-time per case times case count. Advisory estimate; hard session deadline still applies.' }
        Write-QualificationJson -Path (Join-Path $output 'capacity.json') -Value $capacity
        if ($estimate + $sessionWatch.Elapsed.TotalMinutes -gt $CapacityMinutes) {
            throw 'The full declared matrix exceeds the observed runner capacity. Preserve this attempt; prepare sufficient capacity without reducing repetitions, matrix or warm samples.'
        }
        for ($pair = 1; $pair -le $Repetitions; $pair++) {
            $order = if ($pair % 2 -eq 1) { @('baseline','candidate') } else { @('candidate','baseline') }
            for ($position = 0; $position -lt 2; $position++) {
                $role = $order[$position]; $inputState = $inputs[$role]
                foreach ($variant in $plan.variants) {
                    Assert-InputUnchanged $inputState
                    $relative = 'pair-{0:D2}/{1}/{2}-{3}' -f $pair, $role, $variant.runProfile, $variant.cameraPath
                    $root = Join-Path $output $relative
                    $conditions = Get-QualificationConditions
                    $parameters = @{ Capture = $true; OutputRoot = $root; Scenes = $plan.scenes; Profiles = $plan.profiles
                        Zooms = $plan.zooms; Displays = $plan.displays; RasterDpi = $plan.rasterDpi; UiScenarios = @('World'); UiScales = @(1)
                        Warmups = $Warmups; Samples = $(if ($variant.runProfile -eq 'Warm') { $WarmSamples } else { $ColdSamples })
                        RepeatCaptures = 2; RunProfile = $variant.runProfile; CameraPath = $variant.cameraPath; Software = [bool]$Software
                        ClientPath = (Join-Path $inputState.packageRoot 'ForgeLine.Client.exe'); RuntimeAssetRoot = (Join-Path $inputState.packageRoot 'assets/runtime') }
                    $command = Invoke-OwnedScript -ScriptPath (Join-Path $inputState.root 'build/Invoke-RtsReferenceQualification.ps1') `
                        -Parameters $parameters -WorkingDirectory $inputState.root -NativeCapture
                    $manifestPath = Join-Path $root 'manifest.json'
                    if (Test-Path -LiteralPath $manifestPath) {
                        $runs.Add(@{ pair = $pair; position = $position; role = $role; runProfile = $variant.runProfile; cameraPath = $variant.cameraPath
                            manifest = "$relative/manifest.json"; manifestSha256 = Get-QualificationHash $manifestPath; command = $command
                            sdk = $inputState.sdk; conditions = $conditions; conditionsAfter = Get-QualificationConditions })
                    }
                    Assert-QualificationCommand $command
                    Assert-InputUnchanged $inputState
                }
            }
            foreach ($variant in $plan.variants) {
                $before = Join-Path $output ('pair-{0:D2}/baseline/{1}-{2}' -f $pair, $variant.runProfile, $variant.cameraPath)
                $after = Join-Path $output ('pair-{0:D2}/candidate/{1}-{2}' -f $pair, $variant.runProfile, $variant.cameraPath)
                $performance = Invoke-OwnedScript -ScriptPath (Join-Path $repository 'build/Compare-ReferencePerformance.ps1') `
                    -Parameters @{ CurrentRoot = $after; BaselineRoot = $before; ReportPath = (Join-Path $after 'paired-performance.json') }
                $pixels = Invoke-OwnedScript -ScriptPath (Join-Path $repository 'build/Compare-VisualReferenceEvidence.ps1') `
                    -Parameters @{ CurrentRoot = $after; BaselineRoot = $before; ComparePixels = $true; ReportPath = (Join-Path $after 'paired-pixels.json') }
                $comparisons.Add(@{ pair = $pair; runProfile = $variant.runProfile; cameraPath = $variant.cameraPath; performance = $performance; pixels = $pixels })
                if ($performance.exitCode -ne 0 -or $pixels.exitCode -ne 0) { $comparisonFailures.Add("pair-$pair/$($variant.runProfile)-$($variant.cameraPath)") }
            }
        }
        $cohort.collectorStatus = @{ status = 'completed'; reason = 'Native reference matrix collected. Complete gameplay frame, raw timestamp audit, dynamic lifecycle/soak and human visual acceptance remain separate required gates.' }
        if ($comparisonFailures.Count) {
            $failure = 'The predetermined cohort finished with comparator failures: ' + ($comparisonFailures -join ', ')
            $cohort.collectorStatus = @{ status = 'failed'; reason = $failure }
        }
    }
} catch {
    $failure = $_.Exception.Message
    $cohort.collectorStatus = @{ status = 'failed'; reason = $failure }
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
    $sessionWatch.Stop()
    try {
        $retained = Get-QualificationTreeIdentity $output
        $indexed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($reference in $evidence) { [void]$indexed.Add($reference.path) }
        foreach ($path in $retained.files.Keys) {
            if ($indexed.Add($path)) { $evidence.Add(@{ path = $path; sha256 = $retained.files[$path].sha256 }) }
        }
    } catch {
        $failure = 'Cannot seal the complete retained evidence tree: ' + $_.Exception.Message
        $cohort.collectorStatus = @{ status = 'failed'; reason = $failure }
    }
    $cohort.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $cohort.elapsedMilliseconds = $sessionWatch.Elapsed.TotalMilliseconds
    Write-QualificationJson -Path (Join-Path $output 'cohort.json') -Value $cohort
}
# Reporting never launches another measurement. Its exact exit is journalled independently.
$report = Invoke-QualificationProcess -Executable $pythonPath -Arguments @((Join-Path $PSScriptRoot 'qualification.py'),
    '--cohort', (Join-Path $output 'cohort.json'), '--output', (Join-Path $output 'qualification.json')) `
    -WorkingDirectory $repository -JournalRoot (Join-Path $output 'commands') -Id 'report' -TimeoutSeconds 300
if ($report.exitCode -ne 0) { throw "Qualification report failed with exit $($report.exitCode); original evidence is retained." }
if ($failure) { throw $failure }
Write-Output "Qualification evidence retained at $output. Open gates do not constitute native acceptance."
