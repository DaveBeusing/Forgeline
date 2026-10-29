# Static geometry package

Technical owner: `StaticGeometryQualification`. [Ownership.json](Ownership.json) delegates only the current native upload arena/limits, exact scheduling/lifecycle tests, existing upload probe/scripts and the new qualification document. Device/context lifecycle, resource wrappers/retirement, Graphics interfaces, renderer callers, TerrainRenderer and reference host are serial/read-only.

Native CreateInitializedStaticBuffer already allocates Default/GpuLocal memory. Audit all static callers against that override rather than claiming the interface compatibility fallback is the native implementation. Dynamic frame/instance buffers correctly remain Upload-backed. Use the existing process-start fallback flag for matched Default-versus-Upload work; restore it unset afterward.

Verify immediate CPU-span consumption, deferred batching, first-use flush/order, Common promotion and vertex/index final states, 512-byte placements, bounded slots/pressure and last-use retirement. Preserve maximum transfer/retained budgets, failure policy and ten-second fence bounds. Pending staging survives copies; Execute-before-Signal uncertainty remains a sticky fault. No extra queue or per-resource WaitForIdle is allowed.

Extend actual native tests for input mutation, first draw, growth/pressure, injected failures, early wrapper disposal, repeated lifecycle and shutdown. Separate validation/debug-layer correctness from validation-disabled timing; unavailable debug/live-object evidence stays explicit. Record transfer/submission count, destination versus retained/peak staging bytes, pressure waits and one-time transfer/drain separately from steady rendering. Do not infer UMA architecture or GPU-copy duration from a CPU stopwatch.

Use [Protocol.json](Protocol.json) for rendering cohorts. Existing upload microprobes remain separately labelled diagnostics, not interchangeable GPU frame evidence. Hand any missed renderer/terrain caller to integration by exact path; do not edit it concurrently. Deliver StaticGeometryQualification with all failed/qualified cohorts and no universal FPS claim.
