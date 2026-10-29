# Immutable hardware reference baseline

Baseline source: `63d63a3d8d2977dfff37a851d30ee4e3428b7843`, tree `4aad567f2321793d68b01f3fe9964fbc6f66efea`, product 0.1.529. Shared-history compatibility source: `87a14786cb90bff39b33b7edb6069c6dba8b6efc`. Both native runs used a clean SHA-verified sparse checkout with all solution sources and committed embedded/package resources. Unmaterialized authoring/evidence paths remain explicitly sparse; the original published commit/tree identities were verified, not substituted with a local synthetic revision.

The unchanged `Invoke-ReferencePerformance.ps1` / `Invoke-RtsReferenceQualification.ps1` collected native data. [Portable distributions and complete comparison results](evidence/baseline.json) identify every binary, source, image, profile, payload and raw manifest. [reference-raw.zip](evidence/reference-raw.zip) losslessly preserves all nine complete native manifests, CPU/fence chronology and comparison JSON; its SHA-256 and each original manifest SHA-256 are in the summary. Full PNGs/requests/logs remain in the corresponding local `artifacts/foundation/` cohorts and are reproducible below. Retain them read-only; no baseline is automatically adopted as an accepted golden.

## Hardware and payload

Windows 10.0.26300 x64, .NET 10.0.13, pinned SDK 10.0.402, Release. Intel Core Ultra 9 285HX / 24 logical processors, 68137205760 bytes physical RAM. Physical NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, 25309478912 bytes dedicated VRAM, driver 32.0.15.9653; integrated Intel driver 32.0.101.8724. Software fallback, VSync, debug layer, static-upload fallback and DRED were off. The reference runner's display/system power lease was active; ordinary background/thermal activity was not traced and no PIX capture is claimed. UMA/cache-coherent-UMA queries are unavailable in the current public diagnostics.

The same integrity-qualified AssetCompiler 1.13 catalog contains 2055 assets / 94884440 runtime payload bytes. Catalog content signature: `135b44877d4882ac539073738a43af7b4794ff35819cc2c1f55c353771743020`. Existing reference whole-directory fingerprint: `55827DADD0271F6F74F0FB66E2B2774C924C38FC106B6EA351C43BF9A694EBBA`. These are distinct identity algorithms, not interchangeable hashes. All nine native manifests report the same directory fingerprint. Compiled payloads are explicitly supplied; authoring tools are not running during timing.

## Recorded protocols

Every baseline protocol starts two independent client processes at Medium / 1920x1080, two exact readbacks per case and the existing 120 m / 1000 m bookmarks. No timing defaults, shaders, thresholds or old reference fixtures were edited for the baseline.

| Cohort | Existing workload | Discarded warmup / measured frames | Observed statistics above 15% | Incomparable cases |
| --- | --- | --- | --- | --- |
| baseline-warm-sweep | IndustrialBase + BattlefieldCombat, Sweep v1 | 64 / 256 | 0 | 0 |
| baseline-warm-fixed | Same scenes, Fixed | 64 / 256 | 1 | 0 |
| baseline-renderer-cold | Same scenes, RendererCold / Fixed | 0 / 64 (requested warmups recorded as 64) | 2 | 0 |
| baseline-vehicles | Existing CombatSupportRepeated production 1,000 vehicles, Sweep v1 | 64 / 128 | 0 | 0 |
| candidate-default | Same four Sweep cases on the shared-history source, predecessor unset | 64 / 256 | 3 | 0 |

All matched PNG pairs are exact file matches, including all four old-default cases after the history prerequisite. No fallback/submission/image failure is converted to success. Comparisons were advisory (`gateRequested=false`); the recorded threshold exceedances would fail an explicit performance gate. They are not waived or attributed to one cause. Current-only history preparation is outside timed RenderFrame work, but one cohort is insufficient to establish absence of a CPU regression. Human art/physical-monitor approval, sustained live-match FPS and performance acceptance remain open.

Selected unchanged Sweep run-2 timings in milliseconds:

| Case | CPU mean / p95 / p99 | GPU mean / p95 / p99 | Distinct GPU samples |
| --- | --- | --- | --- |
| IndustrialBase-1920x1080-Medium-CloseTactical-Sweep | 11.303 / 15.156 / 16.153 | 8.637 / 12.010 / 12.515 | 249 |
| IndustrialBase-1920x1080-Medium-Strategic-Sweep | 10.829 / 15.114 / 15.793 | 8.670 / 11.215 / 11.955 | 254 |
| BattlefieldCombat-1920x1080-Medium-CloseTactical-Sweep | 11.094 / 17.902 / 18.758 | 9.775 / 16.686 / 17.064 | 253 |
| BattlefieldCombat-1920x1080-Medium-Strategic-Sweep | 11.005 / 16.312 / 17.133 | 9.402 / 12.219 / 13.438 | 254 |

CPU frames bracket RenderFrame, including recording/submission/Present/reuse waits. Prepare, startup, renderer setup, readback, hashing, image encoding and collector work are separate boundaries. Actual timestamp frequency, completed fence IDs, missing/pass timings and valid sample counts are retained. Nested pass scopes must not be summed; latest-completion diagnostics may skip intermediate fences. Last-observed submission/resource values in the portable summary are not labelled as peaks; complete chronological values are in the raw archive. DXGI usage, logical payload, skin capacity, staging and traffic overlap and are not a single VRAM total.

## Reproduce without overwriting evidence

From a clean committed checkout, finish restore/Release build with that revision and compile/seal the repository runtime assets first. Set `RuntimeAssetRoot` explicitly to that immutable catalog, keep diagnostics/fallback overrides unset, and hold hardware/settings/power/background conditions fixed. Use new output directories. Example paths are ignored artifacts, so requests do not dirty the source checkout.

```powershell
$runtime = (Resolve-Path assets/runtime).Path
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Profiles Medium -Displays 1920x1080 -Warmups 64 -Samples 256 -CameraPath Sweep -OutputRoot artifacts/foundation/baseline-warm-sweep
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Profiles Medium -Displays 1920x1080 -Warmups 64 -Samples 256 -CameraPath Fixed -OutputRoot artifacts/foundation/baseline-warm-fixed
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Profiles Medium -Displays 1920x1080 -Warmups 64 -Samples 64 -RunProfile RendererCold -CameraPath Fixed -OutputRoot artifacts/foundation/baseline-renderer-cold
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Profiles Medium -Displays 1920x1080 -Warmups 64 -Samples 128 -Preset Vehicles1000 -CameraPath Sweep -OutputRoot artifacts/foundation/baseline-vehicles
./build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot artifacts/foundation/baseline-warm-sweep/run-2 -BaselineRoot artifacts/foundation/baseline-warm-sweep/run-1 -ReportPath artifacts/foundation/baseline-warm-sweep/pixels.json
```

The candidate compatibility run uses the existing underlying capture command with `-Scenes IndustrialBase,BattlefieldCombat -Zooms CloseTactical,Strategic -CameraPath Sweep -Warmups 64 -Samples 256 -RepeatCaptures 2`, the same runtime root and a fresh output directory, then the existing image/performance comparators against baseline run-1. Check every native exit code. A changed source must be committed/rebuilt and identified as a different candidate; never replace baseline source/build identities with the final PR/version identity.

The proposed 1k/10k/100k synthetic matrix in [Protocol.json](Protocol.json) is a future implementation contract. It is not part of these existing production-asset measurements. No first-snapshot-only or CPU/null-context result is presented as prepared-interpolation or GPU-scaling acceptance.