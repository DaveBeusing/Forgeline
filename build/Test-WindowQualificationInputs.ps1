[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = [IO.Path]::GetFullPath("artifacts/window-input-contracts-$([Guid]::NewGuid().ToString('N'))", $repo)
$allowed = [IO.Path]::GetFullPath('artifacts', $repo) + [IO.Path]::DirectorySeparatorChar
if (-not $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test output root.' }
New-Item -ItemType Directory -Path $root | Out-Null
function Reject([string]$Path, [string]$Expected, [string]$Settings = "$root/settings") {
    $message = ''
    try { & "$PSScriptRoot/Invoke-WindowModeQualification.ps1" -AssetQualificationReport $Path -SettingsRoot $Settings -WindowedReport "$root/window.json" -BorderlessReport "$root/borderless.json" }
    catch { $message = $_.Exception.Message }
    if ($message -notlike "*$Expected*") { throw "Expected '$Expected', observed '$message'." }
    if (Test-Path -LiteralPath "$root/settings") { throw 'Rejected input reached destructive settings setup or native launch.' }
}
try {
    $report = Join-Path $root 'report.json'
    @{success=$true;textureRuntimeBytes=101394467;studioSplashTextureRuntimeBytes=1786417} | ConvertTo-Json | Set-Content -LiteralPath $report
    Reject $report '56 MiB production catalog budget'
    Reject ([IO.Path]::GetRelativePath($repo,$report)) '56 MiB production catalog budget'
    Reject "$root/absent.json" 'Required asset qualification report is absent'
    @{success=$false;textureRuntimeBytes=1000;studioSplashTextureRuntimeBytes=0} | ConvertTo-Json | Set-Content -LiteralPath $report
    Reject $report 'unsuccessful'
    @{success=$true;textureRuntimeBytes=-1;studioSplashTextureRuntimeBytes=0} | ConvertTo-Json | Set-Content -LiteralPath $report
    Reject $report 'Invalid texture footprint totals'
    Reject $report 'repository artifacts directory' ([IO.Path]::GetPathRoot($repo))
    Write-Output 'Window qualification inputs: absolute/relative budgets, missing/failed/invalid reports and owned settings root passed.'
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
