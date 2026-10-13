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
    $manifest = @{schemaVersion=1;status='structural-pass';commit=('a'*40);driver='contract';software=$false;samples=8;
        environment=@{cpu='contract'};runtimeFingerprint='contract';captures=@($capture)}
    function Write-Current { $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $current 'manifest.json') }
    Write-Current
    Copy-Item -LiteralPath (Join-Path $current 'manifest.json') -Destination (Join-Path $baseline 'manifest.json')
    $baselineHash = (Get-FileHash -LiteralPath (Join-Path $baseline 'manifest.json')).Hash
    & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages
    $capture.cpu.p99=10; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'regression.json') }
    $capture.cpu.p99=2; $manifest.driver='other'; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'hardware.json') }
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ReportPath (Join-Path $current 'hardware-image.json') }
    $manifest.driver='contract'; $manifest.software=$true; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'software.json') }
    $manifest.software=$false; $capture.gpu=@{samples=0;mean=$null;p50=$null;p95=$null;p99=$null}; Write-Current
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -GatePerformance -ReportPath (Join-Path $current 'missing-gpu.json') }
    $capture.image='one-pixel.png'; $capture.imageSha256=(Get-FileHash -LiteralPath (Join-Path $current 'one-pixel.png')).Hash; Write-Current
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ReportPath (Join-Path $current 'image-failed.json') }
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -MaximumChangedPixelFraction .01 -ReportPath (Join-Path $current 'image-tolerance.json')
    Reject { & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $current -BaselineRoot $baseline -ReportPath (Join-Path $baseline 'forbidden.json') }
    if ((Get-FileHash -LiteralPath (Join-Path $baseline 'manifest.json')).Hash -ne $baselineHash -or (Test-Path (Join-Path $baseline 'forbidden.json'))) { throw 'Baseline modified.' }
    Write-Output 'Performance comparability, missing GPU, percentile trend, native pixel tolerance and immutable baseline contracts passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'forgeline-performance-*') { throw 'Unsafe temporary cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
