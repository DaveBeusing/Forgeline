[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReferenceRepository,
    [Parameter(Mandatory)][string]$BaselineClient,
    [Parameter(Mandatory)][string]$CandidateClient,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$BaselineSource,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$CandidateSource,
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$reference = (Resolve-Path -LiteralPath $ReferenceRepository).Path
$baseline = (Resolve-Path -LiteralPath $BaselineClient).Path
$candidate = (Resolve-Path -LiteralPath $CandidateClient).Path
$assets = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh capture directory.' }
& git -C $reference symbolic-ref -q HEAD *> $null
if ($LASTEXITCODE -eq 0 -or (& git -C $reference status --porcelain)) { throw 'Use a dedicated clean detached reference checkout.' }
$original = (& git -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve reference checkout.' }
New-Item -ItemType Directory -Path $output | Out-Null
$metadata = [ordered]@{ baselineSource=$BaselineSource; candidateSource=$CandidateSource
    order=@('baseline-a','candidate-a','candidate-b','baseline-b'); warmups=512; samples=256
    runtimeManifestSha256=(Get-FileHash -LiteralPath (Join-Path $assets 'manifest.json')).Hash
    scriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash; exits=[ordered]@{} }
try {
    foreach ($label in $metadata.order) {
        $before = $label.StartsWith('baseline')
        $source = if ($before) { $BaselineSource } else { $CandidateSource }
        & git -C $reference -c core.longpaths=true switch --detach $source *> (Join-Path $output "$label-checkout.log")
        if ($LASTEXITCODE -ne 0) { throw 'Cannot select exact capture source.' }
        foreach ($path in @('Fixed','Sweep')) {
            $arguments = @{ Capture=$true; ClientPath=$(if($before){$baseline}else{$candidate}); RuntimeAssetRoot=$assets
                Scenes=@('DaylightTerrain','LogisticsCorridor'); Profiles=@('High','Low'); Zooms=@('CloseTactical','Strategic')
                Displays=@('1920x1080'); UiScenarios=@('World'); RasterDpi=96; CameraPath=$path
                Warmups=512; Samples=256; RepeatCaptures=2; OutputRoot=(Join-Path $output "$label-$path") }
            & (Join-Path $reference 'build/Invoke-RtsReferenceQualification.ps1') @arguments *> (Join-Path $output "$label-$path.log")
            $metadata.exits["$label-$path"] = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) { throw "Client capture failed: $label-$path" }
        }
    }
} finally {
    & git -C $reference -c core.longpaths=true switch --detach $original *> (Join-Path $output 'restore.log')
    $metadata.exits['restoreReference'] = $LASTEXITCODE
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'protocol.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Reference checkout restoration failed.' }
}
