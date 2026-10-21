#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('Author','Export')][string]$Action = 'Export',
    [Parameter(Mandatory)][ValidateSet('storage_depot','supply_depot','logistics_hub')][string]$Family,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BlendFile, [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 900)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$destination = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $destination) { throw 'Use fresh logistics output; saved masters are never overwritten.' }
$arguments = @('--action', $Action, '--family', $Family)
if ($Action -eq 'Export') {
    if (-not $BlendFile) { $BlendFile = "assets/source/buildings/directorate/authoring/$Family.blend" }
    $arguments += @('--blend-file', [IO.Path]::GetFullPath($BlendFile, $repo))
}
& (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'logistics.py') -InputRoot assets/source -OutputRoot $OutputRoot -ScriptArguments $arguments -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
