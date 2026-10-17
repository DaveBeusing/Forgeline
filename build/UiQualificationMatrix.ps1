Set-StrictMode -Version Latest

function Get-UiQualificationScenarios {
    # Keep this explicit: adding a fixture must deliberately extend acceptance coverage.
    @('Menu','MenuFocus','MenuDisabled','MenuReveal','Loading','LoadingError',
      'NewGame','LoadGame','LoadEmpty','LoadBroken','SettingsLong','ControlsLong','Pause','Credits',
      'SettingsAudio','SettingsGraphics','Controls','SettingsGeneral','SettingsBindings','BindingConflict',
      'DisplayTrial','PauseSettings','MenuHover','MenuPressed',
      'ResourcePressure','LowPower','SupplyCritical','MixedSelection','Targeting','QueueBlocked',
      'Technology','Contacts','DockFocus','DockHover','DockPressed','DockPending','DockAccepted',
      'DockRejected','MinimapStrategic','Onboarding',
      'OnboardingClosing25','OnboardingClosing50','OnboardingClosing75','OnboardingCollapsed',
      'OnboardingOpening25','OnboardingOpening50','OnboardingOpening75',
      'SelectionUnit','SelectionBuilding','SelectionEmpty','SelectionLongName','MissingPlayerData','GameplayTooltip',
      'GroupSummary','ResourceUnknown','SelectedGroup','Operations','OperationsEmpty','OperationsMissing',
      'ProductionQueue','LogisticsBlocked','SupplyPolicy','TechnologyBlocked','DockQueueTooltip','DockTechnologyTooltip',
      'NotificationsCollapsed','NotificationsExpanded','NotificationDetails',
      'RingConstruction','RingProduction','RingUnits','RingLogistics','RingSupply','RingTactical','RingTechnology','RingParameters',
      'RingPending','RingEdge','RingTooltip','RingOpening50','RingClosing50','RingOffscreen')
}

function Assert-UiQualificationUnion {
    param([Parameter(Mandatory)][array]$Cohorts, [Parameter(Mandatory)][int]$ExpectedCases)
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($cohort in $Cohorts) {
        foreach ($key in $cohort['keys']) {
            if (-not $keys.Add($key)) { throw "Overlapping cohort key: $key" }
        }
    }
    if ($keys.Count -ne $ExpectedCases) { throw "Incomplete UI union: $($keys.Count) / $ExpectedCases cases." }
}

function Get-UiQualificationCohorts {
    param([string[]]$Scenarios = @(Get-UiQualificationScenarios))
    foreach ($dpi in @(96,144,192)) {
        foreach ($display in @('1280x720','1920x1080','2560x1440','3440x1440','3840x2160')) {
            # The native request contract permits at most 30 states per process.
            for ($offset = 0; $offset -lt $Scenarios.Count; $offset += 29) {
                $bank = [int]($offset / 29)
                $last = [Math]::Min($offset + 28, $Scenarios.Count - 1)
                [pscustomobject]@{ id = "dpi$dpi-$display-states$($bank+1)"; dpi = $dpi; display = $display
                    scenarios = @($Scenarios[$offset..$last]) }
            }
        }
    }
}

function Assert-UiQualificationCoverage {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)]$Cohort,
          [Parameter(Mandatory)][string]$Commit)
    if ($Manifest.status -cne 'structural-pass' -or $Manifest.commit -cne $Commit) {
        throw 'Incomplete cohort or changed source identity.'
    }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($scenario in $Cohort.scenarios) {
        foreach ($scale in @(.75,1,2)) { [void]$expected.Add("$scenario/$scale") }
    }
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $dimensions = $Cohort.display.Split('x')
    foreach ($capture in $Manifest.captures) {
        $pair = "$($capture.ui.scenario)/$($capture.uiScale)"
        if (-not $expected.Remove($pair) -or -not $keys.Add($capture.key)) {
            throw "Unexpected or duplicate UI case: $pair"
        }
        if ($capture.rasterDpi -ne $Cohort.dpi -or
            $capture.requestedDisplay.width -ne [int]$dimensions[0] -or
            $capture.requestedDisplay.height -ne [int]$dimensions[1] -or
            $capture.actualSurface.width -ne [int]$dimensions[0] -or
            $capture.actualSurface.height -ne [int]$dimensions[1]) {
            throw "Incorrect requested or native output/DPI: $($capture.key)"
        }
        if ($capture.graphics.health.submissionFaultCount -ne 0) { throw "Submission fault: $($capture.key)" }
        $hasSkin = $false
        foreach ($name in @('skin','battlefieldSkin')) {
            if ($capture.ui.PSObject.Properties.Name -contains $name -and $null -ne $capture.ui.$name) {
                $hasSkin = $true
                if ($capture.ui.$name.missingAssets -ne 0 -or $capture.ui.$name.droppedQuads -ne 0) {
                    throw "Incomplete production skin: $($capture.key)"
                }
            }
        }
        if (-not $hasSkin) { throw "Missing production skin diagnostics: $($capture.key)" }
    }
    if ($expected.Count) { throw "Missing $($expected.Count) UI cases in $($Cohort.id)." }
}

function Get-UiRuntimeFingerprint {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root)
    [string[]]$files = @([IO.Directory]::EnumerateFiles($rootPath, '*', [IO.SearchOption]::AllDirectories))
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($rootPath, $file).Replace('\', '/')
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($relative))
            $hash.AppendData([Convert]::FromHexString((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash))
        }
        [Convert]::ToHexString($hash.GetHashAndReset())
    } finally { $hash.Dispose() }
}
