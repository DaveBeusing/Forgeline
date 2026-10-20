#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineClient,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineSource,
    [Parameter(Mandatory)][string]$CandidateClient,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CandidateSource,
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=[IO.Path]::GetFullPath($OutputRoot,$repo)
$allowed=(Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar
if(-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Choose a fresh output directory below artifacts.'}
[void][IO.Directory]::CreateDirectory($root)
$before=[IO.Path]::GetFullPath($BaselineClient,$repo)
$after=[IO.Path]::GetFullPath($CandidateClient,$repo)
$runtime=[IO.Path]::GetFullPath($RuntimeRoot,$repo)
$protocol=@{baselineSource=$BaselineSource;candidateSource=$CandidateSource;
    baselineClientSha256=(Get-FileHash -LiteralPath $before).Hash;candidateClientSha256=(Get-FileHash -LiteralPath $after).Hash;
    manifestSha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash;
    declaredUtc=[DateTime]::UtcNow.ToString('o');warmups=64;samples=256;repeatCaptures=2;
    sequence=@('before-1','after-1','after-2','before-2');maximumRegressionPercent=15;
    driver=@(Get-CimInstance Win32_VideoController|ForEach-Object {"$($_.Name): $($_.DriverVersion)"});
    interpretation='Changed images are candidates for human material/shadow review, not new goldens. GPU world and shadow times remain separate; unavailable timings stay null.'}
$protocol|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root 'protocol.json') -Encoding utf8NoBOM
foreach($member in $protocol.sequence) {
    $busy=@(Get-CimInstance Win32_Process|Where-Object {
        $_.Name -in @('ForgeLine.Client.exe','ForgeLine.Headless.exe','GameplayRecoveryProbe.exe','ForgeLine.AssetCompiler.exe','csc.exe','VBCSCompiler.exe','MSBuild.exe','blender.exe') -or $_.Name -like 'ForgeLine*.Tests.exe' -or $_.Name -like 'ForgeLine*Benchmarks.exe' -or
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match ' test | build |Tests[.]dll|Benchmarks|VBCSCompiler|csc[.]dll|MSBuild[.]dll')})
    if($busy.Count){$busy|Select-Object ProcessId,Name,CreationDate|ConvertTo-Json|Set-Content (Join-Path $root 'competing-work.json');throw 'Competing workload found; retain and stop this cohort.'}
    $client=if($member.StartsWith('before')){$before}else{$after}
    $source=if($member.StartsWith('before')){$BaselineSource}else{$CandidateSource}
    foreach($kind in @('fixed','sweep')) {
        $name="$member-$kind"
        $request=@{commit=$source;runtimeRoot=$runtime;outputRoot=(Join-Path $root $name);software=$false;
            runProfile='Warm';cameraPath=if($kind -eq 'fixed'){'Fixed'}else{'Sweep'};driver=($protocol.driver -join '; ');
            graphicsSettings=@{resolutionScale=1.0;environment=@{daylight=1.0;cloudCover=0.0;wetness=0.0};
                vegetation=@{density=1.0;maximumDistanceMeters=4000.0};terrainResidency=@{enabled=$true;maximumResidentChunks=128}};
            warmups=64;samples=256;repeatCaptures=2;continueAfterMismatch=$false;uiScenarios=@('World');uiScales=@(1.0);
            scenes=@('MainBattleTankProduction','IndustrialBase');displays=@(@{width=1920;height=1080});
            profiles=@(if($kind -eq 'fixed'){'High'}else{'Low';'Medium';'High';'Ultra'});
            zooms=@(if($kind -eq 'fixed'){'CloseTactical';'NormalGameplay';'Strategic'}else{'NormalGameplay'});yawDegrees=45.0}
        $requestPath=Join-Path $root "$name.request.json"
        $request|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
        & nvidia-smi --query-gpu=timestamp,pstate,clocks.gr,power.draw,power.limit --format=csv >> (Join-Path $root 'gpu.csv')
        if($LASTEXITCODE -ne 0){throw 'GPU telemetry query failed.'}
        $info=[Diagnostics.ProcessStartInfo]::new($client)
        $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
        $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
        $info.ArgumentList.Add('--reference-capture-request');$info.ArgumentList.Add($requestPath)
        $process=[Diagnostics.Process]::Start($info)
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $overlap=$false
        do {
            $foreign=@(Get-CimInstance Win32_Process|Where-Object {$_.ProcessId -ne $process.Id -and (
                $_.Name -in @('ForgeLine.Client.exe','ForgeLine.Headless.exe','GameplayRecoveryProbe.exe','ForgeLine.AssetCompiler.exe','csc.exe','VBCSCompiler.exe','MSBuild.exe','blender.exe') -or $_.Name -like 'ForgeLine*.Tests.exe' -or
                $_.Name -like 'ForgeLine*Benchmarks.exe' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match ' test | build |Tests[.]dll|Benchmarks|VBCSCompiler|csc[.]dll|MSBuild[.]dll'))}|
                Select-Object ProcessId,Name,CreationDate)
            @{utc=[DateTime]::UtcNow.ToString('o');capture=$name;foreign=$foreign}|ConvertTo-Json -Depth 5 -Compress|
                Add-Content -LiteralPath (Join-Path $root 'process-monitor.jsonl')
            if($foreign.Count){$overlap=$true}
        } while(-not $process.WaitForExit(2000))
        [IO.File]::WriteAllText((Join-Path $root "$name.stdout.log"),$stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $root "$name.stderr.log"),$stderr.GetAwaiter().GetResult())
        if($process.ExitCode -ne 0){throw "Native capture $name failed with exit $($process.ExitCode)."}
        if($overlap){throw 'Competing workload overlapped capture; no result is accepted.'}
        if((Get-FileHash -LiteralPath (Join-Path $runtime 'manifest.json')).Hash -ne $protocol.manifestSha256){throw 'Runtime changed.'}
        Write-Output "Completed native material matrix: $name"
    }
}
$comparisons=foreach($pair in @(1,2)) {
    foreach($kind in @('fixed','sweep')) {
        $comparisonExit=0
        try {
            & (Join-Path $repo 'build/Compare-ReferencePerformance.ps1') -BaselineRoot (Join-Path $root "before-$pair-$kind") `
                -CurrentRoot (Join-Path $root "after-$pair-$kind") -ReportPath (Join-Path $root "performance-$pair-$kind.json") -GatePerformance `
                *> (Join-Path $root "performance-$pair-$kind.log")
        } catch { $comparisonExit=2;Write-Warning $_.Exception.Message }
        @{pair=$pair;kind=$kind;exitCode=$comparisonExit}
    }
}
@{capturesCompleted=$true;comparisons=@($comparisons);performancePassed=(@($comparisons|Where-Object {$_.exitCode -ne 0}).Count -eq 0);humanMaterialReview='pending';humanShadowContactReview='pending'}|
    ConvertTo-Json -Depth 6|Set-Content (Join-Path $root 'status.json') -Encoding utf8NoBOM
Write-Output "Material/shadow candidate comparisons retained: $root"
