[CmdletBinding()]
param([string]$OutputRoot = "artifacts/strategic-map-audit")
$ErrorActionPreference = "Stop"
$repository = Split-Path $PSScriptRoot -Parent
$tool = Join-Path $repository "tools/ForgeLine.MapCompiler/bin/Release/net10.0/ForgeLine.MapCompiler.dll"
if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Build the Release map compiler first." }
$root = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $root -Force | Out-Null

function Invoke-Audit([string]$Name, [string[]]$Arguments, [bool]$ExpectedSuccess = $true) {
    & dotnet $tool @Arguments *> (Join-Path $root "$Name.log")
    $succeeded = $LASTEXITCODE -eq 0
    if ($succeeded -ne $ExpectedSuccess) { throw "Unexpected audit exit for $Name; see $root/$Name.log." }
}

$first = Join-Path $root "first.json"
$second = Join-Path $root "second.json"
Invoke-Audit "first" @("--audit-all", "--qualification-output", $first, "--preview-directory", (Join-Path $root "previews"))
Invoke-Audit "second" @("--audit-all", "--qualification-output", $second)
foreach ($extension in @(".json", ".md")) {
    $a = (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($first, $extension)) -Algorithm SHA256).Hash
    $b = (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($second, $extension)) -Algorithm SHA256).Hash
    if ($a -ne $b) { throw "Repeated $extension audit differs." }
}
$reports = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
if (@($reports).Count -lt 2 -or @($reports | Where-Object { $_.Audit.ErrorCount -gt 0 }).Count -gt 0) {
    throw "Registered maps did not qualify."
}

$map = $reports[0]
$warning = @($map.Audit.Diagnostics | Where-Object { $_.Severity -eq "Warning" })[0]
if ($null -eq $warning) { throw "Expected a documented crossing-loss warning." }
$exceptions = Join-Path $root "exceptions.json"
@{ $map.Audit.MapKey = @{ $warning.Id = "Deliberate crossing-loss topology; retained for scenario review." } } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $exceptions -Encoding utf8
$accepted = Join-Path $root "accepted.json"
Invoke-Audit "accepted" @("--audit-all", "--qualification-output", $accepted, "--exceptions", $exceptions)
$acceptedReports = Get-Content -LiteralPath $accepted -Raw | ConvertFrom-Json
if (($acceptedReports | Where-Object { $_.Audit.MapKey -eq $map.Audit.MapKey }).Audit.AcceptedWarningCount -ne 1) {
    throw "Explicit warning rationale was not retained."
}
@{ $map.Audit.MapKey = @{ stale = "Stale diagnostics must fail." } } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $exceptions -Encoding utf8
Invoke-Audit "stale-exception" @("--audit-all", "--qualification-output", (Join-Path $root "stale.json"), "--exceptions", $exceptions) $false
'{"duplicate":{},"duplicate":{}}' | Set-Content -LiteralPath $exceptions -Encoding utf8
Invoke-Audit "duplicate-exception" @("--audit-all", "--qualification-output", (Join-Path $root "duplicate.json"), "--exceptions", $exceptions) $false
Invoke-Audit "unknown-option" @("--audit-all", "--unknown") $false
Invoke-Audit "missing-value" @("--audit-all", "--qualification-output") $false
$blocked = Join-Path $root "blocked-output"
"output path fixture" | Set-Content -LiteralPath $blocked -Encoding utf8
Invoke-Audit "output-failure" @("--audit-all", "--qualification-output", (Join-Path $blocked "report.json")) $false
Write-Host "Strategic audit: all registered maps, identical JSON/Markdown, explicit exception, stale/duplicate rejection and controlled CLI/output failures passed."
exit 0
