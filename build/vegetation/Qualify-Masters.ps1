#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath, [switch]$RenderPreviews,
      [string]$RuntimeCache, [switch]$ReuseAuthored, [switch]$ReuseCompiled)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
Import-Module (Join-Path $repo 'tools/blender/BlenderToolchain.psm1') -Force
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Build Release first.' }
if (-not $ReuseAuthored) {
    if (Test-Path -LiteralPath $output) { throw 'Use fresh output.' }
    $arguments = @()
    if ($RenderPreviews) { $arguments += '--render' }
    & (Join-Path $repo 'tools/blender/Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'author.py') -InputRoot assets/source -OutputRoot $output -ScriptArguments $arguments -BlenderPath $BlenderPath -TimeoutSeconds 1800
}
if (-not $ReuseCompiled) {
& python (Join-Path $PSScriptRoot 'retained.py') prepare $output
if ($LASTEXITCODE -ne 0) { throw 'Native proof or aggregation failed.' }
foreach ($variant in @('baseline','first','second','reopened','edited')) {
    $runtime = Join-Path $output "$variant/runtime"
    if (Test-Path -LiteralPath $runtime) { throw 'Use fresh runtime output.' }
    $cache = if ($variant -eq 'baseline') { $RuntimeCache } else { Join-Path $output 'baseline/runtime' }
    if ($cache) {
        # This is an incomplete compiler cache seed, never a deployment runtime.
        # Copy only candidate IDs; the compiler publishes and qualifies a new manifest.
        $cacheManifest = Get-Content -LiteralPath (Join-Path $cache 'manifest.json') -Raw | ConvertFrom-Json
        $wanted = @(Get-ChildItem -LiteralPath (Join-Path $output "$variant/source") -Filter '*.asset.json' -Recurse | ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).id })
        [void][IO.Directory]::CreateDirectory($runtime)
        Copy-Item -LiteralPath (Join-Path $cache 'manifest.json') -Destination (Join-Path $runtime 'manifest.json')
        foreach ($asset in $cacheManifest.assets) {
            if ($asset.id -notin $wanted) { continue }
            $target = Join-Path $runtime $asset.runtimePath
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            Copy-Item -LiteralPath (Join-Path $cache $asset.runtimePath) -Destination $target
        }
    }
    & dotnet $compiler --mode development --source (Join-Path $output "$variant/source") --runtime $runtime --qualification-output (Join-Path $output "$variant/compile.json") 2>&1 | Out-File (Join-Path $output "$variant/compile.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $variant" }
    & dotnet $compiler --mode validate --source (Join-Path $output 'absent-source') --runtime $runtime --qualification-output (Join-Path $output "$variant/source-less.json") 2>&1 | Out-File (Join-Path $output "$variant/source-less.log") -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) { throw "Source-less validation failed: $variant" }
}
}
if (Test-Path -LiteralPath (Join-Path $output 'typed-comparison.log')) {
    Copy-Item -LiteralPath (Join-Path $output 'typed-comparison.log') -Destination (Join-Path $output ('typed-comparison-' + [Guid]::NewGuid().ToString('N') + '.log'))
}
$previous = $env:FORGELINE_VEGETATION_MASTERS
try {
    $env:FORGELINE_VEGETATION_MASTERS = $output
    & dotnet test --project (Join-Path $repo 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj') --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.VegetationBlenderMasterTests *> (Join-Path $output 'typed-comparison.log')
    if ($LASTEXITCODE -ne 0) { throw 'Typed compiled geometry comparison failed.' }
}
finally { $env:FORGELINE_VEGETATION_MASTERS = $previous }
& python (Join-Path $PSScriptRoot 'retained.py') qualify $output
if ($LASTEXITCODE -ne 0) { throw 'Source/runtime determinism failed.' }
