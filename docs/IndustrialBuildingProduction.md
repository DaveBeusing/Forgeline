# Directorate industrial building production

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Scope and retained contracts](#scope-and-retained-contracts)
- [Dimensional and visual brief](#dimensional-and-visual-brief)
- [Editable sources and exports](#editable-sources-and-exports)
- [Material and anchor contracts](#material-and-anchor-contracts)
- [State integration](#state-integration)
- [Qualification procedure](#qualification-procedure)
- [Recorded qualification](#recorded-qualification)
  - [Matched High / 1920x1080 cost](#matched-high--1920x1080-cost)
  - [Original before/after images](#original-beforeafter-images)
  - [Reproduce final native matrix](#reproduce-final-native-matrix)
  - [Remaining acceptance](#remaining-acceptance)
  - [Build, test and canonical smoke results](#build-test-and-canonical-smoke-results)
  - [Reproducible package and installed smoke](#reproducible-package-and-installed-smoke)

</details>

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

## Recorded qualification

The baseline capture is `2b3f585868b99210ff8fe9a893bf301fd1aa93db` (0.1.349-dev+2b3f585868b99210ff8fe9a893bf301fd1aa93db); final model capture is
`cef19eec3ee25e9c4b98b378dd1a533b6f69f0c8` (0.1.350-dev+cef19eec3ee25e9c4b98b378dd1a533b6f69f0c8). Subsequent evidence/document commits do not change geometry.

Native D3D12 qualified 180 cases: five families, Low/Medium/High, 120/420/1000m,
1920x1080, 2560x1440, 3840x2160 and 3440x1440. Each accepted requested backbuffer
has structural checks and exact repeated pixel hashes, with zero fallback meshes
or material/texture binding failures. This is native render-target evidence,
independent of the host-window clamp, not physical-monitor/DPI certification.
Each case has 16 warmups, 64 CPU samples and distinct completed GPU fences.
The main matrix passed 179 of 180 exact-repeat checks. One Smelter Medium/1440p/
420m case differed at terrain pixel (91,253) by one blue-channel value, outside
the building gallery. A separate run of that unchanged case passed four exact
repeats. The qualified set combines 179 original cases with that retry; the
original failure is preserved in the report and local evidence. No pixel
tolerance or gate was weakened.

Both cohorts use NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU: 32.0.15.9653; Intel(R) Graphics: 32.0.101.8724. Capture lighting, fixture version 3,
seed 2026, tick 32, fixed camera and visual age are unchanged. No build, test or
headless workload overlapped either accepted performance cohort.

The [portable report](qualification/industrial-buildings.json) records all cameras,
CPU/GPU distributions, LODs, culling, draw submissions, memory budgets/usages,
resource residency, device state and image hashes. The [scoped inventory](qualification/industrial-building-assets.json)
records all 55 primary/LOD/state IDs, material references, collision and sockets.

### Matched High / 1920x1080 cost

CPU frame values include frame reuse and Present; GPU values use completed fences.
The arrows below show baseline to final p50 milliseconds. Adapter local usage is
process/driver residency, including render targets and allocation pools, not mesh-only
VRAM. Object draws exclude separate terrain, shadow, particle and HUD passes.

| Family | Distance | CPU p50 ms | GPU p50 ms | Object draws | Local usage MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Extractor | 120m | 11.34 -> 11.51 | 11.44 -> 11.78 | 14 -> 14 | 384.1 -> 384.1 |
| Extractor | 420m | 11.49 -> 11.61 | 11.73 -> 11.82 | 35 -> 35 | 384.1 -> 384.1 |
| Extractor | 1000m | 12.47 -> 12.63 | 12.68 -> 12.79 | 37 -> 37 | 384.1 -> 384.1 |
| Smelter | 120m | 11.31 -> 11.72 | 11.53 -> 11.88 | 14 -> 14 | 384.1 -> 384.1 |
| Smelter | 420m | 11.44 -> 11.59 | 11.74 -> 11.85 | 35 -> 35 | 384.1 -> 384.1 |
| Smelter | 1000m | 12.39 -> 12.72 | 12.64 -> 12.81 | 37 -> 37 | 384.1 -> 384.1 |
| ElectronicsPlant | 120m | 11.33 -> 11.76 | 11.57 -> 11.87 | 14 -> 14 | 384.1 -> 384.1 |
| ElectronicsPlant | 420m | 11.59 -> 11.60 | 11.75 -> 11.84 | 35 -> 35 | 384.1 -> 384.1 |
| ElectronicsPlant | 1000m | 12.36 -> 12.42 | 12.68 -> 12.78 | 37 -> 37 | 384.1 -> 384.1 |
| FuelRefinery | 120m | 11.37 -> 11.75 | 11.56 -> 12.07 | 14 -> 14 | 384.1 -> 384.1 |
| FuelRefinery | 420m | 11.63 -> 11.68 | 11.75 -> 11.94 | 35 -> 35 | 384.1 -> 384.1 |
| FuelRefinery | 1000m | 12.49 -> 12.77 | 12.66 -> 12.91 | 37 -> 37 | 384.1 -> 384.1 |
| AmmunitionPlant | 120m | 11.45 -> 11.68 | 11.58 -> 11.89 | 14 -> 14 | 384.1 -> 384.1 |
| AmmunitionPlant | 420m | 11.60 -> 11.64 | 11.73 -> 11.80 | 35 -> 35 | 384.1 -> 384.1 |
| AmmunitionPlant | 1000m | 12.52 -> 12.60 | 12.67 -> 12.81 | 37 -> 37 | 384.1 -> 384.1 |

These sequential cohorts show measured timing variation and any cost increases
shown above must be considered during performance signoff. GPU clocks/thermals
were not recorded; the measurements do not isolate geometry cost or establish
a portable performance improvement, a 60 FPS guarantee or sustained gameplay FPS.

Asset qualification succeeds for 413 assets: 230 meshes, 56 textures, 127 materials,
60,212,858 compiled payload bytes. Compared with the 373-asset baseline
(59,015,415 bytes), the net increase is 1,197,443 bytes
(2.03%). No texture or material is added. All matched views retain
52,708,956 resident texture bytes (50.27 MiB) and 187 SRVs; complete residency counters
are recorded per case.
Existing 64 MiB scene-texture, 512-SRV and canonical draw gates remain unchanged.

At 1000m Low, all five completed families select LOD2. At Medium/High, the smaller
Extractor selects LOD2 while the larger facilities can retain LOD1 because selection
uses projected diameter, quality and hysteresis. Construction/state meshes and
background props are accounted separately in the report; distance is not a fixed
LOD switch. Static camera evidence does not establish moving-camera transition quality.

### Original before/after images

Fourteen selected unedited High/1080 PNGs show all five families close and the
Refinery at all three distances. Full-resolution PNGs for all fifteen baseline
and 180 final cases remain in the local `artifacts/industrial-buildings/isolated-before`
and `integrated-final` directories, plus `integrated-repeat-review` for the retry.
The portable report records every image hash and
comparison, including cases whose PNG is not committed. The fixture places
foundation/frame/shell at Z=-28m, idle/operational/unpowered at Z=0, and
damaged/critical/wreck at Z=28m, with X=-28/0/28m within each row. Selection rings
identify construction entries. At 120m the nearest foundation plate is partly
clipped by the viewport/HUD; use the 420m view for its full footprint. The
completed machinery and other eight state plots remain in the close viewport.

| Family | 120m | 420m | 1000m |
| --- | --- | --- | --- |
| Extractor | [before](images/industrial-buildings/before/ExtractorProduction-1920x1080-High-CloseTactical.png) / [after](images/industrial-buildings/after/ExtractorProduction-1920x1080-High-CloseTactical.png) | local PNG / report hash | local PNG / report hash |
| Smelter | [before](images/industrial-buildings/before/SmelterProduction-1920x1080-High-CloseTactical.png) / [after](images/industrial-buildings/after/SmelterProduction-1920x1080-High-CloseTactical.png) | local PNG / report hash | local PNG / report hash |
| ElectronicsPlant | [before](images/industrial-buildings/before/ElectronicsPlantProduction-1920x1080-High-CloseTactical.png) / [after](images/industrial-buildings/after/ElectronicsPlantProduction-1920x1080-High-CloseTactical.png) | local PNG / report hash | local PNG / report hash |
| FuelRefinery | [before](images/industrial-buildings/before/FuelRefineryProduction-1920x1080-High-CloseTactical.png) / [after](images/industrial-buildings/after/FuelRefineryProduction-1920x1080-High-CloseTactical.png) | [before](images/industrial-buildings/before/FuelRefineryProduction-1920x1080-High-NormalGameplay.png) / [after](images/industrial-buildings/after/FuelRefineryProduction-1920x1080-High-NormalGameplay.png) | [before](images/industrial-buildings/before/FuelRefineryProduction-1920x1080-High-Strategic.png) / [after](images/industrial-buildings/after/FuelRefineryProduction-1920x1080-High-Strategic.png) |
| AmmunitionPlant | [before](images/industrial-buildings/before/AmmunitionPlantProduction-1920x1080-High-CloseTactical.png) / [after](images/industrial-buildings/after/AmmunitionPlantProduction-1920x1080-High-CloseTactical.png) | local PNG / report hash | local PNG / report hash |

### Reproduce final native matrix

```powershell
./build/Invoke-RtsReferenceQualification.ps1 -Capture -OutputRoot artifacts/industrial-buildings-review `
  -Scenes ExtractorProduction,SmelterProduction,ElectronicsPlantProduction,FuelRefineryProduction,AmmunitionPlantProduction `
  -Profiles Low,Medium,High -Zooms CloseTactical,NormalGameplay,Strategic `
  -Displays 1920x1080,2560x1440,3840x2160,3440x1440 `
  -Warmups 16 -Samples 64 -RepeatCaptures 2 -ContinueAfterMismatch
```

Use a clean committed checkout with freshly built binaries and the qualified runtime
assets. Supply `-BaselineRoot` for a matching local baseline; the fifteen matching
High/1080 cases have numerical comparisons, while the other 165 cases have no
paired old-model baseline. Baseline absence is not visual approval.

The exact-repeat investigation used the same command with `-Scenes SmelterProduction`,
`-Profiles Medium -Zooms NormalGameplay -Displays 2560x1440 -RepeatCaptures 4`
and a fresh output root. That run uses identical source/runtime, camera,
lighting, quality, native size and device; surface frame/resize counters start
in a fresh process and are recorded independently. The cause of the original
one-value terrain difference is not established by the successful repeat.

### Remaining acceptance

Final human art/performance signoff and approved golden references remain pending.
Fine state details cannot be assumed readable at every strategic pixel footprint.
Physical-monitor DPI changes, arbitrary moving-camera LOD transitions, deposit-
conforming terrain seating and an extended device/resize soak are not qualified
by these frozen galleries. The local D3D12 debug layer is unavailable; zero
reported debug messages with the layer disabled is not debug-layer validation.

### Build, test and canonical smoke results

Release build completed with zero warnings/errors, 42 project references remain
acyclic, and SemVer is 0.1.350 (one PATCH over integrated master 0.1.349).
The full serialized suite has 1,787 tests: 1,778 passed, nine optional cases
skipped, zero failures in 8m18.524s. All 115 glTF exports have finite attributes,
valid indices/component ranges, unit normals, valid UVs and nondegenerate
triangles; an independent export is byte-identical. No test budget is relaxed.

Headless 64-tick/1,000-entity smoke and 16-tick/10,000-entity stress pass. The
seed-2026 validation match ends naturally at tick 42,922, with player 1
victorious through Command Core destruction and no pending commands.
[Runtime evidence](qualification/industrial-building-runtime.json) records the checks.

The canonical 1,000-instance Windowed smoke passes at 1600x900 with eleven object
batches. Borderless at 5120x2160 fails the unchanged eleven-batch gate with thirteen
object batches. The current integrated baseline master, with unchanged old
industrial models and the same gallery fixture, reproduces thirteen batches
at the same size. This existing ultrawide canonical gate remains unresolved.
Thresholds, renderer, canonical roster and smoke script are unchanged. Both
cohorts have zero material/texture binding failures; intentional canonical
stress-marker fallback proxies are separate from the zero-fallback industrial
reference galleries. Borderless acceptance and merge-ready qualification are
not claimed.

The earlier integrated-before cohort is excluded from accepted performance
comparisons because an operational delivery build overlapped its measurements.
The recorded isolated-before cohort was rebuilt and recaptured after explicit
CPU/GPU clearance. Exploratory matrices remain local and are not acceptance
evidence for this final geometry.

### Reproducible package and installed smoke

Two independent publish trees from `d48a920273915a6d942c1ec598fda22765ed6da5`
are byte-identical under the established reproducibility check. Packaging
validates and copies the exact previously qualified runtime. An installation
outside the checkout starts without authoring sources or the asset compiler:
all 413 compiled containers and their manifest are byte-identical to the
qualified root. Existing studio-intro MP4/WAV media are included separately.
The installed 1,000-instance Windowed smoke presents successfully with eleven
instance batches and zero texture/material binding failures. This functional
smoke does not grant Borderless, art or sustained-performance acceptance.
[Portable package verification](qualification/industrial-building-package.json)
records source identity, archive hash and checks; the archive remains local.
