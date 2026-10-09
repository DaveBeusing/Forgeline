[CmdletBinding()]
param([string]$OutputPath = 'artifacts/visual-asset-inventory.json')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = Join-Path $repositoryRoot 'assets/source'
$manifestPath = Join-Path $repositoryRoot 'assets/runtime/manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Compile runtime assets before exporting the visual inventory.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$runtimeById = @{}
foreach ($record in $manifest.assets) { $runtimeById[$record.id] = $record }
$rows = foreach ($definitionFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.asset.json') {
    $definition = Get-Content -LiteralPath $definitionFile.FullName -Raw | ConvertFrom-Json -AsHashtable
    $sourcePath = Join-Path $definitionFile.DirectoryName $definition.source
    $record = $runtimeById[$definition.id]
    if ($null -eq $record) { throw "Missing runtime record for $($definition.id)." }
    $dimensions = $null
    if ($definition.type -eq 'texture') {
        $bytes = [IO.File]::ReadAllBytes($sourcePath)
        if ([IO.Path]::GetExtension($sourcePath) -eq '.tga') {
            $dimensions = @([BitConverter]::ToUInt16($bytes, 12), [BitConverter]::ToUInt16($bytes, 14))
        } elseif ([IO.Path]::GetExtension($sourcePath) -eq '.png') {
            $dimensions = @(
                ($bytes[16] * 16777216 + $bytes[17] * 65536 + $bytes[18] * 256 + $bytes[19]),
                ($bytes[20] * 16777216 + $bytes[21] * 65536 + $bytes[22] * 256 + $bytes[23]))
        }
    }
    @{
        id = $definition.id; type = $definition.type
        definition = [IO.Path]::GetRelativePath($repositoryRoot, $definitionFile.FullName).Replace('\', '/')
        source = [IO.Path]::GetRelativePath($repositoryRoot, $sourcePath).Replace('\', '/')
        runtime = "assets/runtime/$($record.runtimePath)"
        sourceDimensions = $dimensions
        runtimeTextureFormat = $(if ($definition.ContainsKey('textureFormat')) { $definition.textureFormat } else { 'rgba8Unorm' })
        runtimeTextureLimit = $definition['textureMaxDimension']
        materials = $record['materialReferences']; textures = $record['textureReferences']
        lods = $record['lods']; collision = $record['collisionReference']; sockets = $record['sockets']
        dependencies = $record['dependencies']
        sourceMasterStatus = $(if ($null -ne $dimensions -and [Math]::Max($dimensions[0], $dimensions[1]) -lt 2048) { 'below-production-master-baseline' } else { 'requires-camera-review' })
    }
}
$fullOutputPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($fullOutputPath)) -Force | Out-Null
@{ assets = @($rows | Sort-Object { $_.id }); count = @($rows).Count } |
    ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8
Write-Output "Exported $(@($rows).Count) visual assets to $fullOutputPath"
