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
    $pixels = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=')
    foreach ($directory in @($baseline,$current)) { [IO.File]::WriteAllBytes((Join-Path $directory 'frame.png'),$pixels) }
    $hash = (Get-FileHash -LiteralPath (Join-Path $baseline 'frame.png')).Hash
    $capture = @{key='contract';image='frame.png';imageSha256=$hash;fingerprint='stable';repeatPixelsEqual=$true;motionImages=@();
        actualSurface=@{width=1;height=1};requestedDisplay=@{width=1;height=1};instances=@{fallbackMeshInstances=0};materials=@{bindingFailureCount=0};
        graphics=@{resources=@{textureBindingFailureCount=0};device=@{adapterName='report-contract'}};cpu=@{mean=2};gpu=@{mean=1}}
    $manifest = @{schemaVersion=1;status='structural-pass';commit=('a'*40);driver='contract';software=$true;captures=@($capture)}
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
    Write-Output 'Reference dimensions/checksums, incomplete-state rejection and read-only baseline contracts passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'forgeline-evidence-*') { throw 'Unsafe temporary cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
