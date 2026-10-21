# World prop Blender masters

## Scope and current review status

Starting integration base: master a524b0a8164390abe1e9cb38170e690f46147574,
version 0.1.436. The branch prepares exactly one PATCH to 0.1.437.
The ordered source audit is retained in world-props/Precheck.json.

All 22 existing world prop families are covered: rock, slab, spire, barrier,
modular barrier, concrete block, crate, drum, pallet, pipe section, elbow, utility
box, fence, service fence, industrial light/signage, rubble, scorched barrier,
burst pipe, loading gantry, loading apron, road edge and bridge apron. These last
three are already catalogued as props; infrastructure road/bridge sources remain
separately owned. Vegetation, mineral deposits, terrain and decals are excluded.

The current catalog already contains functional silhouettes, hollow pipes,
flanges, supported gantry bays, atlas UVs, explicit normals and High/Reduced LODs.
This work preserves that artwork and supplies the missing retained editable
Blender sources, with one demonstrated straight-pipe axis correction. It does not infer a new throughput, lamp, damage or cargo state
from decoration. The existing crate is one atlas-authored family; timber/metal
regions retain their existing material and UV identity rather than new gameplay
variants or duplicate stable IDs.

## Ownership and adoption

Native source belongs under assets/staged/world-props/<family>/. Each family
retains a strictly exportable rest master and a separate packed-PBR inspection
project. Reviewed GLBs bind the original 44 High/Reduced IDs under
assets/source/world/meshes/sources/blender/props/. Existing glTF recipes and
inputs remain available. Every definition field except its source path must
match the audited original-bindings.json. Compact high.glb/reduced.glb filenames
keep embedded Blender source clones below Windows file-path limits in deep
worktrees; the stable IDs remain unchanged in their definitions.

The adapter under build/world-props uses the existing bounded static importer,
exporter, pinned Blender launcher and production PBR preview. Common Blender
modules, compiler and renderer contracts are unchanged. No authoring content
belongs in the runtime/player package. Source signatures normalize JSON/glTF
line endings while raw hashes still detect changes during processing.

Engine +Y up/+Z forward maps to Blender +Z up/-Y forward as (x,-z,y), in metres
at unit scale. Pivots, bounds, winding, topology, UV0, material slots, LOD distances and tooling
collision references retain the existing contracts. Straight-pipe positions,
normals and tangents receive the one baked rotation described below; other
families preserve their geometry. Original ForgeLine provenance and shared
Base Color (sRGB), Normal and ORM (linear) atlases are retained. No new texture,
material, transparent billboard, mesh section or shader feature is introduced.

## Straight-pipe axis correction

The original high-detail pipe bore runs along source Z, while existing map
instances scale X as their length: the legacy pipe is [12,4,4] and yard pipes
are [9,2,2]. This stretches flange cross-sections and shortens the bore.
The family-scoped correction bakes +90 degrees around game Y into both existing
pipe LODs: (x,y,z) becomes (z,y,-x). Their unit-cube bounds, pivots, UVs, index
order, material bindings and all map transforms stay unchanged. Tangents and
normals rotate with the geometry and handedness remains unchanged. High/Reduced
triangle counts remain 576/12. No extra batch, texture or source ID is added.

Qualify-PipeAxis.ps1 independently saves/reopens the correction in actual
Blender, compiles original and corrected sources, validates source-less loading
and invokes a typed .NET test of the exact baked rotation. The binding command
requires that separate qualification; it cannot silently turn a preservation
comparison into approval of arbitrary edits. Already-bound sources are refused
by the one-time correction producer. The final retained masters are imported
and qualified against the corrected canonical GLBs.

For authoring assemblies, the corrected straight pipe uses X as its centreline;
3x1x1 reference scale pairs with the existing 3x1x3 elbow vocabulary at nominal
one-metre flange size. Fences retain 8m module pitch, barriers 4m, and gantries
retain supported footplates and an open loading bay. Existing map instances
keep their original scales; these authoring dimensions introduce no sockets,
new transport logic or physical collision authority. Exact live joint/terrain
contact and moving LOD appearance remain visual review items.

## Gameplay and review fixtures

CentralDivideDressing, CentralDivideKitDressing, map definitions, world keys and
original transforms are untouched. PresentationOnly scenery remains public,
noninteractive and nonblocking; compiler collision proxies are tooling metadata
and never become navigation, construction, visibility or cover authority. No
new scenery ECS entities, construction footprint, resource clearance or road
placement logic is introduced.

EnvironmentKitYard and EnvironmentKitBridge retain actual authored placements.
IndustrialBase retains its existing production fixture. WorldPropsGallery adds
22 copied presentation instances, while WorldPropSingle, WorldProps100 and
WorldProps1000 provide bounded synthetic loads without allocating ECS entities.
They prefer existing PresentationOnly yard/module scales, with legacy map scales
as fallback, and retain the existing half-height ground
anchor. They are inspection scenes, not new map placements or gameplay presets.

The pre-existing bridge visual/logistics orientation mismatch described in
ModularEnvironmentKit.md remains an infrastructure review item. A static ground
anchor does not prove every corner contacts uneven terrain or that a moving
camera has no LOD popping; these require native review.

## Reproduction

Build Release first. Use fresh artifacts directories, the pinned Blender 5.2.2
launcher and a material-only cache containing the existing four shared records.

```powershell
python build/world-props/prepare-materials.py artifacts/prop-material-source
./build/Build-RuntimeAssets.ps1 -Mode build -NoBuild -SourceRoot artifacts/prop-material-source -RuntimeRoot artifacts/prop-material-runtime
./build/world-props/Qualify-Masters.ps1 -OutputRoot artifacts/prop-native -MaterialCacheRoot artifacts/prop-material-runtime -NativeBatch -RenderPreviews
python build/world-props/test_contract.py
python build/world-props/retained.py
```

The qualifier imports independently twice, reopens the first saved master, runs
six actual Blender mutation rejections against the visual root, compiles four
source variants per family and validates every catalog with an absent source
root. Repeated/reopened source and runtime bytes must match. Typed .NET tests
compare compiled geometry, generated tangents, material channels, sections and
metadata to the original baseline, including unchanged shared collision proxies.

World-prop CI verifies retained source hashes and the portable signature
regression on hosted Windows. Manual native qualification requires the existing
provisioned Windows/X64/blender runner; a skipped native job is not a pass.

Capture-Review.ps1 uses the real client executable, two full qualified runtimes
and a fresh output. Its 48 paired functional cases cover actual yard/bridge/base
views, all prop families and repeat loads, close/normal/strategic zooms,
Low/Medium/High, 1080p/4K/ultrawide and dark/wet settings. Three warmups, eight
samples and two repeated readbacks establish short functional observations only.
They cannot certify sustained FPS or replace controlled physical AB/BA runs.

## Acceptance boundaries

Render qualification PR #268 is integrated, but its physical paired timings,
complete live-frame/memory/timestamp observations, moving lifecycle and explicit
human visual approval remain separate open gates. No budget, threshold or
golden is relaxed. No production promotion, merge, tag or release is authorized
by a saved master, passing schema or repeatable still image.

## Native authoring and runtime checkpoint

Original-source preservation at 22f5bde1a6ce4544462eb7ca963747ea5524b9c5 passed
88 compiler/source-less catalogs and all 22 typed family comparisons. Its actual
authoring producer scripts match 75ef57ae35e1d665affc56c61aeabbf8d26583ae.
The explicit pipe correction at 22f5bde1a6ce4544462eb7ca963747ea5524b9c5 passed
independent/reopened native exports, four compiler/source-less catalogs and the
exact typed rotation test.

Final compact-binding producer source 09036b0261798a90e954adf840b74757384e2365
passed all 22 native families, 132 actual mutation rejections, 88 compiler/source-
less catalogs and 22 typed comparisons. All repeated/reopened source/runtime
bytes match. The retained verifier confirms 22 rest/inspection pairs and 44
canonical exports against the current source contracts.

Both full catalogs contain 1363 assets. Original/candidate container sizes
are 74,844,887 / 74,844,758 bytes. Exactly 44 scoped prop containers differ; every
unrelated runtime hash and all metadata/material/LOD/collision references match.
Texture-container bytes remain 59,453,925; excluding the existing 1,786,417 splash
bytes leaves 57,667,508 gameplay bytes below the unchanged 58,720,256-byte gate.
This is a compiled-container metric, not complete measured GPU memory.

The full Release build passed with zero warnings/errors. Earlier focused checks passed
91 presentation/reference, three map-dressing, four updated gallery and three
portable source/axis cases. The shared Blender portable suite passed 119 tests.
The three-worker compiler proof passed twelve source-less catalogs. Cache lock,
interruption/recovery, source-less and damaged-deployment process contracts pass.
Map qualification passes 71 reachability and two alternatives each for navigation
and roads. Exact master main CI also passes. These earlier checkpoints are
separate from the final product-source results below.

The first native attempt's material-slot mutation selected a materialless
collision proxy; the scoped test now targets the visual root. A later bound
run correctly failed on a 275-character Windows source-clone path. Compact
filenames reduce that measured path to 239 characters; every renamed GLB hash
remained identical. Failed/interrupted directories are retained as nonpasses,
not reused as final qualification.

## Original functional qualification

Branch art/world-prop-masters, draft PR [#283](https://github.com/DaveBeusing/Forgeline/pull/283),
retains product source 3545a23125d5ca8b3c24d50a8e77d72175c5acf6. The integration
base remains a524b0a8164390abe1e9cb38170e690f46147574; version 0.1.437 is exactly
one PATCH over 0.1.436. Subsequent evidence-only commits do not change compiled
product sources. Their current CI status is reported on the PR.

The final full Release solution run passed 3,297 tests, failed zero and skipped
46 of 3,343 total in 31m 30s. All 22 prop-master cases and the exact pipe-axis
case were enabled. Skips retain the existing other-family opt-ins, hardware
performance opt-ins and unavailable debug-layer/lifetime prerequisites; they
are not counted as passes. The exact product-source CI passed build-test,
frame-contracts, navigation-movement, cpu-scale, retained-props and
retained-minerals. Three provisioned/manual native jobs were skipped.
[Validation](world-props/evidence/validation.json) and
[product CI](world-props/evidence/product-ci.json) retain exact results and log hashes.

### Native player observations

The main matrix passed 48 before/after cases (96 original still PNGs). It covers
1080p/4K/3440x1440, Low/Medium/High, close/normal/strategic cameras, actual
yard/bridge/base scenes, all 22 families, 1/100/1,000 instances and dark/wet
settings. Each side uses the same client binaries, hardware and settings,
three warmups, eight sampled frames and two repeated readbacks. Runtime
catalogs intentionally differ. Fifteen pairs are exact matches; the maximum
changed-pixel fraction is 0.0024488812 (0.245%, the visible straight-pipe
correction in the gallery). No image-similarity tolerance or golden is approved.

Two supplementary paired camera traversals use 64 sampled frames per side:
1,000 props at 1080p and the gallery at 2560x1440. Both retain identical
per-frame instance, culling, High/Reduced and draw counts across all 64 frames.
The 1,000-prop traversal ranges from 0 to 715 visible instances, 0 to 110 High
and 0 to 612 Reduced instances. The 1440p run additionally retains 16 adjacent
camera-pose PNGs. These scripted immutable snapshots cannot certify live
gameplay routes, long lifecycle behavior, smooth motion or human LOD acceptance.

Device: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, D3D12 feature level
12_1, driver 32.0.15.9653, reported dedicated memory 25,309,478,912 bytes.
Native output surfaces match their requested dimensions independently of the
1280x720 host-window clamp. Raster DPI is 96; host DPI is 144. Debug layer was
unavailable. The client reports .NET 10.0.12 on Windows build 26300.

| High/Normal 1080p load | Visible before/after | Draw calls before/after | High/Reduced before/after |
| --- | --- | --- | --- |
| 1 | 1 / 1 | 1 / 1 | 0/1 / 0/1 |
| 100 | 100 / 100 | 23 / 23 | 15/85 / 15/85 |
| 1,000 | 715 / 715 | 25 / 25 | 116/599 / 116/599 |

The Low/Strategic 1,000-prop case draws 999 visible Reduced instances in 22
draws on both sides. All main and supplemental cases have zero mesh fallbacks,
material/texture binding failures, submission faults and repeat mismatches.
The main matrix also reports zero missing/dropped UI assets or glyphs.

High load cases retain 44,743,436 logical resident texture bytes, 10,940,424
resident mesh bytes and 81 descriptors on both sides. Across the main matrix,
maximum reported logical texture residency is 45,017,996 bytes, descriptor use
129 and observed DXGI local usage 750,845,952 bytes. These are scoped observations,
not a complete frame-memory acceptance ledger or a new budget.

For the 1,000-prop High/Normal case, CPU p50/p95/p99 before/after are
7.181/43.953/43.953 ms and 7.333/30.190/30.190 ms (eight samples). GPU values
are 8.157/12.162/12.162 ms and 10.443/11.705/11.705 ms (five completed timestamp
samples per side). Background activity and clocks were uncontrolled. These
small, non-counterbalanced samples do not establish a performance improvement
or acceptance; all raw per-frame samples and hardware details are retained.

[Main matrix](world-props/evidence/native-player/summary.json),
[per-case metrics](world-props/evidence/native-player/captures.json),
[pixel pairs](world-props/evidence/native-player/pixel-pairs.json),
[1,000-prop traversal](world-props/evidence/native-player/supplement/traverse-thousand-summary.json)
and [gallery sequence](world-props/evidence/native-player/supplement/gallery-sequence-summary.json)
retain original report/image hashes. Ten representative stills and two final
pose images are copied byte-for-byte; all original native files remain under
artifacts/pv, artifacts/pt and artifacts/pseq in the local checkout.

[Gallery before](world-props/evidence/native-player/images/gallery-before-WorldPropsGallery-1920x1080-High-CloseTactical.png) /
[gallery after](world-props/evidence/native-player/images/gallery-after-WorldPropsGallery-1920x1080-High-CloseTactical.png).
The baked correction visibly restores the straight pipe's circular cross-section
without moving any placement. Yard and bridge review preserves existing
placements; exact uneven-terrain contact and moving LOD appearance remain
human review items. Four actual Blender inspection PNGs are retained separately
in world-props/evidence/blender-inspection; they are not player screenshots.

### Headless and installed package

The seed-2026, 64-tick baseline/candidate match compares all 24 non-observational
match fields exactly; eight time/allocation/GC fields are explicitly excluded.
The terminal validation match ends at tick 55,210 with player 1 victorious,
zero route failures and zero failed cargo orders.
[Parity](world-props/evidence/gameplay/parity.json) and
[terminal match](world-props/evidence/gameplay/terminal-match.json) retain results.

The canonical Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible
command passes with the qualified candidate runtime. Two independently
published trees are hash-identical; the ZIP is produced once.
FORGELINE-0.1.437-dev-win-x64.zip is 55,144,155 bytes, SHA-256
c97a47e84f43f768068309a809d8067ec9d659937af2b634e0f1e2eedf471fdb.
Its provenance records the clean product source above and SDK 10.0.401.
The local archive is under artifacts/pp; no release or tag is created.

A fresh extraction contains 1,488 files, including its manifest. All 1,487
manifest entries hash correctly; no .blend/.glb/.gltf/Python/PowerShell/C# source
or assets/source tree is included. Installed source-less validation resolves
all 1,363 records, including all 44 scoped visual IDs. Runtime bytes match
the qualified candidate exactly. Two measured native menu starts after one
warmup pass readiness and both application-icon checks using isolated settings.

Installed native gallery and 1,000-prop views pass with zero mesh fallbacks and
zero binding failures: 22/715 visible props in 22/25 draws. The separate mixed
match-client stress smoke completes with zero binding errors and 114 fallback
instances. Its unchanged generic stress producer creates VisualIdentity(1)
entities; that broad smoke is recorded separately from the scoped prop result.
The package retains normal framework-dependent .NET runtime requirements.
[Package and install](world-props/evidence/package/archive.json),
[startup](world-props/evidence/package/startup.json) and
[installed prop report](world-props/evidence/package/installed-prop-manifest.json)
retain exact evidence.

### Commit and integration boundaries

- 75ef57ae35e1d665affc56c61aeabbf8d26583ae: preserved-family contract and isolated authoring qualification.
- 22f5bde1a6ce4544462eb7ca963747ea5524b9c5: explicit baked straight-pipe axis qualification.
- 744821b0559355888ac512f6cda2b598c878e3e4: bounded compiler workers and authentic fixture scales.
- 09036b0261798a90e954adf840b74757384e2365: compact Windows-safe GLB source paths.
- 3545a23125d5ca8b3c24d50a8e77d72175c5acf6: retained native masters and qualified canonical adoption.
- Later commits retain documentation and immutable evidence only.

Functional branch review is supported by these results. Production promotion
remains blocked on the existing controlled physical render qualification,
complete live memory/timestamp/lifecycle measurements and explicit human art
and moving-LOD approval. No renderer contract, gameplay placement, threshold,
texture cap or golden has been changed to admit the props.

## Current integration synchronization

Master advanced during final CI through terrain PR #284 and vegetation PR #281.
The prop branch is rebased onto a2a88f06f24b6a4351b7f1f3c7a56819f0527d5d. The
shared scene enum retains every current-master value, including all vegetation
fixtures, and appends the four prop fixtures. Both scene dispatches are retained.
Version 0.1.438 restores exactly one PATCH over the new 0.1.437 base.

The existing native authoring records remain bound to their actual recorded
inputs. Current [component equivalence](world-props/current-evidence/component-equivalence.json)
confirms byte-identical rest/inspection masters, 44 GLBs, canonical prop inputs
and static authoring modules. The retained verifier passes on the rebased
source. No family is reauthored to synchronize unrelated terrain or vegetation.

Current product source is 828984660fb60b32df6609c7b686828dfa0335d2, version 0.1.438.
Its Release build passes with zero warnings/errors. The full local solution
passes 3,305 tests, fails zero and skips 47 of 3,352 total in 22m 30s. All 23
scoped native-artifact cases are enabled. Eight merged prop/vegetation scene
cases, three prop portable cases and 122 shared portable Blender tests pass.
All six enabled product-source CI jobs pass; three optional native jobs are
skipped. [Current validation](world-props/current-evidence/validation.json) and
[CI](world-props/current-evidence/product-ci.json) retain exact results.

The current master main CI itself stopped at the inherited version gate:
expected 0.1.438, received 0.1.437. Downstream master checks were skipped. This
is recorded as an inherited failure, not a functional pass. The branch's
0.1.438 passes the one-PATCH rule against that exact 0.1.437 base.
[Inherited master CI](world-props/current-evidence/inherited-master-ci.json).

Both current-base catalogs qualify all 1,363 records without sources. Exactly
44 prop containers differ; every unrelated runtime hash and all protected
metadata remain identical. Container sizes are 74,845,298 / 74,845,169 bytes.
Both contain 59,454,336 texture-container bytes, including the unchanged
1,786,417 splash bytes; gameplay bytes are 57,667,919 below the unchanged
58,720,256-byte cap. The 411-byte texture-header increase from the historical
catalog belongs to the integrated terrain source paths on both sides.
[Current catalog comparison](world-props/current-evidence/runtime/catalog-comparison.json).

All 48 paired fixed native cases were executed again on the current source
and terrain profile. The two 64-frame paired traversals were also repeated.
There are 100 original stills and 16 adjacent-pose images, plus two installed
views. All pairs use identical current binaries/hardware/settings. All repeat
readbacks match; mesh fallbacks, binding failures and submission faults are zero.
Fifteen main pairs are exact; maximum changed-pixel fraction remains 0.0024488812.
The 1,000-prop High/Normal case retains 715 visible instances in 25 draws;
Low/Strategic retains 999 in 22. Both 64-frame instance/LOD/draw sequences match
exactly across their pair. All raw CPU/GPU samples and resource observations
are retained, with the same physical-performance and human-review boundaries.
[Current matrix](world-props/current-evidence/native-player/summary.json),
[metrics](world-props/current-evidence/native-player/captures.json),
[traversal](world-props/current-evidence/native-player/supplement/traverse-thousand-summary.json)
and [sequence](world-props/current-evidence/native-player/supplement/gallery-sequence-summary.json).

The current seed-2026, 64-tick run still matches all 24 non-observational match
fields of the actual historical baseline. Authority-domain source is
byte-identical across those inputs, as recorded separately. The current CI's
two-opponent full-match check also passes.
[Current parity](world-props/current-evidence/gameplay/parity.json).

The new canonical reproducible package is
FORGELINE-0.1.438-dev-win-x64.zip, 55,145,878 bytes, SHA-256
a5f6c41bc1417c8f5f354b7f463761511bcb94203f0a7773aab68d3a02fd2b88.
Two publish trees match. Fresh extraction verifies all 1,487 manifest entries
(1,488 files including the manifest), excludes authoring sources and validates
all 1,363 records/44 scoped IDs with an absent source root. Two measured menu
starts after one warmup pass readiness and native icons. Installed gallery
and 1,000-prop views pass with zero fallbacks/binding failures.
[Current package](world-props/current-evidence/package/archive.json) and
[startup](world-props/current-evidence/package/startup.json).

The source stays a draft review candidate. Controlled physical timing, complete
live memory/timestamp/lifecycle measurements and explicit human art/moving-LOD
acceptance remain open. No merge, tag, release, budget relaxation or golden
approval is implied by these functional checks.
