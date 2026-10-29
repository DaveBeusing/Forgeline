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
Standalone preparation timing remains an explicitly unavailable/null scope until
serial host instrumentation is provided. Renderer initialization/startup and
readback/PNG/hash/export remain separate from steady state. GPU world, overlays,
composite, shadows, particles and atmosphere can overlap and must not be summed.

Existing submission/resource records distinguish instance upload traffic and
capacity, retained textures, native upload arena bytes/pressure, retirement and
fence waits. SkinUploadBytes is retained skin-buffer capacity, not measured frame
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
