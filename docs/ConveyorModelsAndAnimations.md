# Conveyor models and belt motion

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Implementation baseline: master `165b7d644e6660c34cb03fa725a0d6e0bf34f024`
(0.1.446), including the existing conveyor visual-kit design from PR #291.
Final integration also includes master
`dbe748d3c1502ad21f8c0d8c962e2de018392faf` (0.1.447, operations rules).
This change prepares 0.1.448 and supplies authored models and compiler records.
The conveyor source and asset compiler contracts are unchanged by that rebase.
Tests, runtime/camera smokes, clearance qualification, LOD acceptance and CI test
runs were deliberately not executed at the user's request. Manual review remains
outstanding. No conveyor simulation, construction UX or building transfer rule
is added by this source package.

<details>
<summary>On this page</summary>

- [Delivered source kit](#delivered-source-kit)
- [Units, origin and sockets](#units-origin-and-sockets)
- [States and animation](#states-and-animation)
- [Collision, selection, supports and terrain](#collision-selection-supports-and-terrain)
- [Materials and LOD handoff](#materials-and-lod-handoff)
- [Build result](#build-result)
- [Re-authoring and manual review](#re-authoring-and-manual-review)

</details>

## Delivered source kit

The immutable planning baseline remains
`assets/source/infrastructure/directorate/conveyors/conveyor-kit.v1.json`.
Production data lives beside it in `conveyor-production.v1.json`; it does not
rename the design contract to an asset definition or change its proposed status.

- `authoring/`: 15 genuine editable Blender 5.2.2 masters, one per physical module.
- `models/`: compiler-ready GLBs, existing-format rigid animation glTFs and real
  `*.asset.json` records, including all three LODs and reserved visual states.
- `tools/blender/conveyor_models.py`: dimensional geometry and export recipes.
- `tools/blender/conveyor_author.py`: native mesh/action authoring and saved-master
  export. GLBs are emitted from the actual Blender mesh and UV data; clips use
  the saved translation FCurves. Collision meshes are editable in each master.

There are twelve planned families plus an independently modelled right-hand
curve and two ground-mounted tier-1 support variants. No negative runtime scale is used. The left hand retains the planned
`infrastructure.directorate.conveyor.curve_90` identity; the other hand uses
`infrastructure.directorate.conveyor.curve_90.right`.

| Module suffix | Shape and interface |
|---|---|
| `straight_short` | 4m carrier, one input and one output |
| `straight_standard` | 8m carrier, one input and one output |
| `curve_90`, `curve_90.right` | Closed 4m-radius quarter-circle carriers, opposite hands |
| `ramp_up`, `ramp_down` | 8m run with 2m rise/fall |
| `merger_2to1` | Two incoming arms and one common throat |
| `splitter_1to2` | One central input and two output arms |
| `io_in`, `io_out` | Distinct source/sink hopper and socket jaws |
| `support_light` | Single column, localized concrete footing |
| `support_heavy` | Two columns, cross-brace, two individual footings |
| `support_light.tier1`, `support_heavy.tier1` | Same ground footprints, actual 7.2m carrier attachment |
| `endcap` | Mechanical stop bar; cap socket, no material-flow outlet |

Junctions use one filled concave Y-shaped deck, rather than overlapping coplanar
belt panels. Curves have real curved volume and both endpoint frames. Rollers,
guards, service drives, direction chevrons, fastening details and foundation
geometry are included; LOD1 removes fasteners/intermediate roller detail and
LOD2 retains the carrier/branch/guard silhouette and direction marks.

## Units, origin and sockets

Blender source masters are metres, +Z up/-Y forward. Engine source coordinates
are +Y up/+Z forward. Every module has its ground-reference pivot at zero; the
transport plane is Y=5.8m and the intact carrier underside is Y=5.2m. Ramps
terminate at Y=5.8m/7.8m and the raised tier increment remains 2m.

Runtime exports uniformly divide all positions and socket coordinates by
**18** to fit the existing canonical unit-box rendering/rigid-clip envelope.
Use a uniform runtime instance scale `(18,18,18)` to recover actual world metres;
do not rescale a source file twice. Keep the ground-reference origin, add whole
2m tier translations to carrier modules in world space, and apply only cardinal
yaw. Support feet stay at ground: select the authored `.tier1` support with a
7.2m attachment for the first raised tier; do not lift a tier-0 support by 2m.
Higher support tiers require an explicitly authored height variant. Normals and UVs
are unaffected by this uniform conversion. The right curve is source-authored,
including reflected port normals and corrected triangle winding.

Compiled `sockets` contain normalized XYZ. The production catalog retains
world-metre coordinates, outward cardinal facing, input/output/cap kind,
belt/building role and relative tier. The current compiler does not serialize
port-facing semantics, so retain this catalog for the placement/logistics owner;
positions alone never authorize a material transfer. Saved-master export rejects
moved or reoriented canonical port empties. Apply object rotation/scale to the
mesh before exporting a geometry edit.

## States and animation

Moving families have the nine reserved states: `flowing`, `idle_no_input`,
`disconnected`, `congested`, `blocked`, `unpowered`, `damaged`, `critical`,
`destroyed`. Supports/endcap have `structural_intact`, `damaged`, `critical`,
`destroyed`. State IDs are the module ID plus the state suffix. Base module IDs
resolve to idle/intact; they do not accidentally start a flow animation.

Damaged/critical states include bent guard/bearer geometry; destroyed carriers
have an actual missing central span and broken stubs. Destroyed supports are
collapsed and have separate simplified collision/selection records. Blocked and
unpowered states show a physical gate/brake; disconnected adapters show open
jaws. Congestion models contain generic queued packs as a state cue, not actual
resource quantities or inventory truth.

The existing renderer has no supported material UV-scroll parameter. This kit
therefore uses its existing bounded **rigid translation clips** for repeated
belt cleats, with static rubber below them. There is no new shader, animation
system, rig, renderer change or simulation clock. A one-second repeating pitch
runs in the `flowing` LOD0/LOD1 sources; terminal covers conceal the reset. Curved
cleats follow short sampled arc translations and stay inside the existing static
mesh envelope. Rollers are static; optional rotation is omitted. LOD2 is static
and preserves direction with chevrons.

Only `flowing` records reference the clips. Inactive, blocked, offline, critical,
damaged and destroyed records remain static, as do their Blender preview
collections. Conditional damaged motion requires an explicit later operating
state integration; it is not inferred from a health percentage. Preview frame
1..25 at 24fps matches the linear clip. The visual cycle is not throughput,
velocity or conservation authority. A future presentation owner selects the
actual state asset and supplies the existing specified MechanicalPose with copied
active seconds only while real operation/visibility allows it. This package
provides no automatic entity, dispatch, power or ownership state.

## Collision, selection, supports and terrain

Every module exposes real collision and selection mesh asset IDs. The selection
hull is a practical volume along the actual elevated shape, not a projected
rectangle blocking the ground beneath it. Destroyed states have separate
selection/collision IDs. The generic compiler records these assets but does not
create a navigation obstacle or change vehicle collision on its own.

For intact suspended modules, the proxy is above Y=5.2m; no ground blockage is
registered along the entire route. Structural supports have individual 0.8m x
0.8m footings. The light footing is centered at X=0. Heavy footings are at
X=-1.6m/+1.6m. Their columns, feet and upper bearer are separate geometry.
The heavy foundation gap is only **2.4m**: it is **not** a truck portal, and its
cross-brace also occupies overhead space. Place supports outside actual vehicle
swept lanes; the 5m clearance requirement applies to unobstructed carrier spans,
not to columns or a heavy support station. Never infer truck access from the
carrier's elevation alone.

The production catalog records per-foot ground blocking, foundation dimensions,
carrier attachment height and terrain policy. A placement owner must sample the
highest traversable terrain under the real span and maintain at least 5m clear
height. Ground-reference placement must not tilt/distort the complete belt to
follow terrain. Footing adjustment is limited to 0.2m; reject unsupported terrain
or select a valid higher tier if that cannot preserve carrier/port alignment.
The source models do not themselves deform foundations, level terrain or
implement placement rejection. Damaged/critical/destroyed clearance needs review
against the actual state geometry.

## Materials and LOD handoff

The original model delivery used `material.world.prop.industrial`. The subsequent
[conveyor material package](ConveyorMaterialsAndTextures.md) now binds all render
records and saved Blender masters to `material.infrastructure.directorate.conveyor`
with its shared BaseColor/Normal/ORM/emission atlas. Charts retain the 4x4 grid and
established inset. Runtime IDs, ports, clips and collision/selection policies are
preserved; finish changes add small static status inlays to the frame part.
See the material handoff for channel, mask, state and texture-budget details.

Each state and default identity has LOD0/LOD1/LOD2, with existing compiler LOD
references at 140m and 420m. These are initial authored handoff distances;
manual tactical/strategic review must confirm popping and readability. The
production catalog records per-state triangles and bounds, source frame,
collision/selection identity, sockets and motion policy.

## Build result

The Release asset compiler build completes with zero warnings/errors. The scoped
asset build accepts **459 records**: 435 mesh records (including state/LOD,
default aliases and proxies), 20 rigid clips, and the existing material plus
three shared textures. It reports no issues. This is a source/asset build, not
an executed test suite or physical/render acceptance. The final build includes the two real raised-tier support variants. No
full-game source build or gameplay/renderer acceptance is claimed.
[Build receipt](ConveyorModelBuild.json) records counts and source identities.
This is historical evidence for the original model generation; current material
generation hashes and build counts are in
[ConveyorMaterialBuild.json](ConveyorMaterialBuild.json).
Runtime cache and intermediate outputs remain under artifacts and are not
committed as authored content.

## Re-authoring and manual review

Generate all masters and sources into a fresh artifact directory:

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/conveyor_author.py -InputRoot assets/source -OutputRoot artifacts/conveyor-author
```

Export an edited saved master (the same exporter reads real saved geometry,
UVs, collision and flow FCurves):

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/conveyor_author.py -InputRoot assets/source -OutputRoot artifacts/conveyor-edit -ScriptArguments @('--action','Export','--module','conveyor_straight_standard','--blend-file',(Resolve-Path assets/source/infrastructure/directorate/conveyors/authoring/conveyor_straight_standard.blend).Path)
```

Copy reviewed masters to `authoring/`, module subdirectories from the generated
`source/` directory to `models/`, and the matching production catalog beside the
design JSON. Exporting one module writes a one-module catalog; merge only that
record into the complete catalog. Do not replace all fifteen records with a
partial export. Rebuild assets after any geometry or action edit.

Manual validation, deliberately not executed by this implementation:

1. Compile the complete normal source catalog with
   `./build/Build-RuntimeAssets.ps1 -Mode build`; inspect source/clip diagnostics.
2. Mate 4m/8m segments, both curve hands, both ramps and all merger/splitter/I/O
   ports at each cardinal yaw. Use uniform scale18, ground origin and whole 2m
   tiers. Check socket/world normals, port surfaces, guard and shoulder joins.
3. Inspect each state and LOD collection in Blender. Play only flowing
   collections. Confirm reset covers, arc cleats, static inactive/offline states,
   and actual missing span/columns in wrecks.
4. Review close/normal/strategic presentation, then both LOD boundaries around
   140/420m. Check retained direction marks, branch silhouettes and state cues.
5. Check collision and selection with actual vehicles and uneven terrain;
   supports must be outside swept lanes. Treat the heavy frame as a blocking
   station, never a road portal. Measure the minimum free height under spans.
6. Run the existing tests and later conveyor placement/simulation integration
   checks manually. No automatic suite, native smoke, CI workflow or new golden
   approval is claimed here.

Existing automated suites, for manual execution only:

```powershell
python -m unittest discover -s tools/blender/tests -p 'test_conveyor_contract.py' -v
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
```

The existing portable conveyor suite covers the design/port contract; it is not
a native model, animation, vehicle-clearance or manual visual acceptance test.
