#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineClient,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineSource,
    [Parameter(Mandatory)][string]$CurrentClient,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CurrentSource,
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [switch]$WarmOnly
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=[IO.Path]::GetFullPath($OutputRoot)
$allowed=(Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar
if(-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Choose a fresh native evidence root below artifacts.'}
foreach($flag in @('FORGELINE_DRED','FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK')) {
    if([Environment]::GetEnvironmentVariable($flag) -eq '1'){throw "Disable $flag for production comparison."}
}
$runtime=[IO.Path]::GetFullPath($RuntimeRoot)
$before=[IO.Path]::GetFullPath($BaselineClient);$after=[IO.Path]::GetFullPath($CurrentClient)
foreach($client in @($before,$after)){if(-not (Test-Path -LiteralPath $client)){throw 'Frozen client executable is missing.'}}
[void][IO.Directory]::CreateDirectory($root)
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json') -Algorithm SHA256).Hash
$driver=@(Get-CimInstance Win32_VideoController|ForEach-Object{"$($_.Name): $($_.DriverVersion)"}) -join '; '
$sequence=@('warm-before-1','warm-after-1','warm-after-2','warm-before-2')
if(-not $WarmOnly){$sequence+=@('cold-before','cold-after')}
$plan=@{declaredUtc=(Get-Date).ToUniversalTime().ToString('o');baselineSource=$BaselineSource;currentSource=$CurrentSource;
    baselineClientSha256=(Get-FileHash -LiteralPath $before).Hash;currentClientSha256=(Get-FileHash -LiteralPath $after).Hash;
    runtimeManifestSha256=$manifestHash;sequence=$sequence;scenes=@('MainBattleTankProduction','IndustrialBase');zooms=@('CloseTactical','NormalGameplay','Strategic');
    warmups=128;samples=512;repeatCaptures=2;maximumRegressionPercent=15;warmOnly=[bool]$WarmOnly;
    interpretation='Two fixed B-A-A-B warm pairs; RendererCold is separate cold/setup evidence with zero effective warmups, not a substitute for failed warm gates. Keep every cohort and all chronological observations.'}
$plan|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root 'protocol.json') -Encoding utf8NoBOM
function Foreign-Work([int]$OwnId=0) {
    @(Get-CimInstance Win32_Process|Where-Object{$_.ProcessId -ne $OwnId -and (
        $_.Name -in @('ForgeLine.Client.exe','ForgeLine.Headless.exe') -or $_.Name -like 'ForgeLine*.Tests.exe' -or $_.Name -like 'ForgeLine*Benchmarks.exe' -or
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match ' test | build |Tests[.]dll|Benchmarks|ResidencyProbe[.]dll'))}|Select-Object ProcessId,Name,CreationDate)
}
function Require-Quiet {
    $busy=@(Foreign-Work)
    if($busy.Count){$busy|ConvertTo-Json -Depth 5|Add-Content -LiteralPath (Join-Path $root 'foreign-work.jsonl');throw 'Competing workload found; retain and stop this cohort.'}
}
try {
    foreach($name in $sequence) {
        Require-Quiet
        $baseline=$name.Contains('before')
        $source=if($baseline){$BaselineSource}else{$CurrentSource}
        $client=if($baseline){$before}else{$after}
        $request=@{software=$false;continueAfterMismatch=$false;commit=$source;runProfile=if($name.StartsWith('cold')){'RendererCold'}else{'Warm'};
            runtimeRoot=$runtime;repeatCaptures=2;warmups=128;samples=512;uiScenarios=@('World');profiles=@('High');
            graphicsSettings=@{environment=@{daylight=1.0;cloudCover=0.0;wetness=0.0};vegetation=@{maximumDistanceMeters=4000.0;density=1.0};
                resolutionScale=1.0;terrainResidency=@{enabled=$true;maximumResidentChunks=128}};
            outputRoot=(Join-Path $root $name);displays=@(@{width=1920;height=1080});uiScales=@(1.0);
            zooms=$plan.zooms;scenes=$plan.scenes;driver=$driver;cameraPath='Fixed';yawDegrees=45.0}
        $requestPath=Join-Path $root "$name.request.json"
        $request|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
        & nvidia-smi --query-gpu=timestamp,pstate,clocks.gr,power.draw,power.limit --format=csv|Add-Content -LiteralPath (Join-Path $root 'gpu.csv')
        $info=[Diagnostics.ProcessStartInfo]::new($client)
        $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
        $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
        $info.ArgumentList.Add('--reference-capture-request');$info.ArgumentList.Add($requestPath)
        $process=[Diagnostics.Process]::Start($info)
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $overlap=$false
        do {
            $foreign=@(Foreign-Work $process.Id)
            @{utc=(Get-Date).ToUniversalTime().ToString('o');capture=$name;ownProcessId=$process.Id;foreign=$foreign}|ConvertTo-Json -Depth 5 -Compress|Add-Content -LiteralPath (Join-Path $root 'process-monitor.jsonl')
            if($foreign.Count){$overlap=$true}
        } while(-not $process.WaitForExit(2000))
        [IO.File]::WriteAllText((Join-Path $root "$name.stdout.log"),$stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $root "$name.stderr.log"),$stderr.GetAwaiter().GetResult())
        if($process.ExitCode -ne 0){throw "Native capture $name failed with exit $($process.ExitCode)."}
        if($overlap){throw 'Competing workload overlapped capture; no result from this cohort is accepted.'}
        if((Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash -ne $manifestHash){throw 'Runtime manifest changed during capture.'}
        Write-Output "Completed native residency matrix: $name"
    }
    $failed=$false
    $pixelsPassed=$true
    function Compare-ResidencyPixels([string]$After,[string]$Before,[string]$Name) {
        $reportPath=Join-Path $root "$Name-pixels.json"
        & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot (Join-Path $root $After) -BaselineRoot (Join-Path $root $Before) -ComparePixels -ReportPath $reportPath
        $report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
        if(@($report.captures).Count -ne 6){return $false}
        foreach($case in $report.captures) {
            if(-not $case.equivalentHardware -or -not $case.sameRuntime -or $null -eq $case.pixelComparison -or $case.pixelComparison.MaximumChannelDelta -ne 0){return $false}
        }
        return $true
    }
    foreach($pair in 1,2) {
        try{& (Join-Path $repo 'build/Compare-ReferencePerformance.ps1') -CurrentRoot (Join-Path $root "warm-after-$pair") -BaselineRoot (Join-Path $root "warm-before-$pair") -ReportPath (Join-Path $root "warm-pair$pair-performance.json") -GatePerformance}
        catch{$failed=$true;Write-Output $_.Exception.Message}
        $pixelResult=@(Compare-ResidencyPixels "warm-after-$pair" "warm-before-$pair" "warm-pair$pair")
        if($pixelResult[-1] -ne $true){$pixelsPassed=$false}
    }
    if(-not $WarmOnly) {
        $pixelResult=@(Compare-ResidencyPixels "cold-after" "cold-before" "cold")
        if($pixelResult[-1] -ne $true){$pixelsPassed=$false}
    }
    @{capturesComplete=$true;warmPerformancePassed=(-not $failed);crossSourcePixelsEqual=$pixelsPassed;humanVisualAcceptance="pending";coldAttempted=(-not $WarmOnly)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'status.json') -Encoding utf8NoBOM
    if($failed -or -not $pixelsPassed){throw 'A fixed warm performance or cross-source pixel check failed; preserve all cohorts without replacement or threshold changes.'}
} catch {
    $_|Out-String|Set-Content -LiteralPath (Join-Path $root 'protocol-error.log') -Encoding utf8NoBOM
    throw
}
