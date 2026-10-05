[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseRef,

    [Parameter(Mandatory = $false)]
    [string]$HeadRef = "HEAD",

    [Parameter(Mandatory = $false)]
    [string]$VersionFile = "Directory.Build.props"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-Commit {
    param([Parameter(Mandatory = $true)][string]$Ref)

    $resolved = @(& git rev-parse --verify "$Ref^{commit}" 2>$null)
    if ($LASTEXITCODE -ne 0 -or $resolved.Count -eq 0)
    {
        throw "Unable to resolve Git commit '$Ref'. Ensure the repository was checked out with full history."
    }

    return $resolved[0].Trim()
}

function Get-VersionAtRef {
    param([Parameter(Mandatory = $true)][string]$Ref)

    $contentLines = @(& git show "${Ref}:$VersionFile" 2>$null)
    if ($LASTEXITCODE -ne 0)
    {
        return $null
    }

    $content = $contentLines -join [Environment]::NewLine
    $majorMatch = [regex]::Match($content, "<ForgeLineVersionMajor>\s*(\d+)\s*</ForgeLineVersionMajor>")
    $minorMatch = [regex]::Match($content, "<ForgeLineVersionMinor>\s*(\d+)\s*</ForgeLineVersionMinor>")
    $patchMatch = [regex]::Match($content, "<ForgeLineVersionPatch>\s*(\d+)\s*</ForgeLineVersionPatch>")

    if (-not $majorMatch.Success -or
        -not $minorMatch.Success -or
        -not $patchMatch.Success)
    {
        return $null
    }

    $major = [int]$majorMatch.Groups[1].Value
    $minor = [int]$minorMatch.Groups[1].Value
    $patch = [int]$patchMatch.Groups[1].Value

    return [pscustomobject]@{
        Major = $major
        Minor = $minor
        Patch = $patch
        Text = "$major.$minor.$patch"
    }
}

$base = Resolve-Commit -Ref $BaseRef
$head = Resolve-Commit -Ref $HeadRef

& git merge-base --is-ancestor $base $head
if ($LASTEXITCODE -ne 0)
{
    throw "Version validation requires the head commit to descend from base $base."
}

$commits = @(& git rev-list --reverse --topo-order "$base..$head")
if ($LASTEXITCODE -ne 0)
{
    throw "Unable to enumerate commits from $base to $head."
}

if ($commits.Count -eq 0)
{
    Write-Host "No commits to validate between $base and $head."
    exit 0
}

$baseVersion = Get-VersionAtRef -Ref $base
$seenDevelopmentVersions =
    [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)

if ($null -ne $baseVersion)
{
    [void]$seenDevelopmentVersions.Add($baseVersion.Text)
}

$bootstrapSeen = $false

foreach ($commit in $commits)
{
    $parentLine = (& git show -s --format=%P $commit).Trim()
    if ($LASTEXITCODE -ne 0)
    {
        throw "Unable to inspect parents for commit $commit."
    }

    $parents = @(
        $parentLine -split " " |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    $current = Get-VersionAtRef -Ref $commit
    if ($null -eq $current)
    {
        throw "Commit $commit does not define ForgeLineVersionMajor/Minor/Patch in $VersionFile."
    }

    if ($parents.Count -gt 1)
    {
        $parentVersions = @(
            foreach ($parent in $parents)
            {
                $parentVersion = Get-VersionAtRef -Ref $parent
                if ($null -ne $parentVersion)
                {
                    $parentVersion
                }
            }
        )

        if ($parentVersions.Count -eq 0)
        {
            throw "Merge commit $commit has no governed versioned parent."
        }

        if ($null -ne $baseVersion -and
            $parentVersions.Count -ne $parents.Count)
        {
            throw "Merge commit $commit integrates an unversioned history after semantic versioning was already established."
        }

        $expected =
            $parentVersions |
                Sort-Object { [version]$_.Text } -Descending |
                Select-Object -First 1

        if ($current.Text -ne $expected.Text)
        {
            throw "Merge commit $commit defines $($current.Text); expected $($expected.Text), the highest already-integrated parent version. Merge commits do not consume an additional PATCH value."
        }

        Write-Host "[semver] merge $commit preserves $($current.Text) across $($parents.Count) parents"
        continue
    }

    if ($parents.Count -eq 0)
    {
        if ($bootstrapSeen -or $current.Text -ne "0.1.0")
        {
            throw "Only the semantic-versioning bootstrap may introduce version 0.1.0 without a governed parent."
        }

        if (-not $seenDevelopmentVersions.Add($current.Text))
        {
            throw "Development version $($current.Text) is already used by another commit."
        }

        $bootstrapSeen = $true
        Write-Host "[semver] $commit establishes baseline $($current.Text)"
        continue
    }

    $previous = Get-VersionAtRef -Ref $parents[0]

    if ($null -eq $previous)
    {
        if ($bootstrapSeen -or $current.Text -ne "0.1.0")
        {
            throw "Only the semantic-versioning bootstrap may introduce a version without a governed parent, and it must establish 0.1.0. Commit $commit defines $($current.Text)."
        }

        if (-not $seenDevelopmentVersions.Add($current.Text))
        {
            throw "Development version $($current.Text) is already used by another commit."
        }

        $bootstrapSeen = $true
        Write-Host "[semver] $commit establishes baseline $($current.Text)"
        continue
    }

    $expectedPatch = $previous.Patch + 1
    if ($current.Patch -ne $expectedPatch)
    {
        throw "Commit $commit defines $($current.Text); expected patch $expectedPatch after $($previous.Text). Every non-merge development commit must increment PATCH by exactly one."
    }

    if ($current.Major -lt $previous.Major -or
        ($current.Major -eq $previous.Major -and
         $current.Minor -lt $previous.Minor))
    {
        throw "Commit $commit decreases MAJOR/MINOR from $($previous.Text) to $($current.Text)."
    }

    if (-not $seenDevelopmentVersions.Add($current.Text))
    {
        throw "Development version $($current.Text) is already used by another non-merge commit in the validated history."
    }

    Write-Host "[semver] $commit $($previous.Text) -> $($current.Text)"
}

Write-Host "Semantic version progression validated for $($commits.Count) commit(s)."
