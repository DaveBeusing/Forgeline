# Asset Pipeline

## Purpose

ForgeLine uses an explicit source-to-runtime asset boundary. Editable authoring files live under `assets/source/`; the Asset Compiler validates and compiles them into runtime-only artifacts under `assets/runtime/`. Runtime code consumes the compiled manifest and `.flasset` files through `ForgeLine.Assets` and never discovers authoring files directly.

The initial production baseline covers static glTF/GLB meshes, PNG/TGA textures, PBR-oriented material definitions, stable asset IDs, dependency validation, LOD/collision/socket metadata, incremental compilation, deterministic runtime paths, actionable diagnostics, and runtime lookup/loading.

## Canonical Directories

```text
assets/
  source/   editable source assets and *.asset.json metadata
  runtime/  compiler-generated output only
```

`assets/runtime/` is generated and must not be edited or committed as authored content.

## Stable Asset IDs

Every source asset is declared by one `*.asset.json` file and must define an explicit stable ID.

Examples:

```text
unit.directorate.main_battle_tank
texture.directorate.armor_albedo
material.directorate.armor
building.directorate.command_core
```

IDs are lowercase dot-separated identifiers. Segments may contain `a-z`, `0-9`, `_`, and `-`. Runtime paths are derived from the ID rather than file-system enumeration order, so moving unrelated files cannot renumber or reorder persistent references.

Once an ID is referenced by game data, treat it as a persistent content contract.

## Asset Metadata

A source asset definition is stored next to or near its authoring file:

```json
{
  "id": "unit.directorate.main_battle_tank",
  "type": "mesh",
  "source": "directorate_main_battle_tank.glb",
  "scale": 1.0,
  "materialReferences": [
    "material.directorate.armor"
  ],
  "lods": [
    {
      "level": 1,
      "assetId": "unit.directorate.main_battle_tank_lod1",
      "maxDistance": 140.0
    }
  ],
  "collisionRequired": true,
  "collisionReference": "unit.directorate.main_battle_tank_collision",
  "sockets": [
    {
      "name": "muzzle",
      "x": 0.0,
      "y": 1.8,
      "z": 4.2
    }
  ]
}
```

Supported metadata includes:

- source path;
- stable asset ID and asset type;
- explicit generic dependencies;
- material and texture references;
- LOD references;
- collision reference and collision requirement;
- named sockets/attachment points;
- reserved animation references;
- canonical source scale.

Animation references are currently rejected because the runtime animation asset type is not implemented yet. This prevents authoring metadata from implying unsupported runtime behavior.

## Source Scale and Coordinates

The baseline requires authored content to use one engine unit per meter and therefore requires `scale: 1.0`. Non-unit import scaling is rejected instead of being applied implicitly. This keeps source content, collision, sockets, simulation dimensions, and renderer expectations on the same physical scale.

Coordinate-axis normalization and broader DCC conversion policy should be added deliberately when the production art toolchain requires it; the first importer does not silently rotate or rescale authored geometry.

## Static Mesh Import

Supported source extensions:

```text
.gltf
.glb
```

Current supported glTF subset:

- glTF 2.x;
- static mesh primitives;
- triangle primitive mode;
- required `POSITION` as FLOAT `VEC3`;
- optional source `NORMAL` as FLOAT `VEC3`;
- optional `TEXCOORD_0` as FLOAT `VEC2`;
- optional source `TANGENT` as FLOAT `VEC4`;
- optional indices using unsigned byte, unsigned short, or unsigned int;
- embedded data-URI buffers;
- external buffers for `.gltf`;
- the binary buffer chunk for `.glb`;
- interleaved buffer views through `byteStride`.

Sparse accessors and non-triangle primitive modes are rejected with diagnostics.

### Production mesh vertex contract

Runtime mesh version 2 uses one 48-byte static-mesh vertex:

```text
Position  float3  offset  0
Normal    float3  offset 12
UV0       float2  offset 24
Tangent   float4  offset 32
```

`Tangent.xyz` stores the tangent direction and `Tangent.w` stores handedness for bitangent reconstruction. The renderer reconstructs `B = cross(N, T) * handedness` and evaluates material normal maps in tangent space.

Normals are normalized during import. If source normals are absent or unusable, the compiler deterministically accumulates indexed triangle face normals and normalizes the result. Existing position-only production assets therefore remain compilable without changing simulation or collision data.

When a material uses a normal map and valid UV0 exists but source tangents are absent or invalid, the compiler deterministically generates tangents using triangle position/UV derivatives, Gram-Schmidt orthogonalization against the vertex normal, and a signed handedness term. Mirrored UVs preserve the opposite handedness. A normal-mapped section that cannot form a valid tangent basis is explicitly downgraded to the development fallback rather than receiving fabricated tangent-space lighting.

### Material slots and migration

The ordered `materialReferences` list in the mesh asset definition is the stable material-slot table. A glTF primitive `material` index maps to the same slot index. If the source primitive omits `material`, a mesh with exactly one stable material reference uses slot zero; multiple declared slots require an explicit primitive assignment.

Runtime mesh version 2 serializes:

- explicit vertex-attribute flags;
- the production vertex stream;
- 32-bit indices;
- stable material IDs;
- contiguous draw sections with material-slot indices.

The current presentation path is optimized for the production assets in `master`, which use one material identity per render mesh/LOD. Multiple sections that resolve to different material slots remain represented in the runtime asset but are surfaced as a development fallback until split-draw batching is required by authored content. This keeps draw-call fragmentation visible instead of silently multiplying submissions.

A textured material requires UV0. A normal-mapped material additionally requires a valid tangent basis. Existing meshes that do not yet satisfy those requirements remain visible through the controlled development fallback and emit `ASSETW002`. Every compiled mesh emits `ASSETI002` with vertex/index/section/material counts, UV/tangent availability, generated normal/tangent counts, and fallback-section count.

LOD references must preserve the same stable material-reference identity and slot order. A lower LOD may simplify geometry, but it cannot silently change the material identity used for gameplay readability.

Runtime mesh version 1 is intentionally not reinterpreted as version 2. Stale runtime mesh payloads fail with an explicit recompile diagnostic; source assets remain authoritative and the Asset Compiler regenerates the new format.

Bounds are calculated from imported positions and recorded in the runtime manifest.

## Texture Import

Supported editable source extensions:

```text
.png
.tga
```

PNG import supports non-interlaced 8-bit RGB/RGBA input with standard PNG filters. TGA import supports uncompressed 24-bit and 32-bit true-color input with top- or bottom-origin data. Both paths preserve alpha and reject malformed input or dimensions above 16384.

Texture semantics are declared explicitly in the source asset definition. Runtime code never infers usage from a file-name suffix.

```json
{
  "id": "texture.directorate.armor_albedo",
  "type": "texture",
  "source": "armor_albedo.png",
  "textureUsage": "baseColor",
  "textureColorSpace": "srgb",
  "textureGenerateMipmaps": true,
  "textureMaxMipLevels": 8
}
```

Supported production usages are:

- `BaseColor`: sRGB. `Color` remains a compatibility alias for the same runtime value.
- `Normal`: linear tangent-space RGB normal data.
- `Orm`: linear packed data with R = ambient occlusion, G = roughness, B = metallic.
- `Emissive`: explicit sRGB or linear color space.
- `TerrainControl`: linear RGBA terrain/splat weights.
- `GenericData`: linear arbitrary data.

Base Color is required to use sRGB. Normal, ORM, Terrain Control, and Generic Data are required to use linear color space. Invalid combinations fail during compilation.

### Offline mip generation

Mipmaps are generated by the Asset Compiler and serialized into the runtime texture. The renderer never decodes PNG/TGA or generates static world-texture mips.

By default a full chain is generated to the valid terminal 1x1 level. `textureGenerateMipmaps: false` keeps only the source level. `textureMaxMipLevels` may deliberately cap a generated chain and must be between 1 and 32.

Filtering is usage-aware:

- sRGB Base Color and sRGB Emissive convert RGB samples to linear light, average them, then encode the result back to sRGB; alpha is averaged linearly;
- linear Emissive and Generic Data are averaged channel-by-channel;
- Normal maps decode tangent-space vectors, average them, and renormalize the result before re-encoding;
- ORM is averaged channel-by-channel without gamma conversion so the AO/Roughness/Metallic channel meanings remain intact;
- Terrain Control RGBA values are averaged as linear weights and renormalized so each generated texel totals 255. A fully zero source footprint resolves deterministically to the first layer at full weight.

Odd dimensions use deterministic area coverage when reducing to the next `max(1, floor(size / 2))` mip dimension, so edge texels are not silently discarded.

### Runtime format and compression

The current compiler emits `Rgba8Unorm` payloads. The Direct3D 12 runtime applies the sRGB interpretation from texture metadata when creating the GPU resource/view.

GPU-native BC compression is intentionally not claimed yet. The repository does not currently contain a justified deterministic BC encoder, and adding one solely to report compressed support would introduce unnecessary dependency and licensing/maintenance cost. The uncompressed path is correct and measurable: compiler diagnostics report resident mip bytes and serialized payload bytes, while runtime qualification reports total texture footprint. A future BC implementation must extend the same usage/color-space/mip contract and demonstrate deterministic output.

### Runtime texture representation

Runtime texture payload version 3 stores:

- width and height;
- runtime format;
- explicit color space;
- semantic usage;
- mip count;
- one subresource-table entry per mip containing width, height, row pitch, absolute payload offset, and byte count;
- the contiguous mip byte payload.

Version-1 and version-2 texture payloads remain readable for compatibility. Source hash, build hash, compiler version, and runtime version remain in the surrounding `.flasset` metadata/manifest instead of being duplicated inside the GPU-facing texture bytes.

## Material Definitions

Material source files are JSON and are referenced by an asset definition with `"type": "material"`.

Example:

```json
{
  "baseColorTexture": "texture.directorate.armor_albedo",
  "normalTexture": "texture.directorate.armor_normal",
  "ormTexture": "texture.directorate.armor_orm",
  "emissiveTexture": "texture.directorate.armor_emissive",
  "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
  "metallicFactor": 1.0,
  "roughnessFactor": 0.5
}
```

Texture references are added to the material dependency graph automatically and must resolve to texture assets. The compiler also validates slot semantics: `baseColorTexture` requires Base Color usage, `normalTexture` requires Normal, `ormTexture` requires ORM, and `emissiveTexture` requires Emissive. A wrong usage fails before runtime output is published.

At runtime, material payloads are parsed into the stable material contract rather than interpreted as D3D12 state. Presentation resolves material IDs to Base Color, Normal, ORM, and optional Emissive GPU texture bindings plus base-color factor, roughness/metallic multipliers, emissive multiplier, and UV scale. `ForgeLine.Graphics` owns the resulting texture resources, SRVs, samplers, upload synchronization, and descriptor lifetime.

Missing optional maps are deterministic: white Base Color, flat tangent-space normal, neutral ORM (`R=1 AO`, `G=1 roughness`, `B=0 metallic`), and black Emissive. A referenced missing/corrupt texture or material uses a visible magenta development fallback and records diagnostics instead of producing undefined rendering.

The ORM convention remains:

```text
R = ambient occlusion
G = roughness
B = metallic
```

## Dependency Graph

The compiler builds an explicit graph from:

- generic dependencies;
- material references;
- texture references;
- material-owned texture references;
- LOD references;
- collision references.

Compilation fails before output publication when a required reference is missing or when a circular dependency exists. Typed references are also validated, for example material references must target materials and collision/LOD references must target meshes.

## Incremental Compilation

Each source asset receives:

- a source hash derived from its `*.asset.json` metadata and source content;
- external `.gltf` buffer content in the source hash where applicable;
- a build hash derived from the source hash, compiler/runtime version, and dependency build hashes.

An unchanged asset with a matching build hash and existing runtime file is skipped. Texture import settings are part of the source `*.asset.json` bytes and therefore participate in the source hash; changing usage, color space, mip generation, or mip limits invalidates only the affected texture and its dependency chain. When a referenced dependency changes, dependent build hashes change and those dependents are rebuilt.

The compiler/runtime version also participates in the build hash, so changes to the compilation contract invalidate stale cached output deterministically. The runtime manifest is emitted in stable ID order and runtime paths are deterministic.

## Runtime Output

Generated files use the `.flasset` container. The container records:

- runtime header version;
- runtime asset type;
- serialized asset metadata;
- compiled payload.

The manifest is written to:

```text
assets/runtime/manifest.json
```

The manifest exposes source/content hash, build hash, runtime/compiler version, dependencies, material/texture references, LODs, collision, sockets, and mesh bounds. Texture payloads carry only runtime-required texture metadata/subresources; source/build provenance remains in this existing manifest/container metadata.

## Runtime Lookup

Runtime consumers load compiled content through:

```csharp
var catalog = RuntimeAssetCatalog.Load("assets/runtime");
var asset = catalog.Get(AssetId.Parse("unit.directorate.main_battle_tank"));
var content = catalog.Read(AssetId.Parse("unit.directorate.main_battle_tank"));
```

The catalog validates stable IDs, duplicate manifest entries, runtime path confinement, container headers, and expected asset types.

## Directorate building and infrastructure source sets

The first building and infrastructure production baseline uses the same compiler contract as units and world assets.

Editable building sources live under:

```text
assets/source/buildings/directorate/
```

Editable road and bridge sources live under:

```text
assets/source/infrastructure/directorate/
```

The nine primary building assets expose LOD1/LOD2 references and share `building.directorate.module.collision_box` as a simplified visual/tooling collision contract. Shared construction and state modules reduce duplicate geometry while preserving stable IDs. Directorate road and bridge meshes compile through the same runtime manifest and are consumed by presentation only; authoritative building footprints, logistics edges, and navigation blockers remain game/simulation data.

## Combat, destruction, and logistics VFX source set

The first VFX baseline is authored below:

```text
assets/source/vfx/
```

It uses ordinary compiled mesh/material assets because the current runtime contract intentionally exposes only Mesh, Texture, and Material asset types. Effect lifetime, distance reduction, event mapping, pooling, and socket behavior are presentation contracts rather than source-asset metadata.

Stable IDs use `vfx.combat.*`, `vfx.destruction.*`, `vfx.logistics.*`, and `material.vfx.*`. Runtime code never reads the glTF or material authoring files directly.

## RTS UI semantic asset set

The initial RTS information-layer source set lives below:

```text
assets/source/ui/
```

Semantic UI IDs use the `ui.icon.*` namespace and compile through the existing material asset path. The current renderer draws compact procedural glyph geometry while compiled material records provide stable runtime identity and tint data. This avoids introducing a parallel atlas/vector format before the asset runtime owns one.

The set covers resources, unit/building roles, commands, cursors, supply states, minimap/strategic symbols, and status indicators. Runtime UI code resolves IDs through `RtsUiIconCatalog`; features do not own duplicate file-path copies. Future texture/vector icon payloads may replace the visual representation while preserving these stable IDs.

See [RTS Information Layer](RTSInformationLayer.md) for mapping and presentation ownership.

## Compiler Usage

From the repository root:

```powershell
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj -- \
  --source assets/source \
  --runtime assets/runtime
```

Force a clean rebuild:

```powershell
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj -- \
  --source assets/source \
  --runtime assets/runtime \
  --clean
```

The command exits non-zero on compiler errors and prints diagnostic codes with the offending stable ID and source path where available.

## Validation

The compiler currently rejects or reports:

- malformed or duplicate stable IDs;
- missing source files;
- unsupported source extensions;
- non-unit source scales;
- missing dependencies;
- typed-reference mismatches;
- circular dependency graphs;
- malformed LOD metadata;
- missing required collision references;
- duplicate or non-finite socket metadata;
- unsupported animation references;
- invalid mesh/accessor/buffer data;
- invalid or unsupported PNG/TGA content;
- invalid texture usage/color-space combinations;
- invalid mip-generation settings;
- invalid material parameters;
- material texture slots that reference the wrong texture usage;
- malformed runtime texture subresource tables or mip payloads;
- runtime path escapes.

Every newly compiled texture emits an `ASSETI001` information diagnostic containing stable asset ID/source path plus usage, color space, dimensions, mip count, runtime format, resident mip bytes, and serialized payload size. Invalid source/import state retains the existing asset diagnostic codes and source/ID context.

Tests cover successful compilation, invalid input, duplicate IDs, missing dependencies, LOD/collision/socket validation, dependency-triggered incremental rebuilds, stable runtime paths, PNG/TGA import, deterministic mip dimensions, sRGB filtering, normal renormalization, ORM preservation, Terrain Control normalization, byte-stable clean recompilation, texture-setting cache invalidation, runtime texture round trips, malformed subresource rejection, glTF/GLB import, bounds, and runtime catalog lookup.

## Current Boundary

This baseline intentionally does not add final Directorate art, animation retargeting, audio conversion, a generic editor framework, automatic content generation, GPU block compression, texture streaming, virtual texturing, or final shipping-package optimization.

Offline mip generation is now a compiler responsibility and the runtime consumes the compiled chain directly. Future BC compression, texture arrays, or streaming must extend the same runtime texture contract rather than create parallel asset formats.

## Committed Vertical Slice world assets

The first authored world set lives below `assets/source/world/`. It includes eight terrain material slots, nine reusable terrain texture sources, eight decal materials, reusable props, vegetation, four resource-deposit families, reduced LOD meshes, and strategic resource-symbol materials.

The original Base Color sources `texture.world.terrain.dry_dirt`, `texture.world.terrain.cracked_earth`, `texture.world.terrain.rocky_scrub`, and `texture.world.terrain.dark_ash` remain shared across materials. `texture.world.terrain.concrete_base` adds the concrete baseline. Reusable `soil_normal`, `rock_normal`, and `hard_surface_normal` sources provide tangent-space detail families, and `terrain_orm` provides the shared linear ORM baseline. All eight terrain materials therefore expose Base Color, Normal, and ORM through the same runtime material contract.

PNG/TGA authoring inputs compile through the standard deterministic texture pipeline with full mip chains and explicit BaseColor/Normal/ORM usage metadata. Terrain material textures are resolved into shared GPU resources; runtime code never reads editable image or material-source files. CI compiles the source tree into ignored `assets/runtime/` output before the Windows graphics smoke test.

See [World Asset Presentation](WorldAssetPresentation.md) for how stable world asset IDs map to Central Divide presentation data.


## Committed Directorate Vertical Slice unit assets

The first production-oriented gameplay-unit set lives below `assets/source/units/directorate/`. Six unit families provide stable LOD0/LOD1/LOD2 meshes, separate collision assets, runtime materials, strategic-symbol materials, and required gameplay-facing sockets. The existing Combat Engineer reuses the Rifle Squad visual family rather than creating a duplicate asset lineage.

The runtime animation asset type is still intentionally absent, so unit source metadata does not claim unsupported animation references. Articulation and VFX integration points are established through named sockets instead.

See [Directorate Unit Assets](DirectorateUnitAssets.md) for the complete unit presentation contract.


## Runtime Qualification

Every successful compiler invocation now performs a second runtime qualification pass. The pass opens every generated payload, decodes texture/material runtime payloads, validates runtime references and LOD chains, records generated asset footprint by type, and reports the largest runtime assets. This catches malformed texture tables, mip ranges, dimensions, or material payloads before packaging.

CI writes the structured result to `artifacts/asset-qualification.json`. See [Asset Performance and Visual Qualification](AssetPerformanceQualification.md) for the complete acceptance and measurement procedure.
