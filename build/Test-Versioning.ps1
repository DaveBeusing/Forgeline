Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Versioning.ps1"
function Assert-Equal($Expected, $Actual) { if ($Expected -ne $Actual) { throw "Expected '$Expected', received '$Actual'." } }
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected validation to reject invalid input.' }
}
$product = Get-ProductVersion
foreach ($version in @($product, "$product-dev", "$product-preview.1", "$product-rc.42", "$product-pr.1.1234.2")) {
    Assert-BuildVersion -Version $version
}
foreach ($suffix in @('preview.0', 'preview.01', 'rc.-1', 'preview', 'pr.1.2', 'preview.1+spoof')) {
    Assert-Rejected { Assert-BuildVersion -Version "$product-$suffix" }
}
Assert-Rejected { Get-ProductVersion -Content '<Project><PropertyGroup><ForgeLineVersionMajor>0</ForgeLineVersionMajor><ForgeLineVersionMinor>1</ForgeLineVersionMinor><ForgeLineVersionPatch>65535</ForgeLineVersionPatch></PropertyGroup></Project>' }
Assert-Equal 1 (Get-NextChannelNumber -Tags @() -ProductVersion $product -Channel preview)
Assert-Equal 43 (Get-NextChannelNumber -Tags @("v$product-preview.2", "v$product-preview.42", "v$product-rc.99", 'v9.0.0-preview.200') -ProductVersion $product -Channel preview)
Assert-Equal 1 (Get-NextChannelNumber -Tags @("v$product-preview.42") -ProductVersion $product -Channel rc)
Assert-Equal 1 (Get-NextChannelNumber -Tags @("v$product-preview.42") -ProductVersion '0.2.0' -Channel preview)
Write-Host 'Version grammar, channel isolation, sequence gaps/reset and numeric metadata limits passed.'
