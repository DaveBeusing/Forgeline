# Infantry atlas orientation refinement

This review candidate corrects a demonstrated UV-origin defect in the existing
Rifle Squad and Combat Engineer masters. It does not replace the established art,
re-rig the figures or claim complete high-fidelity art/animation acceptance.

## Audited integration source

Fresh remote master `a6fe118583be842e00416bcb100d1210f3240a00`, version 0.1.431;
remote HEAD/default/integration branch is master. The only related remote work is
open logistics-building PR #273; no infantry/skin/rig branch exists. PR #185 already
integrated four-figure equipment geometry and static LOD/state meshes. PR #234
already integrated genuine Blender masters, 44-joint skins and nine actions per
role. Both are reused. PR #234's final main check was cancelled; its focused
navigation/frame/CPU checks passed. Its own report retains a failed CPU-tail pair
and power-limited measurements rather than claiming performance acceptance.

Inspected current source definitions, authoring/exporter, immutable pose extraction,
Skin type 6, compiler mapping, tests, references, package rules, native authoring
workflow and architecture/governance/development/asset/version/terrain documents.
PR #234 and render qualification PR #268 have no issue/review/inline comments.
PR #268 is integrated and all its hosted checks passed, while optional physical
jobs were skipped. Its physical timing, full memory/frame/lifecycle and human
review gates remain open. Merging tooling is not acceptance. This branch may be
reviewed in isolation; no merge, release, golden approval or production acceptance
is performed.

Branch `art/infantry-equipment-refinement` starts directly from that master.
Version 0.1.432 is one PATCH above the audited base; reconcile against latest
master before integration. No renderer/compiler/gameplay budget is increased.

## Demonstrated quality gap and change

The old rig builder copied glTF/D3D top-left UVs directly into Blender's bottom-left
image coordinates. Blender's subsequent glTF export flipped V again. Both saved
production skins consequently disagreed with their preserved static LOD0 atlas
coordinates by up to **0.9639999941**. Intended rubber cell 2 sampled cell 14;
other cloth/metal assignments were similarly reflected. Existing close reference
images show the yellow boots and inconsistent equipment surfaces. The compiler
reads TEXCOORD_0 directly; it is not the place to change this source convention.

Correct only native UVs to `(u, 1-v)` and repair the reconstruction path. The final
export matches static source positions exactly and UVs within **1.49011612e-8**.
Existing shared atlas Base Color, Normal and ORM bring fabric, rubber, metal,
paint and restrained markings back to their intended components. The same
material ID and capped texture definitions are retained; no texture is generated.
Static LOD1/2 and damage/wreck geometry stay intact.

Both genuine editable masters now pack the exact existing three atlas maps with
sRGB Base Color and linear Normal/ORM. Principled roughness uses G and metallic
uses B; AO remains the existing runtime lighting channel. Blender preview graphs
are source-only. GLB material placeholders preserve section slots without packing
three 4K maps into every runtime skin source. Runtime PBR still resolves the
canonical material/texture Asset IDs through the normal compiler.

Native assertions preserve mesh vertices/topology, vertex groups/weights, all 44
bone names/rest matrices and every action curve. Independent exported-GLB checks
preserve positions/normals/joint indices/weights, inverse binds, hierarchy, action
names/channels/interpolation and every key exactly. The original has 15,360 rifle
and 22,272 engineer triangle corners; neither triangle budget changes. Native
custom-normal quantization already differs from the static recipe: normals are
compared exactly against the original GLB, not assigned a new cross-format budget.
The first diagnostic attempt using static normals as that reference failed and
is retained locally. An initial material export embedded packed maps; it was
rejected, then replaced with Blender's explicit placeholder export mode.

No demonstrated gait/authority defect is repaired by changing action keys. Nine
action identities, muzzle socket, snapshot-derived selection, collision, scale,
bounds, 90/28-pixel LOD policy, bounded pose caches and one simulation entity per
four-figure squad remain unchanged. No IK, ragdolls or individual controls appear.

Six appended opt-in references exercise each family at 1/100/1000 squad roots.
They copy a fixed walking phase into immutable instances. Existing nine-action
galleries and mixed 1000-squad workload retain their behavior. Source actor count
does not imply every actor is visible or animated at LOD0.

## Reproduction and qualification

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check
# Safe/idempotent recognition of either known native convention; output isolated.
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/refine_infantry_surfaces.py -InputRoot assets/source -OutputRoot artifacts/infantry-refined
python tools/blender/infantry_surface_contract.py --candidate assets/source/units/directorate/authoring/infantry
python -m unittest discover -s tools/blender/tests -p 'test_*contract.py' -v
./tools/blender/Test-BlenderInfantry.ps1 -OutputRoot artifacts/infantry-native
dotnet build ForgeLine.sln --configuration Release
./build/Validate-ProjectReferences.ps1
./build/Validate-SemVer.ps1 -BaseRef origin/master
```

The existing real-Blender gate reopens each saved master twice, exports all nine
actions, compares compiled skin/clip payloads, changes an actual idle key and
validates eight scoped catalogs with their source trees unavailable. Portable
tests independently compare actual static/skinned corner UVs and reject reflected
UVs and shifted geometry. Existing typed tests own skin evaluation, all actions,
socket/matrix tolerances, frozen ticks, lifecycle and allocation/pose-cache limits.

Native review uses `build/infantry-surfaces/Capture-Review.ps1` with isolated
before/after compiled roots and the same clean-source player. It records nine
actions per role, five extra phases, 1080p/4K, Low/Medium/High, actual default camera
distances, close/normal 1/100/1000 roots, dark and wet surfaces. Eight warmups,
32 samples and eight repeat images are diagnostic observations. Changed pixels
are expected for corrected material cells; no human approval or golden update
is implied. Short sampled phases do not establish live gait, moving LOD continuity,
sustained FPS or controlled performance non-regression.

Execution results, exact source identities and remaining gates are recorded below
as qualification completes. Authoring-only success is not completion evidence.

## Executed authoring and functional checkpoint

Pinned Blender 5.2.2 x64 Check and surface refinement pass. Existing
`Test-BlenderInfantry.ps1 -OutputRoot artifacts/infantry-native` passes: two real
reopens per family, all-action compiled parity, actual idle-key mutation and eight
source-less scoped catalogs. It uses the already built Release compiler at
`artifacts/merge274/bin/ForgeLine.AssetCompiler/release/ForgeLine.AssetCompiler.dll`;
the compiler implementation has no change from the audited source. Raw results
remain at `artifacts/infantry-native/result.json`; initial failed diagnostic/export
attempts remain in their separate original output directories.

`dotnet build ForgeLine.sln --configuration Release --artifacts-path artifacts/infantry-build`
passes with zero warnings/errors. Focused Assets skin/rigid pipeline tests: **46
passed, zero skipped** (38.677 seconds). Focused Presentation animation/cache/new
role loads/general reference tests: **83 passed, zero skipped** (39.702 seconds).
The project graph validates 44 projects without cycles. Three new portable source
tests pass, including both real production-role GLBs and negative reflected-UV/
shifted-geometry cases. Full portable/native/player results are recorded separately.
