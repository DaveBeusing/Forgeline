[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$VersionFile = "Directory.Build.props"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedPath = (Resolve-Path -LiteralPath $VersionFile).Path
$content = [System.IO.File]::ReadAllText($resolvedPath)

$majorMatch = [regex]::Match(
    $content,
    "<ForgeLineVersionMajor>\s*(\d+)\s*</ForgeLineVersionMajor>")
$minorMatch = [regex]::Match(
    $content,
    "<ForgeLineVersionMinor>\s*(\d+)\s*</ForgeLineVersionMinor>")
$patchMatch = [regex]::Match(
    $content,
    "<ForgeLineVersionPatch>\s*(\d+)\s*</ForgeLineVersionPatch>")

if (-not $majorMatch.Success -or
    -not $minorMatch.Success -or
    -not $patchMatch.Success)
{
    throw "Unable to locate ForgeLineVersionMajor/Minor/Patch in $VersionFile."
}

$major = [int]$majorMatch.Groups[1].Value
$minor = [int]$minorMatch.Groups[1].Value
$patch = [int]$patchMatch.Groups[1].Value
$nextPatch = $patch + 1

$updated = $content.Remove(
    $patchMatch.Groups[1].Index,
    $patchMatch.Groups[1].Length).Insert(
        $patchMatch.Groups[1].Index,
        $nextPatch.ToString([System.Globalization.CultureInfo]::InvariantCulture))

[System.IO.File]::WriteAllText(
    $resolvedPath,
    $updated,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "FORGELINE version: $major.$minor.$patch -> $major.$minor.$nextPatch"
