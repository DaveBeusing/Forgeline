param([string]$PublishRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repo 'assets/source/audio'
$library = Get-Content (Join-Path $sourceRoot 'library.json') -Raw | ConvertFrom-Json
$playback = Get-Content (Join-Path $sourceRoot 'playback.json') -Raw | ConvertFrom-Json
$world = @($library.assets | Where-Object bus -ne 'ui')
if ($playback.schema -ne 1 -or $playback.clips.Count -ne 56 -or $world.Count -ne 56) { throw 'World playback catalog count differs.' }
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($clip in $playback.clips) {
    if (-not $ids.Add($clip.id)) { throw 'Duplicate world playback clip.' }
    $entry = @($world | Where-Object id -eq $clip.id)
    if ($entry.Count -ne 1 -or $entry[0].sha256 -ne $clip.sha256 -or
        $entry[0].loop_start_frame -ne $clip.loop_start_frame -or
        $entry[0].loop_end_frame_exclusive -ne $clip.loop_end_frame_exclusive) { throw "World playback metadata mismatch: $($clip.id)" }
    if ($PublishRoot) {
        if ($clip.id -notmatch '^audio\.[a-z_]+\.[a-z0-9_]+$') { throw 'Unsafe world audio ID.' }
        $relative = $clip.id.Substring(6).Replace('.', '/') + '.wav'
        $file = Join-Path $PublishRoot "assets/runtime/audio/$relative"
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $clip.sha256) { throw "Packaged world audio differs: $($clip.id)" }
    }
}
if ($PublishRoot) {
    $manifest = Join-Path $PublishRoot 'assets/runtime/audio/playback.json'
    if ((Get-FileHash -LiteralPath $manifest).Hash -ne (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'playback.json')).Hash) { throw 'Packaged playback manifest differs.' }
    $count = @(Get-ChildItem -LiteralPath (Join-Path $PublishRoot 'assets/runtime/audio') -Filter '*.wav' -Recurse).Count
    if ($count -ne 60) { throw "Expected 60 packaged interface/world WAVs, observed $count." }
}
Write-Output 'World audio playback metadata: 56 clips; identity and frame-based loop endpoints passed.'
