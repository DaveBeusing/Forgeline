# Rendering performance work packages

Status: implementation and qualification plan against master `7716007a` (2026-10-28). No performance gain or new acceptance is asserted by this planning change.

## Current implementation

The earlier local checkout was stale. Current master already contains all four requested foundations:

| Requested capability | Existing implementation | Work remaining in this plan |
| --- | --- | --- |
| Real GPU instancing | SimpleInstanceRenderer uses a 112-byte instance stream, frame-slot buffers, material/mesh batches and DrawIndexedInstanced | Qualify batching, CPU preparation, uploads and scaling on matched workloads |
| Default-heap geometry | CreateInitializedStaticBuffer and ResourceScheduling/D3D12UploadArena; terrain and shared cube use the API | Audit coverage, retirement and transfer cost; close measured gaps |
| Prepared interpolation mapping | RenderWorld rebuilds RenderSnapshotIndex when snapshots change | Validate identity/order/session behavior and measure update versus frame cost |
| Reproducible GPU benchmark | Invoke-ReferencePerformance.ps1, native reference collector and fence-qualified timestamps | Extend existing evidence to a controlled scaling matrix and isolated feature comparisons |

Reuse these implementations. Do not replace the production instance layout with an 80-byte cube-only layout or introduce a second benchmark/timestamp architecture. Existing qualification documents contain historical acceptance limits; audit current source and checks before carrying any status forward.

## Implementation order

The numeric prefix is an integration wave. Equal prefixes identify packages that may run concurrently after the prerequisite is released.

| Wave | Package | Depends on | Exclusive write scope |
| --- | --- | --- | --- |
| 01 | [Contracts and baseline](01_Graphics_Foundation.md) | Current master | Shared decisions, interface changes if necessary, ownership and source checkpoint |
| 02 | [GPU instancing](02_GPU_Instancing.md) | 01 | Instance-submission implementation/tests/docs |
| 02 | [Default-heap geometry](02_Default_Heap_Geometry.md) | 01 | Resource-scheduling implementation/tests/docs |
| 02 | [Interpolation mapping](02_Interpolation_Mapping.md) | 01 | Snapshot correspondence implementation/tests/docs |
| 02 | [Hardware GPU benchmark](02_Hardware_GPU_Benchmark.md) | 01 | Qualification scripts, collector and benchmark evidence |
| 03 | [Integration and validation](03_Integration_Validation.md) | All 02 | Shared host wiring, global docs, version, final comparison |

```text
01 Contracts and baseline --+--> 02 Instancing -----------+
                           +--> 02 Default-heap geometry +
                           +--> 02 Interpolation mapping +--> 03 Integration
                           +--> 02 GPU benchmark --------+
```

01 records exact file allowlists, including new files and tests, before wave 02 starts. Reuse the existing [render ownership contracts](../parallelization/Contracts.md) and ownership manifest. The new plan is not permission to overlap an active owner or change frozen contracts. Any missing shared capability returns to the foundation owner for a serial prerequisite; downstream packages do not independently extend Graphics interfaces.

Only one owner changes a file. In particular, SimpleInstanceRenderer and its resource partial belong to instancing; D3D12UploadArena belongs to geometry; RenderWorld/RenderSnapshotIndex belong to interpolation; reference collector/build scripts belong to benchmark. TerrainRenderer changes, graphics layouts, solution files and global workflows are reserved for serial integration unless 01 explicitly transfers ownership.

## Measurement and completion

Run fixed-seed 1,000 / 10,000 / 100,000-instance cohorts, fixed and moving cameras, high/low visibility, terrain-only and combined scenes. Separate synthetic same-mesh submission from representative production assets. Compare matching binaries/settings or explicitly recorded parent revisions, with one feature varied at a time.

Record CPU preparation/recording, GPU pass distributions, p50/p95/p99 frame times, draws/batches, upload bytes, allocations, retained memory and fence waits. GPU time is distinct from Present/display latency and simulation time. Structural draw reduction is not a measured frame-time gain. WARP and null-context runs are functional/CPU evidence only.

Wave 03 publishes reproducible commands, raw artifact locations, qualified comparisons, regressions and limits. Reuse existing opt-in performance gates; do not install machine-dependent thresholds as universal CI gates.

## Repository workflow

Start from current master; use explicit stacked dependencies only where necessary. Keep small technical commits with short imperative English subjects and concise PR descriptions stating behavior and validation. Increment PATCH exactly once per PR using the repository script; refresh after rebasing onto newer master. Preserve the no-squash workflow. Prepare PRs without merging.

GPU culling, ExecuteIndirect, asynchronous queues, mesh shaders, a new material system, snapshot pooling and a separate simulation thread remain follow-up decisions driven by evidence.
