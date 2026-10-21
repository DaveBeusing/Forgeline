#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Author','Export','Check')][string]$Action = 'Export',
    [Parameter(Mandatory)][ValidateSet('rocky_scrub','dry_dirt','cracked_earth','bedrock','crushed_gravel','dark_ash','concrete_base')][string]$Family,
    [string]$Master,
    [string]$InputRoot = 'assets/source',
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 3600
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$out = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh terrain output directory.' }
if (-not $Master) { $Master = "assets/source/world/authoring/terrain/$Family.blend" }
$arguments = @('--action',$Action,'--family',$Family)
if ($Action -ne 'Author') { $arguments += @('--master',[IO.Path]::GetFullPath($Master,$repo)) }
& "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script tools/blender/terrain_pbr.py -InputRoot $InputRoot -OutputRoot $out -BlenderPath $BlenderPath -ScriptArguments $arguments -TimeoutSeconds $TimeoutSeconds
