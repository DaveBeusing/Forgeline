# 02 Hardware GPU benchmark

## Outcome and dependency

Extend the existing native reference-performance lane with reproducible scaling and isolated comparisons. Depends on 01; optimized cohorts run after the corresponding wave-02 changes. Do not introduce a second host, timestamp collector or competing result schema by default.

## Ownership

Own exact reference-qualification scripts, ReferencePerformanceSamples collector files, dedicated collector/schema tests and evidence docs assigned by 01. Shared host orchestration, Graphics interfaces, global workflows and solution changes are reserved for serial integration. Renderer changes belong to their package owners.

## Tasks

1. Reuse Invoke-ReferencePerformance.ps1, Invoke-RtsReferenceQualification.ps1 and Compare-ReferencePerformance.ps1. Retain current source/build/payload checks, read-only baselines, qualification states and opt-in gates.
2. Add a bounded synthetic same-mesh scaling fixture: 1,000 / 10,000 / 100,000 root objects, two populated transform snapshots, high/low visibility, fixed and sample-index-driven moving cameras. Keep representative production-asset cohorts and terrain-only/combined scenes distinct.
3. Publish exact executable commands for the full matrix: fixture/seed, root and expanded counts, resolution, camera/path version, warmups, samples, repeats, quality, submission mode and static-upload fallback. Missing host controls are serial prerequisites through 01/03.
4. Isolate submission, memory placement and interpolation changes one at a time before a combined comparison. Use a recorded parent build or bounded diagnostic reference where historical paths no longer exist; never compare unlike fixtures or CPU-only fake contexts with GPU milliseconds.
5. Reuse fence-qualified timestamp readback and actual queue frequency. Collect delayed results without per-frame idle; disclose skipped completions because the current diagnostics expose the latest completion. Zero/missing samples are unavailable timing, not a zero-cost pass. Keep pass scopes distinct; overlapping scopes cannot be summed.
6. Extend existing evidence compatibly only where a required counter is absent. Capture preparation/recording, GPU pass durations, frame p50/p95/p99, draws/batches, upload traffic, allocations, retained resources and fence waits. Define each boundary; RenderFrame CPU time includes Present/reuse waits and is not whole-match throughput.

## Run conditions and acceptance

- Windows x64 hardware adapter, pinned SDK, Release build, VSync off for throughput, fixed dimensions, debug validation/overlays off. Preserve ordinary client defaults. WARP is functional evidence only.
- Record full SHAs/dirty status, runtime assets, CPU/GPU/driver/OS, memory architecture, settings/fallbacks, buffer count and power/background conditions. Keep independent repeat variance visible.
- Separate warmup, renderer initialization and steady state; preserve frame/fence IDs, actual sample counts and invalid reasons. Drain final results outside measured intervals.
- Contract tests cover arguments, deterministic fixtures, schema compatibility, missing/duplicate samples and failure exit codes. CI contract fixtures are not hardware evidence.
- Run existing baseline cases unchanged before adding scale cohorts; inspect native captures with PIX where available. Preserve existing image review requirements and unqualified status when evidence is incomplete.
- No automatic baseline acceptance and no new universal hardware timing gate. Deliver reproducible raw artifacts plus a portable comparison summary.

See [reference performance contract](../../GraphicsReferencePerformance.md).

Suggested commit: `Extend native rendering performance scaling coverage`
