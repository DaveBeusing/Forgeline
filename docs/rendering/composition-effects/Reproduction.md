# Composition and effect qualification

These instructions describe checks to run, not completed acceptance. Product/tooling
source: `8331e384d1fc7b89c9b40ade7590ce9a5ed6f99a`; original reference:
`cdf97fb9a8ef277cf75d451476a21128d398007c`. Record the actual rebuilt source SHA for
every subsequent run. Use Windows x64, PowerShell 7, the SDK in `global.json`, and
complete source/runtime assets. Run builds and asset compilation before timing;
only one hardware qualification process may use the reference GPU at a time.

## Build and functional checks

Run from a clean, committed checkout. Keep outputs and runtime caches separate
between checkouts. Every native command must have its exit code checked.

```powershell
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
dotnet restore ForgeLine.sln
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build ForgeLine.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
pwsh ./build/Validate-ProjectReferences.ps1
if ($LASTEXITCODE -ne 0) { throw 'Reference validation failed.' }
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
if ($LASTEXITCODE -ne 0) { throw 'Runtime asset qualification failed.' }

$presentation = 'tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj'
$graphics = 'tests/ForgeLine.Graphics.Tests/ForgeLine.Graphics.Tests.csproj'
dotnet test --project $presentation -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.SimpleInstanceRendererTests ForgeLine.Presentation.Tests.PresentationExtractionTests ForgeLine.Presentation.Tests.RenderSnapshotIndexTests
if ($LASTEXITCODE -ne 0) { throw 'Existing presentation contracts failed.' }
dotnet test --project $presentation -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.EffectSubmission.ParticleDepthOrderingTests ForgeLine.Presentation.Tests.EffectSubmission.ParticleEffectSubmissionTests
if ($LASTEXITCODE -ne 0) { throw 'Portable effect contracts failed.' }
dotnet test --project $graphics -c Release --no-build -- --filter-class ForgeLine.Graphics.Tests.SceneComposition.CompositionNumericsNativeTests ForgeLine.Graphics.Tests.SceneComposition.CompositionShaderNativeTests ForgeLine.Graphics.Tests.SceneComposition.CompositionProductNativeTests --parallel none
if ($LASTEXITCODE -ne 0) { throw 'Native composition contracts failed.' }
dotnet test --project $presentation -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ParticleVfxRendererTests
if ($LASTEXITCODE -ne 0) { throw 'Existing particle contracts failed.' }
foreach ($project in @($graphics, $presentation, 'tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj')) {
    dotnet test --project $project -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Affected suite failed: $project" }
}
dotnet test --solution ForgeLine.sln -c Release --no-build --max-parallel-test-modules 1
if ($LASTEXITCODE -ne 0) { throw 'Complete solution validation failed.' }
```

The 21 owned native cases cover DXC compilation and WARP shader/readback assertions,
including sentinels, depth discontinuities and native overlays. Virtual 4K coordinate probes
are numerical coverage; actual 4K product capture remains a separate requirement.
Keep existing failures distinct from this change; an earlier failure does not prove
the remaining skipped gates passed. Existing Windows CI and full suites stay required.

## Portable CPU ordering comparison

This opt-in fixture compares reference/candidate/candidate/reference ordering of
identical complete particle payloads. It measures only sorting at 512/2,048/4,096
particles, not complete preparation, admission, upload, GPU work or frame rate.
A fixed mixed process warmup runs for at least three seconds and 256 calls per
implementation per fixture, followed by 64 warmups before each 256-sample cohort.
Current-thread and all-thread allocation windows are reported separately; the latter
may include test-host work. Runtime tiering is retained; all sample drift stays visible.
Use a fresh output name and the SHA of the built code.

```powershell
$source = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Source identity unavailable.' }
$previousOutput = $env:FORGELINE_PARTICLE_CPU_QUALIFICATION_OUTPUT
$previousSource = $env:FORGELINE_PARTICLE_CPU_SOURCE_SHA
try {
    $name = "particle-ordering-$([guid]::NewGuid().ToString('N')).json"
    $env:FORGELINE_PARTICLE_CPU_QUALIFICATION_OUTPUT = [IO.Path]::GetFullPath("artifacts/composition-effects/$name")
    $env:FORGELINE_PARTICLE_CPU_SOURCE_SHA = $source
    dotnet test --project $presentation -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.EffectSubmission.ParticleOrderingQualificationTests --parallel none
    if ($LASTEXITCODE -ne 0) { throw 'CPU ordering qualification failed.' }
} finally {
    $env:FORGELINE_PARTICLE_CPU_QUALIFICATION_OUTPUT = $previousOutput
    $env:FORGELINE_PARTICLE_CPU_SOURCE_SHA = $previousSource
}
```

## Static shader compiler evidence

The wrapper exports the baseline/default/shared-linear matrix: 96 pixel variants
and two vertex shaders. It retains runtime AA plus specialized Disabled/Low/High
fixtures, native/scaled rendering, atmosphere and haze preprocessor combinations.
Haze with atmosphere disabled is compiler coverage, not a reachable product mode.

```powershell
$dxc = (Get-Command dxc -ErrorAction Stop).Source
$shaderOutput = "artifacts/composition-effects/shaders-$([guid]::NewGuid().ToString('N'))"
pwsh ./build/rendering/composition-effects/Invoke-CompositionShaderQualification.ps1 `
    -BaseRef cdf97fb9a8ef277cf75d451476a21128d398007c -DxcPath $dxc -OutputRoot $shaderOutput
if ($LASTEXITCODE -ne 0) { throw 'Shader compilation failed; retain manifest and logs.' }
```

Retain the compiler identity/hash, HLSL, DXIL, disassembly, manifest and exit codes.
LLVM IR counts and texture-load call sites are not executed samples, device ISA,
register pressure, bandwidth, overdraw or GPU time. This wrapper changes no product
source. `COMPOSITION_SHARED_LINEAR` defaults to `0`; testing candidate `1` in the
real client requires a reviewed one-line change in a separate non-master checkout,
its own commit, and a complete rebuild. Keep default and candidate evidence distinct.

## Source-less native capture and paired hardware comparison

Prepare separate clean reference, default and shared-linear checkouts. Publish each
matching source before any timed run, then copy the complete publish directory to
a fresh directory outside the checkout. Keep its provenance and runtime manifest.

```powershell
$source = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Source identity unavailable.' }
$packageRoot = "artifacts/composition-effects/package-$source"
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -OutputRoot $packageRoot -RuntimeAssetRoot assets/runtime -VerifyReproducible
if ($LASTEXITCODE -ne 0) { throw 'Source-less packaging failed.' }
$standalone = Join-Path ([IO.Path]::GetTempPath()) "forgeline-composition-$source-$([guid]::NewGuid().ToString('N'))"
Copy-Item -LiteralPath "$packageRoot/win-x64/publish" -Destination $standalone -Recurse
$client = Join-Path $standalone 'ForgeLine.Client.exe'
$runtime = Join-Path $standalone 'assets/runtime'
if (Test-Path (Join-Path $standalone 'assets/source')) { throw 'Source assets must not be packaged.' }
```

Run the capture script from the checkout matching that executable. Use a fresh
output directory per cohort/configuration. Compare reference/default and
default/shared-linear separately, in A-B-B-A order, preceded by an A-A control.
Retain both orders and failed cohorts; do not pick only the faster pair.

| Coverage | Choices to record and qualify |
| --- | --- |
| World scale / AA | `1`, `.75`, `.5`; `Disabled`, `Low`, `High` |
| Atmosphere | `Disabled`, `Distance`, `Height`; haze off/on only with atmosphere enabled |
| Output / views | `1920x1080`, `3840x2160`; CloseTactical, NormalGameplay, Strategic |
| World / motion | IndustrialBase, DamageDestruction, BattlefieldCombat; Fixed, Sweep, Traverse |
| UI | Fixed BattlefieldCombat/NormalGameplay; selection, tooltips, notifications and onboarding |

The following is one runnable case; repeat with the other matrix choices, keeping
every paired argument identical. Record incomplete coverage as open.

```powershell
$captureRoot = "artifacts/composition-effects/native-$([guid]::NewGuid().ToString('N'))"
& ./build/Invoke-RtsReferenceQualification.ps1 -Capture `
    -Scenes IndustrialBase,DamageDestruction,BattlefieldCombat -Profiles High `
    -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080,3840x2160 `
    -ResolutionScale .75 -Antialiasing High -AtmosphereQuality Height -LocalizedHaze `
    -ParticleQuality High -RunProfile Warm -CameraPath Sweep -Warmups 64 -Samples 512 -RepeatCaptures 2 `
    -ClientPath $client -RuntimeAssetRoot $runtime -OutputRoot $captureRoot
if ($LASTEXITCODE -ne 0) { throw 'Native capture failed; retain failure.json and all output.' }
```

For HUD capture use `-Scenes BattlefieldCombat -Zooms NormalGameplay -CameraPath Fixed`
and `-UiScenarios SelectionUnit,GameplayTooltip,NotificationsExpanded,Onboarding`.
Keep the same scale/AA/atmosphere/display matrix. Named UI states reject moving
camera paths and other scenes/zooms. Add `-Software` only for functional WARP checks;
those runs cannot satisfy hardware-performance acceptance.

Set `$baselineRoot` and `$currentRoot` to the complete, distinct, matched cohort
directories; use fresh report paths. Preserve the existing 15% timing threshold
and zero-tolerance pixel comparison; neither grants automatic visual approval.

```powershell
pwsh ./build/Compare-ReferencePerformance.ps1 -BaselineRoot $baselineRoot -CurrentRoot $currentRoot -ReportPath "$currentRoot/performance-review.json" -GatePerformance
if ($LASTEXITCODE -ne 0) { throw 'Hardware comparison remains unqualified; retain the report.' }
pwsh ./build/Compare-VisualReferenceEvidence.ps1 -BaselineRoot $baselineRoot -CurrentRoot $currentRoot -ReportPath "$currentRoot/pixel-review.json" -ComparePixels
if ($LASTEXITCODE -ne 0) { throw 'Image comparison failed; retain the report.' }
```

Retain source/build/compiler hashes, runtime-asset fingerprint, exact configuration,
hardware/driver, original images/motion sequences, commands, exit codes and raw
per-frame records. GPU samples/readbacks must have completed submission fences.
Keep CPU recording, Present/frame-reuse waits, GPU frame/pass distributions,
upload/overdraw/binding counters, allocations and memory distinct. Pass intervals
may overlap: never add them as a whole-frame total. Missing metrics are `null` or
unavailable, not zero; unavailable overdraw/upload counters require separate capture.
Check sustained resource/descriptor/memory plateaus and no faults, not a single frame.
Have a human review thin geometry, chromatic edges, fog boundaries, transparent
ordering, soft depth intersections, HUD text and full-resolution selection. Report
priority/admission/visibility changes separately from identical-payload optimizations.
Keep the shared-linear default disabled until paired hardware and visual acceptance
are complete. Any remaining required gate means implemented, not yet accepted.
