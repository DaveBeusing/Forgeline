param([string]$RuntimeRoot = 'assets/runtime')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository ('artifacts/ui-skin-package-contract-' + [Guid]::NewGuid().ToString('N'))
$runtime = Join-Path $output 'assets/runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$original = Get-Content -LiteralPath (Join-Path $RuntimeRoot 'manifest.json') -Raw
$manifest = $original | ConvertFrom-Json
$skin = @($manifest.assets | Where-Object { $_.id.StartsWith('ui.skin.directorate.', [StringComparison]::Ordinal) -or $_.id.StartsWith('ui.artwork.directorate.', [StringComparison]::Ordinal) })
foreach ($record in $skin) {
    $target = Join-Path $runtime $record.runtimePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $RuntimeRoot $record.runtimePath) -Destination $target
}
$manifestPath = Join-Path $runtime 'manifest.json'
Set-Content -LiteralPath $manifestPath -Value $original -Encoding utf8NoBOM
$results = [Collections.Generic.List[object]]::new()
function Test-Case([string]$Name, [bool]$ShouldPass) {
    $passed = $true
    $detail = ''
    try { & "$PSScriptRoot/Validate-UiSkinPackage.ps1" -PublishRoot $output | Out-Null }
    catch { $passed = $false; $detail = $_.Exception.Message }
    if ($passed -ne $ShouldPass) { throw "Unexpected package contract outcome: $Name; $detail" }
    $results.Add(@{ name = $Name; passed = $true; expectedValid = $ShouldPass; diagnostic = $detail })
    Set-Content -LiteralPath $manifestPath -Value $original -Encoding utf8NoBOM
}
Test-Case 'valid_skin_snapshot' $true
$mutated = $original | ConvertFrom-Json
$mutated.assets = @($mutated.assets | Where-Object id -CNE $skin[0].id)
$mutated | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Test-Case 'missing_skin_record' $false
$mutated = $original | ConvertFrom-Json
($mutated.assets | Where-Object id -CEQ $skin[1].id).id = $skin[0].id
$mutated | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Test-Case 'duplicate_skin_id' $false
$mutated = $original | ConvertFrom-Json
$mutated.assets[0].sourcePath = 'reference/ui/directorate/ui_visual_language.png'
$mutated | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Test-Case 'reference_board_registered' $false
$unexpected = Join-Path $output 'unexpected-source.png'
[IO.File]::WriteAllBytes($unexpected, [byte[]]@(1))
Test-Case 'authoring_file_packaged' $false
Remove-Item -LiteralPath $unexpected
$payload = Join-Path $runtime $skin[0].runtimePath
$bytes = [IO.File]::ReadAllBytes($payload)
$changed = [byte[]]$bytes.Clone()
$changed[0] = $changed[0] -bxor 1
[IO.File]::WriteAllBytes($payload, $changed)
Test-Case 'corrupt_skin_payload' $false
[IO.File]::WriteAllBytes($payload, $bytes)
$mutated = $original | ConvertFrom-Json
($mutated.assets | Where-Object id -CEQ $skin[0].id).runtimePath = '../escaped.flasset'
$mutated | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Test-Case 'escaping_runtime_path' $false
$mutated = $original | ConvertFrom-Json
$extra = $skin[0] | ConvertTo-Json -Depth 30 | ConvertFrom-Json
$extra.id = 'ui.artwork.directorate.command.patrol.p32'
$mutated.assets = @($mutated.assets) + @($extra)
$mutated | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Test-Case 'unavailable_role_registered' $false
$unexpected = Join-Path $output 'ForgeLine.UiArtwork.dll'
[IO.File]::WriteAllBytes($unexpected, [byte[]]@(1))
Test-Case 'authoring_tool_packaged' $false
Remove-Item -LiteralPath $unexpected
@{ caseCount = $results.Count; results = $results; fixtureRoot = $output } |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $repository 'artifacts/ui-skin-package-contract.json') -Encoding utf8NoBOM
Write-Output "$($results.Count) skin package integrity and source-exclusion contracts passed."
