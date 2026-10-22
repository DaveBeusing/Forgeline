# Resource deposit PBR surfaces

Initial integration base: `b726ff9c00b7dc74cf285219fa58440acf663e16`, version
0.1.454, including the modular resource cluster sources. The four existing
`material.world.resource.<family>` IDs, factors and one-slot mesh bindings remain
stable. Each now references original family-specific BaseColor, Normal and ORM
sources. No mesh, placement, resource identity, simulation, renderer or shader
source is changed.

Before PR publication, synchronized with `master` at
`8c2609b3afaaf8899f81e66b997dd139f7a84b52`, version 0.1.455, including
[the visual-language specification](ResourceDepositVisualLanguage.md). Its
schema-1 authored UV mode and canonical family dispatch stay unchanged. The
additional surface IDs are an additive authoring inventory; selecting them needs
a separately approved presentation contract, including batching/budget review.
The visual-language roughness ranges and inclusion coverage are art-review targets,
not measurements or acceptance claims for these unqualified maps.

## Surface design and editable sources

`assets/source/world/authoring/resource_surfaces/recipes.json` contains the original
mathematical recipes, fixed seeds, palettes in linear reflectance, relief scales,
roughness values and bounded inclusion metallicity. Macro fields control host/core
exposure, meso fields define geological structure, and finer fields supply grain,
small roughness changes and normal relief. Variation is spatial and correlated
across the PBR channels, not just an independent color tint.

| Family | Macro/meso identity | Surface treatment |
| --- | --- | --- |
| Ferrous Ore | Warped stratified veins and fracture bands | Iron-oxide host/patina, darker core, brighter fresh breaks, sparse exposed metallic inclusions |
| Silicates | Intersecting cleavage directions and cellular facets | Pale mineral plates, broad rough host, smoother breaks, dusty seams; dielectric throughout |
| Volatiles | Sealed cellular nodules and capped concentric layers | Muted mineral shells and lenses, rough patina and dust; dielectric, without wet pipes, liquid transfer or emissive |
| Rare Elements | Sparse inclusions within a substantial muted matrix | Gray-violet host, exposed fracture zones and limited conductive inclusions; no neon surface |

Every family has `weathered`, `fresh_fracture`, `patinated` and `inclusion_rich`
patches in a 2×2 atlas. Independent fields and different weathering/fracture/
inclusion weights change spatial structure, roughness and relief. Each patch
mixes host, core, fracture, dirt, patina and inclusion zones.

Twelve RGBA8 1024×1024 PBR PNGs live under `world/textures/resources`. Four saved
Blender 5.2.2 material projects under `world/authoring/resource_surfaces` pack the
actual BaseColor/Normal/ORM images plus authoring-only height and zone images.
They provide four editor swatches per family. The auxiliary zone image stores
core/fracture/patina/inclusion in RGBA; its alpha is a mask, not opacity. Dirt is
derived from the recipe's finer dust field. Auxiliary images have no runtime asset
descriptors and do not allocate renderer textures or displace geometry.

The JSON recipe and procedural source are editable generation inputs. Packed PBR
image layers are editable raster sources. Edit/repack/save those images and Export
extracts their actual packed PNG bytes. Preview-node edits are deliberately rejected
rather than silently ignored: rebake recipe changes using Author in a fresh artifact
root. Editing an auxiliary diagnostic mask alone does not rebake the final PBR maps.

The existing geometry masters remain unchanged, including their historical shared
atlas preview graphs. Use the new material projects to inspect the new surfaces.
Runtime rendering resolves the unchanged material IDs through the current material
JSON, not a geometry master's preview graph.

## Shared UV, slot and shader contract

Both retained legacy geometry and the cluster family occupy the mineral atlas
rectangle U = 0.75–1.0, V = 0.5–0.75 with an authored 0.018 inset. Keep that UV set
and the single family material slot unchanged. For atlas patch index 0–3:

```text
column = index % 2; row = index / 2 (integer)
uvScale  = [2, 2]
uvOffset = [-1.5 + column/2, -1.0 + row/2]
sampleUv = meshUv * uvScale + uvOffset
```

The inset maps to 0.036–0.464 inside each half-atlas quadrant. Initial authoring
extends a 32-pixel gutter within each 512-pixel source patch. The inset is about
37 source pixels, so the visible UV domain stays inside those extensions. Each
patch field is periodic before gutter extension; distinct patches are not expected
to be continuous with each other. At the coarsest full-atlas mips patch means mix;
they deliberately retain the same family palette.

The existing instanced pixel shader already applies this scale and offset before
sampling all maps. Existing slots remain BaseColor t0, Normal t1, ORM t2, with the
neutral emissive slot t3. There is no new pass, vertex input, per-instance stream,
texture sample or sampler. Positive UV scale preserves tangent handedness. Normal
maps encode normalized tangent vectors with positive Z and matching U/V gradients.

The stable family material ID selects `weathered`. Additive IDs
`material.world.resource.<family>.<variant>` select the other patches using the
same three family textures. The existing family factors are retained for flat-color,
UI and state-tint compatibility. Authored BaseColor is encoded from target linear
zone reflectance divided by that factor, then converted to sRGB; this avoids
accidentally applying the family hue twice.

The additional material IDs are inputs to the presentation-only
[resource selector](ResourceDepositRendering.md). It maps approved surface indices
to these existing IDs through the current shader interface. Canonical IDs still
select the weathered default. Authored map/deposit keys select fixed root-cluster
variants; full multi-cluster field composition and its native qualification remain
a separate integration gate. Full repeat suppression is not claimed.

Untouched/Active/Depleted keep their existing authoritative presentation tint.
Material `emissiveMultiplier` is zero and no resource emissive texture is bound.

## Channels, mip policy and budget boundary

BaseColor uses sRGB; Normal and ORM use linear decoding. ORM packs ambient
occlusion into R, roughness into G and metallicity into B. Local AO stays in
0.82–1.0 and represents fine cracks/dust, not baked directional or large-scale
lighting. Roughness is clamped to 0.36–0.97. Silicates and Volatiles have zero
metallicity; Ferrous/Rare allow only bounded inclusion metal response. Normals
are derived from the same height/zone field and normalized before 8-bit encoding.

The texture descriptors request BC7 Balanced, generated full mip chains and maximum
runtime dimensions of 512 BaseColor, 512 Normal and 256 ORM. The existing material
sampler remains 8× anisotropic. No graphics quality setting or global texture,
descriptor, residency, preparation, sample or performance budget is relaxed.

Theoretical logical BC7 mip bytes for those twelve textures total **3,146,048**
bytes before container metadata or native allocation alignment. This is an
allocation estimate, not a compiled catalog or GPU measurement. The shared
Directorate textures remain used by other assets, so no offsetting residency
saving is claimed. The existing compiled-texture budget must be checked after
manual compilation; adding these maps does not establish that the global gate
passes. Source resolution and source PNG/Blender sizes are separate metrics.

## Provenance and execution status

The fields, palettes, masks and maps are original mathematical artwork authored
for this source set. No external image, model, logo, game artwork, asset pack or
downloaded texture was used. Generation uses the repository's existing PNG writer
and the pinned Blender/NumPy authoring toolchain; neither becomes a player or
headless dependency.

Only initial procedural image creation and native material-project saving were
executed. The resulting hashes, paths and source sizes are recorded in
`resource_surfaces/sources.json`, marked **authored-not-validated**.
**No builds, tests, independent native reopening/export comparisons, CI dispatches,
runtime catalog qualification or GPU captures were executed.** No visual goldens
were changed. Existing GitHub PR workflows may execute automatically; their results
must be reported separately, and human art approval remains pending.

Prepared source contracts cover stable family IDs/factors, 12 texture bindings,
20 default/variant materials, color-space/ORM packing, opaque PBR alpha, normal
length/orientation, roughness/metal bounds, distinct patches/families, native/map
hashes, full-mip BC7 descriptors and compatibility with the existing mesh UV inset.
The mesh-free material preparation helper now expects four default materials plus
twelve family-specific textures; each per-family native qualification seed still
contains exactly one material and three textures.

## Manual acceptance commands — NOT EXECUTED

From the repository root:

```powershell
python build/resource-deposits/surface_contract.py
$blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
# Extract actual packed image layers from saved native projects into fresh roots:
& $blender --background --factory-startup --disable-autoexec --offline-mode --threads 1 --python-exit-code 23 --python build/resource-deposits/surface_author.py -- --action Export --output-root artifacts/resource-surface-export-a
& $blender --background --factory-startup --disable-autoexec --offline-mode --threads 1 --python-exit-code 23 --python build/resource-deposits/surface_author.py -- --action Export --output-root artifacts/resource-surface-export-b
python build/resource-deposits/surface_contract.py --source-root artifacts/resource-surface-export-a/source
# Recreate maps/native projects from deliberately edited JSON recipes, without overwriting saved masters:
& $blender --background --factory-startup --disable-autoexec --offline-mode --threads 1 --python-exit-code 23 --python build/resource-deposits/surface_author.py -- --action Author --output-root artifacts/resource-surface-rebake
```

Compare repeated packed exports byte-for-byte with each other and the recorded
layer hashes. Repack/save a deliberate pixel edit and ensure it survives Export;
changed preview operations, family recipes, wrong color tags, missing layers or
incompatible image sizes must fail. Preview graph changes are not a procedural bake.

After manual source acceptance, run the canonical restore, build, asset compilation,
source-less package and test commands from `Development.md`. Inspect the actual
compiled mip counts, all material/texture bindings, global texture budget and native
resource lifetime. Do not accept the theoretical mip estimate as a passing budget.

Use ResourceDepositsGallery, ResourceDepositSingle, ResourceDeposits100 and
ResourceDeposits1000 at Close/Normal/Strategic zoom, 1080p/4K and Low/Medium/High.
Compare grass, rock and mud backgrounds in sun/shadow, dry/wet conditions and all
existing resource states. Check banding, normal seams/darkening, UV stretch,
excessive shine, coarse atlas bleed, mip shimmer, family readability and repetition.
Additional patch indices require their separate presentation selector before
claiming native coverage of every variant.
