# Directorate combat and support unit production

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Scout Vehicle, Self-Propelled Artillery, Cargo Truck and Supply Truck use
editable component kits, real glTF geometry, three distinct LODs and dedicated
damaged/critical/wreck meshes. Stable primary/LOD/material/collision/symbol IDs,
authoritative scales, movement, hitboxes, navigation, cargo and supply rules remain.
Human art, live interaction and sustained native performance acceptance are pending.

<details>
<summary>On this page</summary>

- [Integration and source ownership](#integration-and-source-ownership)
- [Dimensions and role geometry](#dimensions-and-role-geometry)
- [Sockets, states and rigid motion](#sockets-states-and-rigid-motion)
- [Reference and qualification method](#reference-and-qualification-method)
- [Synchronization with infantry and battlefield production](#synchronization-with-infantry-and-battlefield-production)
- [Final validation](#final-validation)
  - [Matched High/1080p timings](#matched-high1080p-timings)
  - [Repeated-unit resource limits](#repeated-unit-resource-limits)
  - [Original native review images](#original-native-review-images)
- [Reproduction and remaining acceptance](#reproduction-and-remaining-acceptance)
- [Native logistics truck masters](#native-logistics-truck-masters)

</details>

## Integration and source ownership

Ordered precheck verified master `0263a86f`, remote branches, open/closed unmerged
PRs, current sources, architecture/pipeline, tests, CI and repository governance.
No reusable unit-production branch or PR existed. Operational PR 183 and logistics
PR 184 were integrated. No model work begins from an older feature branch.

`assets/source/units/directorate/authoring/` owns four family `.kit.json` files
and `combat_support.modules.json`. Coordinates are vehicle-local metres above
the sampled ground, +Z forward. The exporter subtracts the established movement
height offset and divides by the unchanged authoritative visual scale; normals
use the inverse-transpose conversion. Editable module names, shared sources and
contiguous component ranges are inspectable in recipes and glTF extras.

`python build/Author-CombatSupportUnits.py --update-metadata` uses the existing
Command Core/Tank geometry primitives without changing their implementations.
`--family` narrows ownership and `--output` supports independent byte-identical
exports. There are 145 glTFs: 12 LODs, two weapons, 12 state meshes and 119 individual
modules. The compiler still owns runtime outputs. Generic bevel/detail/mechanical
passes skip this dedicated source subtree; the old running-gear pass only handles
its existing production filenames. No unrelated family is regenerated.

Geometry is original repository content. No third-party mesh, texture, reference
raster, generated image or license grant is introduced. Shared Base Color is
sRGB; Normal/ORM remain linear with the existing atlas, mip and sampler policy.
No new material, texture, descriptor family or shader is required.

## Dimensions and role geometry

| Family | Existing width × height × depth | Root height offset | LOD0 total / LOD1 / LOD2 triangles |
| --- | --- | --- | --- |
| Scout Vehicle | 3.0 × 2.4 × 5.2 m | 1.2 m | 2700 / 736 / 284 |
| Self-Propelled Artillery | 3.8 × 3.2 × 7.5 m | 1.4 m | 5288 / 1196 / 508 |
| Cargo Truck | 3.2 × 2.4 × 6.4 m | 1.1 m | 4014 / 1010 / 376 |
| Supply Truck | 3.4 × 2.5 × 6.8 m | 1.1 m | 4226 / 1178 / 392 |

Scout is a six-wheel reconnaissance hull with elevated rotating sensor and a
separate small weapon. Artillery has continuous track belts, 10 road-wheel hubs,
rear weapon mass, trunnions, recoil cylinders and a long hollow cannon. Cargo
and Supply share physical chassis/cab templates and eight wheels: Cargo exposes
two pallet modules/straps and a loading platform; Supply separates a cylindrical
fuel vessel, ammunition locker, maintenance cabinet and transfer connector.
Cargo/payload geometry is static and does not infer inventory quantities.

LOD0 totals include separate Scout 188-triangle and Artillery 640-triangle weapons;
their primary chassis meshes have 2512 and 4648 triangles. Reduced LODs combine
neutral silhouettes and omit motion metadata. Ground contact follows unchanged
height offsets; no terrain grading, suspension solver or gameplay geometry is added.
Independent simplified collision references and all gameplay footprints are retained.
The long cannon retains its established neutral muzzle overhang.

## Sockets, states and rigid motion

Existing `weapon_muzzle`, `sensor_origin`, artillery `gun_pivot`/`recoil_anchor`,
`cargo_load` and `supply_transfer` coordinates remain unchanged. New weapon
pivots and cosmetic wheel anchors match the authored parts. Cargo deck height
is 1.388 m, Supply cargo anchor 1.4 m and service connector 1.2 m above sampled
ground, preserving the building-interface contracts. No hard docking or new
transfer/navigation rule is introduced; existing truck approaches remain outside
building occupancy bounds.

Four families add 12 damage/wreck meshes, and two separate weapon meshes are
registered through the existing turret path. Cached state mapping consumes only
existing Health/Wreck presentation metadata and preserves warmed zero-allocation
lookup. Damage materials/VFX reinforce actual debris geometry; wrecks use collapsed
family geometry and never become gameplay obstacles or physics destruction.

Rigid wheel/sensor/cannon ranges reuse the existing vertex/index resources and
bounded 32-part/2KiB transform path. Wheels derive angle from copied travel and
their physical radius; sensors retain existing active duration. Rotation now
composes in scaled local metric space before returning to normalized coordinates,
preventing anisotropic vehicle scale from deforming circular wheels or changing
barrel length. Normalized door/recoil travel and neutral LOD fade retain their
contracts. The same transform convention resolves turret and visual muzzle
positions, including narrow compatibility with the existing tank path. No tank
geometry, skeleton runtime, tread circulation or physical suspension is recreated.

Owned active artillery FireMission coordinates supply yaw and the tangent of the
existing parabolic arc (rise 4×apex), clamped to the established cosmetic elevation
range−0.15..0.55 radians. This is bounded presentation, not a new ballistic solver
or a guarantee that steep close-range trajectories align with the clamped gun.
Foreign mission coordinates are rejected before reading the mission. Cancelled/
complete missions are neutral; visible real ShotFired events retain recoil history.
No authoritative component or projectile trajectory is modified.

## Reference and qualification method

Four eight-unit galleries retain actual scales, extracted health/wreck states,
fixed seed 2026/tick 32 and identical 120/420/1000 m cameras. Fixed cosmetic pose
inputs expose idle/aim/moving/recoil combinations; these are immutable review
states, not a live combat workload. A 1000-unit scene repeats all four identities.
Existing real logistics fixtures exercise unchanged cargo/navigation/inventory/
supply systems and compiled vehicle sockets with the new models.

Additional 70 m captures inspect mechanical detail while ordinary RTS LOD thresholds
and hysteresis remain unchanged. Camera-distance overrides apply only to explicit
reference capture requests and retain the old defaults. The 50 m prototype cropped
the nearest vehicle and is not final gallery-framing acceptance.

Final source, matrix, resource/timing comparisons and original native image hashes
are recorded in [the portable capture report](qualification/combat-support-units.json). All raw logs, failed
images, requests and local packages remain separately available; no threshold or
pixel tolerance is weakened to obtain a passing result.

## Synchronization with infantry and battlefield production

The branch integrates master `7e924034` (PR 185). Infantry geometry/strides, owned
source guards, state overlays, battlefield variants and the existing bounded
InspectionDistanceMeters capture path are retained. All 42 master scene values
remain stable; the five combat/support scenes are appended. Vehicle wreck
identities remain stable while their scoped source owner moves to the four
combat/support kits. Three duplicate legacy metadata files are removed; the
infantry exporter respects dedicated vehicle metadata ownership. Earlier raw
capture hashes and numerical results retain their original source context.

The integrated catalog has 492 assets (309 meshes, 56 textures, 127 materials).
Current master 0.1.353 requires one recomputed PR PATCH to 0.1.354.

## Final validation

The qualified runtime is `0.1.354-dev+4fc5305a0aa44df8b275786070d04f2f9a57dc36`.
Documentation-only commits follow this source identity. The fresh baseline uses
master `7e924034` plus the same fixture-only source, committed locally as `dfed6089`.
All 12 matched comparisons retain identical fixture, camera, lighting and profile.
All 170 records (12 before, 144 matrix, four 70 m inspection, six 1, 000-unit and
four real logistics cases) pass structural checks and two exact pixel repeats.
The matrix covers Low/Medium/High, 120/420/1, 000 m and 1080p/1440p/2160p/3440Ã—1440.
No fallback meshes, texture-binding failures or submission faults were reported.
The previously documented logistics terrain-pixel anomaly did not recur here;
this does not erase or explain that earlier failure.

Release build: zero warnings/errors. Full serialized suite: 1, 851 total,
1, 842 passed, nine expected skips, zero failures (8m21.191s). Meaningful metric
rotation regressions first failed on the old code, then passed with the fix.
Independent exports of all 145 glTFs are byte-identical, finite, indexed correctly,
nondegenerate, with unit normals and valid UVs. Geometry/socket/state contracts,
owned/foreign artillery missions, zero-allocation warm paths and native capture
arguments are covered by the full suite.

Compiled catalog: 492 assets, 309 meshes, 56 textures, 127 materials and
66, 611, 751 payload bytes; current-master baseline is 64, 652, 118 bytes.
The increase is 1, 959, 633 bytes, with no added texture or material records.
[Scoped inventory and compiled bounds/sockets/ranges](qualification/combat-support-unit-assets.json)
contain 26 unit mesh identities. DXGI local usage below is whole-process telemetry,
not an attribution of VRAM to these meshes.

Windowed and Borderless startup qualification both pass with 1, 000 rendering
stress instances (the canonical fixture deliberately includes synthetic fallback cubes). The reproducible `0.1.354-dev` package contains all 492 qualified
payloads plus a byte-identical manifest; installed native smoke passes without
an authoring/compiler startup path. The 1, 000/10, 000-entity headless checks complete
64/16 requested ticks. The natural validation match ends at tick 42, 922 with
Command Core destruction, 268 completed cargo orders and zero cargo/route failures.
These headless checks do not establish live graphics performance. See
[runtime, package and terminal evidence](qualification/combat-support-unit-runtime.json).

### Matched High/1080p timings

16 warmups and 64 CPU samples per case; GPU samples come from distinct completed
fences. CPU includes frame reuse and Present. Native readback/PNG export is excluded.
These sequential measurements include host variability and do not establish a
causal speedup or sustained gameplay FPS. Adapter: NVIDIA RTX PRO 5000 Blackwell
Generation Laptop GPU, driver 32.0.15.9653, hardware D3D12 Level 12_1.

| Family / distance | CPU before â†’ after mean ms | GPU before â†’ after mean ms | Instance draws before â†’ after |
| --- | --- | --- | --- |
| ScoutVehicle / 120 m | 2.953 â†’ 3.181 | 1.951 â†’ 3.446 | 12 â†’ 13 |
| ScoutVehicle / 420 m | 1.355 â†’ 1.142 | 0.919 â†’ 1.386 | 2 â†’ 4 |
| ScoutVehicle / 1000 m | 2.202 â†’ 1.373 | 1.145 â†’ 1.615 | 2 â†’ 2 |
| Artillery / 120 m | 2.810 â†’ 2.122 | 3.006 â†’ 2.567 | 2 â†’ 16 |
| Artillery / 420 m | 0.976 â†’ 1.084 | 1.231 â†’ 1.257 | 2 â†’ 4 |
| Artillery / 1000 m | 3.276 â†’ 1.321 | 3.461 â†’ 1.607 | 2 â†’ 2 |
| CargoTruck / 120 m | 0.920 â†’ 2.170 | 1.148 â†’ 2.369 | 7 â†’ 12 |
| CargoTruck / 420 m | 1.077 â†’ 1.187 | 1.298 â†’ 1.394 | 2 â†’ 4 |
| CargoTruck / 1000 m | 1.661 â†’ 1.353 | 1.355 â†’ 1.575 | 2 â†’ 2 |
| SupplyTruck / 120 m | 2.723 â†’ 1.148 | 2.920 â†’ 1.365 | 7 â†’ 12 |
| SupplyTruck / 420 m | 1.129 â†’ 1.728 | 1.325 â†’ 1.956 | 2 â†’ 4 |
| SupplyTruck / 1000 m | 1.409 â†’ 1.477 | 1.602 â†’ 1.690 | 2 â†’ 2 |

### Repeated-unit resource limits

The immutable fixture contains 1, 000 extracted vehicle identities; visibility and
LOD culling are reported separately. This is a bounded rendering sample, not a
1, 000-unit active battle. Full p50/p95/p99, bindings, uploads, LOD counts, textures,
descriptors, post-processing allocations and fence telemetry remain in the JSON.

| Profile / distance | CPU / GPU mean ms | Visible / total | Instance draws | DXGI local usage MiB | Resident textures MiB | Descriptors |
| --- | --- | --- | --- | --- | --- | --- |
| Low / 120 m | 4.432 / 4.537 | 311 / 1000 | 50 | 199.59 | 49.77 | 185 / 4096 |
| Low / 420 m | 3.508 / 2.204 | 977 / 1000 | 7 | 199.60 | 49.77 | 185 / 4096 |
| Low / 1000 m | 3.961 / 3.445 | 1000 / 1000 | 4 | 199.60 | 49.77 | 185 / 4096 |
| High / 120 m | 5.920 / 6.128 | 311 / 1000 | 45 | 384.04 | 49.77 | 185 / 4096 |
| High / 420 m | 3.084 / 3.045 | 977 / 1000 | 4 | 384.04 | 49.77 | 185 / 4096 |
| High / 1000 m | 4.272 / 4.582 | 1000 / 1000 | 5 | 384.04 | 49.77 | 185 / 4096 |

### Original native review images

All linked PNGs are unchanged native outputs. The portable report verifies their
SHA-256 hashes. All four eight-unit galleries at 70 m were inspected for full
framing, grounded running gear, role silhouettes and visible articulated poses.
This implementation review does not replace human art signoff.

- [before-ScoutVehicleProduction-120m](qualification/combat-support-units/before-ScoutVehicleProduction-120m.png)
- [after-ScoutVehicleProduction-120m](qualification/combat-support-units/after-ScoutVehicleProduction-120m.png)
- [after-ScoutVehicleProduction-420m](qualification/combat-support-units/after-ScoutVehicleProduction-420m.png)
- [detail-ScoutVehicleProduction-70m](qualification/combat-support-units/detail-ScoutVehicleProduction-70m.png)
- [before-ArtilleryProduction-120m](qualification/combat-support-units/before-ArtilleryProduction-120m.png)
- [after-ArtilleryProduction-120m](qualification/combat-support-units/after-ArtilleryProduction-120m.png)
- [after-ArtilleryProduction-420m](qualification/combat-support-units/after-ArtilleryProduction-420m.png)
- [detail-ArtilleryProduction-70m](qualification/combat-support-units/detail-ArtilleryProduction-70m.png)
- [before-CargoTruckProduction-120m](qualification/combat-support-units/before-CargoTruckProduction-120m.png)
- [after-CargoTruckProduction-120m](qualification/combat-support-units/after-CargoTruckProduction-120m.png)
- [after-CargoTruckProduction-420m](qualification/combat-support-units/after-CargoTruckProduction-420m.png)
- [detail-CargoTruckProduction-70m](qualification/combat-support-units/detail-CargoTruckProduction-70m.png)
- [before-SupplyTruckProduction-120m](qualification/combat-support-units/before-SupplyTruckProduction-120m.png)
- [after-SupplyTruckProduction-120m](qualification/combat-support-units/after-SupplyTruckProduction-120m.png)
- [after-SupplyTruckProduction-420m](qualification/combat-support-units/after-SupplyTruckProduction-420m.png)
- [detail-SupplyTruckProduction-70m](qualification/combat-support-units/detail-SupplyTruckProduction-70m.png)
- [after-ArtilleryProduction-1000m](qualification/combat-support-units/after-ArtilleryProduction-1000m.png)
- [after-SupplyTruckProduction-1000m](qualification/combat-support-units/after-SupplyTruckProduction-1000m.png)
- [repeated-1000units-420m](qualification/combat-support-units/repeated-1000units-420m.png)
- [traffic-loading-420m](qualification/combat-support-units/traffic-loading-420m.png)
- [traffic-transit-420m](qualification/combat-support-units/traffic-transit-420m.png)
- [traffic-unloading-420m](qualification/combat-support-units/traffic-unloading-420m.png)
- [traffic-sustained-420m](qualification/combat-support-units/traffic-sustained-420m.png)

## Reproduction and remaining acceptance

Run the exporter, Release solution build and canonical runtime-asset build first.
Run the serialized full solution tests and project-reference/version checks.
`build/Invoke-RtsReferenceQualification.ps1 -Capture` selects the four production
scenes, three profiles, three zooms and four displays with `-Warmups 16 -Samples 64
-RepeatCaptures 2 -ContinueAfterMismatch`; the inspection cohort adds
`-InspectionDistanceMeters 70`. The repeated cohort selects
`CombatSupportRepeated`; the traffic cohort selects the four existing logistics
production scenes. No comparison threshold, camera default or renderer budget
was weakened. See the portable report for exact source and per-case parameters.

Human art/material approval, live movement/aim/recoil and LOD transitions,
terrain-edge placement, long-running native performance and debug-layer validation
remain pending. Owned artillery elevation remains clamped and may not align with
steep close-range parabolic trajectories. Cargo payload geometry is cosmetic;
inventory quantity remains authoritative. CI acceptance must refer to the final
published PR head. No merge, release tag or public package is implied.

## Native logistics truck masters

Cargo and Supply now have separate saved Blender production masters and guarded
GLB exports. See [truck ownership, editing and qualification](LogisticsTruckBlender.md).
The existing recipe geometry, physical dimensions, wheel bindings and family roles
remain the reviewed baseline.
