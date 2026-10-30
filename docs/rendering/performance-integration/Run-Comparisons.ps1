[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$BaselineClientDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineCommit,
    [Parameter(Mandatory)][string]$CurrentClientDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CurrentCommit,
    [Parameter(Mandatory)][string]$RuntimeAssetRoot,
    [Parameter(Mandatory)][string]$RequestTemplate,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateRange(3,4096)][int]$Warmups=128,
    [ValidateRange(8,8192)][int]$Samples=512
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$output=[IO.Path]::GetFullPath($OutputRoot,$repo)
$allowed=(Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar
if(-not $output.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Use a fresh output directory below repository artifacts.'}
if((git -C $repo status --porcelain)){throw 'The integration source must be clean.'}
if((git -C $repo cat-file -t $CurrentCommit).Trim() -ne 'commit'){throw 'CurrentCommit must identify an existing source commit.'}
if([Environment]::GetEnvironmentVariable('FORGELINE_DRED') -eq '1'){throw 'Disable DRED for timing.'}
$baseline=[IO.Path]::GetFullPath($BaselineClientDirectory,$repo)
$current=[IO.Path]::GetFullPath($CurrentClientDirectory,$repo)
$runtime=[IO.Path]::GetFullPath($RuntimeAssetRoot,$repo)
$template=[IO.Path]::GetFullPath($RequestTemplate,$repo)
foreach($pair in @(@($baseline,$BaselineCommit),@($current,$CurrentCommit))){
    $identity=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $pair[0] 'ForgeLine.Client.dll')).ProductVersion
    if(-not $identity.EndsWith($pair[1],[StringComparison]::OrdinalIgnoreCase)){throw 'Frozen client source identity mismatch.'}
}
function Inventory([string]$Directory){
    return @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($Directory,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$before=[ordered]@{baseline=Inventory $baseline;current=Inventory $current;runtime=Inventory $runtime}
$before | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'inputs-before.json') -Encoding utf8NoBOM
$drivers=@(Get-CimInstance Win32_VideoController | ForEach-Object {"$($_.Name): $($_.DriverVersion)"}) -join '; '
$scale=@('Scaling1000High','Scaling1000Low','Scaling10000High','Scaling10000Low','Scaling100000High','Scaling100000Low','ScalingTerrainOnly')
$plan=@(
    @('baseline-a-production','baseline','Fixed','production','Default'),
    @('baseline-a-fixed','baseline','Fixed','scaling','Default'),
    @('baseline-a-sweep','baseline','Sweep','scaling','Default'),
    @('candidate-a-fixed','current','Fixed','scaling','Default'),
    @('candidate-a-sweep','current','Sweep','scaling','Default'),
    @('candidate-a-production','current','Fixed','production','Default'),
    @('memory-upload-a','current','Fixed','production','Upload'),
    @('memory-upload-b','current','Fixed','production','Upload'),
    @('candidate-b-production','current','Fixed','production','Default'),
    @('candidate-b-sweep','current','Sweep','scaling','Default'),
    @('candidate-b-fixed','current','Fixed','scaling','Default'),
    @('baseline-b-sweep','baseline','Sweep','scaling','Default'),
    @('baseline-b-fixed','baseline','Fixed','scaling','Default'),
    @('baseline-b-production','baseline','Fixed','production','Default')
)
$manifest=[ordered]@{schemaVersion=1;checkoutCommit=(& git -C $repo rev-parse HEAD).Trim();requestTemplateSha256=(Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash.ToLowerInvariant();runnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant();baselineCommit=$BaselineCommit;currentCommit=$CurrentCommit;warmups=$Warmups;samples=$Samples;repeatCaptures=2;driver=$drivers;startedUtc=[DateTimeOffset]::UtcNow;powerScheme=(& powercfg /getactivescheme | Out-String).Trim();thermals='Not traced or controlled';plan=$plan}
$manifest | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $output 'execution-plan.json') -Encoding utf8NoBOM
$oldFallback=[Environment]::GetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK','Process')
try {
    foreach($row in $plan){
        $name=$row[0];$kind=$row[1]
        $cohort=Join-Path $output $name
        $request=Get-Content -LiteralPath $template -Raw | ConvertFrom-Json
        $request.outputRoot=$cohort
        $request.commit=if($kind -eq 'baseline'){$BaselineCommit}else{$CurrentCommit}
        $request.runtimeRoot=$runtime
        $request.cameraPath=$row[2]
        $request.runProfile='Warm';$request.driver=$drivers;$request.software=$false
        $request.warmups=$Warmups;$request.samples=$Samples;$request.repeatCaptures=2
        $request.continueAfterMismatch=$true
        $request.scenes=@(if($row[3] -eq 'scaling'){$scale}else{'IndustrialBase';'BattlefieldCombat'})
        $request.zooms=@(if($row[3] -eq 'scaling'){'NormalGameplay'}else{'CloseTactical';'Strategic'})
        $request.profiles=@('Medium');$request.displays=@(@{width=1920;height=1080})
        $request.uiScenarios=@('World');$request.uiScales=@(1)
        $request.graphicsSettings.resolutionScale=1
        $requestPath=$cohort+'.request.json'
        $request | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
        if($row[4] -eq 'Upload'){$env:FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK='1'}
        else{Remove-Item Env:FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK -ErrorAction SilentlyContinue}
        $client=if($kind -eq 'baseline'){$baseline}else{$current}
        $started=[DateTimeOffset]::UtcNow
        Write-Output "Starting matched cohort: $name"
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $client 'ForgeLine.Client.exe'))
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$repo
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.ArgumentList.Add('--reference-capture-request');$start.ArgumentList.Add($requestPath)
        $process=[Diagnostics.Process]::Start($start)
        try {
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            $process.WaitForExit()
            $code=$process.ExitCode
            [IO.File]::WriteAllText($cohort+'.log',$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult())
        } finally {$process.Dispose()}
        $result=[ordered]@{cohort=$name;source=$request.commit;fallback=$row[4];startedUtc=$started;completedUtc=[DateTimeOffset]::UtcNow;exitCode=$code;requestSha256=(Get-FileHash -LiteralPath $requestPath -Algorithm SHA256).Hash.ToLowerInvariant()}
        $result | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $output 'cohorts.jsonl') -Encoding utf8
        Write-Output "Completed matched cohort: $name (exit $code)"
    }
} finally {
    if($null -eq $oldFallback){Remove-Item Env:FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK -ErrorAction SilentlyContinue}
    else{[Environment]::SetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK',$oldFallback,'Process')}
}
$after=[ordered]@{baseline=Inventory $baseline;current=Inventory $current;runtime=Inventory $runtime}
$after | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'inputs-after.json') -Encoding utf8NoBOM
if(($before | ConvertTo-Json -Depth 5 -Compress) -cne ($after | ConvertTo-Json -Depth 5 -Compress)){throw 'Frozen inputs changed during timing.'}
function Invoke-MatchedComparison([string]$Current,[string]$Baseline,[string]$Mode='Matched'){
    $label=$Current+'-vs-'+$Baseline
    try {
        & (Join-Path $repo 'build/Compare-ReferencePerformance.ps1') -CurrentRoot (Join-Path $output $Current) -BaselineRoot (Join-Path $output $Baseline) -ComparisonMode $Mode -ReportPath (Join-Path $output ($label+'.performance.json'))
        & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot (Join-Path $output $Current) -BaselineRoot (Join-Path $output $Baseline) -ComparePixels -ReportPath (Join-Path $output ($label+'.images.json'))
    } catch {
        [ordered]@{comparison=$label;mode=$Mode;status='unqualified';error=$_.Exception.Message} | ConvertTo-Json | Set-Content (Join-Path $output ($label+'.unqualified.json'))
    }
}
foreach($suffix in @('fixed','sweep','production')){
    Invoke-MatchedComparison "candidate-a-$suffix" "baseline-a-$suffix"
    Invoke-MatchedComparison "candidate-b-$suffix" "baseline-b-$suffix"
    Invoke-MatchedComparison "baseline-b-$suffix" "baseline-a-$suffix"
    Invoke-MatchedComparison "candidate-b-$suffix" "candidate-a-$suffix"
}
Invoke-MatchedComparison 'memory-upload-a' 'candidate-a-production' 'StaticMemory'
Invoke-MatchedComparison 'memory-upload-b' 'candidate-b-production' 'StaticMemory'
Invoke-MatchedComparison 'memory-upload-b' 'memory-upload-a'
Write-Output 'All matched cohorts, source inventories, advisory comparisons and strict pixel differences retained.'
