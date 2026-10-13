# Directorate operational building production

Starting master: `63569ac2031b4b89c57c55c02b2aa20244d34df9` (0.1.349).
Repository, remote branches, open/closed PRs, gameplay/presentation, tests, CI,
architecture and repository policy were reviewed before source authoring. No
matching open PR or reusable remote operational-building branch existed.

## Inventory and dimensional brief

The baseline already had real glTF sources, shared PBR, three stable mesh IDs,
independent collision metadata and authoritative ECS state extraction. Its
compact boxes lacked a clear loading aperture, generator/distribution split,
inhabited quarters and rotating concave radar geometry. Generic construction
and wreck representations obscured the family identity.

| Family | Gameplay width x depth x height | Before LOD0/1/2 triangles | New LOD0/1/2 triangles | Modeled role |
| --- | --- | --- | --- | --- |
| Vehicle Factory | 26 x 24 x 12m | 324 / 36 / 36 | 540 / 240 / 204 | Open hall, portal jambs/lintel, assembly lanes, gantry, roof ventilation |
| Power Plant | 16 x 16 x 10m | 312 / 36 / 36 | 1016 / 488 / 180 | Generation hall, hollow exhausts, separate transformers/insulators, cooling and service enclosure |
| Barracks | 18 x 16 x 8m | 264 / 48 / 48 | 276 / 140 / 92 | Paired pitched-roof quarters, external windows, entrances and central deployment path |
| Radar | 14 x 14 x 18m | 308 / 48 / 48 | 812 / 348 / 156 | Braced mast, equipment room, closed double-sheet concave reflector and feed |

The normalized source is +Y up, +Z forward, import scale 1. Root placement remains
terrain + half the existing gameplay height; rendered scale remains width,
height, depth. Cosmetic foundations deliberately extend to Y=-.5 so they reach
that existing terrain plane. No gameplay footprint, navigation occupancy,
selection authority, production/power/sensor rule or spawn offset changes.

| Family | Retained primary X/Z envelope | Baseline minimum Y -> new | Upper envelope cap |
| --- | --- | --- | --- |
| Factory | X[-.48,.48], Z[-.39,.49] | -.39 -> -.5 | .42 |
| Power | X[-.43,.42], Z[-.34,.47] | -.4 -> -.5 | .55 |
| Barracks | X[-.43,.43], Z[-.41,.42] | -.28 -> -.5 | .16 |
| Radar | X[-.34,.34], Z[-.30,.30] | -.33 -> -.5 | .5 |

Reduced meshes now use the same foundation/horizontal envelope as their own
primary rather than the baseline generic reduced boxes. Small details are
removed, not rescaled to compensate for gameplay. All three levels keep the
family silhouette. Existing projected-diameter selection (150/45px with 12%
hysteresis), mechanical detail fade and 220/620m metadata remain unchanged.

## Editable sources and export ownership

`assets/source/buildings/directorate/authoring/operational_buildings.kit.json`
is the actual editable component source: four families, named modules/parts,
geometry parameters, material-atlas cells, LOD/state membership and motion
pivots. `build/Author-OperationalBuildings.py` reuses the existing Command Core
hard-surface primitives without modifying the Core models. Boxes have actual
faces and optional manufactured bevels; gable roofs, hollow cylinders, radial
blades, mast braces and concave dishes have real vertices. No rendered-image
substitute, downloaded geometry or new bitmap is used.

```powershell
python build/Author-OperationalBuildings.py --update-metadata
python build/Author-OperationalBuildings.py --output artifacts/operational-reexport
```

The first command explicitly updates the family metadata; an isolated export
never writes bindings. Forty glTF files live under `sources/operational/`: three
LODs and seven states per family. `operational.exports.json` records vertex and
triangle counts, bounds, contiguous rigid groups and SHA256 identities. The
complete report and all forty payload hashes match an independent re-export.
Finite channels, unit normals, UV limits, winding and nondegenerate triangles
are checked by the exporter. The exporter requires NumPy, as does the existing Command Core exporter.
Runtime import validates normals/UVs and generates
tangents through the existing compiler.

Source hierarchy is preserved in glTF `extras.componentHierarchy`, including
module/part names and index ranges. Each export remains one mesh, one primitive
and one material; hierarchy metadata is not an invented runtime node graph.
Rigid components share the same mesh buffers/material, with additional bounded
part draws only at LOD0. Retained older generic authoring tools skip this source
owner, so they cannot overwrite its topology or motion ranges.

The primary identities `building.directorate.vehicle_factory`, `.power_plant`,
`.barracks` and `.radar`, their `.lod1`/`.lod2` identities, strategic symbols and
`building.directorate.module.collision_box` references are retained. Collision
remains an independently controlled simplified proxy; rendered detail does not
replace gameplay collision/navigation authority.

## State, mechanics and circulation

Each family owns `.foundation`, `.structural_frame`, `.partial_shell`,
`.unpowered`, `.damaged`, `.critical` and `.destroyed`. Foundation/columns/cross-braced scaffolds/shells
stage construction; temporary corner scaffolds remain in construction only, not
in completed Power/Barracks/Radar geometry. service covers, broken braces/pipes and fallen roof sheets
provide actual geometry feedback. Wrecks collapse characteristic hall gantry,
generator/exhaust, quarters roof or receiver/mast parts within the footprint.
These consume existing authoritative construction, power, health and wreck
states and existing damage VFX; they add no physics or simulation system.
Command Core retains its dedicated family; other buildings retain their shared
states. Idle uses the existing shared attachment only where the production
facility supports it (Factory/Barracks); it is not invented for Power/Radar.
Dedicated state IDs are cached once; repeated resolution allocates zero bytes.

| LOD0 rigid range | Pivot | Existing motion kind | Rate / travel |
| --- | --- | --- | --- |
| Factory loading door | (0,-.1199999973,.4000000060) | Door | 1 / .35 |
| Factory gantry | (0,.24,-.05) | Crane | .25 / .25 |
| Power cooling rotor | (-.03,.292,-.06) | Fan | 2.2 / 0 |
| Radar array | (0,.285,0) | Radar | .6 / 0 |

Both factory socket names and original coordinates are retained. The authored
leaf is .30 normalized units tall; .35 travel keeps its upper edge below the
roof while opening its lower edge to 4.44m above ground. The fixed portal is
13m wide (12.48m leaf width) and clears 4.2m above ground; floor markings rise to .444m. The
existing 3m-high/7.2m-long MBT therefore fits without changing spawn logic. Its
20m-forward factory spawn remains beyond the front mesh at 11.76m plus 3.6m
vehicle half-depth. The barracks path is 3.06m wide and reaches the front plinth;
its existing infantry spawn offset remains independent of cosmetic entrances.

Existing completed-tick mechanical history supplies phase/door pose. Active
rotors turn; unpowered/idle history freezes phase; wrecks disable motion. Rigid
ranges exhaust the index stream without overlap/gaps. LOD1/2 are static and
receive existing fade behavior. No skeleton, wall-clock phase, extra texture,
new material family or alternate animation owner is introduced.

## Materials and provenance

All geometry is original project-authored hard-surface work. No third-party
model or texture license is introduced. Existing Directorate native 4096-pixel
Base Color/Normal/ORM masters and their provenance remain authoritative. Atlas
cells carry armor, steel, glass/equipment, warnings, concrete, worn/hot surfaces,
vents and service fittings. UV charts remain inset within cells. Base Color uses
sRGB; Normal and ORM stay linear. Runtime BC7 caps remain 2048/1024/512 with real
mip chains. State materials reuse existing bindings and channel conventions.
The catalog adds 28 mesh assets, no material or texture assets: 401 total,
218 meshes, 56 textures and 127 materials. Runtime payload is 59,587,033 bytes:
4,633,935 mesh, 54,798,223 texture and 154,875 material bytes. This is compiled
payload accounting, not total device VRAM.

## Qualification

Native evidence and final validation results are recorded below after export.
No still image or finite benchmark establishes human art signoff, moving-camera
LOD acceptance, live unit spawning, extended device/resize soak, sustained
60 FPS or complete borderless acceptance. Required GitHub checks are inspected
only after the branch is published.


Native capture baseline is `eebfbb15006e723109fab269eaba51cda2842265`; final
source/camera revision is `db6a121fbb53804ad533719cec085e56d5d492a2`. Final runtime
fingerprint is `E0DCBED3EFF2407AAD3D141276A85F9F9F0221CFCE67EF30370E220303883191`.
The preceding 144-case matrix was reviewed and retained locally; temporary
scaffolds were then restricted to construction and the final matrix recaptured.

Twelve before cases use four families x three zooms, Medium, 1920x1080. Final
matrix has 144 cases: four families x three zooms x three profiles x four native
surfaces (1920x1080, 2560x1440, 3840x2160, 3440x1440). Six repeated cases show
250 of each family at three zooms in 1920x1080/3840x2160, Medium. Two additional
225-degree yaw access views show the Factory at 120/420m, Medium, 1920x1080;
these do not replace the matched 45-degree baseline.

Every final case passed source/runtime identity, native dimensions, at least
eight completed GPU samples, two exact repeated pixel captures, no fallback
mesh and no texture/material binding failures. Hardware is NVIDIA RTX PRO 5000
Blackwell Generation Laptop GPU, driver 32.0.15.9653, feature level 12_1.
Requested surfaces are actual backbuffer captures independent of the 1280x720
host-window clamp. Debug layer was requested for smoke but is unavailable.

All twelve matched cases retain exact scene fingerprint, seed 2026, tick 32,
camera target/yaw/pitch/distance, lighting, profile, DPI and device. CPU samples
include frame reuse/Present; readback/PNG export is excluded. Warmup is eight
frames, sample count 32 and repeated capture count two. Short, sequential
frozen-state measurements are not a sustained frame-rate guarantee; scheduling,
cache/driver and display workload still affect timing. No production FPS
improvement is inferred from before/after means.

| Medium FHD matched case | Before GPU mean ms | After GPU mean / p95 ms | Before / after instance draws | After CPU mean ms |
| --- | ---: | ---: | ---: | ---: |
| VehicleFactory / CloseTactical | 12.102 | 10.532 / 10.790 | 24 / 20 | 25.123 |
| VehicleFactory / NormalGameplay | 17.781 | 11.050 / 11.032 | 34 / 34 | 10.686 |
| VehicleFactory / Strategic | 14.994 | 11.682 / 11.645 | 36 / 36 | 11.434 |
| PowerPlant / CloseTactical | 14.423 | 10.448 / 10.617 | 11 / 15 | 10.172 |
| PowerPlant / NormalGameplay | 14.691 | 10.909 / 10.945 | 33 / 33 | 10.641 |
| PowerPlant / Strategic | 14.965 | 11.561 / 11.659 | 35 / 35 | 11.320 |
| Barracks / CloseTactical | 18.492 | 9.977 / 10.175 | 12 / 12 | 25.048 |
| Barracks / NormalGameplay | 14.478 | 10.686 / 10.878 | 34 / 34 | 10.420 |
| Barracks / Strategic | 15.208 | 11.581 / 11.637 | 36 / 36 | 11.420 |
| Radar / CloseTactical | 14.328 | 10.147 / 10.358 | 11 / 15 | 9.813 |
| Radar / NormalGameplay | 14.561 | 10.831 / 10.899 | 33 / 33 | 10.544 |
| Radar / Strategic | 21.251 | 11.496 / 11.593 | 35 / 35 | 11.227 |

The whole final 144-case matrix spans GPU means 5.498–25.206ms and 12–39 instance
draws. Different galleries have distinct state-mesh batches; equal role geometry
shares runtime meshes/materials and instance buffers. Scene fixtures also retain
terrain, outside dressing, HUD and authorized existing VFX, so counts are not
isolated building-only totals.

| Repeated case | Visible scene instances | LOD1 / LOD2 | Instance draws | GPU mean / p95 ms | CPU mean ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1920x1080-Medium-CloseTactical | 44 | 0 / 0 | 8 | 14.230 / 14.796 | 28.631 |
| 1920x1080-Medium-NormalGameplay | 495 | 389 / 0 | 33 | 15.893 / 15.990 | 15.626 |
| 1920x1080-Medium-Strategic | 1152 | 974 / 178 | 31 | 16.384 / 16.437 | 16.216 |
| 3840x2160-Medium-CloseTactical | 44 | 0 / 0 | 8 | 36.378 / 37.544 | 63.251 |
| 3840x2160-Medium-NormalGameplay | 495 | 389 / 0 | 33 | 40.010 / 40.071 | 39.840 |
| 3840x2160-Medium-Strategic | 1152 | 974 / 178 | 31 | 38.133 / 39.270 | 63.701 |

Texture residency spans 52,707,564–52,708,956 bytes; descriptor use
is 186–187. Both remain below the unchanged 64MiB /
512-descriptor qualification limits. DXGI local usage spans 210,010,112–732,672,000
bytes; this adapter/process accounting includes frame targets and other live
resources and is distinct from texture payload bytes or exclusive asset VRAM.

The canonical 1,000-stress Windowed 1600x900 smoke passes with 11 instance draws,
75 total measured draws and 938/1551 visible. Borderless 5120x2160 finishes
normally with 13 instance draws, 80 total measured draws and 950/1551 visible,
then fails the unchanged 11-batch assertion. This is the same 13/11 condition
recorded for the earlier tank/environment integrations; no threshold is relaxed.
Both reports have zero texture/material binding failures. The intentional generic
stress mesh accounts for their fallback counters; the authored building matrices
above have zero fallback meshes. Startup timings do not establish sustained FPS,
physical-window/DPI/focus recovery or borderless acceptance.

## Review images and acceptance boundaries

All ten images below are copied native PNGs without scaling, composition or
image editing. The complete numerical manifests/inventory are adjacent.

| Family | Before | After |
| --- | --- | --- |
| Factory | [Before](images/operational-buildings/factory-before.png) | [After](images/operational-buildings/factory-after.png) |
| Power | [Before](images/operational-buildings/power-before.png) | [After](images/operational-buildings/power-after.png) |
| Barracks | [Before](images/operational-buildings/barracks-before.png) | [After](images/operational-buildings/barracks-after.png) |
| Radar | [Before](images/operational-buildings/radar-before.png) | [After](images/operational-buildings/radar-after.png) |

[Factory access](images/operational-buildings/factory-access.png) shows a real
open portal in the operational plot and the closed leaf in the idle plot.
[Repeated families](images/operational-buildings/repeated-normal.png) shows the
four roles together at normal RTS scale. Close/normal/strategic source views
were inspected: hall/roof, generator/exhausts, paired quarters and supported
receiver remain different silhouettes; construction and collapsed geometry
remain separate states. Strategic geometric details shrink intentionally; role
symbols retain their existing presentation contract. Shared damage materials
remain visibly stylized. Human art signoff, continuous motion/LOD transitions,
live factory/infantry exit review, extended device soak, sustained full populated
60 FPS and borderless draw-budget acceptance remain open. Source geometry and
structural still checks do not establish those approvals.

## Reproduce and validate

```powershell
pwsh build/Build-RuntimeAssets.ps1 -Mode build -ForceRebuild -NoBuild
pwsh build/Build-RuntimeAssets.ps1 -Mode validate -NoBuild
pwsh build/Export-VisualAssetInventory.ps1
pwsh build/Invoke-RtsReferenceQualification.ps1 -Capture `
  -Scenes VehicleFactoryProduction,PowerPlantProduction,BarracksProduction,RadarProduction `
  -Warmups 8 -Samples 32 -RepeatCaptures 2 -ContinueAfterMismatch `
  -OutputRoot artifacts/operational-review
pwsh build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes VehicleFactoryProduction `
  -Profiles Medium -Displays 1920x1080 -Zooms CloseTactical,NormalGameplay `
  -YawDegrees 225 -Warmups 8 -Samples 32 -RepeatCaptures 2 `
  -OutputRoot artifacts/operational-access-review
```

Capture requires a clean committed source and client rebuilt from that SHA.
Use a fresh output directory; evidence is never overwritten. Optional yaw is
finite/bounded to 0–360 degrees, capture-only, and recorded in camera/key;
the standard 45-degree camera remains bit-identical. Acceptance tooling does
not alter the player's camera or default scene matrix.

Restore, 42-project reference validation and full Release build pass with zero
warnings/errors. Official clean build, incremental refinement build and final
integrity validation pass for all 401 records. All 40 exports retain valid bounds,
unit normals, winding, UVs, deterministic payload/report identity and strict
LOD reductions. Runtime contract tests cover tangents, one material section,
retained factory pivots, complete rigid ranges, static reduced LODs, portal/spawn
clearance, family state mapping, phase freeze/wreck disable and zero warm state
resolution allocation. Gallery/capture/motion focused runs pass 37/3/8 cases.

Headless 1,000-entity smoke (64 ticks, seed12345), 10,000-entity stress (16 ticks,
seed67890) and 64-tick Central Divide parity (seed2026) pass. All 24 deterministic
match fields exactly match the previous integrated environment baseline; timing,
GC and allocation measurements are excluded from parity. Natural terminal match
ends at tick42922 with player1 victory/CommandCoreDestroyed and 314 entities.
Gameplay/content tests retain the authoritative definitions; no simulation file
is changed. Version advances once from 0.1.349 to 0.1.350; remote build-test,
rendering/asset workflows and final art/borderless gates still require inspection
when published. No merge or release is implied by local qualification.


Final full Release suite: 1,779 total, 1,770 successful, nine optional skips,
zero failures, 8m34.452s. Command:
`dotnet test --solution ForgeLine.sln -c Release --no-build --max-parallel-test-modules 1 -- --parallel none`.
The ordinary parallel run was also executed. After the aperture predicate was
corrected to exclude floor markings, asset contracts passed; existing warm HUD
allocation checks and a native session-handoff timeout varied under concurrent
load. The complete serialized run passes those same unchanged assertions.
Parallel failures are retained in local logs; this execution strategy does not
weaken thresholds or establish remote CI success. GitHub build-test still gates
integration after publication.

## Integration with industrial building production

The branch integrates master `8ff5f9d` (industrial building production). Both
source owners, state mappings and all operational/industrial review galleries
are retained. Existing industrial enum values remain stable; operational scenes
are appended. Dedicated industrial idle attachments remain family-specific;
operational idle retains its shared attachment.

The combined catalog contains 441 assets (258 meshes, 56 textures, 127 materials).
The PR version advances once from current master 0.1.350 to 0.1.351. Earlier
capture hashes, image comparisons and timing results above describe their
recorded revisions and have not been recaptured for this integration.

Integration validation: Release solution build with zero warnings/errors;
42-project reference validation; all 97 asset tests and 771 presentation tests
pass, with seven optional presentation qualification tests skipped. Both test
projects ran with `--parallel none`. The state-resolution allocation theory
covers Command Core and all nine production families. Existing art/live,
sustained-performance and borderless acceptance limits remain open.
