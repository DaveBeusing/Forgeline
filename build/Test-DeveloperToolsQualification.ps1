[CmdletBinding()]
param([switch]$NoBuild, [string]$OutputRoot = 'artifacts/developer-tools')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputRoot, $repository)
$allowed = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not ($output + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Qualification output must remain under repository artifacts.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$steps = [Collections.Generic.List[object]]::new()
function Invoke-QualificationDotnet([string]$Name, [string[]]$Arguments, [switch]$Reject, [string]$Expected) {
    $lines = @(& dotnet @Arguments 2>&1)
    $code = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    $passed = if ($Reject) { $code -ne 0 -and ($lines -join "`n") -match $Expected } else { $code -eq 0 }
    $steps.Add([ordered]@{ name = $Name; arguments = $Arguments; exitCode = $code; passed = $passed })
    if (-not $passed) { throw "Qualification failed: $Name (exit $code)." }
    return ($lines -join "`n")
}
function Invoke-SelectedTests([string]$Project, [string]$Variant, [string[]]$Classes) {
    $arguments = @('test', '--project', $Project, '--configuration', 'Release', '--no-build', "-p:ForgeLineEnableDeveloperTools=$Variant", '--', '--filter-class') + $Classes + @('--parallel', 'none')
    [void](Invoke-QualificationDotnet "$Project / Developer=$Variant" $arguments)
}
$previousOutput = $env:FORGELINE_QUALIFICATION_OUTPUT
$previousDeveloper = $env:ForgeLineEnableDeveloperTools
$outcome = 'failed'
Push-Location $repository
try {
    $env:FORGELINE_QUALIFICATION_OUTPUT = Join-Path $output 'fixture'
    $env:ForgeLineEnableDeveloperTools = $null
    if (-not $NoBuild) {
        foreach ($variant in @('false', 'true')) {
            foreach ($project in @('tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj', 'tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj', 'src/ForgeLine.Headless/ForgeLine.Headless.csproj')) {
                [void](Invoke-QualificationDotnet "Build $project / Developer=$variant" @('build', $project, '--configuration', 'Release', '--maxcpucount:1', "-p:ForgeLineEnableDeveloperTools=$variant"))
            }
        }
        [void](Invoke-QualificationDotnet 'Build ordinary Debug without Developer property' @('build', 'tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj', '--configuration', 'Debug', '--maxcpucount:1'))
    }
    Invoke-SelectedTests 'tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj' 'false' @('ForgeLine.Game.Tests.DeveloperToolsFoundationTests')
    Invoke-SelectedTests 'tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj' 'false' @('ForgeLine.Client.Tests.DeveloperPublicDistributionTests', 'ForgeLine.Client.Tests.DeveloperMenuCapabilityTests')
    Invoke-SelectedTests 'tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj' 'true' @('ForgeLine.Game.Tests.DeveloperToolsFoundationTests', 'ForgeLine.Game.Tests.DeveloperGameplayTuningTests', 'ForgeLine.Game.Tests.DeveloperSimulationActionsTests', 'ForgeLine.Game.Tests.DeveloperPresetCodecTests')
    Invoke-SelectedTests 'tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj' 'true' @('ForgeLine.Client.Tests.ClientSimulationHostTests', 'ForgeLine.Client.Tests.DeveloperMenuTests', 'ForgeLine.Client.Tests.DeveloperPresetStoreTests', 'ForgeLine.Client.Tests.DeveloperMenuCapabilityTests')
    [void](Invoke-QualificationDotnet 'Ordinary Debug capability negative' @('test', '--project', 'tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj', '--configuration', 'Debug', '--no-build', '--', '--filter-class', 'ForgeLine.Game.Tests.DeveloperToolsFoundationTests', '--parallel', 'none'))
    $fixture = $env:FORGELINE_QUALIFICATION_OUTPUT
    $identity = Get-Content -LiteralPath (Join-Path $fixture 'identity.json') -Raw | ConvertFrom-Json
    $developerHost = Join-Path $repository 'src/ForgeLine.Headless/bin/Developer/Release/net10.0/ForgeLine.Headless.dll'
    $publicHost = Join-Path $repository 'src/ForgeLine.Headless/bin/Release/net10.0/ForgeLine.Headless.dll'
    foreach ($inputKind in @('replay', 'save')) {
        $flag = if ($inputKind -eq 'replay') { '--replay-input' } else { '--load-input' }
        $path = Join-Path $fixture "developer.$inputKind.json"
        $result = Invoke-QualificationDotnet "Separate Developer Headless $inputKind" @($developerHost, '--scenario', 'central-divide', $flag, $path)
        if (-not $result.Contains($identity.ExpectedStateSha256, [StringComparison]::Ordinal)) { throw "Headless $inputKind hash differs from client fixture." }
        [void](Invoke-QualificationDotnet "Public rejects Developer $inputKind" @($publicHost, '--scenario', 'central-divide', $flag, $path) -Reject -Expected 'developer|IncompatibleVersion')
    }
    $preset = Join-Path $fixture 'developer.preset.json'
    [void](Invoke-QualificationDotnet 'Public rejects preset option' @($publicHost, '--preset-input', $preset) -Reject -Expected 'Developer build')
    [void](Invoke-QualificationDotnet 'Developer rejects conflicting seed' @($developerHost, '--preset-input', $preset, '--seed', '742027') -Reject -Expected 'conflicts')
    & "$PSScriptRoot/Test-DeveloperToolsFoundation.ps1" -RepositoryRoot $repository
    if (-not $?) { throw 'Publisher capability contracts failed.' }
    $outcome = 'passed'
}
finally {
    [ordered]@{ schemaVersion = 1; sourceRevision = (& git rev-parse HEAD).Trim(); sourceTree = (& git rev-parse 'HEAD^{tree}').Trim();
        dirty = [bool]@(& git status --porcelain).Count; outcome = $outcome; recordedAtUtc = [DateTimeOffset]::UtcNow.ToString('O');
        steps = @($steps.ToArray()) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'qualification.json') -Encoding utf8
    $env:FORGELINE_QUALIFICATION_OUTPUT = $previousOutput
    $env:ForgeLineEnableDeveloperTools = $previousDeveloper
    Pop-Location
}
