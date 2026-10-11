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
Assert-Equal '0.1.338' (Get-NextPrVersion '0.1.337')
Assert-PrVersion '0.1.337' '0.1.338'
Assert-Rejected { Assert-PrVersion '0.1.337' '0.1.337' }
Assert-Rejected { Assert-PrVersion '0.1.337' '0.1.339' }
Assert-Rejected { Assert-PrVersion '0.1.337' '0.2.0' }
Assert-Rejected { Get-NextPrVersion '0.1.65534' }
Write-Host 'Version grammar, one PATCH per PR and numeric metadata limits passed.'