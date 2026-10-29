[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts/procedural-map-qualification',
    [string]$NativeRequest,
    [string]$BaselineRoot,
    [switch]$GatePerformance,
    [switch]$NoBuild,
    [string]$SourceRevision,
    [string]$SourceManifest
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    if ($SourceRevision) {
        if (-not $SourceManifest -or $SourceRevision -cnotmatch '^[0-9a-f]{40}$') { throw 'An explicit revision requires a verified source manifest.' }
        $source = Get-Content -LiteralPath $SourceManifest -Raw | ConvertFrom-Json
        if ($source.revision -cne $SourceRevision -or @($source.files).Count -lt 100) { throw 'Incomplete source identity.' }
        foreach ($entry in $source.files) {
            if ($entry.path -match '(^/|\\|(^|/)\.\.(/|$)|:)' -or $entry.sha -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid source path or blob identity.' }
            $bytes = [IO.File]::ReadAllBytes((Join-Path $root $entry.path))
            $header = [Text.Encoding]::UTF8.GetBytes("blob $($bytes.Length)`0")
            $blob = [byte[]]::new($header.Length + $bytes.Length)
            $header.CopyTo($blob,0); $bytes.CopyTo($blob,$header.Length)
            if ([Convert]::ToHexStringLower([Security.Cryptography.SHA1]::HashData($blob)) -cne $entry.sha) { throw "Source differs from revision: $($entry.path)" }
        }
        $sourceMode = 'verified-blob-manifest'
    } else {
        $SourceRevision = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source revision.' }
        $dirty = & git status --porcelain
        if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Qualification requires a clean source checkout.' }
        $sourceMode = 'clean-git-checkout'
    }
    if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new evidence directory.' }
    if (-not $NoBuild) {
        & dotnet restore ForgeLine.sln
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
        & dotnet build ForgeLine.sln -c Release --no-restore "-p:SourceRevisionId=$SourceRevision"
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
    & dotnet tools/ForgeLine.MapCompiler/bin/Release/net10.0/ForgeLine.MapCompiler.dll --qualify-generated-maps docs/qualification/procedural-map-seeds.json --output $OutputRoot
    if ($LASTEXITCODE -ne 0) { throw 'Golden seed qualification failed.' }
    @{revision=$SourceRevision;mode=$sourceMode;sourceManifestSha256=$(if($SourceManifest){(Get-FileHash -LiteralPath $SourceManifest -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null})} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot 'source.json') -Encoding utf8
    if ($NativeRequest) {
        $request = Get-Content -LiteralPath $NativeRequest -Raw | ConvertFrom-Json
        if ($request.sourceRevision -cne $SourceRevision -or -not $request.requireSourceStamp) { throw 'Native capture must require the same binary source stamp.' }
        & dotnet benchmarks/ForgeLine.Rendering.Benchmarks/bin/Release/net10.0-windows/ForgeLine.Rendering.Benchmarks.dll --procedural-native $NativeRequest
        if ($LASTEXITCODE -ne 0) { throw 'Native qualification failed.' }
        if ($BaselineRoot) {
            & "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot $request.outputRoot -BaselineRoot $BaselineRoot -GatePerformance:$GatePerformance
        } elseif ($GatePerformance) { throw 'A performance gate requires independent baseline evidence.' }
    } elseif ($BaselineRoot -or $GatePerformance) { throw 'Performance comparison requires native evidence.' }
} finally { Pop-Location }
