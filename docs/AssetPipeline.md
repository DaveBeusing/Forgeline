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
- optional `NORMAL` as FLOAT `VEC3`;
- optional `TEXCOORD_0` as FLOAT `VEC2`;
- optional indices using unsigned byte, unsigned short, or unsigned int;
- embedded data-URI buffers;
- external buffers for `.gltf`;
- the binary buffer chunk for `.glb`;
- interleaved buffer views through `byteStride`.

Sparse accessors and non-triangle primitive modes are rejected with diagnostics.

Meshes compile into a ForgeLine runtime payload containing a versioned interleaved vertex stream and 32-bit index stream. Bounds are calculated from imported positions and recorded in the runtime manifest.

## Texture Import

Supported source extensions:

```text
.png
.tga
```

PNG baseline:

- 8-bit RGB or RGBA;
- non-interlaced;
- standard PNG filters;
- maximum dimension 16384.

TGA baseline:

- uncompressed true-color image type;
- 24-bit or 32-bit;
- top- or bottom-origin input;
- maximum dimension 16384.

Both formats compile to the same versioned RGBA8 runtime payload. This keeps renderer-facing texture data independent of the source file format.

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

Texture references are added to the material dependency graph automatically and must resolve to texture assets.

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

An unchanged asset with a matching build hash and existing runtime file is skipped. When a dependency changes, dependent build hashes change and those dependents are rebuilt.

The runtime manifest is emitted in stable ID order and runtime paths are deterministic.

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

The manifest exposes source/content hash, build hash, runtime/compiler version, dependencies, material/texture references, LODs, collision, sockets, and mesh bounds.

## Runtime Lookup

Runtime consumers load compiled content through:

```csharp
var catalog = RuntimeAssetCatalog.Load("assets/runtime");
var asset = catalog.Get(AssetId.Parse("unit.directorate.main_battle_tank"));
var content = catalog.Read(AssetId.Parse("unit.directorate.main_battle_tank"));
```

The catalog validates stable IDs, duplicate manifest entries, runtime path confinement, container headers, and expected asset types.

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
- invalid material parameters;
- runtime path escapes.

Tests cover successful compilation, invalid input, duplicate IDs, missing dependencies, LOD/collision/socket validation, dependency-triggered incremental rebuilds, stable runtime paths, PNG/TGA import, glTF/GLB import, bounds, and runtime catalog lookup.

## Current Boundary

This baseline intentionally does not add final Directorate art, animation retargeting, audio conversion, a generic editor framework, automatic content generation, GPU texture compression, mip generation, or final shipping-package optimization.

Those capabilities should extend this pipeline rather than create parallel asset formats.

## Committed Vertical Slice world assets

The first authored world set lives below `assets/source/world/`. It includes the eight terrain material slots, eight decal materials, reusable props, vegetation, four resource-deposit families, reduced LOD meshes, and strategic resource-symbol materials.

These files are normal compiler inputs. CI compiles the source tree into ignored `assets/runtime/` output before the Windows graphics smoke test. The client consumes only the runtime manifest and `.flasset` payloads; it does not read glTF or material-source JSON at runtime.

See [World Asset Presentation](WorldAssetPresentation.md) for how stable world asset IDs map to Central Divide presentation data.


## Committed Directorate Vertical Slice unit assets

The first production-oriented gameplay-unit set lives below `assets/source/units/directorate/`. Six unit families provide stable LOD0/LOD1/LOD2 meshes, separate collision assets, runtime materials, strategic-symbol materials, and required gameplay-facing sockets. The existing Combat Engineer reuses the Rifle Squad visual family rather than creating a duplicate asset lineage.

The runtime animation asset type is still intentionally absent, so unit source metadata does not claim unsupported animation references. Articulation and VFX integration points are established through named sockets instead.

See [Directorate Unit Assets](DirectorateUnitAssets.md) for the complete unit presentation contract.
