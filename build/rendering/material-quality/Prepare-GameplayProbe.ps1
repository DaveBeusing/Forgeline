#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory,[Parameter(Mandatory)][string]$OutputRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=[IO.Path]::GetFullPath($OutputRoot,$repo)
$game=[IO.Path]::GetFullPath($GameDirectory,$repo)
if(-not $root.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Choose a fresh diagnostic directory below artifacts.'}
[void][IO.Directory]::CreateDirectory($root)
$references=foreach($path in Get-ChildItem -LiteralPath $game -Filter 'ForgeLine*.dll') {
    '<Reference Include="'+[Security.SecurityElement]::Escape($path.BaseName)+'"><HintPath>'+[Security.SecurityElement]::Escape($path.FullName)+'</HintPath></Reference>'
}
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><PlatformTarget>x64</PlatformTarget><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>'+($references -join '')+'</ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $root 'GameplayRecoveryProbe.csproj'),$project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayRecoveryProbe.cs') -Destination (Join-Path $root 'Program.cs')
try {
    & dotnet build (Join-Path $root 'GameplayRecoveryProbe.csproj') -c Release -p:UseSharedCompilation=false -nodeReuse:false --output (Join-Path $root 'bin') *> (Join-Path $root 'build.log')
    if($LASTEXITCODE -ne 0){throw 'Gameplay diagnostic build failed; inspect build.log.'}
} finally { Move-Item -LiteralPath (Join-Path $root 'GameplayRecoveryProbe.csproj') -Destination (Join-Path $root 'GameplayRecoveryProbe.csproj.txt') }
Copy-Item -Path (Join-Path $game 'ForgeLine*.dll') -Destination (Join-Path $root 'bin') -Force
Write-Output "Public gameplay timeline probe prepared: $root"
