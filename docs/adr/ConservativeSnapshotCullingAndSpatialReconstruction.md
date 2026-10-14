# Conservative snapshot culling and spatial reconstruction

## Decision

Prepare previous-identity matches and conservative 32-instance swept groups on the render owner when an authorized immutable snapshot changes. Reject whole offscreen groups before interpolation and per-instance work. Bound intermediate rotation with spheres and vegetation with its pivot/sweep. Invalid groups fail open; surviving actors retain original individual culling. Preserve current instanced batches, LOD hysteresis and frame-resource fences. An explicit constructor flag keeps the reference path available.

Use existing color/depth in the single scaled composition draw to reject cross-surface sample contributions. Preserve native selection/UI and nearest-depth overlay occlusion. Native scale is unchanged. No jitter/history, vendor dependency, target chain or format migration is added.

Mesh shader and raytracing support is diagnostic only. Failed probes preserve baseline gameplay. ExecuteIndirect does not remove the measured pose/material/LOD CPU work; sparse strategic instanced draws do not justify an indirect resource contract yet. Temporal reconstruction lacks motion vectors, disocclusion and history-reset ownership, so the tested spatial path is selected. Revisit either decision only with representative profiling and correctness evidence.

## Trade-offs

Contiguous groups can have broad bounds when IDs are spatially scattered. Publication preparation adds CPU work and managed scratch that must be reported alongside render-only savings. Meshes remain in the finite compiled-catalog session cache; asynchronous eviction is not introduced. Spatial reconstruction cannot restore subpixel geometry and transparent content still uses opaque depth. Depth edges may look sharper rather than smoother. The unchanged comparison threshold and failed/variable cohorts remain in qualification evidence.

## Validation

Tests compare reference/optimized uploads and counts across tactical/strategic zoom, check rotation/translation sweeps, full generations and allocation-free warmed lookups, and retain existing shadow/visibility/LOD/mechanical contracts. Native tests distinguish depth-edge color mixing, preserve one-pixel native UI, repeat pixels and exercise suspend/resize. Runtime package and software fallback checks use existing ownership. Exact evidence and remaining performance/art limitations are in [scalable rendering](../ScalableRendering.md).
