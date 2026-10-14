# Bounded world environment and terrain residency

## Status

Accepted for presentation ownership and bounded resource contracts. Performance and visual acceptance require measured qualification.

## Decision

Keep all authoritative world chunks in CPU memory and admit GPU terrain resources through a presentation cache. Prepare the camera/shadow union before recording; reject insufficient capacity before mutation. Use deterministic chunk draw order and fence-based eviction. Control textures opt into deferred direct-queue upload completion, with existing fenced staging retirement. Default production capacity is 128 chunks; standalone callers can retain eager construction.

Keep four-layer splatting, runtime material formats, macro/micro normals and existing detailed decal quads. Add an immutable, bounded authored surface-wear source rather than a gameplay trail collector. Add deterministic vegetation density and distance admission around existing visibility, frustum, LOD and wind behavior.

Resolve bounded daylight in copied scene lighting. Compile static cloud/wetness constants into existing world shaders. Compose optional local haze through the existing scene-depth pass, using three density samples and existing opacity bounds. Preserve neutral defaults and native overlays; no simulation dependencies, new targets or general weather system are introduced.

## Consequences

Admission CPU work is visible in timed captures. Ordered uploads remove ordinary per-texture GPU waits, but resource-retirement capacity pressure may still synchronize. Required coverage can fail explicitly instead of showing holes. Logical resident bytes exclude physical allocation granularity and shared material payloads.

Surface wear is limited by control-grid resolution; detailed tracks continue to use authored quads. Cloud modulation is static, wetness has no water accumulation, and local haze is an optical-depth approximation that may miss small volumes on long rays. Spatial AA repeats its three evaluations. Appearance changes follow the existing next-session quality lifecycle and do not require a settings-schema migration.

Performance comparison permits residency changes only as the measured implementation, while retaining effective visual workload equality. Native images and diagnostics accompany timings; short frozen-extraction runs cannot establish sustained live RTS frame rate.

See [World environment rendering](../WorldEnvironmentRendering.md) for bounds, reproduction and qualification limits, and [GPU resource retirement](GpuResourceRetirementAndFaultShutdown.md) for ownership on failures.
