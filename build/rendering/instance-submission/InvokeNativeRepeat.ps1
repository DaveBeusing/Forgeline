[CmdletBinding()]
param(
 [string]$Repository=$env:FORGELINE_INSTANCE_REPOSITORY,
 [string]$ReferenceRepository=$env:FORGELINE_INSTANCE_REFERENCE,
 [string]$OutputRoot=$env:FORGELINE_INSTANCE_OUTPUT,
 [string]$RuntimeAssets=$env:FORGELINE_INSTANCE_ASSETS,
 [string]$BaselineSource=$env:FORGELINE_INSTANCE_BASELINE_SOURCE,
 [string]$ProductSource=$env:FORGELINE_INSTANCE_PRODUCT_SOURCE
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$reference=(Resolve-Path -LiteralPath $ReferenceRepository).Path
$output=[IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
if ($BaselineSource -notmatch '^[a-fA-F0-9]{40}$' -or $ProductSource -notmatch '^[a-fA-F0-9]{40}$') { throw 'Full source SHAs required.' }
& git -C $reference symbolic-ref -q HEAD *> $null
if ($LASTEXITCODE -eq 0 -or (& git -C $reference status --porcelain)) { throw 'Dedicated clean detached checkout required.' }
New-Item -ItemType Directory -Path $output | Out-Null
$metadata=[ordered]@{startedUtc=[DateTime]::UtcNow.ToString('O');baselineSource=$BaselineSource;productSource=$ProductSource;warmups=1024;samples=512;order=@('baseline-a','candidate-a','candidate-b','baseline-b');scriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;assetManifestSha256=(Get-FileHash -LiteralPath (Join-Path $RuntimeAssets 'manifest.json')).Hash;exits=[ordered]@{}}
try {
 foreach ($label in $metadata.order) {
  $baseline=$label.StartsWith('baseline')
  $source=if ($baseline) {$BaselineSource} else {$ProductSource}
  & git -C $reference -c core.longpaths=true switch --detach $source *> (Join-Path $output "$label-checkout.log")
  if ($LASTEXITCODE -ne 0) { throw 'Cannot select capture source.' }
  $client=Join-Path $repo ('artifacts/instance-submission/'+$(if($baseline){'reference-client'}else{'candidate-client'})+'/ForgeLine.Client.exe')
  foreach ($profile in @('High','Low')) {
   $arguments=@{Capture=$true;ClientPath=$client;RuntimeAssetRoot=$RuntimeAssets;Scenes=$(if($profile -eq 'High'){@('IndustrialBase','MainBattleTankProduction','CombatSupportRepeated')}else{@('CombatSupportRepeated')});Profiles=@($profile);Zooms=@('CloseTactical','Strategic');Displays=@('1920x1080');UiScenarios=@('World');RasterDpi=96;Warmups=1024;Samples=512;RepeatCaptures=2;OutputRoot=(Join-Path $output "$label-$profile")}
   & (Join-Path $reference 'build/Invoke-RtsReferenceQualification.ps1') @arguments *> (Join-Path $output "$label-$profile.log")
   $metadata.exits["$label-$profile"]=$LASTEXITCODE
   if ($LASTEXITCODE -ne 0) { throw "Capture failed: $label-$profile" }
  }
 }
} finally {
 & git -C $reference -c core.longpaths=true switch --detach $BaselineSource *> (Join-Path $output 'restore-reference.log')
 $metadata.exits['restoreReference']=$LASTEXITCODE
 $metadata.completedUtc=[DateTime]::UtcNow.ToString('O')
 $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'run.json') -Encoding utf8
 if ($LASTEXITCODE -ne 0) { throw 'Restore the reference checkout before reuse.' }
}
