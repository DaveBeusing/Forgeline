# Runtime Mesh Vertex and Material Contract

## Status

Accepted.

## Context

FORGELINE's production object renderer already owns GPU-resident Base Color, Normal, ORM, and Emissive textures, but the previous mesh payload carried only Position, Normal, and UV. Material identity was selected primarily by presentation catalogs and tangent-space normal mapping could not be evaluated from compiled mesh data.

The production asset set also contains legacy position-only glTF meshes. A migration therefore has to improve the render contract without coupling simulation/collision data to render geometry or making old authored content disappear without diagnosis.

## Decision

Static runtime meshes use version 2 with a 48-byte vertex containing Position float3, Normal float3, UV0 float2, and Tangent float4. Tangent W is signed handedness.

The Asset Compiler owns mesh semantic completion:

- source normals are normalized;
- absent/unusable normals are deterministically generated from indexed triangles;
- source UV0 is preserved explicitly;
- source tangents are normalized when valid;
- tangent-space tangents are generated deterministically when a referenced material has a normal map and UV0 is valid;
- mirrored UV orientation is retained through tangent handedness;
- stable mesh `materialReferences` form the material-slot table;
- primitive material indices map to the same stable slot indices;
- LOD material identity and slot order must remain consistent.

A section whose textured material requires attributes the mesh cannot provide is marked as a development fallback and diagnosed during compilation. No invalid tangent basis is fabricated.

Runtime mesh version 1 is not silently interpreted as version 2. Runtime output is regenerated from source assets.

## Rendering behavior

Presentation uploads the compiled 48-byte vertex stream directly and retains the stable single-material identity used by current production meshes. The D3D12 textured pipeline declares Position, Normal, UV0, and Tangent inputs. The shader reconstructs TBN and evaluates the normal map in tangent space.

Current authored meshes use one material identity per draw mesh. The runtime format can represent multiple sections, but a mesh that requires different material slots is reported as a development fallback until split-draw batching is justified by production content. This prevents arbitrary material fragmentation from becoming an invisible draw-call cost.

Instance color remains a deliberate readability/team tint multiplier. It is not the primary material surface color.

## Architecture consequences

`ForgeLine.Assets` owns the runtime mesh contract and validation.

The Asset Compiler owns glTF attributes, normal/tangent generation, stable material-slot compilation, and migration diagnostics.

`ForgeLine.Graphics` continues to own D3D12 resources and input-layout implementation.

`ForgeLine.Presentation` resolves compiled mesh/material resources and submits them. It does not mutate simulation state.

`ForgeLine.Headless`, simulation, collision, and navigation remain independent from Graphics and from render-mesh UV/tangent data.

## Re-evaluation criteria

Revisit the single-material draw restriction when authored production assets genuinely require multiple material slots and measurements justify split draws, mesh merging, texture arrays, or another batching strategy. Any extension must preserve stable material IDs and keep draw-call/resource cost observable.
