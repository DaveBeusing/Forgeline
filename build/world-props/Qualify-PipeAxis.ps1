#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [Parameter(Mandatory)][string]$SourceRoot,
      [Parameter(Mandatory)][string]$MaterialCacheRoot, [switch]$ReuseAuthored)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputRoot, $repo)
if ((Test-Path $output) -and -not $ReuseAuthored) { throw 'Use fresh axis evidence output.' }
if (-not $ReuseAuthored) {
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'pipe_axis.py') -InputRoot $SourceRoot -OutputRoot $output -TimeoutSeconds 900
}
$native = Get-Content (Join-Path $output 'native-axis.json') -Raw | ConvertFrom-Json
if (-not $native.success -or -not $native.boundsAndTopologyUnchanged -or -not $native.canonicalSourcesUnchanged -or $native.gameRotation -ne 'positive-Y-90-degrees') { throw 'Incomplete native axis correction.' }
$canonical = [IO.Path]::GetFullPath('assets/source', $repo)
foreach ($file in $native.sourceHashes.PSObject.Properties) {
    $path = [IO.Path]::GetFullPath($file.Name, $canonical)
    if (-not $path.StartsWith($canonical + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) { throw 'Original pipe source changed.' }
}
$seed = [IO.Path]::GetFullPath($MaterialCacheRoot, $repo)
$seedManifest = Get-Content (Join-Path $seed 'manifest.json') -Raw | ConvertFrom-Json
if (@($seedManifest.assets).Count -ne 4 -or @($seedManifest.assets | Where-Object type -eq 'mesh').Count) { throw 'Use the four-record PBR material cache.' }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
foreach ($variant in @('baseline','first','second','reopened')) {
    $source = if ($variant -eq 'baseline') { Join-Path $output 'first/baseline-source' } else { Join-Path $output "$variant/source" }
    $runtime = Join-Path $output "$variant/runtime"
    if (Test-Path $runtime) { throw 'Use a fresh runtime directory.' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($runtime))
    Copy-Item -LiteralPath $seed -Destination $runtime -Recurse
    & dotnet $compiler --mode development --source $source --runtime $runtime --qualification-output (Join-Path $output "$variant-compile.json") 2>&1 | Out-File (Join-Path $output "$variant-compile.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE) { throw "Pipe compile failed: $variant" }
    & dotnet $compiler --mode validate --source (Join-Path $output 'absent-source') --runtime $runtime --qualification-output (Join-Path $output "$variant-source-less.json") 2>&1 | Out-File (Join-Path $output "$variant-source-less.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE) { throw "Pipe source-less validation failed: $variant" }
}
function Read-Tree([string]$Path) {
    @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Path, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
}
foreach ($variant in @('second','reopened')) {
    foreach ($kind in @('source','runtime')) {
        if (Compare-Object (Read-Tree (Join-Path $output "first/$kind")) (Read-Tree (Join-Path $output "$variant/$kind"))) { throw 'Pipe source/runtime exports are not deterministic.' }
    }
}
$previous = $env:FORGELINE_PROP_PIPE_AXIS
try {
    $env:FORGELINE_PROP_PIPE_AXIS = $output
    dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') -c Release --no-build -- --filter-class '*WorldPropPipeAxisTests' 2>&1 | Out-File (Join-Path $output 'typed-comparison.log') -Encoding utf8NoBOM
    if ($LASTEXITCODE) { throw 'Typed baked-axis comparison failed.' }
}
finally { $env:FORGELINE_PROP_PIPE_AXIS = $previous }
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); success = $true;
   gameRotation = 'positive-Y-90-degrees'; sourceSignature = $native.sourceSignature;
   deterministicSourceAndRuntime = $true; sourceLessIntegrity = $true; typedCompiledComparison = $true;
   canonicalSourcesUnchanged = $true; boundsAndTopologyUnchanged = $true; humanArtApproval = 'pending' } |
   ConvertTo-Json | Set-Content (Join-Path $output 'qualification.json') -Encoding utf8NoBOM
