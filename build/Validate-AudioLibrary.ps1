param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$audioRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'assets/source/audio'))
$library = Get-Content (Join-Path $audioRoot 'library.json') -Raw | ConvertFrom-Json
if ($library.schema_version -ne 1 -or $library.runtime_manifest_supported_by_master -ne $false -or $library.assets.Count -ne 60) {
    throw 'Unexpected industrial audio authoring manifest contract.'
}
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$hashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[long]$pcmTotal = 0
$loopCount = 0
foreach ($asset in $library.assets) {
    $path = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $asset.path))
    if (-not $path.StartsWith($audioRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not $ids.Add($asset.id) -or -not $hashes.Add($asset.sha256)) { throw 'Unsafe or duplicate audio entry.' }
    $bytes = [IO.File]::ReadAllBytes($path)
    $channels = [BitConverter]::ToUInt16($bytes, 22)
    $pcmBytes = [BitConverter]::ToUInt32($bytes, 40)
    if ($bytes.Length -ne $asset.file_bytes -or $bytes.Length -ne 44 + $pcmBytes -or
        [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF' -or
        [BitConverter]::ToUInt32($bytes, 4) -ne $bytes.Length - 8 -or
        [Text.Encoding]::ASCII.GetString($bytes, 8, 8) -ne 'WAVEfmt ' -or
        [BitConverter]::ToUInt32($bytes, 16) -ne 16 -or [BitConverter]::ToUInt16($bytes, 20) -ne 1 -or
        $channels -notin @(1, 2) -or $channels -ne $asset.channels -or
        [BitConverter]::ToUInt32($bytes, 24) -ne 48000 -or
        [BitConverter]::ToUInt32($bytes, 28) -ne 48000 * $channels * 2 -or
        [BitConverter]::ToUInt16($bytes, 32) -ne $channels * 2 -or [BitConverter]::ToUInt16($bytes, 34) -ne 16 -or
        [Text.Encoding]::ASCII.GetString($bytes, 36, 4) -ne 'data' -or
        $pcmBytes -ne $asset.pcm_bytes -or $pcmBytes -ne $asset.frames * $channels * 2 -or
        $asset.sample_rate -ne 48000 -or $asset.bits_per_sample -ne 16 -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.sha256) { throw "Invalid industrial audio asset: $($asset.id)" }
    if ($asset.loop) {
        $loopCount++
        if ($null -eq $asset.loop_start_frame -or $null -eq $asset.loop_end_frame_exclusive -or
            $asset.loop_start_frame -lt 0 -or $asset.loop_end_frame_exclusive -le $asset.loop_start_frame -or
            $asset.loop_end_frame_exclusive -gt $asset.frames) { throw "Invalid loop frames: $($asset.id)" }
    } elseif ($null -ne $asset.loop_start_frame -or $null -ne $asset.loop_end_frame_exclusive) { throw 'One-shot has loop metadata.' }
    $pcmTotal += $pcmBytes
}
if ($loopCount -ne 15 -or $pcmTotal -ne 20852160) { throw 'Industrial audio content totals changed without qualification.' }
Write-Output "Industrial audio source library: $($ids.Count) unique clips; $loopCount loops; $pcmTotal PCM bytes; hashes and format passed. Gameplay binding remains separate."
