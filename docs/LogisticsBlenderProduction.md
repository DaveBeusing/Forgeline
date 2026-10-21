# Editable logistics Blender sources

Storage Depot, Supply Depot and Logistics Hub already have distinct authored
warehouses, container stacks, fuel vessels, ammunition storage, service fittings,
gantries, construction stages and damage meshes. Preserve that integrated art
in three genuine modular Blender masters and adopt their saved GLB exports through
the same 33 mesh IDs. This closes the native-source gap without regenerating the
existing artwork. Human high-fidelity art acceptance remains a separate decision.

## Verified starting point

Ordered repository audit fetched `master` at
`b32bf04671fb579e01a7e282eac76ff91c5b2f1d`, version `0.1.428`.
GitHub's default/integration branch is `master`; no other remote branch remained.
Related PRs #184 (logistics art), #247 (copied loading/queue overlays),
#268 (render qualification), #270/#271 (industrial masters) and #272 (Command
Core) are merged. Related review threads have no outstanding review comments.
No unfinished logistics Blender branch was available to reuse.
Branch `art/logistics-blender` starts directly from that fetched master and
initially advances PATCH exactly once to `0.1.429`. After concurrent master
integrations it is synchronized with `a6fe1185` / `0.1.431`; current PATCH is
`0.1.432`. See the current-base appendix in [qualification](LogisticsBlenderQualification.md).

The optimized renderer and qualification tooling are integrated. Their strict
physical paired timing, thermal, full-frame/memory/lifecycle and human visual
acceptance gates are not established by the merge. This candidate may be authored
and reviewed; it is not a waiver of production promotion requirements.

## Source and runtime boundaries

Masters live in `assets/source/buildings/directorate/authoring/`:
`storage_depot.blend`, `supply_depot.blend`, `logistics_hub.blend`.
Each contains all eleven existing meshes, named editable module objects, static
service-socket empties and packed shared Base Color/Normal/ORM previews.
Existing runtime state-material overrides, including supported status emissives,
remain unchanged and are exercised with the full canonical catalog. The isolated
16-asset family catalogs qualify geometry dependencies; they are not a complete
renderer installation with all shared state/effect materials.
Engine coordinates remain +Y up/+Z forward, Blender +Z up/-Y forward, metre scale.
Collections isolate the intact LOD0, reduced LODs, construction and damage states.
The collision mesh is retained as a separate contract, not a generated hull.
Source vertices use the established normalized building-local coordinates;
authoritative instance scales are 14×8×14, 16×8×16 and 18×9×18 metres. The frozen
family fixture checks the metre-scaled, translated and rotated socket positions.
Do not apply these instance scales destructively to the normalized source meshes.

`tools/blender/Invoke-BlenderLogistics.ps1 -Family <family> -OutputRoot <fresh-root>`
opens the saved master and exports it. `-Action Author` imports the existing
component ranges only into a fresh output; it never replaces a saved master.
The historical `build/Author-LogisticsBuildings.py`, kits and glTF sources remain
available. Normal production export does not run that generator.

Exports retain ordered section/material identities, topology, UV/tangent channels,
normals, pivots, bounds, sockets, state IDs, LOD references and collision references.
Planar interior module translations can survive a save/reopen/export; unsupported
topology, normal, UV, material, hierarchy, socket and scale edits fail explicitly.
Unchanged UVs are recovered exactly from Blender's float32 preview encoding of
`(u, 1-v)`. A genuine UV edit is rejected even inside the general round-trip
tolerance; this removes arithmetic encoding drift without increasing a tolerance.
Virtual static module ranges exist only in the DCC adapter, adding no runtime
articulation or per-truck material binding. Runtime still uses one indexed section
per family mesh and the established shared PBR atlases. No texture, shader,
descriptor family, mesh budget, residency cap or LOD threshold is increased.

All sources are original repository content. No external image, mesh or license
input is introduced. The player package contains only compiled assets and does
not require Blender, Python, source geometry or the compiler.

## Real loading space

The authoritative docks remain `BuildingLoadingDock` in `BuildingDefinitions.cs`:
one bay, facade +Z, travel +X, 12 m maneuver length plus the actual truck half-length.
Storage service is local `(0,-4,11.5)` m; Supply `(0.96,-4,12.5)` m;
Hub `(0,-4.5,13.5)` m. Building rotation transforms the whole pose.
Cargo/Supply trucks retain 3.2/3.4 m width and 6.4/6.8 m length. Saved cargo-load
anchors remain 1.388 m above ground; Supply's transfer anchor remains 1.2 m.

Interior warehouse/gantry aisles are inspection geometry, not extra usable slots.
There are no new physical ramps, obstacles, navigation blockers, stock counts,
throughput, queue rules or inventory authority. Fuel/ammunition forms identify
the building role, never a fabricated live amount. Actual FIFO and exclusive exit
ownership remain the simulation's responsibility.

The physical queue regression uses real family footprints/docks, mixed truck sizes,
every fleet size from two through ten participants and all four cardinal orientations. Existing neighboring
bay/blocked-exit tests and Loading/Transit/Unloading/Sustained reference scenes
remain in force. New family-specific Repeated/Stress scenes publish exactly
1/100/1,000 copied operational render instances, terrain-grounded and rotated through
all four headings. They are diagnostic workloads, not economic simulations or
evidence that every instance is simultaneously visible.
Single instances use the existing gallery target. Repeated scenes are isolated
render workloads; their intentionally absent player-experience HUD does not
represent a missing building or unavailable mesh.

## Reproduction and acceptance

Use pinned Blender 5.2.2 x64 through the existing launcher:

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/logistics-toolchain
./tools/blender/Test-BlenderLogistics.ps1 -OutputRoot artifacts/logistics-saved
python -m unittest discover -s tools/blender/tests -p 'test_*.py'
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
./build/Validate-ProjectReferences.ps1
./build/Validate-SemVer.ps1 -BaseRef origin/master
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
```

The saved-master check performs independent exports, mutation rejections, a real
saved module-edit positive control, canonical GLB byte comparison, official
compiler build and source-less validation. The dedicated workflow runs portable
contracts on PRs; actual native Blender execution is opt-in on a provisioned
Windows x64 runner. A skipped native CI job does not mean Blender passed.

The [qualification record](LogisticsBlenderQualification.md) contains the executed
full solution, source-less runtime, native before/after and installed-package
results, source identities and unchanged budgets. Human artwork/traffic approval and controlled physical performance
acceptance remain required; no golden, merge, tag or release is authorized by a
successful export or static repeatable image.
