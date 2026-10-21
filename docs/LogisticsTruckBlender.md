# Cargo and Supply truck Blender masters

The existing Cargo and Supply trucks already contain distinct payload geometry,
three LODs, damage/critical/wreck geometry, shared PBR surfaces and eight articulated
wheels. The production gap is a persistent native Blender master for each family.
Preserve that artwork and expose its existing component hierarchy for editing.

## Sources and runtime ownership

`assets/source/units/directorate/authoring/cargo_truck.blend` and
`supply_truck.blend` are separate editable masters saved by Blender 5.2.2 x64.
Each includes chassis, cab, suspension, wheel guards, individual wheels, payload
modules, all existing LOD/state meshes and a protected collision reference.
Only the intact LOD0 collection is visible initially. Other collections remain
available in the Outliner. PBR previews pack existing shared textures; source
material/texture definitions retain authority for the compiled runtime.

The Cargo deck carries two pallet modules, straps, a loading platform and rear
gate. Supply has a fuel vessel, ammunition locker, maintenance cabinet and service
connector. Geometry does not report changing inventory quantities. Both families
reuse the original heavy-frame and cab recipes in `combat_support.modules.json`.

The saved scene shows physical metre dimensions in Blender's +Z-up/-Y-forward
basis, with the established 1.1 m root offset. Protected root transforms apply
the existing game-instance dimensions: Cargo 3.2/2.4/6.4 m and Supply 3.4/2.5/6.8 m.
Export verifies these display transforms and restores the canonical normalized
source frame before the shared geometry/socket validator runs. This display step
does not change authoritative scale, grounding, collision, turning or bay clearance.

The fine authoring component partition is temporary. Export restores the original
runtime mechanical table: one body plus eight wheels at LOD0. Reduced LODs and
damage/wreck states retain their original static tables. Socket coordinates,
wheel rates, material ordering, LOD thresholds and all stable IDs stay authoritative.
No additional runtime part, draw section, material or texture family is introduced.

Canonical GLBs live under
`assets/source/units/directorate/sources/combat_support/<family>/blender/`.
The existing logistics asset definitions select these exports through the ordinary
compiler. Collision sources retain their existing references. Recipe glTFs remain
the provenance/seed archive. The legacy component exporter rejects truck metadata
updates once the saved master owns the family; its isolated recipe exports remain
available for inspection.

## Supported edits and export

Use Edit Mode on a static component, return to Object Mode and save a new working
copy. The guarded rest-surface path preserves topology, explicit normals, UVs,
material slots, vertex identities, sockets, origins and wheel geometry. It accepts
small planar rest-position edits within the original silhouette envelope, bounded
to 0.005 normalized source units. Larger/topology/material changes require explicit
production-contract migration. Do not alter `fl_*` bookkeeping or protected roots.

Editor round-off within the existing source tolerances is snapped to original
channels. Intentional position edits above that tolerance survive; source normals,
UVs and tangents retain their reviewed values. Unmodified GLBs therefore retain
the exact source geometry rather than accumulating repeated float/UV conversions.

From PowerShell 7.4 or later:

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check
./tools/blender/Invoke-BlenderLogisticsTruck.ps1 -Action Export -Family cargo_truck `
  -BlendFile assets/source/units/directorate/authoring/cargo_truck.blend -OutputRoot artifacts/cargo
./tools/blender/Invoke-BlenderLogisticsTruck.ps1 -Action Export -Family supply_truck `
  -BlendFile assets/source/units/directorate/authoring/supply_truck.blend -OutputRoot artifacts/supply
./tools/blender/Test-BlenderLogisticsTruck.ps1 -OutputRoot artifacts/trucks
python -m unittest discover -s tools/blender/tests -p test_logistics_truck_contract.py -v
```

Every output directory must be fresh and disjoint from the source tree. Short
artifact roots keep deeply nested source clones within Windows path limits.
Review exported definitions and GLBs before copying them into canonical source
locations. Author creates an isolated master from the reviewed recipe geometry;
it never replaces an existing production master automatically.

The native qualifier reopens each saved master twice, compares source and compiled
bytes, validates without source directories, rejects unsupported saved edits and
proves that a saved cab edit changes only the intended LOD0 runtime asset. The
manual `Logistics truck Blender qualification` workflow requires a provisioned
Windows/X64/`blender` runner. Hosted Python contracts are discovered by ordinary CI.

## Native qualification boundaries

`CargoTruckProduction` and `SupplyTruckProduction` retain the existing eight-unit
state/motion galleries. Six appended scenes provide isolated family counts:
`CargoTruckSingle`, `CargoTruckHundred`, `CargoTruckRepeated` and the corresponding
Supply names. They contain 1/100/1,000 copied truck identities. Repeated scenes
include a fixed copied wheel-travel pose; they are rendering workloads, not live
1,000-truck logistics simulations. Existing scene enum values and layouts remain.

Real loading/transit/unloading/sustained fixtures continue through the existing
Game docking, transport and supply systems. The loading fixture includes three
Cargo trucks and separate physical Supply loading. Focused physical queue tests
provide additional fleet-size coverage; neither static galleries nor repeated
pixels establish sustained gameplay FPS or live transition acceptance.

## Audit and integration gate

The ordered precheck fetched `origin/master`, verified remote/default branch
`master` at `b32bf04671fb579e01a7e282eac76ff91c5b2f1d`, version 0.1.428, and
found only remote master with no open PR. The previous production PR #186,
FIFO #238, autonomous fuel service #257 and renderer #266/#267 are merged.
Their changed files, available reviews and integration state were inspected.
The existing production kits, compiler mappings, native fixtures, architecture,
development, asset pipeline, terrain and versioning contracts were reused.

The integrated foundation acceptance records technical qualification and project
owner native approval on 19 October 2026. Later renderer lanes retain their own
acceptance limitations. Current master CI run 39547596264 fails its version gate
before build/tests. That inherited outcome is separate from this branch's checks.
Exactly one PR PATCH is prepared: 0.1.428 to 0.1.429.

Current-head required CI, native evidence, reproducible installed-package evidence
and human art review remain separate gates. Branch-local authoring and visual
qualification do not declare production integration approved. Detailed observed
results and remaining blockers belong in the retained qualification evidence.
