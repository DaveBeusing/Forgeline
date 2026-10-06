# Scene Lighting, Exposure, and Output Transfer

## Status

Accepted.

## Context

The production terrain and material pipelines use color-space-aware GPU textures. Base Color textures declared as sRGB are decoded to linear values by the D3D12 sampling path, while Normal and ORM data remain linear.

The interactive renderer presents directly into an R8G8B8A8_UNorm swap-chain back buffer. Before this decision, terrain and production object shaders applied lightweight material modulation and returned those linear values directly to that UNorm target. The result was substantially darker than intended and reduced terrain, infrastructure, and unit readability at the standard RTS camera.

ForgeLine currently has no general HDR intermediate render target or full-screen post-processing pass. Adding one only to correct scene luminance would expand renderer ownership, resource lifetime, render-pass ordering, and performance scope beyond the visual-foundation requirement.

## Decision

ForgeLine uses an explicit presentation-owned scene-lighting baseline for current world rendering.

The client render host owns one SceneLightingSettings value and passes the same immutable configuration to terrain and repeated-instance rendering. The baseline provides:

- one normalized directional light;
- explicit directional color and intensity;
- a hemisphere-style ambient term;
- explicit ambient color and intensity;
- stable manual exposure;
- an ACES-fitted tone-mapping curve;
- explicit linear-to-sRGB transfer before lit world colors are written to the UNorm back buffer.

Automatic exposure is not used. The RTS camera should not change battlefield readability because the player pans across locally bright or dark content.

The transform is applied inside the existing world-surface shaders. UI, debug primitives, and frontend presentation keep their established output path and are not treated as scene-lit surfaces.

The terrain path keeps its existing thirteen texture descriptors. To fit the added scene state inside the D3D12 64-DWORD root-signature budget, terrain Base Color factors are packed into RGB10 values and roughness/metallic factors into paired UNorm16 values. No additional descriptors or terrain draw calls are introduced.

The baseline provides directional light/shadow-side form but does not add cast-shadow maps. Cast shadows require a separate resource/pass/camera-coverage design and must be justified against the RTS rendering budget before integration.

## Alternatives Considered

### Change the swap chain to an sRGB back-buffer format

Rejected for this stage. The renderer contains world, debug, information, frontend, and overlay paths with different current color assumptions. Changing the presentation target globally would require coordinated conversion of every path to avoid double encoding or unintended palette changes.

### Add an HDR intermediate and full-screen tone-mapping pass

Architecturally valid and likely appropriate when ForgeLine requires broader post-processing, HDR output, bloom, or screen-space effects. It is not justified solely to establish the current readable SDR baseline because it introduces a new render target, transition/lifetime rules, at least one additional draw, and broader renderer changes.

### Apply gamma correction only

Rejected. Correct output transfer fixes the linear-to-display mismatch, but explicit exposure and controlled highlight compression are also required to keep industrial materials readable without hard clipping.

### Use automatic exposure

Rejected for the current RTS baseline. Camera-dependent exposure can make the same battlefield state appear materially different during ordinary panning and zooming.

## Trade-offs

- The current output transform is duplicated in the terrain and instance world shaders rather than centralized in one full-screen pass.
- Manual exposure is deliberately less adaptive than photographic rendering but more predictable for RTS readability.
- Packed terrain material factors introduce very small quantization error in exchange for preserving the existing D3D12 root-signature contract.
- Directional/ambient form improves grounding but does not provide geometric cast shadows.

## Consequences

- Production Base Color sampling, lighting math, tone mapping, and output transfer have an explicit color-space contract.
- Terrain and production objects share one scene-lighting configuration.
- Scene exposure remains deterministic from the presentation point of view and does not depend on camera luminance history.
- Visual qualification records the active lighting/exposure state and rejects an invalid or unexpected tone-mapping configuration.
- Simulation, Headless, world ownership, navigation, collision, and gameplay remain independent from rendering.

## Re-evaluation Criteria

Re-evaluate this decision when one or more of the following become production requirements:

- HDR display output;
- bloom or other full-screen post effects;
- color grading;
- screen-space effects;
- multiple lighting environments or day/night transitions;
- cast-shadow maps;
- a common render graph or explicit multi-pass renderer;
- evidence that per-surface tone mapping materially limits quality or performance.

At that point, migrate the output transform to the appropriate common render stage while preserving the explicit color-space and exposure contract.
