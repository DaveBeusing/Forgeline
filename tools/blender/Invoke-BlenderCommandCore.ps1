#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('Author','Export')][string]$Action = 'Export',
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BlendFile = 'assets/source/buildings/directorate/authoring/command_core.blend',
    [string]$BlenderPath, [ValidateRange(1,86400)][int]$TimeoutSeconds = 480)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$destination = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $destination) { throw 'Use fresh Command Core output; saved masters are never overwritten.' }
$arguments = @('--action', $Action, '--fixtures', (Join-Path $PSScriptRoot 'fixtures/command_core.json'))
if ($Action -eq 'Export') { $arguments += @('--blend-file', [IO.Path]::GetFullPath($BlendFile, $repo)) }
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'command_core.py') -InputRoot assets/source -OutputRoot $OutputRoot -ScriptArguments $arguments -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
