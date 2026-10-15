[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceRoot, [Parameter(Mandatory)][string]$OutputPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFullPath($OutputPath).StartsWith([IO.Path]::GetFullPath($EvidenceRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Review must be separate from immutable evidence.' }
$manifestPath = Join-Path $EvidenceRoot 'manifest.json'
if (Test-Path -LiteralPath (Join-Path $EvidenceRoot 'failure.json')) { throw 'Failed evidence cannot enter baseline review.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.status -ne 'structural-pass') { throw 'Only completed structural evidence can enter review.' }
$review = [ordered]@{
    schemaVersion = 1
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath).Hash
    commit = $manifest.commit
    reviewer = ''
    reviewedAtUtc = ''
    rationale = ''
    physicalMonitor = 'pending'
    liveMotion = 'pending'
    gpuPerformance = 'pending'
    captures = @($manifest.captures | ForEach-Object {
        [ordered]@{key=$_.key; imageSha256=$_.imageSha256; decision='pending'; notes='';
            rubric=[ordered]@{typography='pending';hierarchy='pending';contrast='pending';focus='pending';
                hitAlignment='pending';feedbackSeverity='pending';occlusion='pending';intelligence='pending'}}
    })
}
$stream = [IO.File]::Open([IO.Path]::GetFullPath($OutputPath), [IO.FileMode]::CreateNew)
try { $bytes = [Text.Encoding]::UTF8.GetBytes(($review | ConvertTo-Json -Depth 10)); $stream.Write($bytes) }
finally { $stream.Dispose() }
Write-Output 'Pending review template exported. A human must inspect the exact images and complete the decisions; no approval was generated.'
