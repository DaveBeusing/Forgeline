[CmdletBinding()]
param([string]$PublicRoot = 'artifacts/prealpha/win-x64/publish', [string]$RuntimeAssetRoot = 'assets/runtime',
    [string]$OutputRoot = 'artifacts/developer-tools/packages')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$source = (& git -C $repository rev-parse HEAD).Trim()
$public = [IO.Path]::GetFullPath($PublicRoot, $repository)
$assets = [IO.Path]::GetFullPath($RuntimeAssetRoot, $repository)
$output = [IO.Path]::GetFullPath((Join-Path $OutputRoot $source), $repository)
$allowed = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not ($output + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Internal output must stay under repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Internal qualification output already exists; use a new bounded output root for another attempt.' }
& "$PSScriptRoot/Assert-PublicBuild.ps1" -PublishRoot $public
$publicProvenance = Get-Content -LiteralPath (Join-Path $public 'build-provenance.json') -Raw | ConvertFrom-Json
if ($publicProvenance.sourceRevision -ne $source -or $publicProvenance.buildVariant -cne 'Public' -or $publicProvenance.developerToolsEnabled) { throw 'Public provenance differs from this source or variant.' }
$context = [System.Runtime.Loader.AssemblyLoadContext]::new('PublicQualification', $true)
try {
    foreach ($pair in @(@('ForgeLine.Game.dll', 'ForgeLine.Game.DeveloperCommandGateway'),
        @('ForgeLine.Client.dll', 'ForgeLine.Client.ClientDeveloperMenuBackend'), @('ForgeLine.UI.dll', 'ForgeLine.UI.DeveloperMenuModel'))) {
        $assembly = $context.LoadFromAssemblyPath((Join-Path $public $pair[0]))
        if ($null -ne $assembly.GetType($pair[1], $false)) { throw "Public package includes a developer implementation: $($pair[1])." }
    }
}
finally { $context.Unload() }
$internal = Join-Path $output 'Developer'
& dotnet publish (Join-Path $repository 'src/ForgeLine.Client/ForgeLine.Client.csproj') --configuration Release --runtime win-x64 --self-contained false -p:ForgeLineEnableDeveloperTools=true --output $internal
if ($LASTEXITCODE -ne 0) { throw 'Internal Developer publish failed.' }
$rejected = $false
try { & "$PSScriptRoot/Assert-PublicBuild.ps1" -PublishRoot $internal }
catch { if ($_.Exception.Message -notmatch 'Refusing non-public') { throw }; $rejected = $true }
if (-not $rejected) { throw 'Public package guard accepted the Developer output.' }
$manifestPath = Join-Path $assets 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$destination = Join-Path $internal 'assets/runtime'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($record in $manifest.assets) {
    $inputPath = [IO.Path]::GetFullPath($record.runtimePath, $assets)
    $outputPath = [IO.Path]::GetFullPath($record.runtimePath, $destination)
    if (-not $inputPath.StartsWith($assets + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not $outputPath.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime record escapes its package root.' }
    if ((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.runtimeHash) { throw 'Runtime input hash differs from its qualified manifest.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
    Copy-Item -LiteralPath $inputPath -Destination $outputPath
    if ((Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.runtimeHash) { throw 'Internal runtime copy changed.' }
}
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'manifest.json')
& "$PSScriptRoot/Validate-ScenarioPackage.ps1" -PublishRoot $internal
& "$PSScriptRoot/Validate-UiFonts.ps1" -PublishRoot $internal
[ordered]@{ schemaVersion = 1; sourceRevision = $source; buildVariant = 'Developer'; developerToolsEnabled = $true;
    configuration = 'Release'; sdk = (& dotnet --version).Trim(); runtimeAssetManifestSha256 = $manifestHash;
    runtimeAssetSignature = $manifest.contentSignature; scenarioMaps = @(Get-ChildItem -LiteralPath (Join-Path $internal 'maps') -Filter '*.flmap.json' -File |
        Sort-Object Name | ForEach-Object { @{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } });
    publicPublisherRejectedDeveloper = $rejected; nativeInteraction = 'not executed by this script' } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $internal 'build-provenance.json') -Encoding utf8NoBOM
Write-Host "Public and internal package boundaries verified at source $source. Internal artifacts remain local."
