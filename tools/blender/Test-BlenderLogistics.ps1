#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot, [string]$BlenderPath,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 1800)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$root = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $root) { throw 'Use a fresh qualification directory.' }
$compiler = Join-Path $repo 'tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll'
foreach ($family in @('storage_depot','supply_depot','logistics_hub')) {
    $destination = Join-Path $root $family
    $master = Join-Path $repo "assets/source/buildings/directorate/authoring/$family.blend"
    $masterHash = (Get-FileHash -LiteralPath $master).Hash
    foreach ($name in @('first','second')) {
        & (Join-Path $PSScriptRoot 'Invoke-BlenderLogistics.ps1') -Family $family -OutputRoot (Join-Path $destination $name) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    }
    & (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_logistics_contract.py') -InputRoot assets/source -OutputRoot (Join-Path $destination 'native') -ScriptArguments @($family,$master) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    & (Join-Path $PSScriptRoot 'Invoke-Blender.ps1') -Action Script -Script (Join-Path $PSScriptRoot 'tests/native_logistics_sources.py') -InputRoot assets/source -OutputRoot (Join-Path $destination 'source-contracts') -ScriptArguments @($family,$master) -BlenderPath $BlenderPath -TimeoutSeconds $TimeoutSeconds
    $first = Join-Path $destination 'first/source'
    $files = @(Get-ChildItem -LiteralPath $first -File -Recurse)
    foreach ($variant in @('second/source','native/control/source')) {
        $other = Join-Path $destination $variant
        if (@(Get-ChildItem -LiteralPath $other -File -Recurse).Count -ne $files.Count) { throw 'Exported file sets differ.' }
        foreach ($file in $files) {
            $target = Join-Path $other ([IO.Path]::GetRelativePath($first, $file.FullName))
            if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Reopened export differs: $target" }
        }
    }
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $first "buildings/directorate/sources/logistics/$family/blender") -Filter '*.glb')) {
        $production = Join-Path $repo "assets/source/buildings/directorate/sources/logistics/$family/blender/$($file.Name)"
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $production).Hash) { throw "Saved export requires review: $production" }
    }
    & dotnet $compiler --mode build --source $first --runtime (Join-Path $destination 'runtime') --qualification-output (Join-Path $destination 'runtime-qualification.json') *> (Join-Path $destination 'compile.log')
    if ($LASTEXITCODE -ne 0) { throw 'Logistics compilation failed.' }
    & dotnet $compiler --mode validate --source (Join-Path $destination 'absent-source') --runtime (Join-Path $destination 'runtime') *> (Join-Path $destination 'source-less.log')
    if ($LASTEXITCODE -ne 0) { throw 'Logistics source-less validation failed.' }
    if ((Get-FileHash -LiteralPath $master).Hash -ne $masterHash) { throw 'Production master changed.' }
}
@{ success = $true; repeatedSavedExports = $true; canonicalGeometryMatches = $true; sourceLessIntegrity = $true; productionMastersUnchanged = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8NoBOM
