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

## Observed native checkpoint

The qualified authoring/tool content is retained at
`aeee2a3cf76e9f8565be6303f4802b0c6b601f35`, version `0.1.429-dev`.
The first authoring run started before that commit was recorded; its exact family
script content matches that commit. Source signatures bind the actual unchanged
canonical artwork independently. Two genuine Blender projects and eight GLBs are
retained under `assets/staged/artillery`; collision stays an independent reference.
No external artwork, image, third-party mesh or new license input is introduced.

Pinned Blender 5.2.2 x64 build `d13f752e3b9c`, executable SHA-256
`f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0`,
saved both independent rest projects and reopened the first. All four scoped
catalogs compile and pass source-less integrity. First/second/reopened source
and runtime trees are byte-identical. Both opt-in typed tests pass: all mesh,
material, dependency, LOD, collision, socket, mechanical range and surface-channel
comparisons, plus 429 independent runtime pose matrices. Maximum matrix error is
`0.000002026558`, below the unchanged `0.00002` tolerance. Eight invalid native
edits reject and an unchanged saved rest project validates.

Both the full Release solution and the focused test build pass with zero warnings
or errors. All 29 existing portable source/export contracts pass. The dependency
graph passes for 44 projects. The natural seed-2026 validation match terminates
without forced outcomes at tick 55,210: Victory / CommandCoreDestroyed, winner 1,
exit 0. This repeats the prior authoritative result without changing combat code.

The three artillery-only scenes also build with zero warnings/errors in isolated
`artifacts/arf` output, leaving the running full-suite binaries unchanged. All 98
focused scene, authorized artillery aim and existing mechanical regression cases
pass through `dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj
--configuration Release --no-build --artifacts-path artifacts/arf -- --filter-class
ForgeLine.Presentation.Tests.ArtilleryReferenceLoadTests
ForgeLine.Presentation.Tests.VisualReferenceSceneTests
ForgeLine.Presentation.Tests.ArtilleryAimPresentationTests
ForgeLine.Presentation.Tests.MechanicalMotionTests
ForgeLine.Presentation.Tests.MechanicalMotionQualityTests --parallel none`.
The earlier direct-DLL invocation rejected its CLI option before running tests;
its log remains under artifacts and is not counted as a passing test run.

Original qualification, compiler/source-less reports, packed-image hashes,
negative contracts, headless diagnostics and the failed first native attempt are
retained under [`artillery/evidence`](artillery/evidence). The two small Blender
PNGs are authoring inspections, not native game images or approved goldens.

## Integration decision

`ArtillerySingle`, `ArtilleryRepeated100` and `ArtilleryRepeated1000` append native
review scenes without renumbering existing scene identities. They reuse the same
authoritative unit definition and immutable presentation extraction. Repeated
scenes contain only artillery; a quarter share the existing fixed firing/recoil
pose, with the existing travel/aim inputs. They contain no projectile or muzzle
event and do not claim 100/1000 active simulation fire missions. Existing eight-unit
gallery and mixed 1000-vehicle fixture behavior is preserved.

This is a staged preservation/review candidate, not completed high-fidelity
modernization or approved production adoption. Canonical definitions and renderer
bindings are unchanged. Required current-source CI, native visual/moving-LOD and
simultaneous-fire review, 100/1000-artillery physical workload measurements,
source-less installed package checks and explicit human art acceptance must be
reported on their actual sources. Short static captures do not certify sustained
FPS, authoritative firing behavior or every elevation/terrain case. Do not merge,
release, approve goldens or promote source bindings while acceptance is open.
