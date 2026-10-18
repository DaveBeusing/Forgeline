#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$OutputRoot='artifacts/pbr-qualified',
    [string]$CompilerPath='tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll',
    [string]$BlenderPath,
    [switch]$RenderPreviews
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$root=[IO.Path]::GetFullPath((Join-Path $repo $OutputRoot))
$allowed=(Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar
if (-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) {throw 'Choose a fresh contained artifacts output root.'}
$compiler=[IO.Path]::GetFullPath((Join-Path $repo $CompilerPath))
if (-not (Test-Path -LiteralPath $compiler)) {throw 'Build the Release compiler first.'}
[void][IO.Directory]::CreateDirectory($root)
$generations=@()
foreach ($run in @('a','b')) {
    & "$PSScriptRoot/Invoke-ProceduralPbr.ps1" -OutputRoot (Join-Path $root $run) -BlenderPath $BlenderPath -RenderPreviews:$RenderPreviews
    $current=Get-Content (Join-Path $root "$run/current.json") -Raw | ConvertFrom-Json
    $generations+=,$current.generation
}
$a=Get-Content (Join-Path $generations[0] 'generation.json') -Raw | ConvertFrom-Json
$b=Get-Content (Join-Path $generations[1] 'generation.json') -Raw | ConvertFrom-Json
if ($a.key -ne $b.key -or ($a.sourceFiles | ConvertTo-Json -Compress) -ne ($b.sourceFiles | ConvertTo-Json -Compress)) {throw 'Independent CPU source generations differ.'}
& "$PSScriptRoot/Invoke-ProceduralPbr.ps1" -OutputRoot (Join-Path $root 'a') -BlenderPath $BlenderPath -RenderPreviews:$RenderPreviews
if (-not (Get-Content (Join-Path $root 'a/current.json') -Raw | ConvertFrom-Json).cached) {throw 'Verified generation cache was not used.'}
$runtime=Join-Path $root 'runtime'
$timer=[Diagnostics.Stopwatch]::StartNew()
& dotnet $compiler --mode build --source (Join-Path $generations[0] 'source') --runtime $runtime --qualification-output (Join-Path $root 'runtime-qualification.json')
if ($LASTEXITCODE -ne 0) {throw 'Pilot asset compilation failed.'}
$timer.Stop()
# Validate with an absent authoring root: this mode must never discover Blender or sources.
& dotnet $compiler --mode validate --source (Join-Path $root 'absent-source') --runtime $runtime
if ($LASTEXITCODE -ne 0) {throw 'Source-less runtime verification failed.'}
@{schemaVersion=1;sourceParity=$true;cacheVerified=$true;sourceLessVerified=$true;compileSeconds=$timer.Elapsed.TotalSeconds;key=$a.key;sourceFiles=$a.sourceFiles;artApproval='pending'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'qualification.json') -Encoding utf8NoBOM
