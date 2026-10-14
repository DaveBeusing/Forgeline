[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root = Join-Path $tempParent ('forgeline-evidence-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    # Tiny report-contract data; this is not renderer evidence or an accepted golden.
    $baseline = Join-Path $root 'baseline'; $current = Join-Path $root 'current'
    New-Item -ItemType Directory -Path $baseline,$current | Out-Null
    if (-not ('ForgeLine.Client.ReferenceCaptureImage' -as [type])) {
        $source = Get-Content -LiteralPath "$PSScriptRoot/../src/ForgeLine.Client/ReferenceCaptureImage.cs" -Raw
        Add-Type -TypeDefinition ('using System; using System.IO;' + $source.Replace('internal static','public static'))
    }
    foreach ($directory in @($baseline,$current)) { [ForgeLine.Client.ReferenceCaptureImage]::WritePng((Join-Path $directory 'frame.png'),1,1,[byte[]](255,255,255,255)) }
    $hash = (Get-FileHash -LiteralPath (Join-Path $baseline 'frame.png')).Hash
    $capture = @{key='contract';image='frame.png';imageSha256=$hash;fingerprint='stable';repeatPixelsEqual=$true;motionImages=@();
        actualSurface=@{width=1;height=1};requestedDisplay=@{width=1;height=1};instances=@{fallbackMeshInstances=0};materials=@{bindingFailureCount=0};
        graphics=@{resources=@{textureBindingFailureCount=0};device=@{adapterName='report-contract'}};cpu=@{mean=2};gpu=@{mean=1}}
    $manifest = @{schemaVersion=1;status='structural-pass';commit=('a'*40);buildIdentity=('0.1.1-dev+'+('a'*40));runtimeFingerprint=('b'*64);driver='contract';software=$true;captures=@($capture)}
    function Write-Manifests { foreach ($directory in @($baseline,$current)) { $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $directory 'manifest.json') } }
    function Reject([scriptblock]$Action) { $rejected=$false; try { & $Action } catch { $rejected=$true }; if (-not $rejected) { throw 'Malformed evidence was accepted.' } }
    Write-Manifests
    $baselineManifestHash = (Get-FileHash -LiteralPath (Join-Path $baseline 'manifest.json')).Hash
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline
    $report = Get-Content -LiteralPath (Join-Path $current 'comparison.json') -Raw | ConvertFrom-Json
    if ($report.captures[0].comparison -ne 'exact-file-match' -or $report.visualAcceptance -ne 'pending') { throw 'Comparison fabricated visual acceptance.' }
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline }
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -ReportPath (Join-Path $baseline 'new-report.json') }
    if ((Get-FileHash -LiteralPath (Join-Path $baseline 'manifest.json')).Hash -ne $baselineManifestHash -or (Test-Path (Join-Path $baseline 'new-report.json'))) { throw 'Baseline evidence was modified.' }
    $capture.actualSurface.width=2; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'wrong-size.json') }
    $capture.actualSurface.width=1; $capture.imageSha256='0'*64; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'wrong-hash.json') }
    $capture.imageSha256=$hash; $capture.image='../frame.png'; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'escaped.json') }
    $capture.image='frame.png'; $capture.repeatPixelsEqual=$false; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'unstable.json') }
    $capture.repeatPixelsEqual=$true; $manifest.status='in-progress'; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'incomplete.json') }
    $manifest.status='structural-failure'; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'failed-complete.json') }
    $manifest.status='structural-pass';$manifest.buildIdentity='wrong-build'; Write-Manifests
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -ReportPath (Join-Path $root 'wrong-build.json') }
    $manifest.buildIdentity='0.1.1-dev+'+('a'*40); Write-Manifests
    & "$PSScriptRoot/Export-VisualReferenceReview.ps1" -EvidenceRoot $baseline -OutputPath (Join-Path $root 'pending-review.json')
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ApprovalPath (Join-Path $root 'pending-review.json') -ReportPath (Join-Path $root 'unapproved.json') }
    Reject { & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $current -BaselineRoot $baseline -GateImages -ReportPath (Join-Path $root 'missing-approval.json') }
    Write-Output 'Reference dimensions/checksums, incomplete-state rejection and read-only baseline contracts passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'forgeline-evidence-*') { throw 'Unsafe temporary cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
