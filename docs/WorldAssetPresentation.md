# World Asset Presentation

## Purpose

The Vertical Slice world presentation baseline turns the Central Divide gameplay map into a readable RTS battlefield while keeping simulation, authoring assets, compiled runtime assets, and renderer concerns separate.

Runtime code never opens files from `assets/source`. CI and local authoring workflows compile those files with `ForgeLine.AssetCompiler` into `assets/runtime`; the Windows client consumes the resulting `RuntimeAssetCatalog`.

## Terrain material family

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

`TerrainPresentationProfile` resolves the base slope/elevation treatment and explicit Central Divide blend regions for roads, industrial areas, the North Bridge concrete apron, and a scorched combat area. The profile is converted into terrain vertex presentation data when chunk GPU buffers are created, so simulation/world terrain remains material-agnostic.

A deterministic Dirt material is used as the missing-slot fallback. This keeps incomplete development profiles renderable without silently loading source files.

## Decals

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

The first repeat-friendly prop catalog contains rock, barrier, concrete block, crate, drum, pallet, pipe section, utility box, fence, industrial light/signage, and rubble visuals.

Vegetation currently contains conifer, scrub, and grass-clump families. Central Divide places representative instances in the canonical map definition.

Repeated world objects share compiled mesh resources through `RuntimeWorldAssetResources`. Runtime vertex/index buffers are cached by stable asset ID instead of being rebuilt per entity. The current generic renderer still submits one draw per visible entity; true GPU instance-buffer batching remains a benchmark-driven renderer optimization rather than an authoring requirement.

## LOD

Props, vegetation, and resource deposits define an explicit LOD1 mesh reference in their source asset metadata. `WorldPresentationCatalog` supplies the corresponding presentation distance and the renderer chooses High or Reduced LOD before resolving the runtime mesh.

LOD assets currently use deliberately compact primitive source geometry suitable for the Vertical Slice baseline. They are stable replacement points for later production meshes without changing gameplay data or world-object identities.

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
- LOD and strategic-symbol lookup tests;
- canonical map/runtime tests for Rare Elements and spawned world-presentation entities;
- the existing Windows graphics smoke and 1,000-instance render-stress path, now run after runtime asset compilation.

Generated `assets/runtime` content remains ignored by Git and must never be edited as source.
