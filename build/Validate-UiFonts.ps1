param([string]$PublishRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$fonts = Join-Path $root 'assets/source/ui/fonts'
$metadata = Get-Content (Join-Path $fonts 'metrics.json') -Raw | ConvertFrom-Json
if ($metadata.version -ne 1 -or $metadata.resourceId -ne 'font.ui.dejavu_sans' -or $metadata.width -ne 2048 -or $metadata.height -ne 4096) { throw 'Unsupported UI font atlas contract.' }
foreach ($font in $metadata.sources) {
    if ((Get-FileHash (Join-Path $fonts $font.file) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $font.sha256) { throw "Font source hash mismatch: $($font.file)" }
}
$stream = [IO.File]::OpenRead((Join-Path $fonts 'atlas.alpha.gz'))
try {
    $gzip = [IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Decompress)
    try {
        $alpha = [byte[]]::new($metadata.width * $metadata.height)
        $gzip.ReadExactly($alpha)
        if ($gzip.ReadByte() -ne -1) { throw 'Oversized UI atlas.' }
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($alpha)).ToLowerInvariant() -ne $metadata.alphaSha256) { throw 'UI atlas hash mismatch.' }
    } finally { $gzip.Dispose() }
} finally { $stream.Dispose() }
if ($metadata.glyphs.Count -ne 1448 -or $metadata.kerning.Count -ne 3524) { throw 'Unexpected bundled font coverage.' }
if ((Get-FileHash (Join-Path $fonts 'atlas.png') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.pngSha256) { throw 'UI atlas PNG hash mismatch.' }
if ($PublishRoot) {
    $notice = Join-Path $PublishRoot 'ThirdPartyNotices/DejaVuFonts.txt'
    if (-not (Test-Path $notice) -or (Get-FileHash $notice).Hash -ne (Get-FileHash (Join-Path $fonts 'LICENSE.txt')).Hash) { throw 'Published font license missing or changed.' }
    if (@(Get-ChildItem $PublishRoot -Recurse -Filter '*.ttf').Count -ne 0) { throw 'Authoring font files must not ship.' }
}
Write-Host 'UI atlas integrity, coverage, provenance and packaging contracts passed.'
