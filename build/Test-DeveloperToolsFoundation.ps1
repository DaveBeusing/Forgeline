[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-Rejected {
    param([scriptblock]$Action, [string]$Expected)
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    if (-not $failure -or -not $failure.Contains($Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected rejection '$Expected'; observed '$failure'."
    }
}
foreach ($name in @('Publish-PreAlpha.ps1', 'Assert-PublicBuild.ps1')) {
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "PowerShell syntax failure in $name : $errors" }
}
$public = Join-Path $RepositoryRoot 'src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64'
$developer = Join-Path $RepositoryRoot 'src/ForgeLine.Client/bin/Developer/Release/net10.0-windows/win-x64'
& "$PSScriptRoot/Assert-PublicBuild.ps1" -PublishRoot $public
Assert-Rejected { & "$PSScriptRoot/Assert-PublicBuild.ps1" -PublishRoot $developer } 'Refusing non-public'
$previousDeveloperSetting = $env:ForgeLineEnableDeveloperTools
try {
    $env:ForgeLineEnableDeveloperTools = $null
    Assert-Rejected { & "$PSScriptRoot/Publish-PreAlpha.ps1" -ForgeLineEnableDeveloperTools:$true } 'public publisher refuses'
    $env:ForgeLineEnableDeveloperTools = 'true'
    Assert-Rejected { & "$PSScriptRoot/Publish-PreAlpha.ps1" -ForgeLineEnableDeveloperTools:$false } 'public publisher refuses'
}
finally { $env:ForgeLineEnableDeveloperTools = $previousDeveloperSetting }
Write-Host 'Developer foundation publisher contracts:4/4 passed. No package was generated.'
