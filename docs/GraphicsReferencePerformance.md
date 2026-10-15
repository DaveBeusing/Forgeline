# Graphics reference scene and performance qualification

The existing native Central Divide reference runner now supports paired performance runs without adding a gameplay preset or changing normal startup. The representative preset reuses IndustrialBase and BattlefieldCombat: the approved thirteen-building roster, vehicles/infantry, canonical terrain and vegetation, lighting, atmosphere, selection/HUD, visible combat effects and filtered hidden enemies. Vehicles1000 reuses CombatSupportRepeated's 1,000 extracted production vehicles. Camera culling can reduce the visible root population; mechanical parts can expand submitted object-instance counts above the 1,000 root vehicles.

## Setup and invocation

Use Windows x64, the pinned SDK, a clean committed checkout and a new output directory. Finish all builds, asset compilation and other GPU tests before timing. Keep power/thermal conditions, background load, driver, resolution and settings stable. Runtime content is compiled explicitly; the benchmark does not require hidden authoring files at runtime.

```powershell
dotnet build ForgeLine.sln --configuration Release
pwsh ./build/Build-RuntimeAssets.ps1
pwsh ./build/Invoke-ReferencePerformance.ps1 -Samples 512 -OutputRoot artifacts/reference-warm
pwsh ./build/Invoke-ReferencePerformance.ps1 -RunProfile RendererCold -CameraPath Fixed -Samples 128 -OutputRoot artifacts/reference-cold
pwsh ./build/Invoke-ReferencePerformance.ps1 -Preset Vehicles1000 -Samples 512 -OutputRoot artifacts/reference-vehicles
```

Each invocation starts two independent client processes and writes run-1, run-2 and variance.json. The default matrix is Medium/1920x1080 at CloseTactical (120m) and Strategic (1000m). Profiles and Displays accept the existing supported native matrix. Use the underlying runner for the normal 420m bookmark or a specific fixture:

```powershell
pwsh ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes IndustrialBase -Profiles High -Zooms NormalGameplay -Displays 2560x1440 -CameraPath Sweep -Warmups 64 -Samples 512 -OutputRoot artifacts/reference-normal
```

Warm runs discard 64 frames by default. RendererCold discards zero: per-case renderer construction/upload cost is recorded separately, followed by the first rendered frames. Process initialization, fixture creation, catalog loading, hashing and device construction also have separate startup time/allocation fields. The process/device/catalog and OS/driver caches may already be warm in later cases. RendererCold is not an OS cache flush or a claim of cold disk/driver behavior. Requested warmups remain recorded, but run.warmupFrames is zero in this profile.

Sweep version 1 uses sample index, never wall time: phase = 2*pi*index/samples, target offset = (20*sin(phase),0,10*(1-cos(phase))) metres, yaw offset = .15*sin(phase) radians. Pitch, camera distance and target height remain fixed. The sample count therefore defines path sampling density and must match across runs. Fixed preserves the initial pose. After timing, the initial pose is restored and history settles for three frames before fixed-state image readback. Existing seed 2026, completed extraction tick, fixture hash, frozen mechanical time and particle age remain explicit. This measures a moving camera over immutable presentation states, not a natural animated match or simulation CPU time.

## Measurement contract

CPU milliseconds bracket RenderFrame, including recording/submission, Present and frame-resource reuse waits. World/overlay/composite/shadow recording times are separately sampled where available. Current-thread managed allocation bytes bracket that same call; diagnostics, collector storage, PNG encoding and readback are outside it. These allocations exclude workers and native heaps. FPS is the reciprocal of this bracket, not sustained end-to-end application throughput. Diagnostic/collector overhead between frames can affect pacing and must be held constant across cohorts.

GPU timestamps remain Graphics-owned. Diagnostics expose the actual direct-queue frequency; duration is (end-start)*1000/frequency. Only fence-completed readbacks qualify. The warmup boundary is drained before recording, duplicate/older fences are excluded, and the final completed sample is collected after idle. The diagnostics API exposes the latest completion, so intermediate completions can be skipped; raw fence IDs and actual sample counts remain visible. Missing/disabled passes and missing GPU support produce zero samples and null timings, never a zero-cost estimate. World, overlays, composite, shadows, particles and atmosphere have separate distributions where available. Passes can overlap and must not be summed.

Every capture retains raw cpuFrames and gpuFrames, mean/p50/p95/p99 distributions, allocation distributions, per-frame object/terrain/particle submissions, texture upload/release counters, logical resident texture bytes, live/retired resource counts, submission faults and DXGI memory observations. Correlate upload/counter/residency changes with individual frame spikes; no streaming workload is invented for the current fully loaded fixed scenes. Live resource counts are observations, not cumulative buffer allocation accounting. DXGI process usage/budgets, declared dedicated adapter capacity and logical payload bytes are distinct quantities.

Percentiles use sorted observations: p50 is the lower median; p95/p99 use nearest rank ceil(n*p)-1, preserving tail spikes in small windows. Short cold windows are sensitive to first-use JIT/driver work; more than two runs are needed to characterize their variance. Manifests disclose source/build identity, Release/Debug configuration, OS/runtime/architecture/CPU, adapter/feature level/driver/debug layer, resolution, actual settings/fallbacks and runtime payload fingerprint (asset content identity).

## Comparing evidence

```powershell
pwsh ./build/Compare-ReferencePerformance.ps1 -CurrentRoot artifacts/new/run-2 -BaselineRoot artifacts/accepted/run-2
pwsh ./build/Compare-ReferencePerformance.ps1 -CurrentRoot artifacts/new/run-2 -BaselineRoot artifacts/accepted/run-2 -GatePerformance -MaximumRegressionPercent 15 -ReportPath artifacts/new/performance-gate.json
pwsh ./build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot artifacts/new/run-2 -BaselineRoot artifacts/accepted/run-2 -GateImages -ApprovalPath artifacts/accepted/run-2.review.json -ReportPath artifacts/new/pixel-gate.json
```

Performance comparison requires complete structurally qualified evidence and matched case keys, hardware/environment, driver, camera/path version and density, warmup policy, resolution, quality settings, lighting, DPI and UI scale. Software is functional evidence only. Missing GPU timings, mismatched matrices/settings/hardware and zero denominators cannot satisfy an explicit performance gate. Changed asset payloads are disclosed. Default comparisons are advisory; -GatePerformance returns failure for unavailable/unqualified cases or mean/p50/p95/p99 increases above the declared threshold. A single paired delta is variance, not proof of a causal regression or improvement. Reproduce on additional matched cohorts before attributing changes.

Image gating defaults to exact native pixel equality. The constrained decoder accepts only the capture encoder's unfiltered RGBA8 PNGs, checks expected dimensions and bounded decompressed size, and reports maximum/mean channel deltas and changed pixel fraction. -ChannelTolerance permits 0..8 values; -MaximumChangedPixelFraction permits 0..0.05. Both default to zero. Choose and record a reviewed tolerance for a specific baseline; never increase it merely to hide a failure. Fixture/device/driver/camera/profile/lighting/DPI differences require human review and fail an explicit image gate. Numerical tolerance success does not certify silhouettes, shadow attachment, fog authorization or art quality. Existing within-case repeated readbacks remain exact regardless of comparison tolerance.

Baselines are read-only, reports refuse overwrite, and no baseline update is automatic. Keep the prior cohort, review full-resolution images using the [visual rubric](VisualReferenceEvidence.md#review-rubric), record reviewer observations separately, and accept a newly named baseline only after image and performance evidence is reviewed. Older manifests without the extended run/environment contract must be recaptured for performance gating. Shader/device/submission/resource failures produce nonzero exit and failure evidence rather than a partial qualifying report.

## Packaged runtime and CI

```powershell
pwsh ./build/Publish-PreAlpha.ps1 -VerifyReproducible -RuntimeAssetRoot assets/runtime
pwsh ./build/Invoke-ReferencePerformance.ps1 -ClientPath C:/qualification/ForgeLine.Client.exe -RuntimeAssetRoot C:/qualification/assets/runtime -Samples 256 -OutputRoot artifacts/reference-installed
```

The packaged client must match the checkout's full commit. RuntimeAssetRoot is authoritative; select the installed package's adjacent cache to demonstrate source/compiler independence. Relative and absolute output roots are supported. Source cleanliness and build identity checks remain mandatory.

CI runs Test-ReferencePerformance.ps1 and collector contracts without GPU hardware; synthetic contract data is never renderer evidence. The existing WARP lane remains a small functional smoke at 640x360. Native paired performance runs execute only through the manually opted-in scalability job on the self-hosted Windows/X64/forgeline-performance runner and upload their complete artifacts. Hosted or skipped lanes do not claim hardware qualification. Baselines and performance thresholds are not automatically installed on that runner.

## Recorded baseline

Capture source: c43f3c95a8182e47d273b8c8e15b76e1b24e01bd, product 0.1.355-dev, SDK 10.0.401. Windows 10.0.26300/X64, Intel64 Family 6 Model 198 Stepping 2, 24 logical processors; NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, FL12_1, debug layer not enabled. Medium/1920x1080, VSync off; 492 compiled assets, 66,611,751 runtime container bytes. See the [portable cohort report](qualification/graphics-reference-performance.json) for exact distributions, payload/fixture/image hashes, pass/resource data and paired deltas.

Warm representative runs use 64 warmups and 256 samples; two independent processes, four cases each. All eight cases repeat exactly within their process; all four matched cross-process images have zero channel delta. CPU mean paired variance is -2.54%..+3.16%, GPU mean -2.28%..+2.37%; all measured statistics stay below the 15% advisory threshold.

| Run-2 case | CPU mean ms | CPU p99 ms | GPU mean ms | GPU p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Industrial tactical | 32.030 | 40.576 | 29.860 | 36.724 |
| Industrial strategic | 31.302 | 40.990 | 29.772 | 37.392 |
| Combat tactical | 36.445 | 46.696 | 35.268 | 40.799 |
| Combat strategic | 30.765 | 42.126 | 30.113 | 37.173 |

Run-2 records 254 distinct GPU completions per case at 1,000,000,000Hz. Resident texture payload is 52,707,564 industrial / 52,708,956 combat bytes; DXGI local process usage is 250,392,576..250,466,304 bytes. Current-thread allocation p95 is 56 bytes except tactical combat at 536 bytes. These are measurements of the existing renderer, not an optimization claim; the GPU means exceed the 16.67ms engineering target in this workload.

RendererCold additionally uses zero warmups/64 samples in eight successful cases; run-2 setup costs 2.61..3.80 seconds and CPU p99 272.78..646.90ms, exposing first-use cost. Vehicles1000 uses 64 warmups/128 samples in four successful cases. Both paired reports have no unqualified cases or observed deltas above the advisory threshold. Raw logs/requests/native images remain in the immutable local output cohorts and opt-in CI artifacts; portable summaries are not accepted goldens. Live animated combat, sustained gameplay FPS, physical-monitor/DPI inspection and human art acceptance remain separate qualifications.
