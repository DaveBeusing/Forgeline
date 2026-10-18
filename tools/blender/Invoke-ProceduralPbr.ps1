#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$Profile = 'tools/blender/fixtures/pbr.json',
    [string[]]$Families = @('painted_armor'),
    [string]$InputRoot = 'assets/source',
    [string]$OutputRoot = 'artifacts/procedural-pbr',
    [string]$BlenderPath,
    [switch]$Check,
    [switch]$RenderPreviews,
    [ValidateRange(1,3600)][int]$TimeoutSeconds = 600
)
$ErrorActionPreference='Stop'
$arguments=@('--profile',[IO.Path]::GetFullPath($Profile),'--families',($Families -join ','))
if ($Check) { $arguments+='--check' }
if ($RenderPreviews) { $arguments+='--render' }
& "$PSScriptRoot/Invoke-Blender.ps1" -Action Script -Script tools/blender/procedural_pbr.py -InputRoot $InputRoot -OutputRoot $OutputRoot -BlenderPath $BlenderPath -ScriptArguments $arguments -TimeoutSeconds $TimeoutSeconds
