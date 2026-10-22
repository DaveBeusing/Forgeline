# World Asset Presentation

## Purpose

Current authored groups, road-aligned material transitions, rooted volumetric
vegetation and qualification limits are described in [Environment composition](EnvironmentDressing.md).

The Vertical Slice world presentation baseline turns the Central Divide gameplay map into a readable RTS battlefield while keeping simulation, authoring assets, compiled runtime assets, and renderer concerns separate.

Runtime code never opens files from `assets/source`. CI and local authoring workflows compile those files with `ForgeLine.AssetCompiler` into `assets/runtime`; the Windows client consumes the resulting `RuntimeAssetCatalog`.

## Terrain material family

Terrain surface refinement keeps the eight IDs and four active layers per chunk. Base color remains sRGB; normals and ORM remain linear. The world sampler remains 8x anisotropic with the compiled mip chain. Terrain still binds thirteen textures and performs at most thirteen material/control samples per pixel. The direct root signature costs 63 DWORDs; shadow receivers retain buffered constants and cost 18. No extra texture, descriptor, target, overlay geometry or terrain draw is introduced.

`TerrainSurfaceDetailSettings` is a copied construction-time presentation value. Its default enables a 4m repeating color/normal detail with strengths 0.04/0.06. The pixel footprint fades detail between 0.08 and 0.30 of that tile (0.32–1.20m per pixel by default), before minified detail aliases. Color strength is limited to 0–0.08, normal strength to 0–0.12, tile size to 1–32m, and rock slope start/end to validated ordered thresholds. Disabled compiles out both additions; recreate the renderer to change this shader policy. Broad world-space macro tint remains within its existing 0.90–1.08 bound.

Only Rock adapts its existing projection on steep surfaces. Between `1 - abs(normal.y)` of 0.25 and 0.60, UVs transition from XZ to the dominant side plane (ZY for X-facing rock, XY for Z-facing rock). Base, normal and ORM use the same projected coordinates and explicit final-coordinate gradients. Tangential side normal perturbations preserve neutral-map geometry. This bounded slope approximation retains the sample ceiling; it is not seamless triplanar blending. Dominant-axis changes on arbitrary diagonal cliffs remain a visual authoring consideration.

Material source `uvScale` controls the same base/normal/ORM physical scale, multiplied by the profile tile. The reusable refined palette uses:

| Material | Profile tile | Authored UV scale | Effective repeat |
| --- | ---: | ---: | ---: |
| Grass / dirt / rock | 32m | 1 | 32m |
| Mud | 24m | 1.5 | 16m |
| Gravel | 24m | 2 | 12m |
| Industrial ground | 32m | 1.5 | 21.33m |
| Concrete | 24m | 1.5 | 16m |
| Scorched | 32m | 1.25 | 25.6m |

Authored rectangular blend regions now use a cubic smoothstep feather with zero derivative at each end. The road's mud shoulder precedes its gravel region, so the authored gravel center remains dominant. Concrete aprons, industrial yards and scorched regions retain their locations, palette identities and existing decal family. Profile validation rejects invalid IDs/slots, non-finite factors/centers, invalid tiles/extents and negative feathering before control-map uploads. These presentation regions never modify terrain height, navigation, collision or battlefield definitions.

Central Divide defines eight stable terrain material slots:

| Slot | Stable material asset ID |
| --- | --- |
| Grass / ground | `material.world.terrain.grass_ground` |
| Dirt | `material.world.terrain.dirt` |
| Mud | `material.world.terrain.mud` |
| Rock | `material.world.terrain.rock` |
| Gravel | `material.world.terrain.gravel` |
| Industrial ground | `material.world.terrain.industrial_ground` |
| Concrete | `material.world.terrain.concrete` |
| Scorched / battle-damaged | `material.world.terrain.scorched` |

`TerrainPresentationProfile` resolves the base slope/elevation treatment and explicit Central Divide blend regions for roads, industrial areas, the North Bridge concrete apron, and a scorched combat area. When a compiled `RuntimeAssetCatalog` is available, the profile reads base-color, roughness, and metallic factors from the material `.flasset` payloads before terrain chunk GPU buffers are created. Simulation/world terrain therefore remains material-agnostic and runtime never opens authoring files.

The authored profile values remain deterministic development fallbacks. A deterministic Dirt definition is also used for a missing slot, so incomplete runtime catalogs remain renderable without silently loading source files.

## Decals

The existing families now have [editable Blender impression masters and matched
baked PBR maps](BattlefieldDecalBakes.md), retaining the shared quad/material IDs,
UV regions and presentation-only placement/lifetime. Qualification remains
separate from human art and global renderer acceptance.

The initial decal material family contains:

- tire tracks;
- tracked-vehicle marks;
- road wear;
- oil / industrial stains;
- blast marks;
- shell impacts;
- scorch marks;
- concrete cracks.

Central Divide contains representative placements as `BattlefieldWorldObjectDefinition` records. The authored decal plane is `mesh.world.decal.quad`; material choice remains per placed world feature.

## Reusable props and vegetation

The render host supplies one monotonic visual time to both world and shadow instance submission. Conifer, scrub and grass-clump transforms receive a bounded 8-second periodic bend around their authored local origin (root at Y=0; mesh height Y=0..1). Maximum motion is 0.025 radians. Amplitude fades continuously from zero at an 18-pixel projected diameter to full at 72 pixels, independently of LOD switches. Omitting explicit visual time retains static transforms.

The motion operates on copied, already viewer-authorized instances, after the World visibility filter and LOD selection. Batch keys, mesh/material IDs, instance strides, collision and selection bounds are unchanged. Conservative render-only bounds cover the authored plant height and its complete sweep; shadow submission uses the same phase. `AnimatedVegetationInstances` reports participating visible plants. Hidden contacts, targetability, concealment, minimap and tactical icons retain their authoritative policies.

The first repeat-friendly prop catalog contains rock, barrier, concrete block, crate, drum, pallet, pipe section, utility box, fence, industrial light/signage, and rubble visuals.

Vegetation currently contains conifer, scrub, and grass-clump families. Central Divide places representative instances in the canonical map definition.

Repeated world objects share compiled mesh resources through `RuntimeWorldAssetResources`. Runtime vertex/index buffers are cached by stable asset ID instead of being rebuilt per entity. Visible instances are grouped by the resolved runtime mesh and selected LOD, while transform and tint are uploaded through a per-swap-chain-frame instance stream. Each group is submitted with one indexed instanced draw, so repeated props, vegetation, deposits, and shared decal geometry no longer require one draw call per entity.

## Production surface materials

Reusable physical world objects now participate in the same production texture/material foundation as Directorate assets:

- props use the concept color/ORM atlas through `material.world.prop.industrial`, with component UV layouts for steel, wood, concrete, rock and service panels;
- resource deposits sample the mineral cell and retain the shared resource-rock normal family plus resource-specific color factors;
- conifer, scrub, and grass-clump assets select their own foliage cells while retaining the shared vegetation normal family.

This preserves texture reuse and instancing while keeping resource/faction readability in material factors, silhouette, strategic symbols, and UI rather than requiring a unique texture set for every object.

The Asset Compiler deterministically remediates missing UV0 on these static baseline meshes and generates tangent bases for their normal maps. LOD material identity is checked by the production material completeness validation.

Intentional exceptions are the authored decal carrier and purpose-specific VFX/symbol/UI visuals. Terrain uses the dedicated GPU splatting pipeline and does not consume this object-mesh completeness rule.

## LOD

Props, vegetation, and resource deposits define an explicit LOD1 mesh reference in source metadata. Production rendering chooses High or Reduced from projected bounding-sphere size around a 65-reference-pixel threshold with 12% hysteresis. Catalog distance helpers remain available for compatibility. See [Camera and Input](CameraAndInput.md).

Reduced LOD assets retain deliberately compact source geometry. High-detail
props now use manufactured bevels or new functional drum, pipe, pallet, fence,
lamp, rock and rubble geometry. Native 4096-pixel atlas masters supply their
matched physical surface channels; terrain has seven independent source
families. See [Production surfaces](ProductionSurfaceMasters.md).

## Resource deposit families

The battlefield now supports four extractable raw-resource families:

- Ferrous Ore;
- Silicates;
- Volatiles;
- Rare Elements.

Each family has:

- a stable high-detail mesh ID;
- a stable reduced-LOD mesh ID;
- a stable material ID;
- a stable strategic/minimap symbol material ID;
- a family tint used only as renderer fallback.

Resource deposits are normal presentation instances and therefore visible outside the F2 debug layer.

The four families now use distinct clustered source geometry instead of sharing the generic world box. Ferrous Ore reads as dense blocky ore masses; Silicates use a vertical shard/spire cluster; Volatiles use lower faceted nodules; Rare Elements use a sparse, sharply vertical asymmetric shard cluster. Reduced LOD assets preserve the same family silhouette with fewer primitives. Material tint reinforces identity but is not the primary role signal.

### Resource visual variation contract

[Resource deposit visual language](ResourceDepositVisualLanguage.md) defines the
target grounded silhouettes, four weighted cluster morphologies per family,
fragment/size composition, deterministic presentation seed protocol, stable
bindings, conservative bounds, fallback behavior and manual acceptance matrix.
Its [declarative fixture](fixtures/resource-deposit-visual-language.v1.json) is
specification data, not a runtime or CI loader. Existing family IDs and the
High/Reduced state/tint implementation remain stable. Integrated
[cluster geometry](ResourceDepositClusterGeometry.md) now supplies grounded source
forms and optional variants, superseding the earlier vertical-shard description.
Field layouts and variant dispatch require separate integration; this
documentation does not claim native art or performance approval.

### Presentation states

`PresentationExtractor` maps authoritative deposit state into:

- `Untouched`: remaining quantity is still at the initial total;
- `Active`: extraction is occurring or the deposit has already been worked;
- `Depleted`: authoritative remaining quantity is zero.

The state changes the presentation tint without changing the underlying `ResourceDeposit` simulation component.

## Hover, inspection, and strategic symbols

Deposits are inspectable but not commandable. `RtsSelectionController` first performs the normal player-owned command selection test and then a world-feature inspection pick. Clicking a deposit records `InspectedEntity` while leaving the command `SelectionSet` empty.

The client uses the existing interaction debug-draw path for hover/inspection bounds, so resources receive immediate tactical feedback without leaking into movement or combat orders.

The four strategic symbol references are:

- `material.world.symbol.resource.ferrous_ore`;
- `material.world.symbol.resource.silicates`;
- `material.world.symbol.resource.volatiles`;
- `material.world.symbol.resource.rare_elements`.

These references are the stable contract for a future minimap renderer; minimap command interaction itself remains outside this package.

## Central Divide map/editor authoring example

World dressing lives in the canonical map definition rather than renderer code. A representative entry is conceptually:

```csharp
WorldObject(
    "prop.crate.west",
    WorldVisualId.PropCrate,
    WorldPresentationKind.Prop,
    x: 650,
    z: 560,
    scaleX: 5,
    scaleY: 5,
    scaleZ: 5,
    rotationDegrees: 15)
```

The current Editor project remains a foundation host. Until interactive placement tooling is implemented, this map-data pattern is the authoritative editor-facing example: stable visual enum, presentation kind, world position, scale, and rotation. Runtime material/mesh details are resolved later by `WorldPresentationCatalog`.

## Validation

The repository validates this baseline through:

- compilation of the committed `assets/source` tree by the real Asset Compiler;
- runtime-catalog lookup for required world asset IDs;
- terrain material slot and fallback tests;
- resource state extraction tests;
- resource inspection tests that verify deposits do not enter command selection;
- compiled resource-mesh tests that reject a regression to generic box geometry and require distinct high-detail silhouettes;
- LOD and strategic-symbol lookup tests;
- compiled terrain-material resolution with deterministic missing-material fallback;
- repeated-instance batching that verifies multiple visible instances share one instanced draw;
- canonical map/runtime tests for Rare Elements and spawned world-presentation entities;
- the existing Windows graphics smoke and 1,000-instance render-stress path, now run after runtime asset compilation.

Generated `assets/runtime` content remains ignored by Git and must never be edited as source.

## Concept texture completion

Every prop and vegetation mesh now has an authored surface-region UV layout; wood pallets, mineral rocks, concrete barriers, service crates, foliage and machinery retain shared material IDs while sampling the appropriate concept surface cell. Resource-family color and silhouette identity remain intact. The eight existing world decals use their RGBA atlas with bounded mip depth, placement-selected material regions and ordered alpha coverage. The terrain texture family remains complete through the existing GPU splatting path.

## Modular environment sources

The [modular environment kit](ModularEnvironmentKit.md) adds nine prop families
and rooted broadleaf vegetation through the existing High/Reduced world catalog.
Shared materials, normalized pivots, tooling-only collision proxies and stable
world IDs retain existing presentation contracts. The selected ferrous source
is refined under its existing IDs and bounds.

## Battlefield destruction variants

Dedicated fractured rubble, scorched barrier and burst pipe sources extend the
modular kit's atlas/centered-proxy conventions. Existing damaged/destroyed
road/bridge IDs now bind fractured slabs, exposed rebar and broken deck/girder
geometry using authorized infrastructure states. No navigation/physics side
effects or resource changes are added. See
[Infantry and battlefield production](InfantryBattlefieldProduction.md).

## World environment integration

Authored public wear uses ITerrainSurfaceMarkSource and immutable WithSurfaceMarks profiles. Existing detailed decal quads remain the fine-track path. Stable vegetation density and distance admission preserve simulation and intelligence ownership. See [World environment rendering](WorldEnvironmentRendering.md).


Live impact marks and short owned-vehicle tracks reuse the authored decal quad/material IDs through DecalPool. Receiver marks use copied full entity identity and normalized local anchors; invalid or hidden receivers retire their marks. Missing decal meshes/materials skip the mark. See [shared decal decision](adr/BoundedPresentationDecals.md).


RuntimeMaterialDiagnostics now reports unique LoadedMeshCount and logical ResidentMeshBytes. LOD buffers remain loaded on first admitted use and cached for the compiled-catalog session; this is not asynchronous eviction or a physical-residency guarantee. Conservative copied-snapshot groups can skip offscreen instance/asset work before normal individual admission. See [scalable rendering](ScalableRendering.md).
