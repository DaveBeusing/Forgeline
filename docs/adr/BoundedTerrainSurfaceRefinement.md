# Bounded Terrain Surface Refinement

## Decision

Keep the production terrain pipeline, eight stable material IDs, four-active-layer splat maps, thirteen texture/sample ceiling and existing direct/shadow root signatures. Add construction-time validated detail policy, pixel-footprint filtering and a slope-adaptive Rock projection using its existing samples. Refine existing material UV parameters and authored blend feathers. Keep vegetation motion exclusively in copied render transforms.

## Ownership and lifetime

Terrain profiles and visual time belong to Presentation/Client. Graphics continues to own pipelines, buffers, textures, upload storage, descriptors and fence retirement. Detail introduces no new GPU resource class, pass or per-frame shader constants. Configuration is validated before pipeline/resource creation and compiled into the existing pixel shader. Resize, suspension, recording faults and device-loss retirement use the established renderer/device paths.

Vegetation shares the same render and shadow time and keeps its mesh/material batching and instance stride. The render-only culling envelope covers the authored Y=0..1 plant and bounded bend. Snapshots, selection/collision geometry and simulation state remain unchanged. No time, wind, terrain-material or visual-motion state enters authoritative saves.

## Tradeoffs

A single slope-adaptive projection preserves the mandatory thirteen-sample limit. UV transitions and dominant-axis seams on arbitrary cliffs are bounded authoring limitations, rather than a claim of seamless triplanar coverage. Micro detail is low-amplitude and filtered by its actual pixel footprint; compiled mipmaps and 8x anisotropic filtering remain the primary texture minification policy.

All assets retain their source masters, texture dimensions/caps, stable IDs and LOD chains. Existing UV parameters provide scale variation without unique texture sets or new dependencies. Smoothstep region feathers preserve the normalized four-layer control-map contract.

## Qualification

Native regression coverage includes deterministic captures, direct/linear/shadow/atmosphere combinations, resize, steep-rock projection with a textured test material and exact strategic micro-detail removal. Motion tests exercise root anchoring, complete culling sweep, periodicity, hidden-instance exclusion, unchanged batch counts and immutable snapshots. Compiler tests verify the refined production palette, texture references, deterministic UV compilation and invalid scale rejection.

Production hardware comparisons and fixed camera/capture instructions are recorded in WorldAssetPresentation and AssetPerformanceQualification. WARP establishes functional correctness only. Physical monitor readability, arbitrary moving-camera dominant-axis seams, long GPU soak and real device removal remain distinct manual qualification.
