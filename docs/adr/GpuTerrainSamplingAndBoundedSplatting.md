# GPU Terrain Sampling and Bounded Splatting

## Status

Accepted.

## Context

The previous terrain presentation made compiled terrain imagery visible by sampling texture payloads on the CPU while constructing chunk vertices and storing the result as vertex color. That path preserved asset separation but could not provide mipmapped close-detail sampling, anisotropic filtering, tangent-space normal detail, ORM blending, or scalable local material transitions.

FORGELINE terrain remains gameplay-significant, so visual blending must not become an implicit source of collision, navigation, movement, or simulation terrain classification.

## Decision

Terrain rendering uses a bounded four-layer-per-chunk splat model.

- the global Central Divide terrain library contains eight stable material IDs;
- each chunk selects four deterministic active materials from normalized presentation weights;
- a 33 x 33 RGBA control texture stores the local four-layer weights and a normalized mip chain;
- zero/invalid control weights resolve to layer zero;
- tiled Base Color, Normal, and ORM resources come from the compiled runtime material contract;
- one terrain draw binds thirteen SRVs: one control texture plus four resources for each material channel family;
- material UVs are world-X/Z based and apply explicit per-material tile scale;
- material textures use the shared anisotropic/trilinear wrap sampler;
- control maps use the shared linear clamp sampler;
- normal samples are decoded, transformed, blended, and renormalized;
- ORM samples remain linear;
- vertex color is retained only as a low-frequency macro/readability multiplier.

The generic graphics pipeline SRV limit is sixteen, leaving a bounded margin above the thirteen-slot terrain contract without introducing an unbounded bind model.

Map artifact format version 2 records the terrain visual profile, control encoding, four-layer limit, control resolution, and ordered material library. Chunk control data remains a deterministic presentation product of the terrain/profile and is not simulation state.

## Performance budget

A visible terrain chunk requires one indexed draw, thirteen stable texture bindings, and at most thirteen explicit shader texture samples per pixel before hardware anisotropic filtering. Material GPU resources are cached/reused; each chunk owns one persistent control texture and does not allocate descriptors per frame.

Central Divide is 12 x 12 chunks, so the current map owns 144 small 33 x 33 control textures plus mips. Diagnostics expose visible terrain draw count, control-texture count, texture bindings/sample budget, CPU terrain submission time, total resident texture bytes, and SRV descriptor usage. Hardware-dependent frame/GPU timings remain qualification measurements rather than deterministic CI thresholds.

## Consequences

Close tactical zoom receives real mipmapped Base Color and normal detail, while strategic zoom naturally selects lower compiled mips. Chunk seams do not introduce UV discontinuities because tiled material coordinates use world space. The four-layer cap keeps sample and binding cost predictable.

Adding more global terrain materials does not increase per-pixel sampling cost. Future virtual texturing, texture arrays, weather layers, or more sophisticated macro variation must preserve explicit resource budgets and the separation between visual material blending and gameplay terrain state.
