Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
function Get-ProductVersion {
    param([string]$Content = (Get-Content "$PSScriptRoot/../Directory.Build.props" -Raw))
    [xml]$xml = $Content
    $values = foreach ($name in 'ForgeLineVersionMajor', 'ForgeLineVersionMinor', 'ForgeLineVersionPatch') {
        $nodes = @($xml.SelectNodes("/Project/PropertyGroup/$name"))
        if ($nodes.Count -ne 1 -or $nodes[0].InnerText -notmatch '^(0|[1-9][0-9]*)$') { throw "Expected one canonical numeric $name." }
        if ([long]$nodes[0].InnerText -gt 65534) { throw "$name exceeds .NET metadata limits." }
        $nodes[0].InnerText
    }
    return $values -join '.'
}
function Assert-BuildVersion {
    param([string]$Version, [string]$ProductVersion = (Get-ProductVersion))
    $prefix = [regex]::Escape($ProductVersion)
    if ($Version -notmatch "^$prefix(?:-(?:dev|preview\.[1-9][0-9]*|rc\.[1-9][0-9]*|pr\.[1-9][0-9]*\.[1-9][0-9]*\.[1-9][0-9]*))?$") { throw "Invalid build version '$Version' for $ProductVersion." }
}
function Get-NextPrVersion {
    param([string]$BaseVersion)
    $base = [version]$BaseVersion
    if ($base.Build -ge 65534) { throw 'PATCH exhausted .NET metadata range; select a new MAJOR/MINOR boundary explicitly.' }
    return "$($base.Major).$($base.Minor).$($base.Build + 1)"
}
function Assert-PrVersion {
    param([string]$BaseVersion, [string]$HeadVersion)
    $expected = Get-NextPrVersion -BaseVersion $BaseVersion
    if ($HeadVersion -ne $expected) {
        throw "Each PR requires exactly one PATCH increment: expected $expected, received $HeadVersion. Run Increment-PrVersion.ps1 against the current base."
    }
}