[CmdletBinding()]
param([string]$BaseRef = 'origin/master')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
$content = & git show "${BaseRef}:Directory.Build.props"
if ($LASTEXITCODE -ne 0) { throw "Cannot read version at $BaseRef." }
$base = Get-ProductVersion -Content ($content -join "`n")
$next = Get-NextPrVersion -BaseVersion $base
$current = Get-ProductVersion
if ($current -eq $next) { Write-Host "PR version already $next; no further increment."; return }
if ($current -ne $base) { throw "Unexpected version $current; rebase onto current master before preparing the PR." }
$path = "$PSScriptRoot/../Directory.Build.props"
$text = Get-Content $path -Raw
$text = [regex]::Replace($text, '(?<=<ForgeLineVersionPatch>)[0-9]+(?=</ForgeLineVersionPatch>)', ([version]$next).Build.ToString())
[IO.File]::WriteAllText([IO.Path]::GetFullPath($path), $text)
Write-Host "PR version: $base -> $next. Commit Directory.Build.props with this PR."
