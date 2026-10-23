#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Plan','Build','Assets','Contracts','Native','Warp','Package')][string]$Stage = 'Plan',
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$BuildEvidenceRoot,
    [string]$AssetEvidenceRoot,
    [ValidateRange(60,259200)][int]$CommandTimeoutSeconds = 14400
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'rendering/qualification/Qualification.Commands.psm1') -Force
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputRoot, $repository)
# Keep generated evidence in the ignored artifacts tree; never overwrite sources or another attempt.
$artifacts = Join-Path $repository 'artifacts'
if (-not (Test-QualificationPathWithin $output $artifacts) -or $output -eq $artifacts) {
    throw 'OutputRoot must be a new child directory below repository artifacts/.'
}
if (Test-Path -LiteralPath $output) { throw 'Choose a new OutputRoot; previous evidence is immutable.' }
Push-Location $repository
try {
    $source = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $source -notmatch '^[0-9a-f]{40}$') { throw 'Full source SHA required.' }
    $dirty = @(& git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source cleanliness.' }
    if ($Stage -ne 'Plan' -and $dirty.Count) { throw 'Commit changes before executing a source-pinned stage.' }
    if ($Stage -ne 'Plan' -and (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64')) {
        throw 'Execution requires Windows x64; Plan remains available elsewhere.'
    }
    [void][IO.Directory]::CreateDirectory($output)
    [void][IO.Directory]::CreateDirectory((Join-Path $output 'commands'))
    $sdk = (Get-Content (Join-Path $repository 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $binaryRoots = @('src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64',
        'tools/ForgeLine.AssetCompiler/bin/Release/net10.0')
    function Read-PriorStage([string]$Root, [string]$ExpectedStage) {
        if (-not $Root) { throw "$Stage requires the completed $ExpectedStage evidence root for this HEAD." }
        $path = Join-Path ([IO.Path]::GetFullPath($Root, $repository)) 'session.json'
        $prior = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($prior.sourceSha -cne $source -or $prior.sourceDirty -or $prior.requestedStage -cne $ExpectedStage -or
            $prior.executionStatus -cne 'completed-review-required' -or $prior.observedSdk -cne $sdk) {
            throw "Prior $ExpectedStage evidence does not describe a completed, clean build of this HEAD and SDK."
        }
        return $prior
    }
    $operations = [Collections.Generic.List[object]]::new()
    function Add-Command([string]$Id, [string]$Lane, [string]$Executable, [string[]]$Arguments) {
        $operations.Add(@{ id = $Id; stage = $Lane; executable = $Executable; arguments = $Arguments; status = 'not-executed' })
    }
    function Add-Script([string]$Id, [string]$Lane, [string]$Script, [hashtable]$Parameters) {
        $parametersPath = Join-Path $output "$Id.parameters.json"
        Write-QualificationJson -Path $parametersPath -Value $Parameters
        Add-Command $Id $Lane 'pwsh' @('-NoProfile','-NonInteractive','-File',
            (Join-Path $PSScriptRoot 'rendering/qualification/Invoke-QualificationChild.ps1'),
            '-Script',(Join-Path $PSScriptRoot $Script),'-ParametersJson',$parametersPath)
    }
    Add-Command 'restore' 'Build' 'dotnet' @('restore','ForgeLine.sln')
    Add-Script 'references' 'Build' 'Validate-ProjectReferences.ps1' @{}
    Add-Command 'release' 'Build' 'dotnet' @('build','ForgeLine.sln','--configuration','Release','--no-restore')
    Add-Script 'compile-assets' 'Assets' 'Build-RuntimeAssets.ps1' @{
        Mode = 'build'; ForceRebuild = $true; NoBuild = $true
        QualificationOutput = (Join-Path $output 'asset-qualification.json')
        ProgressLog = (Join-Path $output 'asset-compilation.jsonl')
    }
    Add-Script 'asset-contracts' 'Contracts' 'Test-RuntimeAssetPipeline.ps1' @{}
    Add-Command 'solution-tests' 'Contracts' 'dotnet' @('test','--solution','ForgeLine.sln','--configuration','Release','--no-build')
    Add-Command 'headless-smoke' 'Contracts' 'dotnet' @('run','--project','src/ForgeLine.Headless/ForgeLine.Headless.csproj',
        '--configuration','Release','--no-build','--','--ticks','64','--seed','12345','--tick-rate','20',
        '--entities','1000','--diagnostics-output',(Join-Path $output 'headless-smoke.json'))
    Add-Command 'terminal-match' 'Contracts' 'dotnet' @('run','--project','src/ForgeLine.Headless/ForgeLine.Headless.csproj',
        '--configuration','Release','--no-build','--','--scenario','central-divide','--profile','validation',
        '--ticks','80000','--seed','2026','--require-terminal','--diagnostics-output',(Join-Path $output 'terminal-match.json'))
    # Named fixtures exercise authored presentation states, not live input or economic truth.
    $worldScenes = @('DaylightTerrain','IndustrialBase','FormationConvoy','LogisticsCorridor','DamageDestruction',
        'BattlefieldCombat','MaterialFoundation','EffectsBarrage','IndustrialEffects','InfantrySkinning',
        'InfantrySkinningEngineer','MechanicalIdle','MechanicalMoving','MechanicalFiring','MechanicalLoading',
        'MechanicalUnpowered','MechanicalBroken','ResourceDepositVariantsGrass','ResourceDepositVariantsRock',
        'ResourceDepositVariantsMud','ResourceDepositVariantLoad1000','VegetationGrove','WorldPropsGallery',
        'DecalsOverlap','InfrastructureBridgeStates','OperationalBuildingsRepeated','CombatSupportRepeated')
    $uiScenes = @('Operations','OperationsEmpty','OperationsMissing','LogisticsBlocked','QueueBlocked',
        'SelectionUnit','SelectionBuilding','SelectedGroup','RingEdge','RingPending','NotificationsExpanded')
    foreach ($lane in @('Native','Warp')) {
        $common = @{ Capture = $true; Configuration = 'Release'; Warmups = 64; Samples = 512; RepeatCaptures = 8
            Profiles = @('Low','Medium','High','Ultra'); Displays = @('1920x1080','2560x1440','3840x2160','3440x1440') }
        if ($lane -eq 'Warp') { $common.Software = $true }
        $world = $common.Clone()
        $world.Scenes = $worldScenes; $world.Zooms = @('CloseTactical','NormalGameplay','Strategic')
        $world.OutputRoot = Join-Path $output "$lane-world"
        Add-Script "$lane-world" $lane 'Invoke-RtsReferenceQualification.ps1' $world
        $ui = $common.Clone()
        $ui.Scenes = @('BattlefieldCombat'); $ui.Zooms = @('NormalGameplay'); $ui.UiScenarios = $uiScenes
        $ui.UiScales = @(.75,1,2); $ui.OutputRoot = Join-Path $output "$lane-ui"
        Add-Script "$lane-ui" $lane 'Invoke-RtsReferenceQualification.ps1' $ui
    }
    Add-Script 'publish' 'Package' 'Publish-PreAlpha.ps1' @{
        Configuration = 'Release'; VerifyReproducible = $true; RuntimeAssetRoot = 'assets/runtime'
        OutputRoot = (Join-Path $output 'package')
    }
    $manualCases = @(
        @{ id='LIVE-OPS'; action='Select owned production, blocked request and assigned truck; hover/focus Operations rows, navigate, change selection/session and release a captured click.'; expected='Current visible owned IDs only; loaded/requested/delivered labels stay distinct; no release click-through or stale marker.' },
        @{ id='LIVE-CONVEYOR'; action='Select/hover a visible conveyor network, then hide/remove an endpoint and start a new match.'; expected='Current visible owned endpoints only; bounded links/labels; no stale topology or marker.' },
        @{ id='LIVE-INDUSTRIAL-COMBAT'; action='Build powered production and storage with real deliveries; identify and attack them with a normal enemy unit, observe damage/destruction and lost supply, then rebuild and explicitly reconnect a loaded adapter. Destroy an objective Core in a separate match.'; expected='Damage/offline/wreck states match copied facts; no refund or duplicate cargo, no stale Operations or selection identity, normal production resumes, cleared building footprint is traversable, and terminal acknowledgment adds no gameplay tick.' },
        @{ id='LIVE-RADAR-POWER'; action='Without combat destruction, power a normally built radar, disable and restore its generator, then disable its consumer; observe the coverage overlay and previously known versus new enemy contacts across scan cadence.'; expected='Coverage and fresh observations follow completed power allocation and full-demand minimum; known contacts age normally, hidden newcomers remain hidden, restored supply resumes scans, and passive/mobile visual sensing is unchanged.' },
        @{ id='LIVE-ORDERS'; action='Select mixed units, queue/cancel orders, open edge radial menus and overlap HUD panels.'; expected='Order paths remain legible and bounded; panels occlude passive world labels; actions remain usable.' },
        @{ id='DISPLAY'; action='Repeat Low/Medium/High/Ultra at 1080p, 1440p, 4K and ultrawide; UI 75/100/200%; Windows DPI 100/125/150/200%; world scale 50/75/100%.'; expected='Record requested and actual dimensions, effective settings and DPI; sharp native HUD, no clipping; unsupported cells remain blocked.' },
        @{ id='LIFECYCLE'; action='Alt-Tab 10 times; resize 20 times; toggle window/borderless; apply/revert display trial; restart settings; new game/load/exit.'; expected='No device removal, dead input, stale snapshot, leaked surface or black frame; retain before/after diagnostics.' },
        @{ id='IDLE-STRESS'; action='Record 30 minutes idle and 30 minutes moving camera/combat/logistics/resource stress, with initial/warm/final resource counters and frame distributions.'; expected='No binding failures; inspect memory/resource plateau and frame p50/p95/p99; no speed or memory claim from incomplete counters.' },
        @{ id='PACKAGE'; action='Verify ZIP hash/provenance; extract outside checkout, use isolated settings, launch menu/new game/load/restart, close and relaunch without source assets.'; expected='Packaged runtime-only assets resolve; correct source/version/compiler signature; clean first-run settings; startup diagnostics and actual GPU recorded.' }
    )
    $review = @($manualCases | ForEach-Object {
        [ordered]@{ caseId=$_.id; sourceSha=$source; status='not-executed'; action=$_.action; expected=$_.expected
            observed=''; evidence=@(); adapter=''; driver=''; requestedDisplay=''; actualDisplay=''; dpi=''; profile=''; owner=''; issue='' }
    })
    Write-QualificationJson -Path (Join-Path $output 'manual-review.json') -Value $review
    $manifestPath = Join-Path $repository 'assets/runtime/manifest.json'
    $runtime = if (Test-Path -LiteralPath $manifestPath) {
        @{ path=$manifestPath; sha256=Get-QualificationHash $manifestPath; status='observed-cache-only-not-qualified' }
    } else { @{ path=$manifestPath; status='missing' } }
    $report = [ordered]@{ schemaVersion=1; sourceSha=$source; sourceDirty=($dirty.Count -gt 0); requestedStage=$Stage
        createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); pinnedSdk=$sdk; observedSdk=$null
        os=[Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        adapters=@(); runtimeBefore=$runtime; runtimeAfter=$null; operations=$operations; binaries=@(); binariesAfter=@()
        buildEvidence=$BuildEvidenceRoot; assetEvidence=$AssetEvidenceRoot
        captureCoverage=@{ worldScenes=$worldScenes; uiScenarios=$uiScenes; worldCaseCount=($worldScenes.Count*4*3*4); uiCaseCount=($uiScenes.Count*4*4*3) }
        executionStatus='not-executed'; acceptance='open'; nativeHardwareAccepted=$false
        reason='Command success is scoped evidence. Manual review, exact-source CI, package and physical hardware acceptance remain required.' }
    $reportPath = Join-Path $output 'session.json'
    Write-QualificationJson -Path $reportPath -Value $report
    if ($Stage -eq 'Plan') { Write-Output "Plan only: $reportPath"; return }
    try {
        $observedSdk = (& dotnet --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $observedSdk -cne $sdk) { throw "Install SDK $sdk; observed $observedSdk." }
        $report.observedSdk = $observedSdk
        $report.adapters = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,PNPDeviceID)
        if ($Stage -ne 'Build') {
            $build = Read-PriorStage $BuildEvidenceRoot 'Build'
            if ($build.binaries.Count -ne $binaryRoots.Count) { throw 'Incomplete binary provenance.' }
            foreach ($binary in $build.binaries) {
                if ($binary.root -notin $binaryRoots -or
                    (Get-QualificationTreeIdentity (Join-Path $repository $binary.root)).sha256 -cne $binary.sha256) {
                    throw 'Release binaries changed after the recorded build; rebuild this HEAD.'
                }
            }
            $report.binaries = $build.binaries
        }
        if ($Stage -in @('Contracts','Native','Warp','Package')) {
            $assets = Read-PriorStage $AssetEvidenceRoot 'Assets'
            if ($null -eq $assets.runtimeAfter -or
                (Get-QualificationTreeIdentity (Join-Path $repository 'assets/runtime')).sha256 -cne $assets.runtimeAfter.treeSha256) {
                throw 'Runtime payload differs from the recorded asset compilation; rebuild assets.'
            }
        }
        foreach ($operation in @($operations | Where-Object stage -eq $Stage)) {
            $operation.status = 'running'
            $journal = Invoke-QualificationProcess -Executable $operation.executable -Arguments $operation.arguments `
                -WorkingDirectory $repository -JournalRoot (Join-Path $output 'commands') -Id $operation.id `
                -TimeoutSeconds $CommandTimeoutSeconds -MonitorCompetition:($Stage -eq 'Native')
            $operation.journal = "commands/$($operation.id).json"
            $operation.status = if ($journal.exitCode -eq 0) { 'command-succeeded-review-required' } else { 'failed' }
            Assert-QualificationCommand $journal
        }
        $after = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $after -cne $source) { throw 'Source HEAD moved during execution.' }
        $afterDirty = @(& git status --porcelain --untracked-files=normal)
        if ($LASTEXITCODE -ne 0 -or $afterDirty.Count) { throw 'Source changed during execution.' }
        if ($Stage -eq 'Build') {
            $report.binaries = @($binaryRoots | ForEach-Object {
                @{ root=$_; sha256=(Get-QualificationTreeIdentity (Join-Path $repository $_)).sha256 }
            })
        } else {
            foreach ($binary in $report.binaries) {
                $identity = Get-QualificationTreeIdentity (Join-Path $repository $binary.root)
                $report.binariesAfter += @{ root=$binary.root; sha256=$identity.sha256 }
                # Publish intentionally rebuilds with CI/deterministic properties. Its package
                # provenance and reproducibility checks own that output, not the earlier build.
                if ($Stage -ne 'Package' -and $identity.sha256 -cne $binary.sha256) {
                    throw 'Release binaries changed during execution.'
                }
            }
        }
        if ($Stage -eq 'Assets' -or $Stage -in @('Contracts','Native','Warp','Package')) {
            $runtimeIdentity = Get-QualificationTreeIdentity (Join-Path $repository 'assets/runtime')
            if ($Stage -ne 'Assets' -and $runtimeIdentity.sha256 -cne $assets.runtimeAfter.treeSha256) {
                throw 'Runtime payload changed during execution.'
            }
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $report.runtimeAfter = @{ path=$manifestPath; sha256=Get-QualificationHash $manifestPath
                treeSha256=$runtimeIdentity.sha256; compilerVersion=$manifest.compilerVersion; contentSignature=$manifest.contentSignature }
        }
        $report.executionStatus = 'completed-review-required'
    } catch {
        $report.executionStatus = 'failed'
        $report.reason = $_.Exception.Message
        throw
    } finally {
        if ($null -eq $report.runtimeAfter -and (Test-Path -LiteralPath $manifestPath)) {
            $report.runtimeAfter = @{ path=$manifestPath; sha256=Get-QualificationHash $manifestPath }
        }
        # The initial checkpoint already exists. Replace only this attempt's session summary.
        $report | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    }
    Write-Output "Stage complete; acceptance remains open: $reportPath"
} finally { Pop-Location }
