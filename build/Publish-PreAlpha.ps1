param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/prealpha",
    [switch]$VerifyReproducible
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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

$archivePath = Join-Path $outputRootAbsolute "FORGELINE-prealpha-win-x64.zip"
if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

Write-Output "Pre-alpha publish: $publishDirectory"
Write-Output "Pre-alpha archive: $archivePath"
