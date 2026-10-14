# Bounded analytic GPU particles

## Context

Combat/destruction/logistics extraction already publishes viewer-authorized effect instances through a bounded mesh VFX pool. The graphics backend has one direct queue, frame-indexed upload resources and optional linear scene/atmospheric composition, but no general compute/indirect resource contract. A first particle runtime must preserve that authority and lifetime boundary.

## Decision

Presentation maps existing effect asset IDs to version-1 visual emitter descriptors. Smoke, dust, sparks and explosion embellishments use immutable seeded initial conditions. A vertex shader evaluates velocity, gravity, curl, lifetime and camera-facing expansion; the pixel shader evaluates procedural coverage, sampled opaque-depth rejection and a bounded soft intersection fade. This is GPU analytic simulation, not a compute/UAV or indirect implementation. CPU code handles bounded emission admission, age publication and sorting; it never calculates evolving particle positions or reads them back.

The supported D3D12 path uses one indexed-instanced quad draw and three fixed upload streams at the default swap-chain count. Low/Medium/High hold at most 512/2048/4096 particles. Presentation retains at most 2048 emitter identities in reused arrays/maps. Effects outside this runtime, unavailable/corrupt asset bindings, unsupported devices and disabled quality retain the existing mesh path within a separate 32/64/128 Low/Medium/High fallback cap (128 for Disabled). Supported mappings rejected by admission or fill limits are suppressed rather than escaping the budget through mesh rendering. No event source, projectile, gameplay entity or material atlas is duplicated.

Stable emitter IDs use pool slot/generation for transients and full entity identity plus effect kind for persistent state. Reordering synthetic snapshot instances cannot replay an emitter. Copied spawn ticks reject already-aged transients; seeds and clock state reset at the session boundary. Only current World-visible snapshot instances participate; removing authorization immediately removes that emitter and its visual particles. No trail survives independently of the copied source.

## Composition and ownership

Graphics owns all native objects. The particle submission follows opaque terrain/world, precedes scene output/overlay, unbinds the DSV and transitions the shared typeless scene depth to a pixel SRV, then restores DepthWrite and the DSV. Particle pipelines require alpha blending, reserve t15 and forbid depth writes. Linear particles blend before exposure/ACES; direct output uses the existing exposure/transfer convention. The alpha channel of blended FP16 pixels cannot retain the negative clear/debug sentinel.

One depth descriptor is shared with atmosphere when enabled. No new render target or particle texture is allocated. The particle pass has two GPU timestamps per frame, resolved and published only after its submission fence completes; analytic update and rendering are combined in this measurement. Missing queue timestamps mean unavailable timing, not zero cost. Fixed buffer payload is diagnosed separately from texture residency and adapter budget/driver allocation.

Upload streams reuse the synchronized frame index. Wrappers retire through the existing resource/fence owner. Surface depth follows the existing idle-before-resize/configuration rule. Disposal and partial buffer creation release every owned resource; confirmed device removal uses the existing graphics fault shutdown. The renderer does not attempt independent device recreation.

## Trade-offs and limits

Analytic evaluation avoids persistent UAV state, synchronization, compaction and indirect dispatch overhead for this small population. It still uploads initial conditions/age and sorts a bounded population on CPU. Qualified GPU timing establishes its actual cost; a compute path should replace it only after a measured need and an engine-owned resource contract exist.

Blended sorting uses emitter anchor distance, not exact evolved particle depth. Procedural sprites intentionally do not sample the authored mesh atlas; the same stable IDs, palette and scale define the new visual, and mesh fallback continues to use the compiled textures. Atmospheric extinction currently uses opaque depth, so translucent fog composition is an approximation. Smoke/fill overlap, diagonal viewing angles and full-match visual readability need manual assessment. This path is supplementary to tactical UI and never changes concealment, collision, hit resolution or authoritative simulation.

Production perspective depth reconstructs view distance with B/(depth-A), where A is -Projection.M33 and B is Projection.M43 for the existing right-handed perspective matrices. Native tests distinguish this sign from the formerly incorrect signed M33 input. Identity-depth and linear/direct composition tests remain separate. Current instance membership and fallback admission are cleared each preparation; hidden or removed copied sources cannot retain replacement decisions. Both snapshot publication and mesh submission are bounded, including when the particle backend is unavailable.