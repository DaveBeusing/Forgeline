# Runtime Texture and Material Binding

## Status

Accepted for the Direct3D 12 world-rendering path.

## Context

FORGELINE source assets already compile through stable asset IDs into runtime mesh, texture, and material payloads. Terrain background textures were previously made visible by CPU sampling compiled RGBA8 data while terrain presentation vertices were built. That path preserved architecture boundaries but did not provide the common GPU texture/material foundation required by units, buildings, resources, infrastructure, props, effects, or later terrain splatting.

The renderer must support real immutable GPU textures, supplied mip chains, color-space correctness, deterministic missing-resource behavior, and material reuse without placing Direct3D 12 concepts in simulation/gameplay state.

## Decision

### Runtime texture contract

Compiled runtime textures expose:

- width and height;
- RGBA8 runtime format;
- explicit sRGB or linear color space;
- semantic usage;
- one or more supplied mip levels;
- per-mip width, height, row pitch, and byte payload.

Version-1 RGBA8 payloads remain readable as sRGB color textures for compatibility. Version-2 payloads carry the explicit metadata above.

Normal, ORM, and generic data textures require linear color space. Color space is not inferred from file names at runtime.

### Graphics ownership

`ForgeLine.Graphics` owns:

- D3D12 default-heap texture resources;
- staged copy uploads and resource-state transitions;
- upload retirement fences;
- shader-visible SRV descriptors;
- descriptor allocation/reuse;
- static sampler definitions;
- texture lifetime diagnostics.

Texture creation uploads every supplied mip once. Static world material textures are not re-uploaded per frame.

One live texture consumes one shader-visible SRV descriptor. Descriptor indices return to the allocator only after the owning texture is disposed and GPU work is retired.

### Sampler policy

The graphics pipeline owns centralized static samplers.

- `s0`: world-material sampling using wrap addressing, linear mip filtering, and anisotropic filtering.
- `s1`: linear clamp sampling for resources that require edge clamping.

Presentation and gameplay code do not create duplicate sampler objects.

### Runtime material contract

Runtime materials remain identified by stable asset IDs and expose:

- Base Color texture;
- Normal texture;
- ORM texture;
- optional Emissive texture;
- base-color factor;
- roughness multiplier;
- metallic multiplier;
- emissive multiplier;
- UV scale.

The object renderer batches by runtime mesh and material identity. Material parameters travel as presentation-side instance data. The renderer binds Base Color, Normal, ORM, and Emissive textures through the graphics interface; it does not store D3D12 descriptor handles in render/game state.

The current material shader samples all four channels. The existing lighting model remains intentionally limited, so Normal/ORM influence is conservative until a fuller lighting path is introduced.

### Fallback behavior

Missing optional maps use deterministic resources:

- Base Color: white;
- Normal: flat tangent-space normal;
- ORM: AO 1, roughness 1, metallic 0;
- Emissive: black.

A missing/corrupt explicitly referenced texture or required material uses a visible magenta development fallback and records a binding diagnostic. Rendering continues in a controlled degraded state rather than using stale/undefined descriptors.

### Layer boundaries

`ForgeLine.Assets` owns serialized runtime texture/material contracts and stable identifiers.

`ForgeLine.Presentation` resolves those IDs into presentation-owned material resources and applies gameplay-readable state tinting.

`ForgeLine.Graphics` owns GPU resources and Direct3D 12 implementation details.

Simulation, Game, and Headless do not depend on graphics resources, descriptors, or materials.

## Alternatives considered

### Keep CPU texture sampling for all world objects

Rejected. It prevents normal/ORM/emissive material channels, increases CPU-side surface processing, and cannot provide the common production material path required by repeated units and buildings.

### Put descriptor indices in material/game data

Rejected. Descriptor indices are graphics-device lifetime state and would couple persistent/presentation contracts to one D3D12 heap instance.

### Introduce bindless or virtual texturing now

Rejected. The current Vertical Slice does not require that complexity. A bounded SRV heap with explicit material slots is simpler, measurable, and sufficient for the present RTS content scale.

### Infer texture usage from names

Rejected. File naming is an authoring convention, not a reliable runtime color-space contract.

## Consequences

- object materials can sample real GPU textures while simulation remains graphics-free;
- existing untextured materials continue through deterministic fallback maps;
- supplied mip chains and sRGB/linear metadata are preserved to the GPU;
- repeated meshes with different materials form distinct render batches;
- resource diagnostics can identify descriptor pressure, texture residency, and binding failures;
- disposal currently retires GPU work before returning texture descriptors, favoring correctness over aggressive asynchronous destruction;
- future terrain splatting can reuse the same texture/material resource foundation.

## Re-evaluation criteria

Revisit the binding model if measured descriptor pressure, draw-call pressure, texture residency, or material-count growth makes explicit per-material bindings a demonstrated bottleneck; if texture arrays/bindless resources become necessary for terrain or large content sets; or if the lighting model requires a different normal/tangent/material parameter layout.
