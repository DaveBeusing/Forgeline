#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientDirectory,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$client = [IO.Path]::GetFullPath($ClientDirectory)
$root = [IO.Path]::GetFullPath($OutputRoot)
$allowed = (Join-Path $repo 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) { throw 'Choose a fresh output root below repository artifacts.' }
if (-not (Test-Path -LiteralPath (Join-Path $client 'ForgeLine.Graphics.dll'))) { throw 'ClientDirectory must contain the frozen graphics binary.' }
foreach ($name in @('FORGELINE_DRED','FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK')) {
    if ([Environment]::GetEnvironmentVariable($name) -eq '1') { throw "Disable $name for the paired production baseline." }
}
[void][IO.Directory]::CreateDirectory($root)
$references = foreach ($path in Get-ChildItem -LiteralPath $client -Filter '*.dll') {
    try { [void][Reflection.AssemblyName]::GetAssemblyName($path.FullName) } catch { continue }
    '<Reference Include="' + [Security.SecurityElement]::Escape($path.BaseName) + '"><HintPath>' + [Security.SecurityElement]::Escape($path.FullName) + '</HintPath></Reference>'
}
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><PlatformTarget>x64</PlatformTarget><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + ($references -join '') + '</ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $root 'UploadProbe.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UploadProbe.cs') -Destination (Join-Path $root 'Program.cs')
try {
    & dotnet build (Join-Path $root 'UploadProbe.csproj') -c Release --output (Join-Path $root 'bin') *> (Join-Path $root 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed; inspect build.log.' }
} finally {
    Move-Item -LiteralPath (Join-Path $root 'UploadProbe.csproj') -Destination (Join-Path $root 'UploadProbe.csproj.txt')
}
# The exact frozen managed/native dependencies must accompany the measurement host.
Copy-Item -Path (Join-Path $client '*.dll') -Destination (Join-Path $root 'bin') -Force
& dotnet (Join-Path $root 'bin/UploadProbe.dll') (Join-Path $root 'measurement.json') $SourceSha *> (Join-Path $root 'measurement.log')
if ($LASTEXITCODE -ne 0) { throw 'Native upload measurement failed; inspect measurement.log.' }
Get-Content -LiteralPath (Join-Path $root 'measurement.json') -Raw
