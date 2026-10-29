# Native rendering scaling qualification

This package extends the existing native reference lane. Its immutable definition
is [Protocol.json](../performance-foundation/Protocol.json), fixture version 1.
It does not replace the host, Graphics timestamp collector, renderer, instance
layout, queues, production defaults or result manifest schema 1.

## Workloads and commands

Use Windows x64 hardware, the pinned SDK and a clean Release build. Compile/seal
runtime assets before timing and supply their absolute root. Finish all builds
and other GPU workloads first. Keep power, thermal and background conditions
stable and record any unavailable observations. Normal client defaults remain
unchanged. The explicit reference host uses Immediate presentation, validation
off, three admitted back buffers and the existing reference World UI; report its
overlay pass separately. WARP is functional evidence only.

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
$runtime = (Resolve-Path assets/runtime).Path

# Existing production workloads remain separate and unchanged.
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Preset Representative -Warmups 64 -Samples 256 -CameraPath Sweep -OutputRoot artifacts/reference/production-sweep
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Preset Representative -Warmups 64 -Samples 256 -CameraPath Fixed -OutputRoot artifacts/reference/production-fixed
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -RunProfile RendererCold -Warmups 64 -Samples 64 -CameraPath Fixed -OutputRoot artifacts/reference/production-cold
./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Preset Vehicles1000 -Warmups 64 -Samples 128 -CameraPath Sweep -OutputRoot artifacts/reference/production-vehicles

# Complete synthetic matrix: 7 scenes x 2 camera paths x 2 independent processes.
foreach ($path in @('Fixed','Sweep')) {
  ./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Preset Scaling -Profiles Medium -Displays 1920x1080 -ResolutionScale 1 -Warmups 128 -Samples 512 -CameraPath $path -StaticUploadFallback Default -SubmissionMode Current -OutputRoot "artifacts/reference/scaling-default-$path"
}
```

Scaling selects Scaling1000High/Low, Scaling10000High/Low,
Scaling100000High/Low and ScalingTerrainOnly at NormalGameplay (420 m).
Seed 2026, snapshots 31/32, 50 ms duration and session 1 are fixed. Entity IDs are
1..N, generation 1. Each root uses the existing compiled crate visual, one
material, no mechanics/skin/wind and the ordinary production vertex/112-byte
instance streams. The exact grid, uint hash/jitter, current translation/yaw and
scale follow the frozen protocol. Both states are populated independently;
prepared history is built outside timing, while the native host still renders
alpha=1. CPU intermediate-alpha correspondence belongs to its separate lane.

When invoking the underlying script directly, pass real PowerShell arrays, for
example `-Scenes @('IndustrialBase','BattlefieldCombat')`. Comma-containing scalar
scene/profile/zoom/UI arguments are rejected before capture: .NET enum parsing
can otherwise combine such names and silently select a different valid value.
The Scaling preset builds its typed list internally and avoids that ambiguity.

Low shifts i%10!=0 by +4000 m X in both states. High/Low describe intended
distributions, not invented measured visibility. A SHA-256 binds version, scene,
bindings, target and every previous/current entity/transform. Existing scene enum
values, fingerprints and current-only behavior are preserved. TerrainOnly has
two empty object states on the same canonical terrain and camera. Root counts
are 0/1000/10000/100000; actual candidate/visible/expanded main/shadow populations,
batches and resolved resource identities remain in each native manifest and raw
per-frame submission diagnostics. Do not substitute names for those counters.

## Isolated comparisons

Current indexed instancing is the executable submission mode; the script does not
invent a legacy-mode toggle. Submission/correspondence changes require two named
builds with identical fixtures and populated history. Correspondence Update and
repeated intermediate-alpha lookup are separate CPU cohorts, never GPU timings.
Until those package builds are available their isolated performance is unavailable.

The existing static-memory diagnostic is process-start scoped and can be compared
without a new host:

```powershell
foreach ($path in @('Fixed','Sweep')) {
  ./build/Invoke-ReferencePerformance.ps1 -RuntimeAssetRoot $runtime -Preset Scaling -Profiles Medium -Displays 1920x1080 -Warmups 128 -Samples 512 -CameraPath $path -StaticUploadFallback Upload -OutputRoot "artifacts/reference/scaling-upload-$path"
  ./build/Compare-ReferencePerformance.ps1 -CurrentRoot "artifacts/reference/scaling-upload-$path/run-1" -BaselineRoot "artifacts/reference/scaling-default-$path/run-1" -ComparisonMode StaticMemory -ReportPath "artifacts/reference/memory-$path.json"
}
```

Default explicitly unsets FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK; Upload sets 1
only for the child process, restoring the original process environment even on
failure. Inherit preserves the old script behavior. The collector records the
actual process control. Ordinary Matched comparisons require recorded controls
to agree; StaticMemory permits precisely this labelled fallback difference.
Both modes require identical runtime payloads, fixtures, hardware and settings.
The comparator continues to support legacy reports when both lack new controls.
A new/old pair with unrecorded fallback identity stays unqualified.

For feature candidates keep baseline-a, candidate-a, candidate-b, baseline-b in
that order, using separate source-clean installed clients and fresh output roots.
Keep independent process variance visible, then compare the serially integrated
combined candidate. No combined or optimized gain follows from API choice or
draw-count reduction. Baselines are read-only and no adoption is automatic.

## Measurement boundaries and missing data

Manifest schema 1 is retained. The optional performance collector schema 2 adds
bounded contiguous CPU indices, observed CPU-submission/completed-GPU fence IDs,
Present and reuse waits, actual per-GPU-sample queue frequency and completion
observation accounting. GPU timestamps are still read only after fence completion;
warmup/final drains are outside measured intervals. There is no new per-frame idle.
Duplicate/older observations, missing/zero durations and zero frequency cannot
become samples. Frequency changes within a window fail explicitly. Gaps between
accepted fence values are unobserved queue values, not proven missing rendered
frames; other submissions may occupy them. Raw chronology and accepted counts
remain authoritative. Zero samples produce null distributions, never zero cost.

RenderFrame CPU includes preparation performed inside that existing bracket,
recording/submission, Present and slot reuse waits. Present/reuse are overlapping
subsets, not additions. Current-thread allocation excludes workers/native heaps.
Preparation/recording/submit/retire phase timings already exist in each raw
cpuFrames[].submission.assetPreparation record and are reused without another
host instrument. Their render-session boundary is distinct from whole-match
throughput. Renderer initialization/startup and
readback/PNG/hash/export remain separate from steady state. GPU world, overlays,
composite, shadows, particles and atmosphere can overlap and must not be summed.

Existing submission/resource records distinguish instance upload traffic and
capacity, retained textures, retirement and fence waits. Native upload-arena
destination/staging bytes and pressure belong to the separately owned upload
probe and are unavailable in this host. SkinUploadBytes is retained skin-buffer capacity, not measured frame
traffic. These and DXGI usage overlap; no fabricated total VRAM is reported.
UMA/cache-coherent UMA and PIX captures remain unavailable unless independently
observed. Keep raw sample count, invalid reasons and each repeat's p50/p95/p99.

The optional 15% gate is unchanged. Mismatched payload/settings, software, missing
timing and invalid samples cannot satisfy it. Human image/physical-monitor review,
animated match throughput and sustained gameplay FPS remain separate acceptance.

## Validation and evidence

Dedicated fixture/collector tests exercise bounded populations, deterministic
history, identities, the existing request schema, CPU sample uniqueness, delayed
GPU completions, missing/zero timings, frequency changes and failure exits. The
existing Test-ReferencePerformance.ps1 remains unchanged and exercises historical
schema/image contracts. Synthetic contracts are not native performance evidence.

Executed source/build, hardware/payload identities, raw cohort paths and measured
results are recorded below after qualification. Incomplete rows remain explicitly
unqualified; they must not be replaced by historical foundation acceptance.

## Executed hardware characterization

Unchanged baseline: `a761969668136f32ce99438e4b6a407094c9567e`, product 0.1.530.
Scaling/static-memory/production-regression measurement source:
`e01365a5062556546011b8fffe38f45bb54e5b50`, product 0.1.531. Both used clean Git
status, pinned SDK 10.0.402 and a complete Release build with zero warnings/errors.
Later argument-validation and evidence-documentation changes do not relabel those
binaries. Native fixture, collector and renderer source blobs retain that identity.

Windows 10.0.26300 x64; Intel Core Ultra 9 285HX, 24 logical processors,
68,137,205,760 bytes physical RAM; physical NVIDIA RTX PRO 5000 Blackwell Generation
Laptop GPU, 25,309,478,912 dedicated bytes, driver 32.0.15.9653. The secondary Intel
adapter was not selected. VSync/debug/software mode were off; actual queue
frequency was 1,000,000,000 Hz. The Balanced power scheme was observed. Background
load and thermals were not traced or actively controlled. No PIX installation
was found; UMA/coherent-UMA queries and physical-monitor/human art approval remain
unavailable. These limits are not silently converted into acceptance.

All runs used the same sealed 2055-asset catalog, content signature
`135b44877d4882ac539073738a43af7b4794ff35819cc2c1f55c353771743020`, runtime directory
fingerprint `55827DADD0271F6F74F0FB66E2B2774C924C38FC106B6EA351C43BF9A694EBBA`.
Every request, manifest, complete CPU/fence chronology, comparison and command
setting is retained under `artifacts/native-scaling/`; the portable index is
`artifacts/native-scaling/summary.md`. Source/build/assembly/payload identities and
every manifest SHA-256 are recorded there without normalizing away repeat variance.

The planned evidence contains **64 captures in 17 complete native manifests**:
28 unchanged production baselines, 28 full synthetic scaling cases, four isolated
static-memory cases in Default-A/Upload-A/Upload-B/Default-B order, and four
post-change production cases. All native manifests are structural-pass. All requested
timing comparisons reported zero unqualified entries. All 20 cross-cohort image comparisons are exact
file matches, including the four old production images. Repeated image readbacks
remain exact. Human visual acceptance is still pending. One additional scalar-
argument invocation selected only one production case; its request/result are
retained but excluded from this four-case protocol count. That ambiguity is now
rejected and covered by a failure-exit test.

All nonempty scaling cases retain one compatible main-object draw at the fixed
pose. The 100k High fixed case records 100,000 visible/submitted roots and
11,200,000 instance-upload bytes per frame. Draw reduction alone does not make
its CPU preparation/recording cheap:

| 100k High independent repeat | CPU frame p95 ms | GPU frame p95 ms |
|---|---:|---:|
| Fixed 1 | 874.891 | 17.275 |
| Fixed 2 | 502.630 | 12.542 |
| Sweep 1 | 617.941 | 15.600 |
| Sweep 2 | 887.680 | 18.735 |

This is a substantial CPU scaling limit in the existing renderer, not an FPS
improvement claim. All roots, expanded submissions, visible ranges, phase and
allocation distributions, resource counters and actual GPU completion counts
remain visible in the raw files. Reported last-observation shadow counts are not
relabelled as window peaks. The reference World UI/terrain contribution remains
part of the frame, separate from the same-binding object draw.

The two matched static-memory pairs observed Upload fallback GPU mean regressions
of **26.94% and 51.03%**, and CPU mean regressions of **20.91% and 41.19%**, versus
Default. Both pairs retained exact images. All eight reported timing statistics
in each pair exceeded the optional +15% threshold. No statistic in either
within-mode repeat comparison exceeded that positive regression threshold; the
complete signed deltas remain in JSON. This characterizes paths honoring the
existing diagnostic flag on this workload/hardware, not proof that every mesh
already uses GPU-local geometry or a new optimization introduced here.

Unchanged baseline repeat comparisons recorded 3/1/9/0 positive-threshold
exceedances for warm Sweep/warm Fixed/RendererCold/Vehicles1000 respectively.
Synthetic Fixed/Sweep repeats recorded 9/41. Those are statistics, not case counts;
all advisory reports remain unmodified. Large independent variance prevents a
general performance-acceptance claim. No new timing gate, threshold adjustment,
baseline adoption, renderer optimization or combined-feature speedup is asserted.
Optimized submission/correspondence cohorts still require their package builds;
the combined comparison remains serial integration scope.

Validation: 11 fixture/old-prop cases and 24 collector/history/script cases pass,
including the actual ambiguous-argument regression. Existing unmodified
Test-ReferencePerformance contracts pass. The unchanged 46-project architecture
and the exact nine-file native-scaling ownership check pass; the single PATCH
uses the separately serialized version exception and real repository version
scripts. The base commit's full CI run was cancelled, not green; required final
PR build-test and the normal terminal gate remain mandatory and unchanged.

Portable local deliverables, retained independently of repository source scope:

- `artifacts/native-scaling-raw-v2.zip`, SHA-256
  `31ebf5f93ad864489ca01dbcef2a02475e3d267826a9438526b2888b91498c25`.
- `artifacts/native-scaling-images.zip`, SHA-256
  `6186dbd1e3e49ab159ffee220d0195df93ca14292abe804e8ac75527a62ff3a7`.

The first archive preserves original native/comparator schemas and raw data, not
a competing collector. The second preserves the full-resolution PNGs for review.
Neither is an automatically accepted golden or a claim of whole-match throughput.
