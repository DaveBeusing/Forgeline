# Directional shadow depth and contact occlusion

## Decision

Use one stabilized camera-relative orthographic directional depth map as the measured, bounded alternative to cascades. Low/Medium/High select 1024/2048/4096 square maps, 180/450/750 metre radii and 1024/4096/8192 caster submissions. Every profile has one map and a hard 256-draw limit. Disabled is the default and allocates no shadow targets, descriptors or constant arena. General cascades, time-of-day, ray tracing and screen-space AO are outside this implementation.

The footprint is fixed within a profile. Zoom does not continuously resize it; camera target coordinates are rounded to whole texels in light-space X/Y. This prevents static-frame jitter and preserves a bounded shadow distance at strategic zoom. Receivers fade smoothly at the footprint/radius edge. The tradeoff is deliberately limited shadow coverage and resolution; this is not full-frustum cascade coverage. Profile changes require renderer reconstruction because receivers are compiled against the configured graphics contract.

## Ownership and order

Graphics owns a typeless R32 target with D32 depth and R32 shader-resource views per back buffer. Its reusable frame fence protects target and upload-arena reuse. Recording is Shadow Depth → World → optional scene composition → Overlay. A shadow pass can occur exactly once, before world draws. Receiver binding requires the same frame's completed shadow recording; incomplete or reversed passes fault before submission. Configure/disable require idle outside recording. Resolution-independent maps survive swap-chain resize; surface measurements are cleared. Minimize/occlusion pause the existing submission path. Device removal and teardown preserve the existing fatal-session and retained-ownership policy.

Shadow receivers reserve pixel texture t15 and frame CBV b1. Buffered vertex constants b0 replace the terrain's nearly full root-constant budget without changing its data layout. A fixed aligned arena has 4096 slots per back buffer; slot zero holds shadow data and the remainder holds per-draw constants. Exhaustion is explicit. Ordinary frames do not allocate descriptors or perform full-GPU idle waits. Disabled rendering retains the original root-constant/material path.

## Lighting and visibility

Terrain and opaque instanced geometry cast and receive. Object shadows reuse the same interpolated transforms, material/mesh resolution and projected-size LOD selection; they have separate frame-indexed upload streams and cannot overwrite data already recorded by another pass. Shadow preparation does not alter the world renderer's retained LOD decisions. VFX are excluded. Terrain is capped at 128 shadow draws and objects consume the remaining quality budgets.

Caster input is exclusively the immutable, viewer-authorized RenderWorld. The extractor excludes enemy entities that are not currently identified; the shadow renderer also requires World visibility. It never reads intelligence contacts, last-known positions, ECS or live simulation state. Thus a hidden entity cannot reveal itself through a cast shadow. Gameplay visibility and deterministic simulation remain unchanged.

Four bilinearly weighted depth comparisons provide PCF. Receiver-plane depth correction handles PCF footprints on slopes, with a small bounded depth bias. Direct illumination alone is multiplied by cast-shadow visibility. A separate contact term uses the actual light-space receiver/blocker gap, fades out over 1.5 metres and attenuates ambient by at most 15% (12% default). This is depth-blocker contact occlusion, not SSAO/GTAO or material AO. Material AO, hemisphere ambient, emissive and the existing exposure/output transform retain their separate roles.

## Evidence and limits

WARP pixel regressions compare enabled/disabled output, hidden-caster invariance, identical static frames, direct/linear output, suspension, resize and release. Reference zooms 120/420/1000 metres and raster scales 100/150/200% are covered; raster scaling does not establish physical-monitor DPI transition acceptance. Pure tests cover profile payload, invalid settings/matrices and subtexel stabilization. Native fault tests cover configuration during recording, missing End and reversed pass order. A 10000-instance test verifies the Low 1024-caster cap without changing snapshot contents.

An opt-in isolated hardware fixture compares Disabled/Low/Medium/High on the same nine chunks, 1000 opaque instances, 1280×720 surface and three zooms. It records adapter/driver, sample counts, CPU and completed-fence GPU times, draws/casters and target payload. WARP timings are never treated as hardware performance. Real device removal, long graphics/occlusion soak, moving-unit visual acceptance and physical monitor readability remain separate manual qualification.
