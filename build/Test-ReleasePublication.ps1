# Verify the real publication path using a local artifact and mocked GitHub.
# No secrets, remote release, tags or uploads are created.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
$names = @('GH_TOKEN', 'RELEASE_ADMIN_READ_TOKEN', 'GITHUB_REPOSITORY', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("release-publication-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$global:releasePublicationTestState = @{
    enabled = $true; exists = $false; claimed = $false; uploads = 0; creates = 0; tagName = ''
    sha = (& git rev-parse HEAD).Trim()
}
function gh {
    [CmdletBinding(PositionalBinding = $false)]
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments,
          [Parameter(ValueFromPipeline)][string]$RequestBody)
    begin { $bodyLines = [Collections.Generic.List[string]]::new() }
    process { if ($RequestBody) { $bodyLines.Add($RequestBody) } }
    end {
        $state = $global:releasePublicationTestState
        $global:LASTEXITCODE = 0
        if ($Arguments[0] -eq 'release') {
            if ($Arguments -contains '--clobber') { throw 'Publication attempted overwrite.' }
            $state.uploads++
            return
        }
        $methodIndex = [Array]::IndexOf($Arguments, '--method')
        $method = if ($methodIndex -ge 0) { $Arguments[$methodIndex + 1] } else { 'GET' }
        $path = @($Arguments | Where-Object { $_ -like 'repos/*' })[0]
        if ($path -like '*/immutable-releases') { return @{ enabled = $state.enabled } | ConvertTo-Json }
        if ($path -like '*/git/ref/tags/*') { return @{ object = @{ type = 'tag'; sha = 'annotated' } } | ConvertTo-Json -Depth 10 }
        if ($path -like '*/git/tags/*') { return @{ object = @{ type = 'commit'; sha = $state.sha } } | ConvertTo-Json -Depth 10 }
        if ($method -eq 'GET' -and $path -like '*/releases?per_page=*') {
            $items = if ($state.exists) { @(@{ tag_name = $state.tagName }) } else { @() }
            return ConvertTo-Json -InputObject $items -Depth 10
        }
        if ($method -eq 'POST' -and $path -like '*/git/refs') {
            if ($state.claimed) { $global:LASTEXITCODE = 1; return }
            $state.claimed = $true
            return '{"ref":"publication-claim"}'
        }
        if ($method -eq 'POST' -and $path -like '*/releases') {
            if ($state.exists) { $global:LASTEXITCODE = 1; return }
            $body = $bodyLines -join "`n" | ConvertFrom-Json
            if (-not $body.draft) { throw 'Release was not prepared as a draft.' }
            $state.exists = $true
            $state.tagName = $body.tag_name
            $state.creates++
            return '{"id":1}'
        }
        if ($method -eq 'PATCH' -and $path -like '*/releases/1') {
            if ($state.uploads -ne 1) { throw 'Publication preceded upload.' }
            return '{"immutable":true,"html_url":"https://example.invalid/test-release"}'
        }
        throw "Unexpected mocked request $method $path"
    }
}
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe publication was accepted.' }
}
try {
    $env:GH_TOKEN = 'mock-write'
    $env:RELEASE_ADMIN_READ_TOKEN = 'mock-read'
    $env:GITHUB_REPOSITORY = 'DaveBeusing/Forgeline'
    $env:GITHUB_RUN_ID = '100'
    $env:GITHUB_RUN_ATTEMPT = '1'
    $version = "$(Get-ProductVersion)-preview.1"
    $sha = $global:releasePublicationTestState.sha
    $archive = Join-Path $temporary "FORGELINE-$version-win-x64.zip"
    [IO.File]::WriteAllBytes($archive, [byte[]](1, 2, 3, 4))
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content "$archive.sha256"
    @{
        version = $version; sourceRevision = $sha; sourceTreeClean = $true
        runId = '100'; runAttempt = '1'; runUrl = 'https://example.invalid/test-run'
    } | ConvertTo-Json | Set-Content "$archive.provenance.json"
    $notes = Join-Path $temporary 'notes.md'
    'Reviewed test release notes and compatibility.' | Set-Content $notes
    $parameters = @{ Version = $version; SourceRevision = $sha; ArtifactRoot = $temporary; NotesPath = $notes }
    Assert-Rejected { & "$PSScriptRoot/Publish-Release.ps1" -Version "$(Get-ProductVersion)-pr.1.100.1" -SourceRevision $sha }
    $global:releasePublicationTestState.enabled = $false
    Assert-Rejected { & "$PSScriptRoot/Publish-Release.ps1" @parameters }
    $global:releasePublicationTestState.enabled = $true
    'corrupt' | Set-Content "$archive.sha256"
    Assert-Rejected { & "$PSScriptRoot/Publish-Release.ps1" @parameters }
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content "$archive.sha256"
    & "$PSScriptRoot/Publish-Release.ps1" @parameters
    # A crash after claim but before draft creation must still prevent reuse.
    $global:releasePublicationTestState.exists = $false
    Assert-Rejected { & "$PSScriptRoot/Publish-Release.ps1" @parameters }
    # An existing draft/release also blocks a version without relying on claims.
    $global:releasePublicationTestState.exists = $true
    $global:releasePublicationTestState.claimed = $false
    Assert-Rejected { & "$PSScriptRoot/Publish-Release.ps1" @parameters }
    if ($global:releasePublicationTestState.uploads -ne 1 -or $global:releasePublicationTestState.creates -ne 1) {
        throw 'Release collision attempted a second upload/create.'
    }
    if ($env:GH_TOKEN -ne 'mock-write') { throw 'Administration-read token was not restored.' }
    Write-Host 'PR exclusion, immutability gate, checksum validation, draft publication and collision refusal passed.'
    $global:LASTEXITCODE = 0
} finally {
    Remove-Item Function:gh
    Remove-Variable releasePublicationTestState -Scope Global
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temp cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
