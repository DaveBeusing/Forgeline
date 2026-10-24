[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PublishRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PublishRoot)
$mapRoot = Join-Path $root 'maps'
$expected = @('central-divide.flmap.json', 'central-divide.flmap.json.sha256')
$actual = @(Get-ChildItem -LiteralPath $mapRoot -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($mapRoot, $_.FullName) } | Sort-Object)
if (@(Compare-Object ($expected | Sort-Object) $actual).Count -ne 0) { throw 'The package must contain exactly the registered scenario files.' }
# Execute in the exported folder: startup and restoration must use only deployed data.
Push-Location $root
try {
    & dotnet (Join-Path $root 'ForgeLine.Client.dll') --validate-scenarios
    if ($LASTEXITCODE -ne 0) { throw "Exported scenario validation failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
Write-Output 'Compiled scenario package and deterministic recovery qualified.'
