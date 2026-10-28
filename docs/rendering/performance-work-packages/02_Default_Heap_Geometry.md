# 02 Default-heap geometry

## Outcome and dependency

Qualify and improve existing GPU-local static geometry and staged uploads. Depends on 01. Terrain and cube callers already use CreateInitializedStaticBuffer; the native upload arena creates default resources. Do not rebuild that subsystem.

## Ownership

Own the exact ResourceScheduling native files, initialized-static-buffer lifecycle tests and resource-scheduling docs assigned by 01. Graphics interface changes return to 01. Renderer callers, TerrainRenderer residency, SimpleInstanceRenderer, global device lifecycle and benchmark scripts remain outside this package; request serial integration for any caller gap.

## Tasks

1. Audit static mesh callers and prove the native result is default memory rather than the interface's compatibility upload implementation. Inventory dynamic buffers that correctly remain upload-backed.
2. Reuse the existing FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK=1 diagnostic control for matched comparisons. Record its process-start value; restore normal default behavior outside reference runs.
3. Verify batched staging copies, state promotion/transitions, first-use flushing, bounded arena slots, pressure waits and last-use retirement. Preserve current capacity/error policies unless a measured issue justifies a reviewed change.
4. Measure startup transfer cost, transfer/submission count, retained/peak staging bytes, destination bytes and backpressure. Separate one-time initialization from frame-time residency churn.
5. Fix only demonstrated redundant transfers, allocation churn or retirement gaps within the assigned boundary. Do not add a second queue, new terrain LOD or mesh merging to obscure the comparison.
6. Hand any missed static caller to wave 03 with exact location and required change; avoid editing another owner's renderer partial.

## Acceptance

- Native tests cover initial data, index/vertex final state, first draw, pressure, failure injection, early wrapper disposal, repeated lifecycle and shutdown.
- Staging remains alive through copy completion and becomes reusable afterward; no per-resource WaitForIdle is added.
- Fixed resident geometry does not reupload every frame. Streaming-driven uploads are reported rather than hidden.
- Compare fallback/default on identical terrain/object scenes, adapter and payload. Report unified versus discrete memory architecture and startup versus steady-state effects.
- Native correctness runs inspect debug-layer/live-object diagnostics. Performance runs use validation-disabled settings and disclose unavailable evidence.

Suggested commit: `Qualify static geometry uploads and retirement`
