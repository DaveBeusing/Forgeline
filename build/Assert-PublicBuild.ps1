[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PublishRoot)
foreach ($required in @('ForgeLine.Client.dll', 'ForgeLine.Game.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw "Public package assembly missing: $required" }
}
# Inspect metadata without invoking any product entry point or static capability factory.
$context = [System.Runtime.Loader.AssemblyLoadContext]::new("PublicBuild-$([Guid]::NewGuid())", $true)
try {
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Filter 'ForgeLine.*.dll' -File)) {
        $assembly = $context.LoadFromAssemblyPath($file.FullName)
        $metadata = @($assembly.GetCustomAttributes([System.Reflection.AssemblyMetadataAttribute], $false))
        $variant = @($metadata | Where-Object Key -eq 'BuildVariant')
        $capability = @($metadata | Where-Object Key -eq 'DeveloperToolsEnabled')
        if ($variant.Count -ne 1 -or $variant[0].Value -cne 'Public' -or
            $capability.Count -ne 1 -or $capability[0].Value -ine 'false') {
            throw "Refusing non-public or contradictory assembly: $($file.Name)"
        }
    }
}
finally { $context.Unload() }
Write-Host 'Public build metadata verified.'
