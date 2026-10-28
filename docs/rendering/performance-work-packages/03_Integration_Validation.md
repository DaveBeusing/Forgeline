# 03 Integration and validation

## Outcome and dependencies

Integrate all four wave-02 results and publish a matched performance report. Existing GPU instancing, default geometry, prepared mapping and native benchmarking are the baseline, not newly introduced capabilities.

## Ownership

Own shared host wiring including ClientRenderHost where needed, serial TerrainRenderer caller fixes, global documentation, version preparation, solution/build/workflow changes if justified and the final comparison report. Resolve any overlap with the existing ownership manifest before editing. Do not concurrently modify a wave-02 owner's files.

## Implementation order

1. Integrate the 01 checkpoint and any minimal shared contract gaps. Confirm all consumers and doubles compile.
2. Integrate the four disjoint 02 branches; order within this step is flexible. Keep each implementation/qualification result reviewable.
3. Apply explicitly handed-off host/terrain changes serially, preserving Prepare/Record/Submit/Retire, main/shadow storage and the graphics owner-thread lifecycle. Existing optimized defaults stay intact unless evidence supports a deliberate adjustment.
4. Run one-feature comparisons and then the combined result on the same hardware, assets and matrix. Existing master must remain a named, immutable baseline.
5. Update Graphics, PresentationExtractionAndDebugging, Development and GraphicsReferencePerformance only where behavior or evidence changes. Historical qualification records remain distinguishable from current acceptance.

## Validation and report

Follow the current [canonical validation](../../Development.md), including version/build identity, project references, Release build, native client smoke, headless smoke/stress, solution tests and the natural terminal-match gate. Do not copy stale SDK versions or obsolete commands into the plan.

Run focused lifetime/correspondence/submission/collector tests. Native correctness coverage includes capacity growth, delayed frame reuse, resize/minimize/restore, session restart, partial initialization and shutdown. Review images including shadows, materials, LOD and authorization filtering. Run timing separately with validation disabled.

Publish exact commands, full baseline/current SHAs, payload/hardware identity, warmup/sample/repeat counts, raw artifact locations, valid sample counts and limitations. Report CPU preparation/recording, GPU distributions, present/fence blocking, memory, uploads and startup separately. Disclose regressions and variance; do not infer gains from API choice or draw reduction alone. The existing reference workloads may already be GPU-bound, so CPU improvements need not increase FPS.

## Delivery

One short-lived branch/PR per bounded package where practical; shared prerequisite changes integrate first. Use short imperative English commits and concise PR descriptions stating behavior and validation. Run Increment-PrVersion.ps1 against current origin/master and commit exactly one PATCH increment per PR; refresh after rebasing. No tags, release, squash or merge is part of this planning delivery.

Suggested commit: `Integrate rendering performance qualification results`
