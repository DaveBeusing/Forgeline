[CmdletBinding()]
param([Parameter(Mandatory)][string]$BenchmarkDirectory,
      [Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$benchmark = (Resolve-Path -LiteralPath $BenchmarkDirectory).Path
if (Test-Path -LiteralPath (Join-Path $benchmark 'TerrainProbe.dll')) { throw 'Use an untouched benchmark output directory as input.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh probe directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$runtimeRoot = Split-Path (Get-Command dotnet).Source
$sdkVersion = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
if ((& dotnet --version).Trim() -ne $sdkVersion -or $LASTEXITCODE -ne 0) { throw 'Use the repository-pinned SDK.' }
$reference = Get-ChildItem (Join-Path $runtimeRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '10.0.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $runtimeRoot "sdk/$sdkVersion/Roslyn/bincore/csc.dll"
$arguments = @($compiler, '/noconfig', '/nostdlib+', '/langversion:14.0', '/nullable:enable', '/target:exe', '/deterministic+', "/out:$output/TerrainProbe.dll")
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in (Get-ChildItem (Join-Path $reference.FullName 'ref/net10.0/*.dll'))) {
    if ($names.Add($file.Name)) { $arguments += "/reference:$($file.FullName)" }
}
foreach ($file in (Get-ChildItem (Join-Path $benchmark '*.dll'))) {
    if ($names.Add($file.Name)) { $arguments += "/reference:$($file.FullName)" }
}
$arguments += (Join-Path $PSScriptRoot 'TerrainProbe.cs')
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Probe compile failed: $LASTEXITCODE" }
Copy-Item -Path (Join-Path $benchmark '*.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $benchmark 'ForgeLine.Rendering.Benchmarks.runtimeconfig.json') -Destination (Join-Path $output 'TerrainProbe.runtimeconfig.json') -Force
Copy-Item -LiteralPath (Join-Path $benchmark 'ForgeLine.Rendering.Benchmarks.deps.json') -Destination (Join-Path $output 'TerrainProbe.deps.json') -Force

if (Test-Path -LiteralPath (Join-Path $benchmark 'runtimes')) { Copy-Item -LiteralPath (Join-Path $benchmark 'runtimes') -Destination $output -Recurse -Force }
