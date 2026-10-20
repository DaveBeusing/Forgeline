<#
.SYNOPSIS
Compiles baseline, default and shared-linear composite shader variants without editing product sources.
.DESCRIPTION
Exports static DXIL compiler evidence. The candidate define applies to extracted HLSL only.
Native captures must use independently committed, rebuilt source states in isolated worktrees.
The production default remains disabled pending native image and paired hardware acceptance.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseRef,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$DxcPath = 'dxc',
    [string]$PythonExecutable = 'python',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
$compilerScript = Join-Path $PSScriptRoot 'Compile-SceneComposite.py'
$arguments = @($compilerScript, '--repository-root', $repository, '--base-ref', $BaseRef,
    '--output', $output, '--dxc', $DxcPath)
& $PythonExecutable @arguments
if ($LASTEXITCODE -ne 0) { throw "Composite compilation failed with exit code $LASTEXITCODE; inspect $output/manifest.json." }

Write-Output @'
No product source or default marker was changed. The shared-linear compile result is not an enabled native build.

Native source preparation:
  Use separate non-master worktrees for the verified baseline, default implementation and candidate.
  Keep the existing source-only optimization marker at zero in the default implementation.
  Any native candidate opt-in must be an explicit reviewed one-line change in its isolated worktree,
  followed by its own commit and rebuild. Do not relabel default binaries as candidate binaries.
  Record full source SHA, exact shader/source hashes, runtime fingerprint, compiler and build identities.

Existing native entry points (run separately on Windows x64 after source-less builds are prepared):
  build/Invoke-RtsReferenceQualification.ps1 -Capture
    -Antialiasing Disabled|Low|High -AtmosphereQuality Disabled|Distance|Height
    -ResolutionScale 1|0.75 -LocalizedHaze (only when atmosphere is enabled)
    -Scenes IndustrialBase,DamageDestruction,BattlefieldCombat
    -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080,3840x2160
    -RunProfile Warm -CameraPath Fixed|Sweep|Traverse -Warmups 64 -Samples 512 -RepeatCaptures 2
    -ClientPath <matching-published-client> -RuntimeAssetRoot <matching-runtime-assets>
    -OutputRoot <fresh-evidence-directory>
  build/Compare-ReferencePerformance.ps1 -CurrentRoot <current> -BaselineRoot <baseline>
    -ReportPath <fresh-comparison-file>
  build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot <current> -BaselineRoot <baseline>
    -ComparePixels -ReportPath <fresh-image-comparison-file>

The values separated by | are matrix choices, not literal command syntax. Run the same choices in paired
baseline/current cohorts, with one GPU qualification process at a time. Preserve raw pass times and
upload/overdraw counters. Use the existing human image-review workflow; do not accept goldens automatically.
Record unavailable GPU values as null. WARP/Null Graphics and static DXIL counts cannot satisfy hardware gates.
'@
