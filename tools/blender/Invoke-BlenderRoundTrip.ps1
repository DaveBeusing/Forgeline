#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('RoundTrip','Import','Export')][string]$Action = 'RoundTrip',
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$SourceRoot = 'assets/source',
    [string]$FixtureContract = 'tools/blender/fixtures/production.json',
    [string[]]$AssetId = @(), [string]$BlendFile, [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 180, [string]$CancellationFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
$inputPath = [IO.Path]::GetFullPath($SourceRoot, $repo)
if (Test-Path -LiteralPath $output) { throw 'Round-trip output must be fresh; existing authoring projects are never overwritten.' }
$separator = [IO.Path]::DirectorySeparatorChar
if (($inputPath + $separator).StartsWith($output + $separator, [StringComparison]::OrdinalIgnoreCase) -or
    ($output + $separator).StartsWith($inputPath + $separator, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source/output directories cannot overlap.' }
$fixture = [IO.Path]::GetFullPath($FixtureContract, $repo)
if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'Fixture contract does not exist.' }
$arguments = @('--action',$Action,'--fixtures',$fixture)
foreach ($id in $AssetId) { $arguments += @('--asset-id',$id) }
if ($BlendFile) { $arguments += @('--blend-file',[IO.Path]::GetFullPath($BlendFile,$repo)) }
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'roundtrip.py') `
    -InputRoot $inputPath -OutputRoot $output -ScriptArguments $arguments -BlenderPath $BlenderPath `
    -TimeoutSeconds $TimeoutSeconds -CancellationFile $CancellationFile
