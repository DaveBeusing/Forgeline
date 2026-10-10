[CmdletBinding()]
param([ValidateSet('pr', 'preview', 'rc', 'stable')][string]$Channel, [string]$ReleaseTag)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
$product = Get-ProductVersion
$sha = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Full source SHA required.' }
if ($Channel -eq 'pr') {
    if ($env:GITHUB_EVENT_NAME -ne 'pull_request') { throw 'PR identity requires a pull request event.' }
    $event = Get-Content $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
    # Identify the tested synthetic merge commit. Globally unique run/attempt.
    $version = "$product-pr.$($event.number).$env:GITHUB_RUN_ID.$env:GITHUB_RUN_ATTEMPT"
} else {
    if ($env:GITHUB_REF -ne 'refs/heads/master' -or $env:GITHUB_REPOSITORY -ne 'DaveBeusing/Forgeline') { throw 'Release allocation requires canonical master.' }
    $repository = $env:GITHUB_REPOSITORY
    $master = Invoke-GitHub -Path "repos/$repository/git/ref/heads/master"
    if ($master.object.sha -ne $sha) { throw 'Release allocation requires current master.' }
    $stableRefs = @(Invoke-GitHub -Path "repos/$repository/git/matching-refs/tags/v$product")
    $stable = @($stableRefs | Where-Object ref -eq "refs/tags/v$product")
    if ($Channel -eq 'stable') {
        if ($ReleaseTag -ne "v$product" -or $stable.Count -ne 1) { throw 'Stable publication requires an existing exact annotated tag.' }
        if ($stable[0].object.type -ne 'tag') { throw 'Lightweight release tags are prohibited.' }
        $tag = Invoke-GitHub -Path "repos/$repository/git/tags/$($stable[0].object.sha)"
        if ($tag.object.type -ne 'commit' -or $tag.object.sha -ne $sha) { throw 'Release tag must identify the tested master commit.' }
        $version = $product
    } else {
        if ($stable.Count) { throw "v$product exists; select the next release target." }
        $version = $null
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            # Matching refs is a complete Git refs response. Ref creation is atomic;
            # collisions retry from the durable registry, retaining failed-build gaps.
            $references = @(Invoke-GitHub -Path "repos/$repository/git/matching-refs/tags/v$product-$Channel.")
            $tags = @($references | ForEach-Object { $_.ref -replace '^refs/tags/', '' })
            $number = Get-NextChannelNumber -Tags $tags -ProductVersion $product -Channel $Channel
            $candidate = "$product-$Channel.$number"
            $tag = Invoke-GitHub -Method POST -Path "repos/$repository/git/tags" -Body @{
                tag = "v$candidate"; object = $sha; type = 'commit'
                message = "Reserved $candidate for run $env:GITHUB_RUN_ID attempt $env:GITHUB_RUN_ATTEMPT; source $sha. Never reuse or move."
            }
            $body = @{ ref = "refs/tags/v$candidate"; sha = $tag.sha } | ConvertTo-Json -Compress
            $result = $body | & gh api --method POST "repos/$repository/git/refs" --input - 2>$null
            if ($LASTEXITCODE -eq 0) { $version = $candidate; break }
            # Retry only confirmed collisions; authentication/service errors stop.
            $collision = Invoke-GitHub -Path "repos/$repository/git/ref/tags/v$candidate"
            if ($collision.ref -ne "refs/tags/v$candidate") { throw 'Unexpected allocation failure.' }
        }
        if (-not $version) { throw 'Allocation exhausted collision retries.' }
    }
}
Assert-BuildVersion -Version $version
if ($env:GITHUB_OUTPUT) { "version=$version" >> $env:GITHUB_OUTPUT; "sha=$sha" >> $env:GITHUB_OUTPUT }
Write-Host "Build identity: $version+$sha"
