#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientDirectory,[Parameter(Mandatory)][string]$OutputRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=[IO.Path]::GetFullPath($OutputRoot)
$client=[IO.Path]::GetFullPath($ClientDirectory)
$allowed=(Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar
if(-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Choose a fresh probe output root below artifacts.'}
[void][IO.Directory]::CreateDirectory($root)
$references=foreach($path in Get-ChildItem -LiteralPath $client -Filter '*.dll') {
    try{[void][Reflection.AssemblyName]::GetAssemblyName($path.FullName)}catch{continue}
    '<Reference Include="'+[Security.SecurityElement]::Escape($path.BaseName)+'"><HintPath>'+[Security.SecurityElement]::Escape($path.FullName)+'</HintPath></Reference>'
}
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><PlatformTarget>x64</PlatformTarget><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>'+($references -join '')+'</ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $root 'MaterialWorldProbe.csproj'),$project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MaterialWorldProbe.cs') -Destination (Join-Path $root 'Program.cs')
try {
    & dotnet build (Join-Path $root 'MaterialWorldProbe.csproj') -c Release --output (Join-Path $root 'bin') *> (Join-Path $root 'build.log')
    if($LASTEXITCODE -ne 0){throw 'Probe build failed; inspect build.log.'}
} finally { Move-Item -LiteralPath (Join-Path $root 'MaterialWorldProbe.csproj') -Destination (Join-Path $root 'MaterialWorldProbe.csproj.txt') }
Copy-Item -Path (Join-Path $client '*.dll') -Destination (Join-Path $root 'bin') -Force
Write-Output "Frozen material world probe prepared: $root"
