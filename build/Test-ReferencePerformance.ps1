[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root = Join-Path $tempParent ('forgeline-performance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    # Synthetic report/image contracts; never publish these as renderer measurements.
    if (-not ('ForgeLine.Client.ReferenceCaptureImage' -as [type])) {
        $source = Get-Content -LiteralPath "$PSScriptRoot/../src/ForgeLine.Client/ReferenceCaptureImage.cs" -Raw
        Add-Type -TypeDefinition ('using System; using System.IO;' + $source.Replace('internal static','public static'))
    }
    if (-not ('ReferenceImageComparison' -as [type])) { Add-Type -Path "$PSScriptRoot/ReferenceImageComparison.cs" }
    $baseline = Join-Path $root 'baseline'; $current = Join-Path $root 'current'
    New-Item -ItemType Directory -Path $baseline,$current | Out-Null
    $pixels = [byte[]]::new(400)
    for ($i=3; $i -lt $pixels.Length; $i+=4) { $pixels[$i]=255 }
    foreach ($directory in @($baseline,$current)) { [ForgeLine.Client.ReferenceCaptureImage]::WritePng((Join-Path $directory 'frame.png'),10,10,$pixels) }
    $pixels[0]=1; [ForgeLine.Client.ReferenceCaptureImage]::WritePng((Join-Path $current 'one-lsb.png'),10,10,$pixels)
    $pixels[0]=255; [ForgeLine.Client.ReferenceCaptureImage]::WritePng((Join-Path $current 'one-pixel.png'),10,10,$pixels)
    $delta = [ReferenceImageComparison]::Compare((Join-Path $current 'one-lsb.png'),(Join-Path $baseline 'frame.png'),10,10,1,0)
    if (-not $delta.WithinTolerance -or $delta.MaximumChannelDelta -ne 1) { throw 'Bounded channel tolerance failed.' }
    $delta = [ReferenceImageComparison]::Compare((Join-Path $current 'one-pixel.png'),(Join-Path $baseline 'frame.png'),10,10,0,.01)
    if (-not $delta.WithinTolerance -or $delta.ChangedPixels -ne 1) { throw 'Changed pixel fraction boundary failed.' }
    function Reject([scriptblock]$Action) { $rejected=$false; try { & $Action } catch { $rejected=$true }; if (-not $rejected) { throw 'Invalid qualification was accepted.' } }
    Reject { [ReferenceImageComparison]::Compare((Join-Path $current 'frame.png'),(Join-Path $baseline 'frame.png'),9,10,0,0) }
    [IO.File]::WriteAllBytes((Join-Path $current 'broken.png'),[byte[]](1,2,3))
    Reject { [ReferenceImageComparison]::Compare((Join-Path $current 'broken.png'),(Join-Path $baseline 'frame.png'),10,10,0,0) }
    $capture = @{key='contract';image='frame.png';imageSha256=(Get-FileHash -LiteralPath (Join-Path $current 'frame.png')).Hash;
        fingerprint='stable';repeatPixelsEqual=$true;motionImages=@();actualSurface=@{width=10;height=10};requestedDisplay=@{width=10;height=10};
        instances=@{fallbackMeshInstances=0};materials=@{bindingFailureCount=0};graphics=@{resources=@{textureBindingFailureCount=0};device=@{adapterName='contract'};health=@{submissionFaultCount=0}};
        cpu=@{samples=8;mean=2;p50=2;p95=2;p99=2};gpu=@{samples=8;mean=1;p50=1;p95=1;p99=1};
        camera=@{distance=420};profile=@{quality='Medium'};lighting=@{exposure=1};rasterDpi=96;uiScale=1;
        run=@{runProfile='Warm';cameraPath='Fixed';cameraPathVersion=1;warmupFrames=64}}
    $manifest = @{schemaVersion=1;status='structural-pass';commit=('a'*40);buildIdentity=('0.1.1-dev+'+('a'*40));driver='contract';software=$false;samples=8;
        environment=@{cpu='contract'};runtimeFingerprint=('b'*64);captures=@($capture)}
    function Write-Current { $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $current 'manifest.json') }
    Write-Current
    Copy-Item -LiteralPath (Join-Path $current 'manifest.json') -Destination (Join-Path $baseline 'manifest.json')
    $baselineHash = (Get-FileHash -LiteralPath (Join-Path $baseline 'manifest.json')).Hash
    # Synthetic review exercises validation only; it is not renderer evidence or human approval.
    $approvalPath = Join-Path $root 'synthetic-review.json'
    & "$PSScriptRoot/Export-VisualReferenceReview.ps1" -EvidenceRoot $baseline -OutputPath $approvalPath
    $review = Get-Content -LiteralPath $approvalPath -Raw | ConvertFrom-Json
    $review.reviewer='Synthetic contract';$review.reviewedAtUtc='2026-10-15T00:00:00Z';$review.rationale='Synthetic contract only'
    foreach ($row in $review.captures) { $row.decision='accepted';foreach ($property in $row.rubric.PSObject.Properties) {$property.Value='accepted'} }
    $review | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $approvalPath
    $capture.profile = @{quality='Medium';resolutionScale=1;textures='Full';vegetationMotion=$true;requestedSettings=@{antialiasing='Low'}}
    Write-Current
    & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'profile-compatible.json')
    $compatible = Get-Content (Join-Path $current 'profile-compatible.json') -Raw | ConvertFrom-Json
    if ($compatible.cases[0].status -ne 'comparable') { throw 'Equivalent legacy effective settings were not comparable.' }
    $capture.graphics.device.advanced = @{meshShaderQuerySucceeded=$true;meshShadersSupported=$true;raytracingQuerySucceeded=$true;raytracingSupported=$true}
    Write-Current
    & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'capability-compatible.json')
    $capture.graphics.device.adapterName = 'other-adapter'
    Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'adapter-changed.json') }
    $capture.graphics.device.adapterName = 'contract'
    $capture.graphics.device.Remove('advanced')
    $capture.profile.resolutionScale = .75
    Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'profile-changed.json') }
    $capture.profile.resolutionScale = 1
    $capture.profile.environment = [ordered]@{daylight=1;cloudCover=0;wetness=.5}
    Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'wetness-changed.json') }
    $capture.profile.Remove('environment')
    $capture.surfaceMarks = @(@{kind='TireTrack';widthMeters=3})
    Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'marks-changed.json') }
    $capture.Remove('surfaceMarks')
    $capture.profile = @{quality='Medium'}
    Write-Current
    & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath
    $capture.cpu.p99=10; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'regression.json') }
    $capture.cpu.p99=2; $manifest.driver='other'; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'hardware.json') }
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath -ReportPath (Join-Path $current 'hardware-image.json') }
    $manifest.driver='contract'; $manifest.software=$true; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'software.json') }
    $manifest.software=$false; $capture.gpu=@{samples=0;mean=$null;p50=$null;p95=$null;p99=$null}; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'missing-gpu.json') }
    $capture.image='one-pixel.png'; $capture.imageSha256=(Get-FileHash -LiteralPath (Join-Path $current 'one-pixel.png')).Hash; Write-Current
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath -ReportPath (Join-Path $current 'image-failed.json') }
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath -MaximumChangedPixelFraction .01 -ReportPath (Join-Path $current 'image-tolerance.json')
    $manifest.runtimeFingerprint='c'*64;Write-Current
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath -ReportPath (Join-Path $current 'changed-runtime.json') }
    $manifest.runtimeFingerprint='b'*64;$manifest.commit='c'*40;$manifest.buildIdentity='0.1.1-dev+'+('c'*40);Write-Current
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath -ReportPath (Join-Path $current 'changed-source.json') }
    $manifest.commit='a'*40;$manifest.buildIdentity='0.1.1-dev+'+('a'*40);Write-Current
    $review.captures[0].rubric.focus='pending';$review | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $approvalPath
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath $approvalPath -ReportPath (Join-Path $current 'incomplete-rubric.json') }
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -ReportPath (Join-Path $baseline 'forbidden.json') }
    if ((Get-FileHash -LiteralPath (Join-Path $baseline 'manifest.json')).Hash -ne $baselineHash -or (Test-Path (Join-Path $baseline 'forbidden.json'))) { throw 'Baseline modified.' }
    Write-Output 'Performance comparability, missing GPU, percentile trend, native pixel tolerance and immutable baseline contracts passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'forgeline-performance-*') { throw 'Unsafe temporary cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
