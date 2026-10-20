#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Script, [Parameter(Mandatory)][string]$ParametersJson)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    $parameters = Get-Content -LiteralPath $ParametersJson -Raw | ConvertFrom-Json -AsHashtable
    $global:LASTEXITCODE = 0
    & $Script @parameters
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.ToString())
    exit 1
}
