# Artillery Blender preservation and inspection

## Current-source audit

Integration/default branch `master` was fetched at
`b32bf04671fb579e01a7e282eac76ff91c5b2f1d`, version `0.1.428`.
The remote branch inventory contained only `master`; there was no unfinished
artillery branch or open related PR to continue. Integrated PR #186 already owns
the artillery kit, 4648-triangle chassis, 640-triangle weapon, reduced LODs and
damage/critical/wreck geometry. PRs #228–231 supply the strict Blender round trip,
surface review, typed mechanical authoring and saved tank precedent. Their changed
files, review comments and source-bound checks were inspected. Historical failures
are not relabelled as passing current-master results.

The demonstrated gap is the missing saved native artillery master. Existing
artwork is reused, without reauthoring geometry, shared PBR atlases or mechanical
metadata. The independent collision, stable IDs, muzzle/gun/recoil/turret sockets,
3.8 × 3.2 × 7.5 scale, 1.4-m root height, state selection and projected-size LOD
thresholds/hysteresis remain authoritative. There is no deployed gameplay mode,
circulating tread, new physical stabilizer or new ballistic controller.

PR #268 is integrated, but its physical jobs were skipped and its complete-frame,
GPU-memory, thermal, lifecycle and explicit human acceptance gates remain open.
The ownership manifest still reserves canonical sources/compiler. This work stays
outside those paths. Exact master main CI run 39547596264, job 119049220997 fails
the version step: expected `0.1.429`, found `0.1.428`; subsequent build/product gates
are skipped. This is inherited integration history, not an artillery result.

## Authoring boundary

`build/artillery/fixture.json` binds all seven existing visual mesh IDs plus the
independent collision ID. `author.py` delegates strict import/export to the current
adapter. The rest project preserves source geometry, original part hierarchy,
ordered material slots, UVs/normals/tangents and named sockets. Collections expose
LOD/state assets separately; the intact chassis and weapon are initially visible.
Coordinates retain engine `(x,y,z)` → Blender `(x,-z,y)`, metre units and original
normalized source pivots. Instance scale is applied only by the existing runtime
and inspection fixture, not silently baked into compiler sources.

The separate packed PBR inspection project contains generated idle, forward,
reverse, stopped, paused, firing, recovery, unpowered, destroyed-disabled and
LOD-neutral sample poses. These are sampled copied presentation inputs, not new
runtime animation clips or authoritative combat events. Its part frames are
flattened only after composing complete world matrices, avoiding Blender local
TRS shear under the artillery's nonuniform metric scale. The strict export master
keeps the original hierarchy. Independent .NET comparison retains the established
0.00002 matrix tolerance, including outer turret yaw, elevation, recoil, wheel
travel and LOD-neutral behavior.

Packed images retain original source bytes, sRGB Base Color and linear Normal/ORM.
Inspection uses the existing shared shader approximation; ORM AO remains a game
shader input. It does not bake arbitrary material graphs or promise Blender/game
shader parity. Rest projects accept preservation exports only; changed mechanical
properties are rejected. Deliberate supported metadata editing remains owned by
the existing `Invoke-BlenderMotion.ps1` workflow. Inspection actions/PBR graphs
are deliberately rejected as strict export inputs.

## Reproduction

Use pinned Blender 5.2.2 x64, .NET SDK 10.0.401 and PowerShell 7.4+:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/ar-check
pwsh build/artillery/Qualify-Master.ps1 -OutputRoot artifacts/ar2 -RenderPreviews
python build/artillery/retained.py artifacts/ar2
python build/artillery/retained.py
```

Choose fresh short output paths. Qualification saves independent first/second
rest masters, reopens the first, compares source/runtime bytes, compiles all four
catalogs, validates without source access, rejects invalid native edits and checks
typed geometry/material/LOD/socket/range plus 429 pose matrices. It hashes all
canonical sources before/after. The optional `FORGELINE_ARTILLERY_RUNTIME_SEED`
test input composes a full sealed isolated player catalog without editing
`assets/runtime` or canonical definitions.

The first real run under `artifacts/ar1` failed the generic nested inspection
matrix representability check after strict rest export. Its logs remain intact.
The family-scoped flattened inspection addresses that representability limit;
no runtime transform, source part hierarchy or comparison tolerance is changed.

## Integration decision

This is a staged preservation/review candidate, not completed high-fidelity
modernization or approved production adoption. Canonical definitions and renderer
bindings are unchanged. Required current-source CI, native visual/moving-LOD and
simultaneous-fire review, 100/1000-artillery physical workload measurements,
source-less installed package checks and explicit human art acceptance must be
reported on their actual sources. Short static captures do not certify sustained
FPS, authoritative firing behavior or every elevation/terrain case. Do not merge,
release, approve goldens or promote source bindings while acceptance is open.
