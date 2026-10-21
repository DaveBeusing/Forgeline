# Infantry atlas orientation refinement

**Current PR: 0.1.434 on tank-integrated master `3c732aa5`.** See the final
post-tank section and [current evidence index](qualification/infantry-surfaces/post-tank/index.json).
Earlier 0.1.432/433 observations below retain their actual source identities.
Performance qualification fails and human/global renderer acceptance stays open.

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
Initial version 0.1.432 is one PATCH above that audited base. The later master
reconciliation below advances the current PR to 0.1.433. No renderer/compiler/
gameplay budget is increased.

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

## Native checkpoint and capture admission repair

Clean implementation source `f3eda9de1c9fc032796cbf999b4cbecaf623a92c` passes a
source-bound solution rebuild with zero warnings/errors (32.03 seconds). An earlier
overlapping build failed because the running catalog validator held compiler
files open; its original log is retained. Both full 1,363-asset catalogs validate
without sources. Each is **74,844,434 container bytes**, with every individual
container length unchanged. IDs, dependencies, material/texture references, LODs,
collision, sockets, mechanical metadata and bounds are identical. Of 1,363 payloads,
**1,359 are byte-identical**: only the two skinned LOD0 meshes and two skins change.
All eighteen animation payloads remain byte-identical; their container hashes
change only because their source GLB hash changes. No texture/material payload grows.

The first native batch (`artifacts/ifn`) completes fourteen pairs / 28 original
images: four nine-action gallery cases (1080p/4K), six default-distance cases and
four Low/Medium cases. All fourteen pairs repeat independently eight times and
have zero binding/mesh fallback failures. All ten static-distance/quality pairs
are PNG-byte-identical; the four close LOD0 gallery pairs change as expected.
Actual default cameras select LOD1 at CloseTactical and LOD2 at NormalGameplay/
Strategic for both roles; these pictures are not counted as animated LOD0 loads.

That batch then **fails**, before graphics startup, on the first pure load request:
the client's separate MechanicalSeconds allowlist had not admitted the appended
scene kinds. A separate actual-process probe reproduces exit 1 and the exact
ArgumentOutOfRangeException. The existing gallery/distance/quality records retain
their original source identity and are not relabelled as a successful full batch.
The follow-up adds precisely those six scene kinds to bounded capture admission;
all nine focused client request/image-contract tests pass (20.380 seconds), including
six new phase-boundary cases. No renderer, timing threshold or workload is changed.

Continuation uses a fresh root and repeats the gallery on the repaired source,
then runs all load/dark/wet/phase cohorts. `-SkipCohorts distances,quality` explicitly
retains the ten already completed pairs on their original source. Combining these
source-bound subsets covers the original forty-pair matrix; the repeated gallery
is additional evidence. This is a capture-input validation change, not evidence
that earlier source hashes are identical to the later source.

The repeat-refinement check also found duplicate packed maps: Save As remapped
relative paths for the staging directory before the master was relocated to its
canonical location. The helper now identifies exact canonical packed bytes for
reuse and disables staging-path remapping when saving canonical-relative paths.
Both failed diagnostic attempts are retained. Corrected UV error is measured after the edit;
the earlier report's similarly named field was an input-alternative diagnostic.
The independent exported-corner contract remains the authority for output UV error.

The repaired native refinement succeeds (`artifacts/infantry-idempotent-3`): both
GLBs remain byte-identical, both saved masters retain exactly three canonical
packed maps and corrected native UV error is zero. Four focused portable tests
pass, including rejection of an unchecked extra mesh. The final saved masters
then pass the complete existing native authoring gate again at
`artifacts/infantry-native-final`: two reopens per role, all-action payload parity,
an edited idle key and eight source-less scoped catalogs. Each reopen now also
checks actual packed PNG bytes, sRGB/linear color spaces and correctly resolving
canonical-relative image paths. No rig, animation or runtime GLB changes accompany
the saved-path repair.

## Completed native matrix and master reconciliation

The continuation at clean source `57a632b5afad82eac5c988d818a6c1a5c4caaa98`
completes thirty pairs / 60 original images in `artifacts/ifn2`. Together with
the first batch, there are **44 pairs / 88 originals**, covering the planned
forty distinct cases plus four repeated gallery pairs. The ten distance/quality
pairs retain source `f3eda9de`; the thirty continuation pairs retain `57a632b5`.
No failed overall attempt is relabelled, and no source identity is substituted.

At the actual 40 m High/1080p crowd view, each role submits 1/100/**551 visible of
1000 input** LOD0 squads (4/400/2204 figures). The 1000-root cases evaluate three
shared poses, submit four instance draws, and report zero skin/static or mesh
fallbacks. Recorded skin upload storage is 4,720,032 rifle / 6,731,424 engineer
bytes; this is the existing diagnostic counter, not complete GPU memory. The
nine-action galleries evaluate nine poses. Existing sixteen-variant, 16-MiB per
frame-slot/four-slot and CPU skin-cache caps remain unchanged.

Master advanced through logistics PR #273 during capture. Synchronization uses
`a391a5bb0c0e25f079fa394613a5f84dfe7e9728` (0.1.432). All **81** integrated
scene numeric IDs, including the nine logistics references, are preserved; the
six unintegrated infantry references append after them. The current PR is
**0.1.433**, exactly one PATCH above that base. Both logistics and infantry
dispatch paths remain intact. That master changes logistics sources/references
and fleet tests; it does not change infantry master/GLB/atlas ownership or the
skeletal renderer/compiler. Existing native observations retain their historical
sources and 0.1.432 build identities. Fresh integrated catalogs, affected tests,
native load/gallery checks and packaging are qualified separately below.

## Final integrated implementation and functional results

The measured implementation and packaged player are clean source
`1991b0c2abbed292ee2a90b7df0e08822f933154`, version **0.1.433-dev**, on
master `a391a5bb0c0e25f079fa394613a5f84dfe7e9728`. The later evidence commit
changes documentation and retained evidence only. It does not relabel historical
captures or replace the package's recorded source with the evidence commit.

The integrated Release solution builds with zero warnings/errors. Focused tests
pass: Assets **46**, Presentation **92**, Client **9**, with zero skipped. All
**114** portable Blender contract tests pass. The real Blender 5.2.2 authoring
gate passes again on the final saved masters, including actual action mutation,
reopening, packed-map bytes/color spaces/paths and source-less compiled parity.
The 44-project graph is acyclic. The actual headless central-divide validation
match reaches **Victory / CommandCoreDestroyed**, player 1, tick **55,210** with
seed 2026 and an 80,000-tick bound. This verifies termination, not physical timing.

Fresh integrated before/after catalogs each contain **1,363 assets and 74,844,665
container bytes**; every container length, ID, reference and metadata record is
unchanged. Exactly four payloads change (two skins and their two LOD0 meshes),
1,359 remain byte-identical, and all eighteen animation payloads remain
byte-identical. Gameplay texture container storage is **57,667,508 bytes**, below
the unchanged 56-MiB limit. No asset/compiler/renderer/gameplay budget is raised.
The current candidate manifest SHA-256 is
`a21607f5c916b8d4ed9f5f5df83bf0c8a201c5a44f4ab841c68038bf55799078`;
its content signature is
`984d9402cc2748dfd7b8985e0074576c0fa87ed75f80e50f603c23a1ced8a1eb`.

Implementation-source hosted checks all finish: [build-test](https://github.com/DaveBeusing/Forgeline/actions/runs/39591628645/job/119194226434),
[navigation-movement](https://github.com/DaveBeusing/Forgeline/actions/runs/39591628644/job/119193744021),
[cpu-scale](https://github.com/DaveBeusing/Forgeline/actions/runs/39591628730/job/119193743928)
and [frame-contracts](https://github.com/DaveBeusing/Forgeline/actions/runs/39591628651/job/119193743753)
pass; optional native-reference is skipped. Hosted checks do not substitute for
the local physical results below. Current evidence-head checks remain visible on
[draft PR #277](https://github.com/DaveBeusing/Forgeline/pull/277).

## Final native observations and failed performance qualification

The integrated source adds fourteen before/after pairs and two rear views in
`artifacts/ifn3`, four installed-player views in `ifni`, and predetermined ABBA
repeats in `ifp` and `ifpc`. The complete inventory contains **66 paired cases,
138 original PNGs, 697,023,933 image bytes**. These include repeated settings;
they are not 138 distinct visual scenarios. All originals verify their recorded
SHA-256, actual dimensions and eight equal readbacks. All paired geometry,
instance/submission/resource counters, fixtures, cameras, profiles, lighting,
DPI and times agree. There are zero mesh/skin fallbacks or material/texture
binding failures. Sixteen pairs are PNG-byte-identical. Corrected LOD0 surfaces
produce the intended changes in the other fifty pairs.

The maximum observed tracked texture residency is **44,812,076 bytes**, descriptor
usage **93/4096**, and evaluated poses **9/16**. Maximum recorded skin upload
storage is **20,194,272 bytes across three frame slots**; the unchanged cap is
16 MiB per slot with at most four slots. Neither diagnostic is a complete VRAM
measurement or approval of the global memory/frame/lifecycle gate.

**Performance qualification fails.** The unchanged
`Compare-ReferencePerformance.ps1 -GatePerformance` applies a 15% bound to CPU/GPU
mean, p50, p95 and p99. Across the sixteen initial cohorts, two pass and fourteen
fail, with 94 statistic exceedances and zero unqualified cases. One retained
short phase sample moves CPU mean from 21.516 to 58.869 ms; another moves GPU
mean from 12.481 to 16.751 ms. Neither is discarded as an outlier.

The predetermined integrated ABBA repeats use 32 warmups, 128 measured frames and
eight readbacks per case, with baseline A / candidate B / candidate C / baseline D.
All four gallery comparisons (rifle phases 0 and .75, B/A and C/D) fail, retaining
eight statistic exceedances. Both two-role 1000-root crowd comparisons at phase
.375 pass, with zero exceedances. These successful crowd repeats do not erase
the original failed close-load cohort. Same-runtime controls fail five of six
cohorts (ten statistic exceedances), demonstrating observed drift without
establishing its cause or waiving any failed asset comparison.

Read-only, one-second NVIDIA telemetry is retained throughout both repeat batches:
546 gallery and 1,115 crowd samples, all **P8**, graphics clock **172–180 MHz**,
maximum utilization **100%**, temperatures **42–44 C**, observed peak power
**18.63 / 20.29 W**, power limit reported **[N/A]**. This is not a qualified
normal-power benchmark and does not establish a configured power cap. No power
settings, thresholds, workload sizes or renderer hot paths are changed. Further
physical qualification needs a controlled operating environment and must retain
these failures. Human art/animation/LOD-transition review and the broader renderer
physical timing, complete memory/frame and lifecycle gates remain open.

## Reproducible package and fresh installation

The actual clean-source `Publish-PreAlpha.ps1 -VerifyReproducible` run produces
`artifacts/ifpkg/FORGELINE-0.1.433-dev-win-x64.zip`: **55,140,397 bytes**, SHA-256
`CB5BE6680BCC98A7543D8C652601F00988661DA2BE87391FA944B43C4A30EE0F`.
Both independently published checksum trees agree; this does not assert two
independently generated ZIP files are byte-identical. Provenance records the
clean implementation source, SDK 10.0.401 and the exact runtime content signature.
No Blender source, glTF/GLB, exporter script, source tree or compiler ships.

A fresh extraction into the sibling `infantry-player433` directory, outside the
checkout, verifies **all 1,487 file hashes**. Both published-directory and fresh
installation source-less smoke tests exit 0. Their child PATH contains only
system/.NET locations; Blender remains installed on the workstation. The
installation contains all **1,364 compiled files byte-identically**, plus 63
existing publisher-added media files. Those extras explain differing directory
fingerprints. All four installed-player native views are pixel-exact to the
matching integrated development views, with eight repeat readbacks each.

## Review evidence and remaining acceptance

[Evidence index](qualification/infantry-surfaces/index.json) records every original
image's source/key/path/dimensions/size/SHA-256, ten unmodified selected PNG copies,
exact original and retained hashes for gzip-compressed metadata/logs, telemetry,
failed attempts, and the complete local evidence archive's size/hash. Gzip
decompression restores the original bytes. Raw frame samples and failed gates
are retained alongside successful checks. The complete local archive contains
all 138 original PNGs and original metadata; it is separate from the player ZIP.

| Role | Original integrated baseline | Corrected integrated candidate |
| --- | --- | --- |
| Rifle Squad | [1080p / 20 m](qualification/infantry-surfaces/images/ifn3/gallery/before/InfantrySkinning-1920x1080-High-CloseTactical-MechanicalT0.375-Inspection20.png) | [1080p / 20 m](qualification/infantry-surfaces/images/ifn3/gallery/after/InfantrySkinning-1920x1080-High-CloseTactical-MechanicalT0.375-Inspection20.png) |
| Combat Engineer | [1080p / 20 m](qualification/infantry-surfaces/images/ifn3/gallery/before/InfantrySkinningEngineer-1920x1080-High-CloseTactical-MechanicalT0.375-Inspection20.png) | [1080p / 20 m](qualification/infantry-surfaces/images/ifn3/gallery/after/InfantrySkinningEngineer-1920x1080-High-CloseTactical-MechanicalT0.375-Inspection20.png) |

This is a reviewable atlas-origin correction and portable authoring repair with
functional/native/package evidence. It is **not** accepted high-fidelity
modernization or a performance-qualified release. PR #277 stays draft; no merge,
tag, release or golden approval is performed.

## Post-tank master reconciliation: current 0.1.434

During finalization, tank finish PR #278 advanced master to
`3c732aa519bb54bec17070bcdad378c232ea9de1`, version 0.1.433. Real conflicts in
the scene enum/time guard, client capture time admission and its tests are
resolved by preserving both tank and infantry behavior. All **84** master scene
IDs remain unchanged; the six infantry load references append after them.
Both test groups survive. Version **0.1.434** is exactly one PATCH above this base.
The infantry masters, GLBs, atlas maps, authoring helpers, compiler, skeletal
renderer and gameplay code are unchanged by this reconciliation.

The new clean measured/package source is
`c85fd2987611de126963c93608cf3eda66636680`. Its Release solution build passes
with zero warnings/errors. All **119** portable contracts pass. Focused tests
pass: Assets **46**, Presentation **103**, Client **12**. Three optional compiled
tank parity cases initially skip because their catalog environment is unset;
all **three** are then explicitly rerun with the real old/new tank catalogs and
pass at 30/60/144 Hz. There are **164 unique passing focused cases**, including
those actual compiled geometry/binding/mechanical-matrix comparisons. Earlier
Blender native and headless results retain their stated source; their infantry
authoring/gameplay implementations have no intervening change.

Fresh current-master baseline/candidate catalogs both validate without sources:
1,363 assets, 74,844,665 bytes each, unchanged individual container sizes and
metadata, the same four infantry payload changes and eighteen byte-identical
animation payloads. The new candidate manifest SHA-256 is
`3541dc2ae0f6275bf2b7c9b1357539f932598eae6c3b746cae1e790e50a5c579`,
content signature
`1226b257a9eb59a022a0605394a40dcecd4efd46f041582965d7a1287dcc2827`.

Four additional native before/after pairs cover both role galleries at 20 m and
both 1000-root loads at 40 m, High/1080p, with 8 warmups / 32 samples / 8 equal
readbacks. Matching geometry/resource counters and zero fallback/binding failures
are verified. **Performance still fails:** the current gallery has four engineer
CPU statistic exceedances; the crowd cohort passes. No historical failure is
discarded. Four further actual installed-player captures match this development
source pixel-exactly. Across the original index and this source-bound addendum,
there are **70 paired cases and 150 original PNGs**, including repeated settings.

The current reproducible-publish-tree run produces
`artifacts/ifpkg434/FORGELINE-0.1.434-dev-win-x64.zip`, **55,141,213 bytes**,
SHA-256 `5b06d740b0e7f51d09aa863ec29a7b0771d86d15e77242e4d73a370fddf065f1`.
Provenance records clean source `c85fd298`. Fresh extraction into sibling
`infantry-player434` verifies all **1,487 hashes**. Published and installed
source-less smoke tests both exit 0 without timeout. The same 1,364 compiled
files are byte-identical to the candidate catalog, plus 63 publisher media files;
no authoring assets/scripts/compiler ship. The earlier 0.1.433 package is retained
as historical evidence and is not relabelled 0.1.434.

[The post-tank index](qualification/infantry-surfaces/post-tank/index.json)
retains twelve new original-image identities, two unchanged selected PNG copies,
exact compressed raw logs/manifests/test results, package provenance and the
53,278,121-byte complete local addendum archive, SHA-256
`96cf20bb905653e801ca1ca975dbfd7daa6f8ca6448030e24e5ae010d93ea31d`.
The two archives together retain every original image and raw measurement.
Selected current images: [rifle](qualification/infantry-surfaces/post-tank/images/rifle.png)
and [engineer](qualification/infantry-surfaces/post-tank/images/engineer.png).

The subsequent documentation/evidence commit changes no production inputs from
`c85fd298`; its current-head checks are tracked on PR #277. This remains a draft
with failed physical performance qualification and pending human/global renderer
acceptance. No merge, tag, release or golden approval is performed.
