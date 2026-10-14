param([string]$PublishRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$audioRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/source/audio/ui'
$expected = @{ navigation = 6720; intent = 10560; accepted = 18240; rejected = 21120 }
foreach ($name in $expected.Keys) {
    $source = Join-Path $audioRoot "$name.wav"
    $bytes = [IO.File]::ReadAllBytes($source)
    if ($bytes.Length -ne 44 + $expected[$name] -or
        [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF' -or
        [BitConverter]::ToUInt32($bytes, 4) -ne $bytes.Length - 8 -or
        [Text.Encoding]::ASCII.GetString($bytes, 8, 8) -ne 'WAVEfmt ' -or
        [BitConverter]::ToUInt32($bytes, 16) -ne 16 -or
        [BitConverter]::ToUInt16($bytes, 20) -ne 1 -or
        [BitConverter]::ToUInt16($bytes, 22) -ne 2 -or
        [BitConverter]::ToUInt32($bytes, 24) -ne 48000 -or
        [BitConverter]::ToUInt32($bytes, 28) -ne 192000 -or
        [BitConverter]::ToUInt16($bytes, 32) -ne 4 -or
        [BitConverter]::ToUInt16($bytes, 34) -ne 16 -or
        [Text.Encoding]::ASCII.GetString($bytes, 36, 4) -ne 'data' -or
        [BitConverter]::ToUInt32($bytes, 40) -ne $expected[$name]) { throw "Invalid UI PCM cue: $name" }
    if ($PublishRoot) {
        $published = Join-Path $PublishRoot "assets/runtime/audio/ui/$name.wav"
        if (-not (Test-Path $published) -or (Get-FileHash $source).Hash -ne (Get-FileHash $published).Hash) { throw "Packaged UI cue differs: $name" }
    }
}
$notice = Join-Path $audioRoot 'NOTICE.txt'
if ((Get-Content $notice -Raw) -notmatch 'CC0 1.0') { throw 'UI sound provenance notice is missing.' }
if ($PublishRoot) {
    $publishedNotice = Join-Path $PublishRoot 'ThirdPartyNotices/UiAudio.txt'
    if (-not (Test-Path $publishedNotice) -or (Get-FileHash $notice).Hash -ne (Get-FileHash $publishedNotice).Hash) { throw 'Packaged UI sound notice differs.' }
}
Write-Output 'Four bounded original PCM cues, provenance and package identity passed.'
