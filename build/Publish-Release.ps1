[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$SourceRevision,
      [string]$ArtifactRoot = 'artifacts/release', [string]$NotesPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
Assert-BuildVersion -Version $Version
if ($Version -match '-(dev|pr\.)') { throw 'Local/PR artifacts cannot be released.' }
if ($SourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'Full source SHA required.' }
$repo = $env:GITHUB_REPOSITORY
if ($repo -ne 'DaveBeusing/Forgeline') { throw 'Only the canonical repository can publish.' }
if (-not $env:RELEASE_ADMIN_READ_TOKEN) { throw 'Configure RELEASE_ADMIN_READ_TOKEN with Administration:read to verify release immutability.' }
# GITHUB_TOKEN does not grant administration. A separate read-only token checks
# the setting; it is never used to upload assets or alter repository settings.
$writeToken = $env:GH_TOKEN
try {
    $env:GH_TOKEN = $env:RELEASE_ADMIN_READ_TOKEN
    $setting = Invoke-GitHub -Path "repos/$repo/immutable-releases"
    if (-not $setting.enabled) { throw 'Enable GitHub immutable releases before publication.' }
} finally { $env:GH_TOKEN = $writeToken }

$tagName = "v$Version"
$reference = Invoke-GitHub -Path "repos/$repo/git/ref/tags/$tagName"
if ($reference.object.type -ne 'tag') { throw 'Release tag must be annotated.' }
$tag = Invoke-GitHub -Path "repos/$repo/git/tags/$($reference.object.sha)"
if ($tag.object.type -ne 'commit' -or $tag.object.sha -ne $SourceRevision) { throw 'Tag provenance mismatch.' }
$archive = Join-Path $ArtifactRoot "FORGELINE-$Version-win-x64.zip"
$provenance = Get-Content "$archive.provenance.json" -Raw | ConvertFrom-Json
if ($provenance.version -ne $Version -or $provenance.sourceRevision -ne $SourceRevision -or
    -not $provenance.sourceTreeClean -or $provenance.runId -ne $env:GITHUB_RUN_ID -or
    $provenance.runAttempt -ne $env:GITHUB_RUN_ATTEMPT) { throw 'Artifact provenance mismatch.' }
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedLine = "$checksum  $([System.IO.Path]::GetFileName($archive))"
if ((Get-Content "$archive.sha256" -Raw).Trim() -ne $expectedLine) { throw 'Artifact checksum mismatch.' }
if (-not $NotesPath -or -not (Test-Path -LiteralPath $NotesPath)) { throw 'Reviewed release and compatibility notes required.' }
$notes = Get-Content -LiteralPath $NotesPath -Raw
if ([string]::IsNullOrWhiteSpace($notes)) { throw 'Release notes must not be empty.' }
$notes += "`n`nVersion: $Version`nSource: $SourceRevision`nBuild: $($provenance.runUrl)`nSHA-256: $checksum`n"

# Creation fails on every collision, including an incomplete draft from an
# interrupted attempt. There is no update/clobber/delete path.
$release = Invoke-GitHub -Method POST -Path "repos/$repo/releases" -Body @{
    tag_name = $tagName; name = "FORGELINE $Version"; body = $notes
    draft = $true; prerelease = $Version.Contains('-')
}
& gh release upload $tagName $archive "$archive.sha256" "$archive.provenance.json" --repo $repo
if ($LASTEXITCODE -ne 0) { throw 'Release upload failed. Draft retained for investigation; do not overwrite it.' }
$published = Invoke-GitHub -Method PATCH -Path "repos/$repo/releases/$($release.id)" -Body @{ draft = $false }
if (-not $published.immutable) { throw 'Publication did not confirm GitHub immutable release enforcement.' }
Write-Host "Published immutable release $($published.html_url)"
