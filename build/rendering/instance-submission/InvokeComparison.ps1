[CmdletBinding()]
param(
    [string]$Repository = $env:FORGELINE_INSTANCE_REPOSITORY,
    [string]$ReferenceRepository = $env:FORGELINE_INSTANCE_REFERENCE,
    [string]$OutputRoot = $env:FORGELINE_INSTANCE_OUTPUT,
    [string]$RuntimeAssets = $env:FORGELINE_INSTANCE_ASSETS,
    [string]$BaselineSource = $env:FORGELINE_INSTANCE_BASELINE_SOURCE,
    [string]$ProductSource = $env:FORGELINE_INSTANCE_PRODUCT_SOURCE,
    [string]$ClientSuffix = $env:FORGELINE_INSTANCE_CLIENT_SUFFIX,
    [switch]$NativeOnly = ($env:FORGELINE_INSTANCE_NATIVE -eq '1')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($BaselineSource -notmatch '^[a-fA-F0-9]{40}$' -or $ProductSource -notmatch '^[a-fA-F0-9]{40}$') { throw 'Supply full baseline and product source SHAs.' }
$repo = (Resolve-Path -LiteralPath $Repository).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRepository).Path
$assets = (Resolve-Path -LiteralPath $RuntimeAssets).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh cohort directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$artifactRoot = Join-Path $repo 'artifacts/instance-submission'
$metadata = [ordered]@{ startedUtc = [DateTime]::UtcNow.ToString('O'); baselineSource = $BaselineSource;
    productSource = $ProductSource; nativeOnly = [bool]$NativeOnly;
    probeSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'SubmissionProbe.cs')).Hash;
    scriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash;
    assetManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $assets 'manifest.json')).Hash;
    assetContentSignature = (Get-Content -LiteralPath (Join-Path $assets 'manifest.json') -Raw | ConvertFrom-Json).contentSignature;
    exits = [ordered]@{} }
Push-Location $repo
try {
    if (!$NativeOnly) {
        foreach ($label in @('baseline-a','candidate-a','baseline-b','candidate-b')) {
            $probeName = if ($label.StartsWith('baseline')) { 'reference-probe' } else { 'product-probe' }
            $source = if ($label.StartsWith('baseline')) { $BaselineSource } else { $ProductSource }
            $probe = Join-Path $artifactRoot $probeName
            & dotnet (Join-Path $probe 'SubmissionProbe.dll') (Join-Path $output "$label.json") $assets *> (Join-Path $output "$label.log")
            $metadata.exits[$label] = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) { throw "CPU cohort failed: $label" }
            $result = Get-Content -LiteralPath (Join-Path $output "$label.json") -Raw | ConvertFrom-Json
            if (!$result.SourceVersion.EndsWith($source, [StringComparison]::OrdinalIgnoreCase)) { throw "Incorrect source: $label" }
            $option = if ($label.EndsWith('-a')) { '--frame-hotpaths' } else { '--culled-hotpath' }
            $kind = if ($label.EndsWith('-a')) { 'frame' } else { 'culled' }
            & dotnet exec --runtimeconfig (Join-Path $probe 'SubmissionProbe.runtimeconfig.json') --depsfile (Join-Path $probe 'SubmissionProbe.deps.json') (Join-Path $probe 'ForgeLine.Rendering.Benchmarks.dll') $option (Join-Path $output "$label-$kind.json") *> (Join-Path $output "$label-$kind.log")
            $metadata.exits["$label-$kind"] = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) { throw "Existing benchmark failed: $label-$kind" }
        }
    } else {
        # This must be a dedicated, clean detached comparison checkout.
        & git -C $reference symbolic-ref -q HEAD *> $null
        if ($LASTEXITCODE -eq 0) { throw 'Reference checkout must be detached.' }
        if ((& git -C $reference status --porcelain)) { throw 'Reference checkout must be clean.' }
        try {
            foreach ($side in @('baseline','candidate')) {
                $source = if ($side -eq 'baseline') { $BaselineSource } else { $ProductSource }
                & git -C $reference -c core.longpaths=true switch --detach $source *> (Join-Path $output "$side-checkout.log")
                if ($LASTEXITCODE -ne 0) { throw 'Cannot select exact capture source.' }
                $clientName = if ($side -eq 'baseline') { 'reference-client' } else { 'candidate-client' }
                $client = Join-Path $artifactRoot "$clientName$ClientSuffix/ForgeLine.Client.exe"
                foreach ($path in @('Fixed','Sweep','Budget')) {
                    $scenes = if ($path -eq 'Fixed') { @('IndustrialBase','BattlefieldCombat','MainBattleTankProduction','CombatSupportRepeated','MechanicalFiring','EffectsBarrage','OperationalBuildingsRepeated') } else { @('CombatSupportRepeated') }
                    $arguments = @{ Capture = $true; ClientPath = $client; RuntimeAssetRoot = $assets;
                        Scenes = $scenes; Profiles = $(if ($path -eq 'Budget') { @('Low') } else { @('High') }); Zooms = @('CloseTactical','Strategic');
                        Displays = @('1920x1080'); UiScenarios = @('World'); RasterDpi = 96;
                        Warmups = 64; Samples = 128; RepeatCaptures = 2; CameraPath = $(if ($path -eq 'Budget') { 'Fixed' } else { $path });
                        OutputRoot = (Join-Path $output "$side-$path") }
                    & (Join-Path $reference 'build/Invoke-RtsReferenceQualification.ps1') @arguments *> (Join-Path $output "$side-$path.log")
                    $metadata.exits["$side-$path"] = $LASTEXITCODE
                    if ($LASTEXITCODE -ne 0) { throw "Native cohort failed: $side-$path" }
                }
            }
        } finally {
            & git -C $reference -c core.longpaths=true switch --detach $BaselineSource *> (Join-Path $output 'restore-reference.log')
            $metadata.exits['restoreReference'] = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) { throw 'Restore the dedicated reference checkout before reuse.' }
        }
        $metadata['pixelChecks'] = [ordered]@{}
        foreach ($path in @('Fixed','Sweep','Budget')) {
            & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot (Join-Path $output "candidate-$path") -BaselineRoot (Join-Path $output "baseline-$path") -ComparePixels -ReportPath (Join-Path $output "$path-pixels.json") *> (Join-Path $output "$path-pixels.log")
            $metadata.exits["$path-pixels"] = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) { throw "Native image comparison failed structurally: $path" }
            $pixelReport = Get-Content -LiteralPath (Join-Path $output "$path-pixels.json") -Raw | ConvertFrom-Json
            $rows = @($pixelReport.captures)
            $outside = @($rows | Where-Object { $null -eq $_.pixelComparison -or !$_.pixelComparison.WithinTolerance }).Count
            # Inspect row values: a structural exit and aggregate list do not certify pixels.
            $metadata.pixelChecks[$path] = [ordered]@{ inspectedRows = $rows.Count; outsideTolerance = $outside; humanApproval = 'pending' }
        }
    }
} finally {
    $metadata.completedUtc = [DateTime]::UtcNow.ToString('O')
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'run.json') -Encoding utf8
    Pop-Location
}
