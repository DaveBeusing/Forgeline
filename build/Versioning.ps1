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
function Get-NextChannelNumber {
    param([string[]]$Tags, [string]$ProductVersion, [ValidateSet('preview', 'rc')][string]$Channel)
    $pattern = '^v' + [regex]::Escape($ProductVersion) + '-' + $Channel + '\.([1-9][0-9]*)$'
    [long]$maximum = 0
    foreach ($tag in $Tags) {
        if ($tag -match $pattern) { $maximum = [Math]::Max($maximum, [long]$Matches[1]) }
    }
    if ($maximum -eq [long]::MaxValue) { throw "Channel sequence exhausted." }
    return $maximum + 1
}
function Invoke-GitHub {
    param([string]$Path, [ValidateSet('GET', 'POST', 'PATCH')][string]$Method = 'GET', $Body)
    $arguments = @('api', '--method', $Method, $Path)
    if ($null -ne $Body) { $result = $Body | ConvertTo-Json -Depth 20 -Compress | & gh @arguments --input - }
    else { $result = & gh @arguments }
    if ($LASTEXITCODE -ne 0) { throw "GitHub $Method $Path failed; publication is prohibited." }
    $parsed = $result -join "`n" | ConvertFrom-Json -NoEnumerate
    # Empty registry/release lists must produce zero objects, not a null entry
    # that strict-mode property filters would treat as a malformed response.
    foreach ($item in @($parsed)) {
        if ($null -ne $item) { Write-Output $item }
    }
}
