#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-QualificationJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 100) + "`n")
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes) } finally { $stream.Dispose() }
}

function Get-QualificationHash {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-QualificationPathWithin {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)
    $pathValue = [IO.Path]::GetFullPath($Path).TrimEnd([char[]]'\/')
    $rootValue = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    return $pathValue.Equals($rootValue, [StringComparison]::OrdinalIgnoreCase) -or
        $pathValue.StartsWith($rootValue + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Get-QualificationTreeIdentity {
    param([Parameter(Mandatory)][string]$Root)
    $rawRoot = Get-Item -LiteralPath $Root -Force
    $ancestor = $rawRoot
    while ($null -ne $ancestor) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Qualification input paths must not cross links or reparse points.' }
        $ancestor = $ancestor.Parent
    }
    $resolved = (Resolve-Path -LiteralPath $Root).Path
    $entries = @(Get-Item -LiteralPath $resolved; Get-ChildItem -LiteralPath $resolved -Recurse -Force)
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Qualification inputs must not contain symbolic links or reparse points.'
    }
    $records = [Collections.Generic.List[string]]::new()
    $files = [ordered]@{}
    foreach ($entry in $entries | Where-Object { -not $_.PSIsContainer }) {
        $relative = [IO.Path]::GetRelativePath($resolved, $entry.FullName).Replace('\', '/')
        if ($relative -match '[\x00-\x1f]') { throw 'Control characters are not permitted in evidence paths.' }
        if ($files.Contains($relative)) { throw 'Case-colliding input paths cannot qualify on Windows.' }
        $hash = Get-QualificationHash $entry.FullName
        $files[$relative] = @{ bytes = $entry.Length; sha256 = $hash }
        $records.Add("$relative`0$($entry.Length)`0$hash")
    }
    if ($records.Count -eq 0) { throw 'Empty input directories cannot qualify.' }
    $records.Sort([StringComparer]::Ordinal)
    $digest = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($records -join "`n") + "`n"))
    return @{ sha256 = [Convert]::ToHexString($digest).ToLowerInvariant(); files = $files }
}

function Assert-QualificationPackage {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Source)
    $identity = Get-QualificationTreeIdentity $Root
    foreach ($path in $identity.files.Keys) {
        if ($path -match '(?i)(^|/)(assets/source|blender|ForgeLine[.]AssetCompiler|src)(/|[.]|$)|[.](blend|blend1|py|cs|csproj)$') {
            throw "Player package includes authoring or source dependencies: $path"
        }
    }
    foreach ($path in @('ForgeLine.Client.exe', 'build-provenance.json', 'manifest.sha256', 'assets/runtime/manifest.json')) {
        if (-not $identity.files.Contains($path)) { throw "Missing packaged input: $path" }
    }
    $provenance = Get-Content -LiteralPath (Join-Path $Root 'build-provenance.json') -Raw | ConvertFrom-Json
    if ($provenance.sourceRevision -isnot [string] -or $provenance.sourceRevision -cne $Source -or $provenance.configuration -cne 'Release' -or
        $provenance.runtimeIdentifier -cne 'win-x64' -or $provenance.sourceTreeClean -isnot [bool] -or $provenance.sourceTreeClean -ne $true) {
        throw 'Package provenance must describe the requested clean Release win-x64 source.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath (Join-Path $Root 'manifest.sha256')) {
        if ($line -notmatch '^([0-9a-f]{64})  ([^\r\n]+)$') { throw 'Invalid package checksum record.' }
        $expected = $Matches[1]; $relative = $Matches[2]
        if (-not $seen.Add($relative) -or -not $identity.files.Contains($relative) -or
            $identity.files[$relative].sha256 -cne $expected) { throw "Package checksum mismatch: $relative" }
    }
    if ($seen.Count -ne $identity.files.Count - 1) { throw 'Package checksum inventory does not cover the entire source-less package.' }
    return @{ package = $identity; runtime = (Get-QualificationTreeIdentity (Join-Path $Root 'assets/runtime')); provenance = $provenance }
}

function New-QualificationPlan {
    param([ValidateRange(5,1000)][int]$Repetitions = 5, [ValidateRange(512,8192)][int]$WarmSamples = 512,
        [ValidateRange(8,8192)][int]$ColdSamples = 128, [ValidateRange(64,4096)][int]$Warmups = 64)
    return [ordered]@{
        repetitions = $Repetitions; warmSamples = $WarmSamples; coldSamples = $ColdSamples; warmups = $Warmups; rasterDpi = 96
        scenes = @('IndustrialBase', 'BattlefieldCombat', 'CombatSupportRepeated')
        profiles = @('Medium', 'High'); zooms = @('CloseTactical', 'NormalGameplay', 'Strategic')
        displays = @('1920x1080', '2560x1440', '3840x2160')
        variants = @(@{ runProfile = 'Warm'; cameraPath = 'Fixed' }, @{ runProfile = 'Warm'; cameraPath = 'Sweep' },
            @{ runProfile = 'Warm'; cameraPath = 'Traverse' }, @{ runProfile = 'RendererCold'; cameraPath = 'Fixed' })
    }
}

function Assert-QualificationVersionDelta {
    param([Parameter(Mandatory)][string]$Before, [Parameter(Mandatory)][string]$After)
    $pattern = '<ForgeLineVersionPatch>([0-9]+)</ForgeLineVersionPatch>'
    $old = [regex]::Matches($Before, $pattern); $current = [regex]::Matches($After, $pattern)
    if ($old.Count -ne 1 -or $current.Count -ne 1 -or
        [uint64]$old[0].Groups[1].Value + 1 -ne [uint64]$current[0].Groups[1].Value) { throw 'The serialized version delta must be exactly one PATCH.' }
    $expected = $Before.Remove($old[0].Index, $old[0].Length).Insert($old[0].Index, $current[0].Value)
    if ($After -cne $expected) { throw 'The shared version file contains changes beyond its single PATCH value.' }
}

function Copy-QualificationEvidence {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Destination)
    $identity = Get-QualificationTreeIdentity $Root
    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    $index = Get-Content -LiteralPath (Join-Path $rootPath 'index.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($index -isnot [Collections.IDictionary] -or -not $index.Contains('schemaVersion') -or
        ($index.schemaVersion -isnot [int] -and $index.schemaVersion -isnot [long]) -or $index.schemaVersion -ne 1 -or
        -not $index.Contains('files') -or $index.files -isnot [Collections.IList] -or $index.files.Count -eq 0) {
        throw 'Imported evidence requires an integer schemaVersion 1 and a nonempty checksum array.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($reference in $index.files) {
        if ($reference -isnot [Collections.IDictionary] -or -not $reference.Contains('path') -or
            -not $reference.Contains('sha256') -or $reference.path -isnot [string] -or
            $reference.sha256 -isnot [string] -or $reference.sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw 'Every imported file reference must be an object with string path and lowercase SHA256.'
        }
        $path = $reference.path
        if ($path -notmatch '^[A-Za-z0-9_. /-]+$' -or $path.StartsWith('/') -or
            @($path -split '/' | Where-Object { $_ -in @('', '.', '..') }).Count -or
            $path -ceq 'index.json' -or -not $seen.Add($path) -or -not $identity.files.Contains($path) -or
            $identity.files[$path].sha256 -cne $reference.sha256) { throw 'Imported evidence contains an escaping, duplicate, missing or changed artifact.' }
    }
    if ($seen.Count -ne $identity.files.Count - 1) { throw 'Imported evidence has undeclared files. Only the declared evidence tree may be copied.' }
    foreach ($group in @('validationEvidence', 'supplemental')) {
        if (-not $index.ContainsKey($group)) { $index[$group] = @() }
        if ($index[$group] -isnot [Collections.IList]) { throw 'Imported evidence groups must be arrays.' }
        foreach ($reference in $index[$group]) {
            if ($reference -isnot [Collections.IDictionary] -or -not $reference.Contains('path') -or
                -not $reference.Contains('sha256') -or -not $reference.Contains('kind') -or
                $reference.path -isnot [string] -or $reference.sha256 -isnot [string] -or
                $reference.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $reference.kind -isnot [string] -or
                [string]::IsNullOrWhiteSpace($reference.kind)) { throw 'Gate references require string kind, path and lowercase SHA256.' }
            if (-not $seen.Contains($reference.path) -or $identity.files[$reference.path].sha256 -cne $reference.sha256) {
                throw 'A gate reference is not bound to an indexed evidence artifact.'
            }
        }
    }
    if (Test-Path -LiteralPath $Destination) { throw 'Imported evidence destination already exists.' }
    [void][IO.Directory]::CreateDirectory($Destination)
    foreach ($path in $identity.files.Keys) {
        $target = Join-Path $Destination $path
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy((Join-Path $rootPath $path), $target, $false)
        if ((Get-QualificationHash $target) -cne $identity.files[$path].sha256) { throw 'Imported evidence changed while it was copied.' }
    }
    return $index
}

function Get-QualificationConditions {
    $conditions = [ordered]@{ observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); powerPlan = 'unavailable'
        powerSource = 'unavailable'; thermalState = 'unavailable'
        thermalReason = 'Portable Windows CIM does not establish physical GPU temperature, clock or throttling stability.' }
    try {
        $power = @(Get-CimInstance -Namespace root/cimv2/power -ClassName Win32_PowerPlan -Filter 'IsActive=True')
        if ($power.Count -eq 1) { $conditions.powerPlan = "$($power[0].ElementName):$($power[0].InstanceID)" }
        $batteries = @(Get-CimInstance -ClassName Win32_Battery)
        if ($batteries.Count -gt 0 -and @($batteries | Where-Object { $_.BatteryStatus -notin @(2,3,6,7,8,9,11) }).Count -eq 0) {
            $conditions.powerSource = 'ac'
        } elseif ($batteries.Count -eq 0) {
            $conditions.powerSource = 'unavailable'
            $conditions.powerSourceReason = 'No battery enumerated; AC availability is not inferred from absence.'
        } else { $conditions.powerSource = 'battery-or-unknown' }
    } catch { $conditions.observationError = $_.Exception.Message }
    return $conditions
}

function Get-QualificationCompetition {
    param([int]$ChildRootId = 0)
    $processes = @(Get-CimInstance -ClassName Win32_Process)
    $owned = [Collections.Generic.HashSet[int]]::new()
    if ($ChildRootId -gt 0) { [void]$owned.Add($ChildRootId) }
    do {
        $changed = $false
        foreach ($entry in $processes) {
            if ($owned.Contains([int]$entry.ParentProcessId) -and $owned.Add([int]$entry.ProcessId)) { $changed = $true }
        }
    } while ($changed)
    return @($processes | Where-Object {
        -not $owned.Contains([int]$_.ProcessId) -and
        ($_.Name -match '^ForgeLine.*[.]exe$|^blender[.]exe$|^csc[.]exe$|^VBCSCompiler[.]exe$|^MSBuild[.]exe$|Probe[.]exe$' -or
            ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)\b(build|test|publish)\b|Tests[.]dll|Benchmarks|VBCSCompiler|csc[.]dll|MSBuild[.]dll'))
    } | Select-Object ProcessId, ParentProcessId, Name)
}

function Invoke-QualificationProcess {
    param([Parameter(Mandatory)][string]$Executable, [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory, [Parameter(Mandatory)][string]$JournalRoot,
        [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Id,
        [ValidateRange(1,259200)][int]$TimeoutSeconds = 3600, [switch]$MonitorCompetition)
    $stdoutPath = Join-Path $JournalRoot "$Id.stdout.log"
    $stderrPath = Join-Path $JournalRoot "$Id.stderr.log"
    $monitorPath = Join-Path $JournalRoot "$Id.monitor.jsonl"
    $recordPath = Join-Path $JournalRoot "$Id.json"
    foreach ($path in @($stdoutPath, $stderrPath, $monitorPath, $recordPath)) {
        if (Test-Path -LiteralPath $path) { throw 'A command journal already exists; it must never be overwritten.' }
    }
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $WorkingDirectory; $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $record = [ordered]@{ executable = $Executable; arguments = $Arguments; workingDirectory = $WorkingDirectory
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); exitCode = -1; nativeExitCode = $null; elapsedMilliseconds = 0.0
        timedOut = $false; competitionDetected = $false; launchError = $null }
    $outStream = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $errStream = $null; $monitor = $null; $process = $null; $outputTask = $null; $errorTask = $null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $errStream = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $monitor = [IO.StreamWriter]::new([IO.File]::Open($monitorPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read))
        $monitor.AutoFlush = $true
        if ($MonitorCompetition) {
            $foreign = @(Get-QualificationCompetition)
            $monitor.WriteLine((@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); foreign = $foreign } | ConvertTo-Json -Depth 6 -Compress))
            if ($foreign.Count) { $record.competitionDetected = $true; throw 'Competing workload exists before native capture.' }
        }
        $process = [Diagnostics.Process]::Start($info)
        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($outStream)
        $errorTask = $process.StandardError.BaseStream.CopyToAsync($errStream)
        while (-not $process.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalSeconds -gt $TimeoutSeconds) { $record.timedOut = $true; $process.Kill($true); break }
            if ($MonitorCompetition) {
                $foreign = @(Get-QualificationCompetition -ChildRootId $process.Id)
                $monitor.WriteLine((@{ utc = [DateTimeOffset]::UtcNow.ToString('o'); foreign = $foreign } | ConvertTo-Json -Depth 6 -Compress))
                if ($foreign.Count) { $record.competitionDetected = $true; $process.Kill($true); break }
            }
        }
        $process.WaitForExit()
        $record.nativeExitCode = $process.ExitCode
        $outputTask.GetAwaiter().GetResult(); $errorTask.GetAwaiter().GetResult()
        $record.exitCode = $process.ExitCode
        if ($record.timedOut -or $record.competitionDetected) { $record.exitCode = -1 }
    } catch {
        $record.launchError = $_.Exception.Message
        if ($null -ne $process -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if ($null -ne $process -and $process.HasExited) { $record.nativeExitCode = $process.ExitCode }
        if ($null -ne $outputTask) { $outputTask.GetAwaiter().GetResult() }
        if ($null -ne $errorTask) { $errorTask.GetAwaiter().GetResult() }
    } finally {
        $watch.Stop(); $record.elapsedMilliseconds = $watch.Elapsed.TotalMilliseconds
        $record.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        if ($null -ne $process) { $process.Dispose() }
        if ($null -ne $monitor) { $monitor.Dispose() }
        $outStream.Dispose(); if ($null -ne $errStream) { $errStream.Dispose() }
        $record.stdout = @{ path = [IO.Path]::GetFileName($stdoutPath); sha256 = Get-QualificationHash $stdoutPath }
        $record.stderr = $(if (Test-Path -LiteralPath $stderrPath) { @{ path = [IO.Path]::GetFileName($stderrPath); sha256 = Get-QualificationHash $stderrPath } } else { $null })
        $record.monitor = $(if (Test-Path -LiteralPath $monitorPath) { @{ path = [IO.Path]::GetFileName($monitorPath); sha256 = Get-QualificationHash $monitorPath } } else { $null })
        Write-QualificationJson -Path $recordPath -Value $record
    }
    return $record
}

function Assert-QualificationCommand {
    param([Parameter(Mandatory)]$Command)
    if ($Command.exitCode -ne 0) { throw "Command failed with exit $($Command.exitCode): $($Command.executable). Retain the original command journal and logs." }
}

Export-ModuleMember -Function Write-QualificationJson, Get-QualificationHash, Test-QualificationPathWithin,
    Get-QualificationTreeIdentity, Assert-QualificationPackage, New-QualificationPlan, Get-QualificationConditions,
    Invoke-QualificationProcess, Assert-QualificationCommand, Assert-QualificationVersionDelta, Copy-QualificationEvidence
