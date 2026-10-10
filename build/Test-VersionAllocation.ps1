# Exercise the actual allocator against an in-memory GitHub transport. No
# network, repository writes, tokens or remote tags are used by this test.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
$names = @('GITHUB_REF', 'GITHUB_REPOSITORY', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_OUTPUT', 'GITHUB_EVENT_NAME', 'GITHUB_EVENT_PATH')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("version-allocation-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$global:versionAllocationTestState = @{
    refs = @{}; sha = (& git rev-parse HEAD).Trim()
    tagIndex = 0; collideOnce = $true; remoteCalls = 0
}
function gh {
    [CmdletBinding(PositionalBinding = $false)]
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments,
          [Parameter(ValueFromPipeline)][string]$RequestBody)
    begin { $bodyLines = [Collections.Generic.List[string]]::new() }
    process { if ($RequestBody) { $bodyLines.Add($RequestBody) } }
    end {
    $state = $global:versionAllocationTestState
    $state.remoteCalls++
    $bodyText = $bodyLines -join "`n"
    $methodIndex = [Array]::IndexOf($Arguments, '--method')
    $method = if ($methodIndex -ge 0) { $Arguments[$methodIndex + 1] } else { 'GET' }
    $path = @($Arguments | Where-Object { $_ -like 'repos/*' })[0]
    $global:LASTEXITCODE = 0
    if ($path -like '*/git/ref/heads/master') {
        return @{ object = @{ sha = $state.sha } } | ConvertTo-Json -Depth 10
    }
    if ($path -like '*/git/matching-refs/tags/*') {
        $prefix = 'refs/tags/' + ($path -split '/tags/', 2)[1]
        $items = @($state.refs.Values | Where-Object { $_.ref.StartsWith($prefix, [StringComparison]::Ordinal) })
        return ConvertTo-Json -InputObject $items -Depth 10
    }
    if ($method -eq 'POST' -and $path -like '*/git/tags') {
        $state.tagIndex++
        return @{ sha = "tag-object-$($state.tagIndex)" } | ConvertTo-Json
    }
    if ($method -eq 'POST' -and $path -like '*/git/refs') {
        $body = $bodyText | ConvertFrom-Json
        if ($state.collideOnce) {
            $state.collideOnce = $false
            # Simulate another writer winning creation after the registry read.
            $state.refs[$body.ref] = @{ ref = $body.ref; object = @{ type = 'tag'; sha = 'competitor' } }
            $global:LASTEXITCODE = 1
            return
        }
        if ($state.refs.ContainsKey($body.ref)) { $global:LASTEXITCODE = 1; return }
        $state.refs[$body.ref] = @{ ref = $body.ref; object = @{ type = 'tag'; sha = $body.sha } }
        return $bodyText
    }
    if ($path -like '*/git/ref/tags/*') {
        $ref = 'refs/tags/' + ($path -split '/tags/', 2)[1]
        if ($state.refs.ContainsKey($ref)) { return $state.refs[$ref] | ConvertTo-Json -Depth 10 }
    }
    throw "Unexpected mock request: $method $path"
    }
}
try {
    $env:GITHUB_REF = 'refs/heads/master'
    $env:GITHUB_REPOSITORY = 'DaveBeusing/Forgeline'
    $env:GITHUB_RUN_ID = '100'
    $env:GITHUB_RUN_ATTEMPT = '1'
    $env:GITHUB_OUTPUT = Join-Path $temporary 'output'
    $env:GITHUB_EVENT_NAME = 'push'
    $product = Get-ProductVersion
    & "$PSScriptRoot/Set-BuildVersion.ps1" -Channel preview
    if ((Get-Content $env:GITHUB_OUTPUT -Raw) -notmatch [regex]::Escape("version=$product-preview.2")) { throw 'Collision did not allocate the next version.' }
    $env:GITHUB_RUN_ATTEMPT = '2'
    & "$PSScriptRoot/Set-BuildVersion.ps1" -Channel preview
    if ((Get-Content $env:GITHUB_OUTPUT -Raw) -notmatch [regex]::Escape("version=$product-preview.3")) { throw 'Rerun reused a reserved number.' }
    & "$PSScriptRoot/Set-BuildVersion.ps1" -Channel rc
    if ((Get-Content $env:GITHUB_OUTPUT -Raw) -notmatch [regex]::Escape("version=$product-rc.1")) { throw 'RC sequence is not independent.' }
    $before = $global:versionAllocationTestState.remoteCalls
    $env:GITHUB_EVENT_NAME = 'pull_request'
    $env:GITHUB_REF = 'refs/pull/12/merge'
    $env:GITHUB_EVENT_PATH = Join-Path $temporary 'event.json'
    '{"number":12}' | Set-Content $env:GITHUB_EVENT_PATH
    & "$PSScriptRoot/Set-BuildVersion.ps1" -Channel pr
    if ($global:versionAllocationTestState.remoteCalls -ne $before) { throw 'PR attempted a remote allocation.' }
    if ((Get-Content $env:GITHUB_OUTPUT -Raw) -notmatch [regex]::Escape("version=$product-pr.12.100.2")) { throw 'PR identity mismatch.' }
    $rejected = $false
    try { & "$PSScriptRoot/Set-BuildVersion.ps1" -Channel preview } catch { $rejected = $true }
    if (-not $rejected) { throw 'PR context accepted release allocation.' }
    Write-Host 'Atomic collision recovery, durable rerun identity, channel reset and PR isolation passed.'
} finally {
    Remove-Item Function:gh
    Remove-Variable versionAllocationTestState -Scope Global
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    # Exact owned temp directory, checked before recursive removal.
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temp cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
