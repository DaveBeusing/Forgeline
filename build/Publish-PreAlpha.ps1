param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/prealpha",
    [switch]$VerifyReproducible
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. "$PSScriptRoot/Versioning.ps1"
$buildVersion = if ($env:FORGELINE_BUILD_VERSION) { $env:FORGELINE_BUILD_VERSION } else { "$(Get-ProductVersion)-dev" }
Assert-BuildVersion -Version $buildVersion
$sourceRevision = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') { throw "Full Git SHA required." }
$dirty = @(& git status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw "Cannot inspect source cleanliness." }
if ($buildVersion -notmatch '-(dev|pr\.)' -and $dirty.Count) { throw "Published builds require a clean source tree." }
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw "Cannot identify SDK." }
$buildTime = [DateTimeOffset]::UtcNow.ToString("O")

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/ForgeLine.Client/ForgeLine.Client.csproj"
$runtimeAssets = Join-Path $repoRoot "assets/runtime"
$checklist = Join-Path $repoRoot "docs/PreAlphaVerificationChecklist.md"
$readme = Join-Path $repoRoot "README.md"

function Publish-Client {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }

    New-Item $Destination -ItemType Directory -Force | Out-Null

    $publishArguments = @(
        "publish",
        $project,
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--self-contained", "false",
        "--no-restore",
        "-p:ContinuousIntegrationBuild=true",
        "-p:Deterministic=true",
        "--output", $Destination
    )

    & dotnet @publishArguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $runtimeAssets)) {
        throw "Compiled runtime assets are missing: $runtimeAssets"
    }

    $assetDestination = Join-Path $Destination "assets/runtime"
    New-Item $assetDestination -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $runtimeAssets "*") $assetDestination -Recurse -Force

    Copy-Item $readme (Join-Path $Destination "README.md") -Force
    Copy-Item $checklist (Join-Path $Destination "PreAlphaVerificationChecklist.md") -Force

    # The same approved inputs and timestamp are reused for reproducibility.
    @{
        product = "FORGELINE"; productVersion = Get-ProductVersion
        version = $buildVersion; informationalVersion = "$buildVersion+$sourceRevision"
        sourceRevision = $sourceRevision; repository = "https://github.com/DaveBeusing/Forgeline"
        channel = if ($buildVersion.Contains('-')) { $buildVersion.Split('-')[1].Split('.')[0] } else { "stable" }
        buildTimeUtc = $buildTime; runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT
        runUrl = if ($env:GITHUB_RUN_ID) { "https://github.com/DaveBeusing/Forgeline/actions/runs/$env:GITHUB_RUN_ID" } else { $null }
        targetFramework = "net10.0-windows"; runtimeIdentifier = "win-x64"
        configuration = $Configuration; sdk = $sdk; sourceTreeClean = ($dirty.Count -eq 0)
    } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $Destination "build-provenance.json") -Encoding utf8NoBOM

    $manifestPath = Join-Path $Destination "manifest.sha256"
    Get-ChildItem $Destination -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($Destination, $_.FullName).Replace("\", "/")
            $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        } |
        Set-Content $manifestPath -Encoding utf8NoBOM

    return $manifestPath
}

function Compare-PublishTrees {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,
        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    $leftManifest = Get-Content (Join-Path $Left "manifest.sha256")
    $rightManifest = Get-Content (Join-Path $Right "manifest.sha256")
    $difference = Compare-Object $leftManifest $rightManifest

    if ($difference) {
        $difference | Format-Table | Out-String | Write-Error
        throw "Pre-alpha publish output is not reproducible."
    }
}

$outputRootAbsolute = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts")) + [System.IO.Path]::DirectorySeparatorChar
if (-not ($outputRootAbsolute + [System.IO.Path]::DirectorySeparatorChar).StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must remain inside repository artifacts."
}
$archivePath = Join-Path $outputRootAbsolute "FORGELINE-$buildVersion-win-x64.zip"
foreach ($path in @($archivePath, "$archivePath.sha256", "$archivePath.provenance.json")) {
    if (Test-Path -LiteralPath $path) { throw "Artifact already exists: $path. Refusing replacement." }
}
$publishDirectory = Join-Path $outputRootAbsolute "win-x64/publish"

if ($VerifyReproducible) {
    $verificationRoot = Join-Path $outputRootAbsolute "reproducibility"
    $first = Join-Path $verificationRoot "first"
    $second = Join-Path $verificationRoot "second"

    Publish-Client -Destination $first | Out-Null
    Publish-Client -Destination $second | Out-Null
    Compare-PublishTrees -Left $first -Right $second

    if (Test-Path $publishDirectory) {
        Remove-Item $publishDirectory -Recurse -Force
    }

    New-Item (Split-Path -Parent $publishDirectory) -ItemType Directory -Force | Out-Null
    Copy-Item $first $publishDirectory -Recurse -Force
    Remove-Item $verificationRoot -Recurse -Force
}
else {
    Publish-Client -Destination $publishDirectory | Out-Null
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([System.IO.Path]::GetFileName($archivePath))" | Set-Content "$archivePath.sha256" -Encoding utf8NoBOM
Copy-Item (Join-Path $publishDirectory "build-provenance.json") "$archivePath.provenance.json"

Write-Output "Pre-alpha publish: $publishDirectory"
Write-Output "Pre-alpha archive: $archivePath"
