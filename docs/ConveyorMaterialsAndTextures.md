# Conveyor materials and textures

Implementation base: master `32234ea6` (0.1.448), including the model kit from
PR #297. Final integration includes master `a3d2893e` (0.1.449, Operations guidance). This package prepares 0.1.450. Automated tests, runtime/camera smokes,
resolution comparisons and manual art acceptance were deliberately not run at
the user's request. The scoped asset build is recorded separately in
[ConveyorMaterialBuild.json](ConveyorMaterialBuild.json).

## Material specification

All fifteen physical modules and their state/default/LOD render records bind
`material.infrastructure.directorate.conveyor`. Native Blender masters bind the
same maps with relative `//../materials/` paths. The original global industrial
atlas remains available to other units and props. The conveyor planning JSON is
unchanged; this production finish supersedes its provisional shared-atlas-only
restriction with one dedicated, bounded atlas rather than individual module sets.

Source directory:
`assets/source/infrastructure/directorate/conveyors/materials/`.
The four runtime texture IDs have prefix
`texture.infrastructure.directorate.conveyor.` and suffixes below.

| Suffix / source | Color space / channels | Runtime size / mips |
| --- | --- | --- |
| `base` / `conveyor_base.png` | sRGB opaque BaseColor, no baked lighting | 2048, 5 |
| `normal` / `conveyor_normal.png` | linear tangent normal, +Y | 1024, 4 |
| `orm` / `conveyor_orm.png` | linear R=neutral AO, G=roughness, B=metallic | 512, 3 |
| `emissive` / `conveyor_emissive.png` | sRGB, black except tiny pilot areas | 512, 3 |

All five authored PNGs are native 2048 square RGBA8; alpha is always opaque.
All four runtime chains use existing offline BC7 compilation and terminate at
128 square. Their combined BC7 payload is **7,667,712 bytes**, excluding headers
and catalog metadata. This is an extra resident atlas when other objects still
need the original shared atlas; it is not a whole-game VRAM estimate. Compared
with the three-map atlas previously referenced by conveyor meshes, the extra
fourth 512 chain costs 344,064 bytes of payload. Runtime never loads source PNGs.

`conveyor.material.json` uses BaseColor factor `[1,1,1,1]`, metallic/roughness
factors `1`, UV scale `[1,1]`, offset `[0,0]`, emissive multiplier **0.35**.
No transparency, per-state material allocation, UV scroll or new shader parameter
is required. Every mesh retains one material binding; no decal draw pass is added.
State plates add 24 triangles per moving module/LOD and 12 per structural or
closure module/LOD; destroyed models have no plate. No extra moving part is added.

## Atlas, masks and physical response

The grid is 4x4, row-major from the PNG top left. Mesh charts retain the existing
0.018 normalized inset. A 32px edge extension per 512px source cell protects the
36.864px inset. At terminal mip the inset is 2.304 texels. Content is designed as
broad marks and low-frequency variation; native minification/moiré acceptance
still requires the user's camera review.

| Cell | Surface / purpose | Base roughness / metallic |
| --- | --- | --- |
| 0 | olive painted frame and motor casing | .72 / 0 |
| 1 | graphite structural steel | .56 / .88 |
| 2 | dark composite belt, two broad shallow grooves | .94 / 0 |
| 3 | rubber/composite intermediate rollers | .94 / 0 |
| 4 | active printed double bars and small green pilot | .78 / 0 |
| 5 | broad amber/graphite safety stripes and directional geometry | .78 / 0 |
| 6 | concrete foundations | .91 / 0 |
| 7 | heat-treated exposed end rollers and bolts | .65 / .85 |
| 8 | worn paint, exposed steel and localized crack | .72 / 0 before wear |
| 9 | crossed power-circle offline print | .82 / 0 |
| 10 | generic queued package cover | .88 / 0 |
| 11 | soot, localized heat and wider crack | .83 / 0 before wear |
| 12 | idle hollow-circle print | .78 / 0 |
| 13 | blocked/congested X and small amber pilot | .78 / 0 |
| 14 | Directorate rank/service bars | .72 / 0 |
| 15 | separated mechanical-link print | .78 / 0 |

`conveyor_wear.png` is the source-only RGB mask: **R dirt, G exposed substrate,
B heat/soot**. The deterministic generator derives BaseColor, tangent normal and
ORM from these same fields. Paint chips blend toward exposed steel in linear
reflectance and increase metalness; dust raises roughness and attenuates exposed
metal; heat darkens and covers the substrate rather than painting metallic orange
light onto it. AO stays neutral: this surface atlas contains no object cavity bake.
Paint, rubber, concrete and warning ink remain dielectric. Damaged/critical paint
charts choose cells 8/11; foundations, belt and rollers retain their own response.
Normals come from a common shallow height field, with green aligned to increasing
Blender V, opposite PNG row direction.

The mask is an editable reference/export of the recipe fields, not a fifth
runtime sample. Edit the field expressions and palette in
`tools/blender/conveyor_surfaces.py`, then regenerate all maps together. Editing
only the exported mask does not automatically rebake the PBR maps. The generated
`materials/recipe.json` records palette, cell semantics, channel meanings and
parameters. No external imagery or third-party licensing dependency is added.

## Simulation observation to presentation

The machine-readable contract is
`assets/source/infrastructure/directorate/conveyors/conveyor-materials.v1.json`.
It lists every physical module, valid states, material/texture IDs, default state
and selection precedence. Its observation names are a future presentation
contract, not invented live simulation enums. There is no conveyor component
binding in this package. A presentation owner must supply copied, visible,
authoritative observations and choose the existing module state asset suffix.
Never derive state, throughput, ownership or hidden inventory from the artwork.

| Observation | Asset suffix / surface cue | Motion and geometry cue |
| --- | --- | --- |
| operating with real visible flow | `.flowing`, printed double bars, dim green pilot | existing LOD0/1 cleat motion; LOD2 static chevrons |
| enabled with no input/load | `.idle_no_input`, hollow circle, no light | empty static belt |
| missing mechanical link | `.disconnected`, split-link symbol | open jaws, static |
| downstream backed up | `.congested`, amber X/pilot | queued generic packs, static |
| actual blocked route | `.blocked`, amber X/pilot | striped closed gate, static |
| unavailable power | `.unpowered`, crossed power circle, no emission | engaged brake, static |
| damage requiring damage presentation | `.damaged`, chipped frame/crack | bent guard, static |
| critical damage | `.critical`, localized soot/heat/crack | deformed bearer, static |
| destroyed | `.destroyed`, exposed wreck surface, no emission | physically interrupted/collapsed route, static |
| intact support/endcap | `.structural_intact`, Directorate/service print | no operating status or motion |

Precedence: destroyed > critical > damaged > unpowered > disconnected > blocked >
congested > flowing > idle. Structural supports and endcaps only accept their
four valid structural/damage states. Damage selection intentionally stops the
existing motion until a later explicit operating/damage integration is authored.
The shared blocked/congested X is disambiguated by gate versus queued-pack
geometry. Small symbol detail is for nearby inspection; distant state reading
also relies on the existing silhouette, interrupted route, load and motion cues.
No time-based blinking, light source, bloom, smoke or particle system is added.

Merger/splitter branches retain their distinct Y topology and direction chevrons.
Ramps keep low/high ports and the same finish across their incline. Both curve
hands use authored geometry with consistent charts. I/O adapters retain separate
inlet/outlet jaws and striped service zones. Decals are opaque atlas inlays on
the existing drive/guard, with 5–10mm offsets to avoid coplanar faces. The small
Directorate/service mark occupies only a corner of the drive plate; no ownership
recoloring or large faction billboard is introduced. Wreck selection/collision,
ports, ground reference, scale18 and 5m-clearance policies remain as documented
in [the model handoff](ConveyorModelsAndAnimations.md). Added status plates do not
serve as collision or navigation authority.

## Reproduction

Run from repository root with the pinned Blender 5.2.2 installation. These are
authoring/build commands, not test commands. Use fresh artifact output folders.

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/conveyor_surfaces.py -InputRoot assets/source -OutputRoot artifacts/conveyor-materials/textures -TimeoutSeconds 180
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/conveyor_author.py -InputRoot assets/source -OutputRoot artifacts/conveyor-materials/author-final -TimeoutSeconds 600
python tools/blender/conveyor_material_package.py --author-root artifacts/conveyor-materials/author-final --stage artifacts/conveyor-materials/staged/source
dotnet build tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj --configuration Release
dotnet tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll --mode build --source artifacts/conveyor-materials/staged/source --runtime artifacts/conveyor-materials/staged/runtime --qualification-output artifacts/conveyor-materials/asset-build.json
```

The texture generator writes only the conveyor material folder. Native authoring
creates fresh editable masters and exports their actual UV/mesh/action data.
Adoption requires all fifteen modules, copies them into the production kit and
stages the complete module/animation/material/texture set. Re-authoring regenerates
geometry from the recipe; preserve hand edits and export an edited saved master
with the model handoff's `--action Export` command instead. Do not replace a full
production catalog with a single-module export.

## Manual acceptance, pending

The user will run tests manually. No automated suite, runtime smoke, CI workflow,
native gameplay capture or golden-image approval has been executed here.

1. Build the full source catalog and run the existing tests manually. The scoped
   build proves package compilation only, not whole-game acceptance.
2. Inspect every module/state/LOD in its Blender master. Confirm relative image
   paths and matching BaseColor/Normal/ORM/emission, especially black offline lamps.
3. In the native game presentation, compare 1080p, 1440p and 4K at near/RTS/far
   distances and the 140/420m LOD transitions. Check chevron/branch direction,
   paused versus active, block versus backlog, damage versus critical and offline
   readability, including monochrome captures.
4. Move the camera slowly across grazing angles and LOD boundaries. Look for
   plate flicker, atlas bleed, stripe moiré, normal seams, excessive highlights,
   light spill and pilot disappearance. Adjust content/mip budgets only on evidence.
5. Confirm one bound material per mesh, shared texture residency, no per-instance
   allocation growth, and stopped animation for all inactive/damaged/wreck states.
6. Repeat the existing vehicle/terrain/port acceptance with actual placement and
   collision integration; visual surface preparation cannot prove road clearance.
