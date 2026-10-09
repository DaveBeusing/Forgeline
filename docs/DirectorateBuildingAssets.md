# Directorate Building and Infrastructure Assets

## Purpose

The Directorate building and infrastructure presentation set connects the existing authoritative construction, economy, power, production, logistics, combat-damage, and strategic-infrastructure state to compiled runtime visuals. Presentation never owns or duplicates those gameplay states.

The first visual baseline intentionally uses compact modular geometry. Stable asset IDs, source/runtime separation, state contracts, LOD behavior, and reuse are the production contracts; higher-detail art can replace the authored geometry without changing gameplay IDs.

## Included building families

| Gameplay building | Stable presentation asset | Primary visual function |
| --- | --- | --- |
| Command Core | `building.directorate.command_core` | dominant command anchor |
| Mine / Extractor | `building.directorate.extractor` | extraction head, machinery, material handling |
| Smelter | `building.directorate.smelter` | furnace/exhaust/process mass |
| Electronics Fabricator | `building.directorate.electronics_plant` | enclosed controlled industrial processing |
| Refinery | `building.directorate.fuel_refinery` | tanks, pipes, pressure/process structures |
| Vehicle Factory | `building.directorate.vehicle_factory` | wide production hall and vehicle-scale access |
| Storage Depot | `building.directorate.storage_depot` | warehouses and repeated storage modules |
| Supply Depot | `building.directorate.supply_depot` | storage plus readable distribution/service modules |
| Power Plant | `building.directorate.power_plant` | generation hall and distribution/exhaust structures |
| Logistics Hub | `building.directorate.logistics_hub` | loading lanes, cargo modules, service gantry |
| Ammunition Plant | `building.directorate.ammunition_plant` | reinforced processing chambers, warning access, exhaust |
| Barracks | `building.directorate.barracks` | repeated quarters, canvas panels, central service entry |
| Radar | `building.directorate.radar` | raised sensor array, support tower, equipment cabinets |

The existing gameplay `BuildingId` values and building-definition keys remain authoritative. The presentation catalog maps those IDs to stable Directorate runtime assets rather than creating a second gameplay roster.

The current compact geometry is the building readability baseline rather than a flat placement marker. Command, production, storage, and supply functions are distinguished through footprint, massing, access/loading forms, tanks, vents, and service attachments. Future art may increase fidelity, but it must preserve those normal-RTS-camera role reads.

## Source layout

Editable sources live below:

```text
assets/source/buildings/directorate/
  materials/
  modules/
  sources/
  symbols/

assets/source/infrastructure/directorate/
  bridges/
  materials/
  roads/
  sources/
  symbols/
```

Generated runtime content remains below `assets/runtime/` and is never edited as source.

## Shared modular building kit

The first kit establishes reusable structural and state geometry:

```text
building.directorate.module.foundation
building.directorate.module.structural_frame
building.directorate.module.partial_shell
building.directorate.module.state_idle
building.directorate.module.state_unpowered
building.directorate.module.state_damaged
building.directorate.module.state_critical
building.directorate.module.destroyed
building.directorate.module.collision_box
```

The construction and state modules are intentionally shared by all thirteen families. This keeps the initial art vocabulary consistent and makes repeated geometry eligible for the existing indexed-instancing path.

## Construction presentation

`PresentationExtractor` reads the authoritative `ConstructionSite.Progress` value and publishes one of three visual stages:

- below 34%: foundation;
- 34% to below 67%: structural frame;
- 67% to completion: partial shell.

Completion removes `ConstructionSite` and the completed building switches to its family-specific runtime mesh. No presentation state advances construction and no render asset changes footprint occupancy.

## Operational state presentation

For completed supported buildings, state is derived from existing ECS components in priority order.

| Presentation state | Existing authoritative source |
| --- | --- |
| Damaged / Critical | `HealthState` |
| Unpowered | `PowerConsumer` or `PowerGenerator` |
| Idle | `ResourceExtractor`, `ProductionFacility`, or `UnitProductionFacility` |
| Operational | completed building with no higher-priority condition |
| Destroyed | presentation-only wreck identity created at authoritative combat destruction |

Brownout and offline power consumers use the unpowered treatment in this baseline because both represent insufficient power for normal operation.

Critical states do not rely on tint alone. Idle, unpowered, damaged, and critical buildings add a shared state-geometry layer. Destroyed buildings use a dedicated collapsed module. Runtime material factors reinforce those shapes but are not the sole signal.

The current asset runtime has no general skeletal/mechanical animation or effect asset contract. Machinery animation, sparks, smoke, fire, richer emissive behavior, and destruction VFX therefore remain later presentation layers rather than invented simulation state.

## Destruction boundary

Combat destruction remains authoritative. When a completed supported building is destroyed, the gameplay entity is removed through the existing lifecycle system. A presentation-only wreck entity preserves:

- world transform;
- visual identity;
- stable building identity;
- intelligence signature when available.

The wreck has no command, combat, production, power, storage, or navigation authority.

## LOD and collision

Each primary family exposes LOD0, LOD1, and LOD2 references. The current presentation thresholds are 220 m and 620 m.

All thirteen primary assets reference the shared simplified collision asset:

```text
building.directorate.module.collision_box
```

That asset is a tooling/runtime visual contract only. Gameplay placement, occupancy, navigation, combat hitboxes, and construction validation continue to use existing simulation-owned footprint/spatial contracts and never change with render LOD.

Presentation picking and culling use the authored visual transform and orientation rather than changing the simulation footprint. Building selection outlines are drawn on the visual ground plane and rotate with the building, preventing the outline from cutting through the building mass or becoming axis-misaligned after orientation changes.

## Strategic symbols

Building classes expose stable strategic/minimap symbol references:

```text
material.directorate.symbol.building.command
material.directorate.symbol.building.extraction
material.directorate.symbol.building.processing
material.directorate.symbol.building.factory
material.directorate.symbol.building.storage
material.directorate.symbol.building.supply
material.directorate.symbol.building.power
```

These are shared presentation bindings for future minimap/strategic consumers. They do not claim that a dedicated minimap renderer exists yet.

## Road kit

The initial Directorate road kit contains:

- straight;
- short curve;
- long curve;
- T junction;
- four-way junction;
- industrial-yard transition;
- shoulder;
- damaged segment;
- destroyed segment.

Central Divide already owns a real `GroundRoad` logistics graph. `BattlefieldRuntime` creates presentation-only road geometry from those authoritative node positions. The visual entities never participate in routing, capacity, collision, or navigation.

Each non-crossing edge now renders a 12-meter primary surface plus two 2.4-meter shoulders. The surface follows the endpoint height slope while the shoulders use a separate gravel/dirt material family to establish a readable transition into surrounding terrain. Road nodes inspect only the already-authored graph connectivity to choose presentation geometry: degree-two turns receive short/long curve overlays, three-way nodes receive T-junction surfaces, and four-way nodes can use the cross-junction asset. These overlays smooth the visual topology without creating or modifying a logistics edge.

This composition keeps road topology legible at normal and strategic RTS zoom through width, silhouette, and edge contrast rather than relying on tiny texture detail. The industrial-yard transition asset remains available for later authored yard connections without adding new gameplay roads.

## North Bridge and South Ford

Central Divide already has functional strategic crossing gameplay, so bridge presentation is part of this baseline.

The North Bridge uses:

```text
infrastructure.directorate.bridge.road.intact
infrastructure.directorate.bridge.road.damaged
infrastructure.directorate.bridge.road.destroyed
```

Its visual state is driven directly by the existing `StrategicInfrastructureState`:

- `Operational` -> intact;
- `Restoring` -> damaged/repairable;
- `Disabled` -> destroyed.

The South Ford remains a road-surface presentation family rather than a bridge mesh, while its existing strategic-infrastructure state remains authoritative.

Disabling or restoring either crossing still changes the real logistics edge and navigation blocker through `StrategicInfrastructureSystem`; presentation only reflects the result.

## Production material treatment

Directorate building and infrastructure material IDs remain stable while their source definitions now reuse the shared production texture library:

- normal/operational structures select structural steel, equipment, concrete, warning and sensor cells through authored component UVs;
- operational road surfaces use the brighter, high-roughness concrete atlas cell and existing hard-surface normal detail;
- road shoulders use a warmer gravel/mineral material to provide edge and terrain-transition definition;
- damaged/destroyed road presentation has a darker, high-roughness material binding ready for future authoritative road-condition states;
- bridges select structural metal and concrete cells;
- Damaged, Critical, and Destroyed building states select the worn armor atlas cell and damaged-metal normal family;
- Critical additionally uses the shared functional status-emissive map at a restrained multiplier;
- Unpowered keeps the structural texture family while its existing material factors suppress visual energy.

This keeps state readability tied to existing authoritative state extraction without creating unique full texture sets for each damage level. Critical presentation still includes geometry/VFX cues, so gameplay state never relies on color or emissive treatment alone.

Position-only baseline glTFs receive deterministic compiler box-projected UV0 and generated tangent bases when required. Collision-only geometry remains intentionally outside the textured production-mesh rule.

## Renderer and performance

Building and infrastructure meshes load through `RuntimeAssetCatalog` and `RuntimeWorldAssetResources`.

`SimpleInstanceRenderer`:

1. resolves building or infrastructure presentation metadata;
2. selects the stable runtime mesh;
3. applies building LOD selection where applicable;
4. resolves compiled material tint;
5. submits optional building state geometry;
6. batches identical runtime mesh IDs through the existing per-frame indexed-instancing stream.

Repeated buildings therefore share the same batching architecture as units and world props.

## Validation

Automated coverage verifies:

- all thirteen building families compile;
- LOD and shared collision references resolve;
- shared construction/state modules resolve;
- building and infrastructure strategic symbols resolve;
- complete road and bridge source families resolve;
- construction progress maps to foundation/frame/shell presentation;
- power, damage, critical, and wreck state extraction;
- critical state geometry is present in addition to material treatment;
- Central Divide creates road surfaces, paired shoulders, curve transitions, and junction presentation from the real road graph;
- road presentation counts and node-shape selection remain derived from existing graph topology rather than duplicate route data;
- road surface, shoulder, and damaged material IDs resolve through the normal runtime material path;
- North Bridge and South Ford receive the correct infrastructure presentation identity;
- repeated Directorate buildings retain the shared indexed-instancing path;
- the normal Windows D3D12 smoke consumes the compiled runtime manifest.

## Current boundary

This baseline integrates the required Vertical Slice functions without expanding gameplay mechanics and now carries production Base Color/Normal/ORM material treatment through the existing runtime path. Roads are not player-constructible, rail content remains outside the current slice, and no new economic or power mechanic is introduced. Higher-detail unique hero art, mechanical building animation, emissive animation, audio, repair visuals, and editor-specific road placement can extend the stable contracts later.


## VFX integration

Building damage and destruction presentation is extended by the shared VFX layer documented in [Combat, Destruction, and Logistics VFX](CombatAndLogisticsVfx.md).

Damaged buildings receive light smoke, Critical buildings receive heavier smoke/fire/sparks, and destroyed building wrecks receive one-shot building-scale destruction layers plus persistent wreck smoke/fire. These effects consume the existing `HealthState` and presentation-only wreck identity and never alter building footprint, occupancy, combat, production, power, or logistics authority.

Supply/cargo effects are likewise driven only when existing transport/supply state reports real loading, unloading, refuel, or rearm activity.

## Concept surface completion

All thirteen constructible building classes now have textured compiled geometry and LOD bindings. Logistics Hub, Ammunition Plant, Barracks and Radar no longer use the untextured placement cube. Their compact functional layouts use the same construction/state kit, collision contract and strategic symbols as the other classes. Source geometry changes presentation only.

Building, road and bridge UVs select concrete, structural steel, service panels, glass, warning and heat-treated surfaces from the shared concept atlas. Existing stable material IDs and instancing remain intact. See [surface authoring](../assets/source/materials/directorate/surfaces/README.md).

State material selection takes precedence over the static primary mesh material, so Unpowered, Damaged and Critical treatment is not overwritten by the operational material. The existing 112-byte instance stream carries atlas scale/offset and emissive strength.
