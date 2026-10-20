[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CommandFile,
    [Parameter(Mandatory)][string]$OutputRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commandPath = (Resolve-Path -LiteralPath $CommandFile).Path
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a fresh quiet-window directory.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$stopPath = Join-Path $outputPath 'stop'
$observationsPath = Join-Path $outputPath 'observations.jsonl'
$rootProcess = $PID
$observer = Start-Job -ArgumentList $rootProcess,$stopPath,$observationsPath -ScriptBlock {
    param($rootProcess,$stopPath,$observationsPath)
    $owned = [Collections.Generic.HashSet[int]]::new()
    [void]$owned.Add($rootProcess)
    $prior = @{}
    $violations = @()
    $count = 0
    while (!(Test-Path -LiteralPath $stopPath)) {
        $processes = @(Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name)
        do {
            $added = $false
            foreach ($process in $processes) {
                if ($owned.Contains([int]$process.ParentProcessId) -and $owned.Add([int]$process.ProcessId)) { $added = $true }
            }
        } while ($added)
        $foreign = @()
        foreach ($process in $processes) {
            if ($owned.Contains([int]$process.ProcessId) -or $process.Name -notmatch '^(dotnet|MSBuild|VBCSCompiler|blender|dxc|ForgeLine\..*)\.exe$') { continue }
            $live = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
            if (!$live) { continue }
            $cpu = $live.TotalProcessorTime.TotalSeconds
            $key = "$($process.ProcessId):$($live.StartTime.Ticks)"
            $delta = if ($prior.ContainsKey($key)) { [Math]::Max(0,$cpu-$prior[$key]) } elseif ($count) { $cpu } else { 0 }
            $prior[$key] = $cpu
            $busy = $delta -gt .1 -or $process.Name -eq 'ForgeLine.Client.exe'
            $item = [ordered]@{ pid=$process.ProcessId; name=$process.Name; cpuDeltaSeconds=$delta; busy=$busy }
            $foreign += $item
            if ($busy) { $violations += [ordered]@{ utc=[DateTime]::UtcNow.ToString('O'); process=$item } }
        }
        $sample = [ordered]@{ utc=[DateTime]::UtcNow.ToString('O'); foreign=$foreign }
        [IO.File]::AppendAllText($observationsPath,($sample | ConvertTo-Json -Depth 5 -Compress)+[Environment]::NewLine)
        $count++
        Start-Sleep -Seconds 2
    }
    [ordered]@{
        qualified=($violations.Count -eq 0 -and $count -gt 1)
        samples=$count
        intervalSeconds=2
        foreignCpuThresholdSeconds=.1
        violations=$violations
        limitation='Bounded process observations of foreign build/test/renderer executables; cannot exclude shorter transients or all user/OS activity. No command lines or private paths recorded.'
    }
}
$exit = $null
try {
    & $commandPath
    $exit = $LASTEXITCODE
    if ($exit -ne 0) { throw "Qualification command exited $exit." }
}
finally {
    [IO.File]::WriteAllText($stopPath,'stop')
    $observer | Wait-Job | Out-Null
    $result = Receive-Job $observer -ErrorAction Stop
    Remove-Job $observer
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Encoding utf8
    if (!$result.qualified) { throw 'Quiet-window observations failed; preserve this cohort as unqualified.' }
}
