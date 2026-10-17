# Directorate tank Blender production

The editable production source is
`assets/source/units/directorate/authoring/main_battle_tank.blend`.
Its seven compiler inputs are the GLB files under
`assets/source/units/directorate/sources/main_battle_tank/blender/`.
The existing tank asset definitions bind these files; the separate collision
source, IDs, gameplay sockets, LOD thresholds and shared material stay unchanged.
The saved project owns subsequent supported shape edits. The component recipe
and its glTF/kit exports remain a deterministic reconstruction seed, not a command
to overwrite a reviewed Blender edit.

## Art and budgets

The broad industrial chassis gains separated skirt armor, towing/service details,
readable rotating wheel spokes and a less repetitive tread pattern. The turret
has layered sloping cheek armor, a protected second optical unit, side tool lockers
and smoke hardware. Existing shared paint, restrained wear, rubber, glass and
heat-treated steel remain the only materials. Fine repeated cylinders give way
to larger planes and silhouette breaks readable from RTS cameras.

LOD0 is 5,528 chassis plus 1,736 turret triangles (7,264 total, previously 8,024).
LOD1/2 remain 892/244 triangles and keep neutral combined weapon silhouettes.
Damaged/critical/wreck geometry remains 88/180/300 triangles. No new texture,
material slot or mechanical range is added; eight wheel bindings and the existing
cannon binding retain their names, pivots, rates and recoil travel. Static treads
and symmetric wheel shells remain static geometry. Rotating spokes use the existing
wheel transform; physical suspension and circulating tread links are not supported.

## Edit and export

Open the saved project in pinned Blender 5.2.2. Assets, parts, LODs, states and
sockets retain their logical properties. Use object visibility to inspect variants;
there is no need to move them apart. The rest project has no generated actions,
preview cameras or packed runtime clips. Scene unit scale is 1.0; the existing
4 x 3 x 7.2 gameplay instance scale remains external to the normalized source.

Supported manual edits move existing static vertices within the retained tank
silhouette, preserving topology, atlas UVs, explicit face normals and all frame
transforms. Translation or coplanar adjustment of a static fitting is supported.
Moving-part geometry, material changes, socket edits, noncoplanar reshaping,
modifiers, new objects, constraints and animation actions fail validation. Explicit
normal channels remain the source owner; their compressed Blender preview is
re-encoded after reopening. New topology or normal changes require a reviewed
recipe/source migration and regeneration, not bypassing the validator.

```powershell
./tools/blender/Invoke-BlenderTank.ps1 -Action Export `
  -BlendFile assets/source/units/directorate/authoring/main_battle_tank.blend `
  -OutputRoot artifacts/tank-candidate -RenderPreviews
./tools/blender/Test-BlenderTank.ps1 -OutputRoot artifacts/tank-check -RenderPreviews
```

The launcher requires fresh contained artifact folders and leaves failed logs.
Outputs include the isolated source closure, `tank-report.json`, sampled
`poses.json`, separate `inspection.blend` and optional CPU Cycles previews.
Generated inspection actions are review aids, never export input or player clips.
The full qualifier reopens the saved rest project twice, compares source/runtime
bytes, rejects invalid edits and compiles a saved positive static-shape edit.
Use `-CompilerPath` for an isolated SDK artifacts build. The standard asset compiler
and source-less integrity remain mandatory; producing a GLB alone is insufficient.

After review, copy only the seven GLB files and matching definitions from the
candidate `source/units/directorate/` subtree, alongside the edited rest project.
Rebuild the full runtime catalog. Never copy authoring projects into runtime or
make the player open source files. Runtime/package checks exclude `.blend` files.

## Reconstruction and recovery

The seed lives in `main_battle_tank.kit.json` and `build/Author-MainBattleTank.py`.
With Python and NumPy, `python build/Author-MainBattleTank.py --output artifacts/tank-seed`
reconstructs twelve glTF/kit exports without changing production bindings.
`--update-metadata` deliberately switches definitions back to seed geometry; use
it only for a reviewed reconstruction, then run `Invoke-BlenderTank.ps1 -Action Author`
to create a new rest source and export candidate. Compare contracts and native
references before adopting it. Preserve the previous edited project first.
The Blender pipeline uses bundled NumPy, no downloaded addon or player dependency.

A definition mismatch requires explicit migration/regeneration. Do not repair it
by editing logical properties or removing tests. Retain failed output and choose
a new directory after correcting the source. Human visual acceptance, live effects
alignment and sustained performance acceptance remain distinct from structural
or sampled matrix tests.
