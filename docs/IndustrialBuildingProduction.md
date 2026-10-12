# Directorate industrial building production

## Scope and retained contracts

The five existing Extractor, Smelter, Electronics Plant, Fuel Refinery and
Ammunition Plant families now have dedicated editable component kits, assembled
glTF LOD0/1/2 and eight construction/state exports per family. Existing primary
IDs, `.lod1`/`.lod2` IDs, gameplay building definitions, footprints, power,
production, extraction, logistics, health and intelligence authority are retained.
Command Core, units, other buildings and the infrastructure kit are not rebuilt.

The integrated baseline is master `63569ac2031b4b89c57c55c02b2aa20244d34df9`. Its primary
sources contain 120 Extractor vertices, 480 Smelter/Electronics vertices and
576 Refinery/Ammunition vertices. Each industrial LOD2 previously referenced the
same source as LOD1. Construction, status attachments and wrecks previously
used the generic shared building modules. The dedicated reference galleries
were added before changing the models so before/after images share one fixture.

## Dimensional and visual brief

Normalized coordinates use Y up, +Z service access and the existing centre pivot.
Each kit records original horizontal extents and maximum height. Foundation
seats now reach Y=-0.5, the existing centre-plus-half-height ground plane; this
cosmetic ground contact does not change simulation footprints or collision.
The centre samples existing terrain height. This is a rigid foundation, not
new dynamic terrain deformation, deposit-conforming geometry or animated mining.

| Family | Functional silhouette and modules | Surface/ground interface |
| --- | --- | --- |
| Extractor | vertical extraction head, supporting gantry, open feed hopper, roller conveyor and loading housing | broad grounded mineral seat; existing deposit/extractor state remains authoritative |
| Smelter | refractory cylindrical furnace, flared overhead feed, hollow exhaust crown, heavy duct and open slag mouth | steel/refractory process core with concrete slag access |
| Electronics Plant | enclosed clean processing shell, paired air-conditioning units, conditioned-power cabinet and open loading portal | closed processing volume, inset service windows, controlled access apron |
| Fuel Refinery | unequal process towers, horizontal/vertical tanks, swept pipe manifold, pump and inspection walkway | separated process equipment with deliberate inlet connections and service route |
| Ammunition Plant | repeated reinforced bays, raised blast partitions, shielded loading area and small exhausts | concrete protective divisions and geometric warning/portal cues |

Temporary corner scaffolds belong only to construction frame/shell stages.
Completed families retain their functional gantries, housings, furnace supports
and blast partitions without generic unattached corner posts.

Role identity is retained in reduced meshes rather than depending on small
fittings or tint. All five use the Command Core's manufactured bevel and atlas
chart conventions. Open hoppers have separate inner walls and rims; hollow
exhausts have inner walls. Swept pipe bends join continuous circular rings instead
of overlapping cylinder segments. Physical overlaps at inlet/flange or structural
supports are intentional connections, not independent crossing pipes.

## Editable sources and exports

Each `assets/source/buildings/directorate/authoring/<family>.kit.json` contains
named modules, parent hierarchy, per-part dimensions, atlas cells, explicit LOD
membership, radial segment counts, state recipes, bounds, anchors and provenance.
Edit this source and regenerate with:

```powershell
python build/Author-IndustrialBuildings.py
# Optional isolated export for review/determinism comparison:
python build/Author-IndustrialBuildings.py --family smelter --output artifacts/industrial-export
```

The tool requires numpy and reuses the established Command Core component
exporter. Outputs live in `sources/industrial/<family>/`: `lod0.gltf`, `lod1.gltf`,
`lod2.gltf`, state meshes, individual `kit/*.gltf` modules and `exports.json`.
glTF contains explicit POSITION/NORMAL/TEXCOORD_0, indexed triangles, identity
node transforms, component hierarchy/index ranges and anchor metadata. Runtime
assembly uses one material section for efficient instancing; hierarchy remains
editable source metadata and independently inspectable module files.

Normals and atlas-inset UV charts are authored; the existing importer generates
orthogonal tangents. LOD0 includes edge bevels, process rings, conveyor rollers,
louvers and access markings. LOD1 removes small fittings and bevels while keeping
functional openings and process identity. LOD2 additionally reduces radial detail
and nonessential pipe routes. Existing projected-size thresholds (150/45 pixels,
12% hysteresis) remain authoritative; camera distance alone does not select LOD.

| Family | LOD0 triangles | LOD1 triangles | LOD2 triangles |
| --- | ---: | ---: | ---: |
| Extractor | 1,260 | 344 | 232 |
| Smelter | 1,236 | 388 | 248 |
| Electronics Plant | 1,124 | 260 | 184 |
| Fuel Refinery | 1,788 | 524 | 276 |
| Ammunition Plant | 1,064 | 264 | 216 |

The counts describe exported geometry, not arbitrary content limits or FPS
guarantees. The shared independent `building.directorate.module.collision_box`
reference is retained for every primary mesh. Gameplay navigation, placement,
picking and hit testing retain their existing authoritative contracts.

## Material and anchor contracts

Existing `material.directorate.building.structural` and state materials remain
stable. Components select steel, service panels, concrete, warning, heat-treated
steel, vents, mineral and worn-metal cells of the existing sixteen-cell atlas.
Base Color is sRGB; tangent Normal and packed ORM are linear. The native 4096px
masters and BC7/mip/runtime limits are unchanged; no additional texture is added.
See [Production surfaces](ProductionSurfaceMasters.md) for exact memory budgets.

Each primary definition publishes three named static inspection anchors matching
its editable kit (service/loading access and family process points). These are
source/tooling sockets, not new logistics destinations, hazard areas or animation
controllers. No mechanical motion is invented for these five buildings.

Geometry is original repository-authored content. No external model, generated
image, downloaded texture or third-party geometry is used. The provenance does
not invent a new license grant; existing repository rights and runtime notices
apply. Historical source models remain available as references.

## State integration

New mesh IDs extend each existing `building.directorate.<family>` prefix with
`foundation`, `structural_frame`, `partial_shell`, `idle`, `unpowered`, `damaged`,
`critical` and `destroyed`. `BuildingPresentationCatalog` resolves these five
families while other families retain their shared state bindings.

Construction remains driven by real progress below 34%, below 67% and completion.
Idle adds a closed gate aligned with the conveyor, slag mouth or loading access;
unpowered adds a mechanical isolation cabinet at the visible service frontage.
Damaged/critical overlays expose broken panels and process elements, reinforcing
the existing state material and smoke/fire hooks. Wrecks retain the family's
process remnant (drill head, furnace mouth, conditioning unit, towers or blast
partitions) on a collapsed foundation. They have no gameplay authority.

Existing power, production/extraction, health priority and fog/intelligence
filtering are unchanged. Active/idle geometry is not a new transport or hazard
mechanic. Construction and wreck representations are purpose-specific meshes;
completed state attachments retain the normal family LOD selection.

## Qualification procedure

Five fixed scenes (`ExtractorProduction`, `SmelterProduction`,
`ElectronicsPlantProduction`, `FuelRefineryProduction`, `AmmunitionPlantProduction`)
show nine authoritative states each, with original footprint scale. Review plots
are cleared of unrelated world dressing and the camera/terrain/seed/tick remain
fixed. These are art fixtures, not live production simulations.

Compile with `build/Build-RuntimeAssets.ps1 -Mode build`, export the visual
inventory, then commit and rebuild the client before capturing. Use the established
native reference procedure at CloseTactical/NormalGameplay/Strategic (120/420/1000m),
matched quality/resolution/driver and no overlapping GPU workload. Compare full
resolution images and resource/CPU/GPU distributions with the baseline, not WARP
performance. Structural tests and captures do not replace final human art signoff.
