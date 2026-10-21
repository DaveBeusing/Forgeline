#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Author','Export')][string]$Action = 'Author',
    [Parameter(Mandatory)][ValidateSet('cargo_truck','supply_truck')][string]$Family,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$SourceRoot = 'assets/source',
    [string]$BlendFile, [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 180
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BlenderToolchain.psm1') -Force
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = Resolve-BlenderOutput -Path $OutputRoot -RepositoryRoot $repo
if (Test-Path -LiteralPath $output) { throw 'Truck output must be fresh; existing masters are never overwritten.' }
$inputPath = [IO.Path]::GetFullPath($SourceRoot,$repo)
$separator = [IO.Path]::DirectorySeparatorChar
if (($inputPath + $separator).StartsWith($output + $separator, [StringComparison]::OrdinalIgnoreCase) -or
    ($output + $separator).StartsWith($inputPath + $separator, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source/output directories cannot overlap.' }
$arguments = @('--action',$Action,'--family',$Family,'--fixtures',(Join-Path $PSScriptRoot 'fixtures/logistics_trucks.json'))
if ($BlendFile) { $arguments += @('--blend-file',[IO.Path]::GetFullPath($BlendFile,$repo)) }
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'logistics_truck.py') `
    -InputRoot $inputPath -OutputRoot $output -ScriptArguments $arguments `
    -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
