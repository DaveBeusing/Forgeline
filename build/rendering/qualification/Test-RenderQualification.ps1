#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Qualification.Commands.psm1') -Force
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('forgeline-qualification-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$cleanupTestRoot = $true
$failureRecord = $null
$script:passed = 0
function Assert-Case([bool]$Condition, [string]$Reason) {
    if (-not $Condition) { throw $Reason }
    $script:passed++
}
function Assert-Rejected([scriptblock]$Action, [string]$Reason) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-Case $rejected $Reason
}
try {
    $plan = New-QualificationPlan
    Assert-Case (($plan.scenes.Count * $plan.profiles.Count * $plan.zooms.Count * $plan.displays.Count * $plan.variants.Count) -eq 216) 'Matrix coverage changed.'
    Assert-Case ($plan.repetitions -eq 5 -and $plan.warmSamples -eq 512 -and $plan.warmups -eq 64) 'Required sampling defaults changed.'
    Assert-Rejected { New-QualificationPlan -Repetitions 4 } 'Four pairs must be rejected.'
    Assert-Rejected { New-QualificationPlan -WarmSamples 511 } 'Shortened warm sampling must be rejected.'
    Assert-Rejected { New-QualificationPlan -Warmups 63 } 'Shortened warmup must be rejected.'
    $jsonPath = Join-Path $testRoot 'immutable.json'
    Write-QualificationJson -Path $jsonPath -Value @{ status = 'original' }
    $originalHash = Get-QualificationHash $jsonPath
    Assert-Rejected { Write-QualificationJson -Path $jsonPath -Value @{ status = 'replacement' } } 'A prior evidence file must not be replaced.'
    Assert-Case ((Get-QualificationHash $jsonPath) -ceq $originalHash) 'Rejected overwrite modified original bytes.'
    Assert-Case (Test-QualificationPathWithin (Join-Path $testRoot 'child') $testRoot) 'Contained paths were missed.'
    Assert-Case (-not (Test-QualificationPathWithin ($testRoot + '-sibling') $testRoot)) 'Sibling prefix was mistaken for containment.'
    $versionBefore = '<Project><ForgeLineVersionPatch>424</ForgeLineVersionPatch><Nullable>enable</Nullable></Project>'
    $versionAfter = '<Project><ForgeLineVersionPatch>425</ForgeLineVersionPatch><Nullable>enable</Nullable></Project>'
    Assert-QualificationVersionDelta -Before $versionBefore -After $versionAfter
    Assert-Case $true 'The valid single PATCH delta must pass.'
    Assert-Rejected { Assert-QualificationVersionDelta -Before $versionBefore -After $versionBefore } 'Missing PATCH increment was accepted.'
    Assert-Rejected { Assert-QualificationVersionDelta -Before $versionBefore -After ($versionAfter.Replace('enable','disable')) } 'Unrelated settings were hidden in the serial version exception.'
    Assert-Rejected { Assert-QualificationVersionDelta -Before $versionBefore -After ($versionAfter + "`n") } 'Trailing shared-file edits were hidden by normalization.'

    $tree = Join-Path $testRoot 'tree'
    [void][IO.Directory]::CreateDirectory($tree)
    [IO.File]::WriteAllText((Join-Path $tree 'b'), 'second')
    [IO.File]::WriteAllText((Join-Path $tree 'a'), 'first')
    $firstIdentity = Get-QualificationTreeIdentity $tree
    Assert-Case ($firstIdentity.files.Count -eq 2) 'Input inventory omitted files.'
    Assert-Case ((Get-QualificationTreeIdentity $tree).sha256 -ceq $firstIdentity.sha256) 'Stable content produced unstable identity.'
    [IO.File]::WriteAllText((Join-Path $tree 'a'), 'changed')
    Assert-Case ((Get-QualificationTreeIdentity $tree).sha256 -cne $firstIdentity.sha256) 'Changed payload was not detected.'

    $importRoot = Join-Path $testRoot 'external'
    [void][IO.Directory]::CreateDirectory($importRoot)
    Write-QualificationJson -Path (Join-Path $importRoot 'gate.json') -Value @{ status = 'open' }
    $gateHash = Get-QualificationHash (Join-Path $importRoot 'gate.json')
    Write-QualificationJson -Path (Join-Path $importRoot 'index.json') -Value @{
        schemaVersion = 1; files = @(@{ path = 'gate.json'; sha256 = $gateHash })
        validationEvidence = @(@{ kind = 'required-ci'; path = 'gate.json'; sha256 = $gateHash }); supplemental = @() }
    $imported = Copy-QualificationEvidence -Root $importRoot -Destination (Join-Path $testRoot 'copied')
    Assert-Case ($imported.validationEvidence.Count -eq 1 -and (Get-QualificationHash (Join-Path $testRoot 'copied/gate.json')) -ceq $gateHash) 'Imported gate evidence was lost or changed.'
    $booleanIndexRoot = Join-Path $testRoot 'boolean-index'
    [void][IO.Directory]::CreateDirectory($booleanIndexRoot)
    [IO.File]::Copy((Join-Path $importRoot 'gate.json'), (Join-Path $booleanIndexRoot 'gate.json'), $false)
    Write-QualificationJson -Path (Join-Path $booleanIndexRoot 'index.json') -Value @{
        schemaVersion = $true; files = @(@{ path = 'gate.json'; sha256 = $gateHash })
        validationEvidence = @(@{ kind = 'required-ci'; path = 'gate.json'; sha256 = $gateHash }); supplemental = @() }
    Assert-Rejected { Copy-QualificationEvidence -Root $booleanIndexRoot -Destination (Join-Path $testRoot 'boolean-copy') } 'Boolean schemaVersion was coerced into integer 1.'
    Assert-Rejected { Copy-QualificationEvidence -Root $importRoot -Destination (Join-Path $testRoot 'copied') } 'Previously copied evidence was overwritten.'
    [IO.File]::WriteAllText((Join-Path $importRoot 'unlisted.txt'), 'not declared')
    Assert-Rejected { Copy-QualificationEvidence -Root $importRoot -Destination (Join-Path $testRoot 'undeclared') } 'Undeclared files were copied into qualification evidence.'

    $package = Join-Path $testRoot 'package'
    [void][IO.Directory]::CreateDirectory((Join-Path $package 'assets/runtime'))
    [IO.File]::WriteAllText((Join-Path $package 'ForgeLine.Client.exe'), 'test fixture, never executed')
    [IO.File]::WriteAllText((Join-Path $package 'assets/runtime/manifest.json'), '{}')
    Write-QualificationJson -Path (Join-Path $package 'build-provenance.json') -Value @{
        sourceRevision = ('a' * 40); configuration = 'Release'; runtimeIdentifier = 'win-x64'; sourceTreeClean = $true }
    $checksumLines = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
        (Get-QualificationHash $_.FullName) + '  ' + [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/') })
    [IO.File]::WriteAllText((Join-Path $package 'manifest.sha256'), ($checksumLines -join "`n") + "`n")
    $checkedPackage = Assert-QualificationPackage -Root $package -Source ('a' * 40)
    Assert-Case ($checkedPackage.package.files.Count -eq 4) 'A checksum-complete source-less package was rejected.'
    Assert-Rejected { Assert-QualificationPackage -Root $package -Source ('b' * 40) } 'Package source mismatch was accepted.'
    [IO.File]::WriteAllText((Join-Path $package 'assets/runtime/manifest.json'), '{"changed":true}')
    Assert-Rejected { Assert-QualificationPackage -Root $package -Source ('a' * 40) } 'Corrupted packaged runtime payload was accepted.'
    [IO.File]::WriteAllText((Join-Path $package 'authoring.py'), 'forbidden')
    Assert-Rejected { Assert-QualificationPackage -Root $package -Source ('a' * 40) } 'Player package source/compiler dependencies were accepted.'

    $pwsh = (Get-Command pwsh).Source
    $child = Join-Path $testRoot 'argument-child.ps1'
    # Emit known UTF-8 bytes; Console.Write may use the Windows console code page.
    [IO.File]::WriteAllText($child, @'
param([string]$Token)
$bytes = [Text.Encoding]::UTF8.GetBytes($Token)
$stdout = [Console]::OpenStandardOutput()
$stdout.Write($bytes, 0, $bytes.Length)
$stdout.Flush()
exit 7
'@)
    $literal = 'space " quote ` $() ; unicode-ö'
    $command = Invoke-QualificationProcess -Executable $pwsh -Arguments @('-NoProfile','-File',$child,'-Token',$literal) `
        -WorkingDirectory $testRoot -JournalRoot $testRoot -Id 'failure'
    Assert-Case ($command -is [Collections.IDictionary]) 'The process wrapper emitted internal task results alongside its command journal.'
    Assert-Case ($command.exitCode -eq 7 -and $command.nativeExitCode -eq 7) 'The actual native failure exit was lost.'
    $actualBytes = [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $testRoot 'failure.stdout.log')))
    $expectedBytes = [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($literal))
    Assert-Case ($actualBytes -ceq $expectedBytes) "Literal argument bytes changed: expected $expectedBytes; actual $actualBytes."
    Assert-Rejected { Assert-QualificationCommand $command } 'A failed native command was accepted.'
    Assert-Rejected { Invoke-QualificationProcess -Executable $pwsh -Arguments @('-NoProfile','-File',$child) -WorkingDirectory $testRoot -JournalRoot $testRoot -Id 'failure' } 'An existing command journal was reused.'
    $saved = Get-Content -LiteralPath (Join-Path $testRoot 'failure.json') -Raw | ConvertFrom-Json
    Assert-Case ($saved.stdout.sha256 -ceq (Get-QualificationHash (Join-Path $testRoot 'failure.stdout.log'))) 'Command output hash does not bind its actual log.'

    $arrayChild = Join-Path $testRoot 'array-child.ps1'
    [IO.File]::WriteAllText($arrayChild, 'param([string[]]$Names,[switch]$Capture) if (($Names -join ",") -cne "one,two three" -or -not $Capture) { throw "Splat changed" }; exit 0')
    $parametersPath = Join-Path $testRoot 'parameters.json'
    Write-QualificationJson -Path $parametersPath -Value @{ Names = @('one','two three'); Capture = $true }
    $splat = Invoke-QualificationProcess -Executable $pwsh -Arguments @('-NoProfile','-File', (Join-Path $PSScriptRoot 'Invoke-QualificationChild.ps1'),
        '-Script', $arrayChild, '-ParametersJson', $parametersPath) -WorkingDirectory $testRoot -JournalRoot $testRoot -Id 'splat'
    Assert-Case ($splat -is [Collections.IDictionary]) 'A successful process returned more than its command journal.'
    Assert-Case ($splat.exitCode -eq 0) 'The wrapper changed string arrays or switch parameters.'
    [IO.File]::WriteAllText($arrayChild, 'param([string[]]$Names,[switch]$Capture) throw "Deliberate script failure"')
    $thrown = Invoke-QualificationProcess -Executable $pwsh -Arguments @('-NoProfile','-File', (Join-Path $PSScriptRoot 'Invoke-QualificationChild.ps1'),
        '-Script', $arrayChild, '-ParametersJson', $parametersPath) -WorkingDirectory $testRoot -JournalRoot $testRoot -Id 'throw'
    Assert-Case ($thrown -is [Collections.IDictionary]) 'A thrown script failure returned more than its command journal.'
    Assert-Case ($thrown.exitCode -ne 0) 'A thrown PowerShell prerequisite failure returned success.'

    $slow = Join-Path $testRoot 'slow-child.ps1'
    [IO.File]::WriteAllText($slow, 'Start-Sleep -Seconds 20; exit 0')
    $timeout = Invoke-QualificationProcess -Executable $pwsh -Arguments @('-NoProfile','-File',$slow) -WorkingDirectory $testRoot `
        -JournalRoot $testRoot -Id 'timeout' -TimeoutSeconds 1
    Assert-Case ($timeout -is [Collections.IDictionary]) 'A timed-out process returned more than its command journal.'
    Assert-Case ($timeout.timedOut -and $timeout.exitCode -ne 0 -and $null -ne $timeout.nativeExitCode) 'A timed-out native process was accepted or lost its actual exit.'
    Write-Output "PASS: $script:passed qualification orchestration assertions. No physical GPU acceptance is inferred."
} catch {
    $failureRecord = $_
    try {
        $artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../artifacts'))
        [void][IO.Directory]::CreateDirectory($artifactRoot)
        $failureRoot = Join-Path $artifactRoot ('render-qualification-contracts-failure-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $failureRoot -ErrorAction Stop | Out-Null
        $snapshotPath = Join-Path $failureRoot 'synthetic-fixtures-and-journals'
        $originalIdentity = Get-QualificationTreeIdentity $testRoot
        Copy-Item -LiteralPath $testRoot -Destination $snapshotPath -Recurse -ErrorAction Stop
        $snapshotIdentity = Get-QualificationTreeIdentity $snapshotPath
        if ($snapshotIdentity.sha256 -cne $originalIdentity.sha256) { throw 'Synthetic failure evidence changed during retention.' }
        Write-QualificationJson -Path (Join-Path $failureRoot 'synthetic-failure.json') -Value @{
            schemaVersion = 1; artifactKind = 'synthetic-qualification-contract-test-failure'
            observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); message = $failureRecord.Exception.Message
            scriptStackTrace = $failureRecord.ScriptStackTrace; position = $failureRecord.InvocationInfo.PositionMessage
            note = 'Synthetic test fixtures and journals only. No product measurements or physical GPU acceptance.'
            snapshot = @{ path = 'synthetic-fixtures-and-journals'; sha256 = $snapshotIdentity.sha256; files = $snapshotIdentity.files }
        }
        Write-Host "Preserved synthetic qualification failure evidence: $failureRoot"
    } catch {
        $cleanupTestRoot = $false
        Write-Warning "Could not retain synthetic failure artifacts: $($_.Exception.Message). Original journals remain at $testRoot."
    }
    throw $failureRecord
} finally {
    if ($cleanupTestRoot) {
        try { Remove-Item -LiteralPath $testRoot -Recurse -Force }
        catch {
            if ($null -eq $failureRecord) { throw }
            Write-Warning "Synthetic test cleanup failed: $($_.Exception.Message). The original test failure is retained."
        }
    }
}
