# Exercise actual scripts in an isolated repository; no remote/tag writes allowed.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("pr-version-" + [guid]::NewGuid().ToString('N'))
$names = @('GITHUB_EVENT_NAME','GITHUB_EVENT_PATH','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_ENV','FORGELINE_BUILD_VERSION')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
function gh { throw 'Version scripts must never call GitHub.' }
function Run-Git([string[]]$Arguments) {
    & git @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git $Arguments failed." }
}
New-Item -ItemType Directory -Path "$temporary/build" | Out-Null
foreach ($file in 'Versioning.ps1','Increment-PrVersion.ps1','Validate-SemVer.ps1','Set-BuildVersion.ps1') {
    Copy-Item "$PSScriptRoot/$file" "$temporary/build/$file"
}
Copy-Item "$PSScriptRoot/../Directory.Build.props" "$temporary/Directory.Build.props"
Push-Location $temporary
try {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null) }
    Run-Git @('init','--quiet')
    Run-Git @('config','core.autocrlf','false')
    Run-Git @('config','commit.gpgsign','false')
    Run-Git @('config','user.name','Version test')
    Run-Git @('config','user.email','version@example.invalid')
    Run-Git @('add','.')
    Run-Git @('commit','--quiet','-m','Base')
    $base = (& git rev-parse HEAD).Trim()
    . ./build/Versioning.ps1
    Run-Git @('checkout','--quiet','-b','version-first-pr',$base)
    $previous = Get-ProductVersion
    ./build/Increment-PrVersion.ps1 -BaseRef $base
    $first = Get-Content Directory.Build.props -Raw
    ./build/Increment-PrVersion.ps1 -BaseRef $base
    if ($first -cne (Get-Content Directory.Build.props -Raw)) { throw 'Repeated preparation consumed PATCH.' }
    $next = Get-NextPrVersion $previous
    if ((Get-ProductVersion) -ne $next) { throw 'Incorrect PATCH increment.' }
    Run-Git @('add','Directory.Build.props')
    Run-Git @('commit','--quiet','-m','PR')
    ./build/Validate-SemVer.ps1 -BaseRef $base
    $rejected = $false
    try { ./build/Validate-SemVer.ps1 -BaseRef HEAD } catch { $rejected = $true }
    if (-not $rejected) { throw 'Missing increment was accepted.' }
    # HeadRef is authoritative, not the working tree.
    $rejected = $false
    try { ./build/Validate-SemVer.ps1 -BaseRef $base -HeadRef $base } catch { $rejected = $true }
    if (-not $rejected) { throw 'HeadRef was ignored.' }
    # Two PRs may prepare the same PATCH; only the first integration can use it.
    $firstPr = (& git rev-parse HEAD).Trim()
    Run-Git @('checkout','--quiet','-b','version-second-pr',$base)
    ./build/Increment-PrVersion.ps1 -BaseRef $base
    if ((Get-ProductVersion) -ne $next) { throw 'Concurrent PR did not prepare the same PATCH.' }
    'Independent second PR change' | Set-Content second-pr.txt
    Run-Git @('add','Directory.Build.props','second-pr.txt')
    Run-Git @('commit','--quiet','-m','Concurrent PR')
    $secondPr = (& git rev-parse HEAD).Trim()
    ./build/Validate-SemVer.ps1 -BaseRef $base -HeadRef $secondPr

    Run-Git @('checkout','--quiet','-b','version-integration',$base)
    Run-Git @('merge','--quiet','--no-ff','--no-edit','version-first-pr')
    $firstIntegration = (& git rev-parse HEAD).Trim()
    ./build/Validate-SemVer.ps1 -BaseRef $base -HeadRef $firstIntegration
    $rejected = $false
    try { ./build/Validate-SemVer.ps1 -BaseRef $firstIntegration -HeadRef $secondPr } catch { $rejected = $true }
    if (-not $rejected) { throw 'Concurrent PR reused the first integrated PATCH.' }

    Run-Git @('checkout','--quiet','version-second-pr')
    Run-Git @('merge','--quiet','--no-edit','version-integration')
    $rejected = $false
    try { ./build/Validate-SemVer.ps1 -BaseRef $firstIntegration } catch { $rejected = $true }
    if (-not $rejected) { throw 'Synchronizing Git history silently accepted a reused PATCH.' }
    ./build/Increment-PrVersion.ps1 -BaseRef version-integration
    $following = Get-NextPrVersion $next
    if ((Get-ProductVersion) -ne $following) { throw 'Synchronized PR did not prepare the next PATCH.' }
    $prepared = Get-Content Directory.Build.props -Raw
    ./build/Increment-PrVersion.ps1 -BaseRef version-integration
    if ($prepared -cne (Get-Content Directory.Build.props -Raw)) { throw 'Repeated concurrent PR preparation consumed PATCH.' }
    Run-Git @('add','Directory.Build.props')
    Run-Git @('commit','--quiet','-m','Reconcile concurrent PR version')
    ./build/Validate-SemVer.ps1 -BaseRef $firstIntegration
    Run-Git @('checkout','--quiet','version-integration')
    Run-Git @('merge','--quiet','--no-ff','--no-edit','version-second-pr')
    ./build/Validate-SemVer.ps1 -BaseRef $firstIntegration
    if ((Get-ProductVersion) -ne $following) { throw 'Sequential integrations lost the reconciled PATCH.' }
    Write-Host 'Concurrent PR preparation, stale-version rejection and sequential integration passed.'
    Run-Git @('checkout','--quiet','--detach',$firstPr)

    '{"number":42}' | Set-Content event.json
    $env:GITHUB_EVENT_NAME = 'pull_request'
    $env:GITHUB_EVENT_PATH = Join-Path $temporary 'event.json'
    $env:GITHUB_RUN_ID = '123'
    $env:GITHUB_RUN_ATTEMPT = '1'
    $env:GITHUB_ENV = Join-Path $temporary 'identity.txt'
    ./build/Set-BuildVersion.ps1
    if ((Get-Content $env:GITHUB_ENV -Raw).Trim() -ne "FORGELINE_BUILD_VERSION=$next-pr.42.123.1") { throw 'PR identity mismatch.' }
    $env:GITHUB_RUN_ATTEMPT = '2'
    ./build/Set-BuildVersion.ps1
    if ((Get-ProductVersion) -ne $next) { throw 'CI rerun changed product version.' }
    $env:GITHUB_EVENT_NAME = 'push'
    ./build/Set-BuildVersion.ps1
    if ((Get-Content $env:GITHUB_ENV | Select-Object -Last 1) -ne "FORGELINE_BUILD_VERSION=$next-dev") { throw 'Master identity mismatch.' }
    if (@(& git tag --list).Count) { throw 'Unexpected automatic tag.' }
    Write-Host 'Idempotent PR increment, explicit refs, PR/master identities and tag-free reruns passed.'
} finally {
    Pop-Location
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Item Function:gh
    # Only remove the unique test-owned temporary directory.
    if ([IO.Path]::GetFullPath($temporary).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}

