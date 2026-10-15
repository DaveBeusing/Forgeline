param([Parameter(Mandatory)][string]$PublishRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $repository 'assets/source/ui/directorate/skin.catalog.json') -Raw | ConvertFrom-Json
$references = Get-Content -LiteralPath (Join-Path $repository 'assets/reference/ui/directorate/catalog.json') -Raw | ConvertFrom-Json
$runtime = [IO.Path]::GetFullPath((Join-Path $PublishRoot 'assets/runtime'))
$manifest = Get-Content -LiteralPath (Join-Path $runtime 'manifest.json') -Raw | ConvertFrom-Json
$skin = @($manifest.assets | Where-Object { $_.id.StartsWith('ui.skin.directorate.', [StringComparison]::Ordinal) })
if ($skin.Count -ne $source.sprites.Count) { throw 'Packaged skin source/runtime count differs.' }
$ids = @($skin.id | Sort-Object -Unique)
if ($ids.Count -ne $skin.Count) { throw 'Duplicate packaged skin IDs.' }
foreach ($sprite in $source.sprites) {
    $record = @($skin | Where-Object id -CEQ $sprite.id)
    if ($record.Count -ne 1) { throw "Missing packaged skin: $($sprite.id)" }
    $record = $record[0]
    if ($record.type -cne 'texture' -or $record.sourcePath -cne "ui/directorate/$($sprite.file)") {
        throw "Incorrect packaged skin mapping: $($sprite.id)"
    }
    $payload = [IO.Path]::GetFullPath((Join-Path $runtime $record.runtimePath))
    if (-not $payload.StartsWith($runtime + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Packaged skin escapes runtime root: $($sprite.id)"
    }
    if (-not (Test-Path -LiteralPath $payload -PathType Leaf) -or
        (Get-Item -LiteralPath $payload).Length -ne $record.runtimeBytes -or
        (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.runtimeHash) {
        throw "Missing or corrupt packaged skin: $($sprite.id)"
    }
}
foreach ($record in $manifest.assets) {
    if ($record.id.StartsWith('reference.', [StringComparison]::Ordinal) -or
        $record.sourcePath -match '(^|[/\\])reference([/\\]|$)' -or
        [IO.Path]::GetFileName($record.sourcePath) -in $references.references.file) {
        throw "Reference board entered runtime manifest: $($record.id)"
    }
}
foreach ($file in Get-ChildItem -LiteralPath $PublishRoot -File -Recurse) {
    if ($file.Extension -in '.png', '.svg', '.ttf' -or $file.Name -like '*.asset.json' -or
        $file.Name -eq 'skin.catalog.json' -or $file.Name -like 'ForgeLine.AssetCompiler*') {
        throw "Authoring-only content entered player package: $($file.FullName)"
    }
}
Write-Output "$($skin.Count) packaged skin payloads verified; reference boards and authoring files excluded."
