[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [int]$RenderStressInstances = 1000,
    [string]$OutputRoot = 'artifacts/rts-reference'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$referenceRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputRoot))
$displays = @(@(1920, 1080), @(2560, 1440), @(3840, 2160), @(3440, 1440))
foreach ($display in $displays) {
    foreach ($zoom in @('CloseTactical', 'NormalGameplay', 'Strategic')) {
        $name = "$($display[0])x$($display[1])-$zoom"
        $settingsRoot = Join-Path $referenceRoot "$name/settings"
        $settingsDirectory = Join-Path $settingsRoot 'FORGELINE'
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
        @{
            schemaVersion = 1; windowWidth = $display[0]; windowHeight = $display[1]
            uiScale = 1.0; edgeScrollEnabled = $false; showOnboarding = $false; showStudioSplash = $false
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settingsDirectory 'settings.json') -Encoding utf8
        $reportPath = Join-Path $referenceRoot "$name/metrics.json"
        & dotnet run --project (Join-Path $repositoryRoot 'src/ForgeLine.Client/ForgeLine.Client.csproj') `
            --configuration $Configuration --no-build -- --smoke-test --render-stress $RenderStressInstances `
            --reference-zoom $zoom --settings-root $settingsRoot --visual-qualification-output $reportPath
        if ($LASTEXITCODE -ne 0) { throw "Reference qualification failed for $name." }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.metrics.textureBindingFailureCount -ne 0 -or $report.metrics.materialBindingFailureCount -ne 0) {
            throw "Runtime resource binding failures in $name."
        }
        if ($report.metrics.surface.width -ne $display[0] -or $report.metrics.surface.height -ne $display[1]) {
            Write-Warning "Display limited $name to $($report.metrics.surface.width)x$($report.metrics.surface.height); requested resolution is unqualified."
        }
    }
}
