#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineProbe,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineSource,
    [Parameter(Mandatory)][string]$CandidateProbe,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CandidateSource,
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=[IO.Path]::GetFullPath($OutputRoot,$repo)
if(-not $root.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Choose a fresh evidence root below artifacts.'}
[void][IO.Directory]::CreateDirectory($root)
$runtime=[IO.Path]::GetFullPath($RuntimeRoot,$repo)
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash
$driver=@(Get-CimInstance Win32_VideoController|ForEach-Object {"$($_.Name): $($_.DriverVersion)"}) -join '; '
$sequence=@('before-1','after-1','after-2','before-2')
@{baselineSource=$BaselineSource;candidateSource=$CandidateSource;runtimeManifestSha256=$manifestHash;sequence=$sequence;
    declaredUtc=[DateTime]::UtcNow.ToString('o');driver=$driver;warmups=64;samples=256;profiles=@('Low','Medium','High','Ultra');
    scenes=@('MainBattleTankProduction','IndustrialBase');zooms=@('NormalGameplay');
    resolution='1920x1080';method='Separate completed-fence world and shadow timestamp series; fixed pose and target-pan. Full-client references remain separate.'}|
    ConvertTo-Json -Depth 6|Set-Content (Join-Path $root 'protocol.json') -Encoding utf8NoBOM
function Foreign-Work([int]$OwnId=0) {
    @(Get-CimInstance Win32_Process|Where-Object {$_.ProcessId -ne $OwnId -and (
        $_.Name -in @('ForgeLine.Client.exe','ForgeLine.Headless.exe','MaterialWorldProbe.exe','GameplayRecoveryProbe.exe','ForgeLine.AssetCompiler.exe','csc.exe','VBCSCompiler.exe','MSBuild.exe','blender.exe') -or
        $_.Name -like 'ForgeLine*.Tests.exe' -or $_.Name -like 'ForgeLine*Benchmarks.exe' -or
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match ' test | build |Tests[.]dll|Benchmarks|VBCSCompiler|csc[.]dll|MSBuild[.]dll'))}|
        Select-Object ProcessId,Name,CreationDate)
}
foreach($member in $sequence) {
    if(@(Foreign-Work).Count){throw 'Competing workload found before capture.'}
    $before=$member.StartsWith('before')
    $executable=[IO.Path]::GetFullPath($(if($before){$BaselineProbe}else{$CandidateProbe}),$repo)
    $source=if($before){$BaselineSource}else{$CandidateSource}
    $info=[Diagnostics.ProcessStartInfo]::new($executable)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.Environment['FORGELINE_BENCHMARK_DRIVER']=$driver
    $info.ArgumentList.Add((Join-Path $root "$member.json"));$info.ArgumentList.Add($runtime);$info.ArgumentList.Add($source)
    $process=[Diagnostics.Process]::Start($info)
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$overlap=$false
    do {
        $foreign=@(Foreign-Work $process.Id)
        @{utc=[DateTime]::UtcNow.ToString('o');cohort=$member;foreign=$foreign}|ConvertTo-Json -Depth 4 -Compress|
            Add-Content -LiteralPath (Join-Path $root 'process-monitor.jsonl')
        if($foreign.Count){$overlap=$true}
    }while(-not $process.WaitForExit(2000))
    [IO.File]::WriteAllText((Join-Path $root "$member.stdout.log"),$stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $root "$member.stderr.log"),$stderr.GetAwaiter().GetResult())
    if($process.ExitCode -ne 0){throw "World probe $member failed: $($process.ExitCode)."}
    if($overlap){throw 'Competing workload overlapped the measurement.'}
    if((Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash -ne $manifestHash){throw 'Runtime assets changed.'}
    Write-Output "Completed separated GPU cohort: $member"
}
