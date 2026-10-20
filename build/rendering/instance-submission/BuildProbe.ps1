[CmdletBinding()]
param([Parameter(Mandatory)][string]$BenchmarkDirectory,
      [Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$benchmark = (Resolve-Path -LiteralPath $BenchmarkDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$runtimeRoot = Split-Path (Get-Command dotnet).Source
$sdkVersion = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
$reference = Get-ChildItem (Join-Path $runtimeRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '10.0.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $runtimeRoot "sdk/$sdkVersion/Roslyn/bincore/csc.dll"
$arguments = @($compiler, '/noconfig', '/nostdlib+', '/langversion:14.0', '/nullable:enable', '/target:exe', "/out:$output/SubmissionProbe.dll")
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in (Get-ChildItem (Join-Path $reference.FullName 'ref/net10.0/*.dll'))) {
    if ($names.Add($file.Name)) { $arguments += "/reference:$($file.FullName)" }
}
foreach ($file in (Get-ChildItem (Join-Path $benchmark '*.dll'))) {
    if ($names.Add($file.Name)) { $arguments += "/reference:$($file.FullName)" }
}
$arguments += (Join-Path $PSScriptRoot 'SubmissionProbe.cs')
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Probe compile failed: $LASTEXITCODE" }
Copy-Item -Path (Join-Path $benchmark '*.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $benchmark 'ForgeLine.Rendering.Benchmarks.runtimeconfig.json') -Destination (Join-Path $output 'SubmissionProbe.runtimeconfig.json') -Force
Copy-Item -LiteralPath (Join-Path $benchmark 'ForgeLine.Rendering.Benchmarks.deps.json') -Destination (Join-Path $output 'SubmissionProbe.deps.json') -Force
