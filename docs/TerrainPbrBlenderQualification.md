# Terrain Blender material qualification

Seven saved Blender masters now supply the 21 canonical terrain texture inputs
used by eight existing material slots. The integrated source art is preserved;
this is a functional saved-source/edit/bake adoption, not newly accepted art.
Industrial and scorched retain their shared ash set and separate factors/tiling.

[Source/export contract](TerrainPbrBlender.md) ·
[Exact source bindings](qualification/terrain-pbr/SourceBindings.json) ·
[Machine results](qualification/terrain-pbr/Qualification.json) ·
[Original native samples](qualification/terrain-pbr/NativeSamples.json.gz).

## Source and implementation boundaries

Ordered audit fetched default/integration `master` at
`a524b0a8164390abe1e9cb38170e690f46147574`, tracked **0.1.436**. No related
remote terrain/surface branch or open PR remained. Superseded #100 continues in
merged #101; existing #111/#149/#166 terrain work is reused. Related reviews and
inline comment collections were empty. Current-master main CI subsequently
completed successfully; the native scout job was skipped. That is the result of
that master, not a claimed result for this feature branch.

`art/terrain-pbr-master` advances exactly one PATCH to **0.1.437**. No tag,
release, merge or golden approval is created. Optimized terrain/residency and
render qualification are integrated, but the documented physical performance,
thermal/full-memory/lifecycle and human art gates remain open.

| Boundary | Commit |
| --- | --- |
| Saved material authoring and Cycles export contract | `a156bb95` |
| Seven packed native masters | `8afd9418` |
| Repeat/export/native qualification workflow | `ba1422d2` |
| Native socket restoration and float32 endpoints | `faf5a756`, `e0f4b5c9` |
| Contract and swatch inspection scale | `50867db7` |
| Exact swatch UV corner association | `5443bcf1` |
| Canonical 21 baked PNGs, definitions and source index | `89fd4a79ba5abc798adbaf20e90891580b378b44` |

The last row is the measured source-migration boundary. Profile-only correction
`68d3765982936c8d6ae22860509fa616ecc62c8a` is the final production/package
source. Subsequent
evidence/collector documentation is separate. Production code, canonical assets,
version and compiler must remain identical across that later evidence boundary.

## Actual saved sources and bake fidelity

Blender **5.2.2 x64**, build `d13f752e3b9c`, bundled Python **3.13.13**;
executable SHA256
`f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0`.
All seven genuine compressed projects have native `BLENDER17-01` headers,
three original packed 4096-square maps each, relative image/packed-file paths,
and no local workspace path marker. File sizes are 20.8–35.9 MB. Exact master and
original/export hashes are in the source index.

Two independent saved-master opens per family run CPU Cycles EMIT at 4096 square,
one sample/thread, with no lighting or display transform baked. All **49**
exported source files match byte-for-byte: 21 PNGs, 21 definitions and seven
editor-only glTF swatches. Every decoded channel of all **21 maps** is exactly
equal to the retained original, including opaque alpha. Maximum component delta
is **zero**. No normal orientation, UV, PBR factor or mip policy is reinterpreted.

The final native control run rejects **91** actual bpy edits: 13 per family,
including mirrored UV associations, invalid graph/units/filtering, incorrect
normal/ORM color spaces, and out-of-range controls. Valid float32 endpoints pass.
An actual saved/reopened 0.96-red linear tint produces changed Base Color pixels
and exactly unchanged Normal/ORM pixels. The exporter does not regenerate over
that edit or modify any canonical master. Ordinary CI's portable contracts are
not a substitute for these local real-Blender cases.

## Compiled and logical continuity

Both complete catalogs contain **1,363 assets**: 309 meshes, 901 textures,
132 materials, two skins, 18 animations and one UI skin. Official development
compilation and subsequent validation with an absent source root pass.

All **1,363 runtime payloads are byte-identical**. Exactly 29 containers have
metadata changes: 21 textures update source path/source hash/build hash, and eight
terrain materials update only their dependency build hash. The compiler source
hash is SHA256 of definition bytes followed by source PNG bytes, not the PNG hash
alone. All source paths, PNG hashes and combined compiler hashes are verified.

| Complete catalog metric | Before | After |
| --- | ---: | ---: |
| Container bytes | 74,844,887 | 74,845,298 |
| Texture container bytes | 59,453,925 | 59,454,336 |
| Studio splash excluded from gameplay metric | 1,786,417 | 1,786,417 |
| Gameplay texture containers | 57,667,508 | 57,667,919 |
| Unchanged gameplay cap | 58,720,256 | 58,720,256 |

The **411-byte** difference is longer source-path metadata. Encoded mips, texture
dimensions, resident payloads, formats and materials are unchanged. Container
length is not total GPU memory. No cap, timeout, threshold or frozen test is raised.

A separately compiled fresh closure contains exactly **29 assets**: 21 textures
and eight materials, 14,714,035 container bytes. All 29 complete containers match
the corresponding canonical containers byte-for-byte and validate with no source
root. This is the material dependency closure, not a full standalone game catalog.

The source-migration boundary changes no C# renderer/profile, world, navigation,
road, simulation, headless or compiler source. The separate profile correction
changes only authored industrial region data. Eight slots, four active layers, 33x33 controls, physical
tile/UV scales and thirteen terrain bindings/samples remain unchanged.

## Executed local checks

Complete adopted-source Release solution build: **zero warnings/errors**, elapsed
6m 18.78s. The graph validator passes **44 projects**; exact one-PATCH validation
passes. **122** portable Blender tests pass.

| Existing affected C# suite | Total | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: | ---: |
| Assets | 170 | 170 | 0 | 0 |
| Terrain Presentation | 56 | 54 | 2 optional | 0 |
| World | 33 | 33 | 0 | 0 |
| Prototype battlefield | 8 | 8 | 0 | 0 |

The natural before/after seed-2026 validation matches both end in Victory /
CommandCoreDestroyed, winner 1, at tick **55,210**. All **24 semantic fields**
match, including lifecycle, entity/command counts, opponents, participants,
transport/distribution, Fuel/Ammunition and artillery results. Eight declared
timing/allocation/GC fields are excluded; no performance equivalence is inferred.
The final `68d37659` profile source also completes the same natural match with
all 24 semantic fields identical to the original base.

## Profile correction from native evidence

The High/Normal/1080p DaylightTerrain reference shows an isolated industrial
rectangle whose 300x240m fully weighted core overwhelms the small structures.
That is a demonstrated presentation quality gap, rather than permission to
regenerate already integrated texture art. The profile-only change at
`68d3765982936c8d6ae22860509fa616ecc62c8a` replaces the two large rectangles
with 96x68m rotated cores, 58m feathers and low-strength offset fringe patches.
Existing center dominance, four small service yards, road/concrete/scorched
regions, material factors/tiling and blend/palette algorithms are preserved.

Four new tests require industrial center coverage, natural material recovery at
former rectangle corners, normalized finite weights and bounded transition
steps at both working areas. Complete Release build passes with zero warnings
or errors in 1m30.67s. Terrain Presentation passes 58 cases, skips two optional
cases and fails zero (60 total). The three native High/Normal/1080p pilot cases
pass structural/readback/binding checks. Visual inspection confirms a smaller,
softer-edged footprint; human art approval remains open.

The material payload-equivalence result applies to both phases: source textures
and compiled payloads do not change for this profile correction. Its new pixels
are intentional candidate evidence and are not relabelled as a zero-delta
texture migration or accepted golden.

## Native visual and installed package observations

The source-migration matrix has 69 pairs: 54 main cases across Low/Medium/High,
120/420/1000m and exact 1080p/4K; six dark High/Normal cases; six wet High/Normal
cases; three Medium/Normal/1080p Traverse cases. Both sides use the same clean
`89fd4a79` executable and distinct officially compiled base/candidate catalogs.
There are 68 exact RGBA/PNG pairs plus the one original dark-4K difference and
its exact isolated paired retry documented below.

The profile candidate adds the same 69 cases on `68d37659`, compared with the
source-migrated `89fd4a79` references. **46 cases change intentionally, 23 remain
exact.** Maximum changed fraction is 66.8265%, maximum channel delta 121. These
are visible profile candidates, not a passing zero-tolerance comparison or newly
approved goldens. Three earlier profile pilot cases are retained separately.
Every source/candidate case has exact within-run repeated readbacks and zero
fallback meshes, material/texture binding failures or submission faults.

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver
32.0.15.9653, feature level 12_1, 25,309,478,912 dedicated video-memory bytes;
software adapter false, debug layer disabled. Windows 10.0.26300 x64, .NET
10.0.12; SDK 10.0.401. Each native case uses 32 warmups, 128 CPU samples,
distinct completed GPU fences and two exact final readbacks, VSync off. All
scenes retain 144 terrain chunks, thirteen texture bindings and a thirteen-sample
maximum per terrain draw. Landmarks are DaylightTerrain and the real map's
bridge/yard scene targets.

| Native observed maximum | Source before | Source after | Profile candidate |
| --- | ---: | ---: | ---: |
| Resident texture bytes | 45,075,196 | 45,075,196 | 45,075,196 |
| Shader-resource descriptors | 139/4096 | 139/4096 | 139/4096 |
| Resident chunks | 94 | 94 | 94 |
| Per-case CPU p99 ms | 102.2678 | 602.7260 | 470.6595 |
| Per-case GPU p99 ms | 48.060320 | 54.827904 | 48.063968 |

Raw frame/resource samples, device/settings/camera/source identities and hashes
are retained in the compressed report. Windows were shared-workstation functional
captures, not controlled performance runs; elapsed tail differences establish no
regression attribution, speedup or sustained FPS acceptance. Resident texture
counters are not total VRAM. Traverse qualifies bounded admissions in this
protocol; final still readbacks do not certify continuous live LOD/shimmer.

Official two-publish reproducibility succeeds. The clean-source **0.1.437-dev**
ZIP is **55,143,995 bytes**, SHA256
`6677f846ca421c0cabcb8129af500a875cb8d35839bd64847ed34bd76af312bf`.
Fresh extraction outside the source checkout verifies all **1,487 file hashes**
and all 1,363 runtime assets. No Blender, glTF/GLB, Python, PowerShell or AssetCompiler
file ships. With system/.NET-only child PATH, three normal and three wet installed
native scenes exactly match the final profile's RGBA and PNG bytes.

The complete retained set is **218 native cases**: 138 original migration cases,
two isolated retry cases, 69 profile cases, three pilots and six installed cases.
Ten original native PNGs are committed without editing:
[industrial before](images/terrain-pbr/industrial-before.png) /
[after](images/terrain-pbr/industrial-after.png),
[strategic before](images/terrain-pbr/strategic-before.png) /
[after](images/terrain-pbr/strategic-after.png),
[yard before](images/terrain-pbr/yard-before.png) /
[after](images/terrain-pbr/yard-after.png),
[bridge](images/terrain-pbr/bridge.png),
[wet yard](images/terrain-pbr/yard-wet.png),
[dark 4K bridge](images/terrain-pbr/bridge-dark-4k.png),
[tactical industry](images/terrain-pbr/industrial-tactical.png).

## Reproduction and CI

Use fresh output roots; retained evidence must not be overwritten. Executed
commands include:

```powershell
dotnet build ForgeLine.sln --configuration Release --no-restore -m:1 -nr:false
python -m unittest discover -s tools/blender/tests -p 'test_*.py'
dotnet test --project tests/ForgeLine.Assets.Tests/ForgeLine.Assets.Tests.csproj --configuration Release --no-build
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class '*Terrain*'
dotnet test --project tests/ForgeLine.World.Tests/ForgeLine.World.Tests.csproj --configuration Release --no-build
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.PrototypeBattlefieldTests
./build/Validate-ProjectReferences.ps1
./build/Validate-SemVer.ps1 -BaseRef origin/master
./tools/blender/Invoke-TerrainPbr.ps1 -Family dry_dirt -OutputRoot artifacts/tp/export-a/dry_dirt
dotnet tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll --mode development --source assets/source --runtime artifacts/tp/r --qualification-output artifacts/tp/canonical-qualification.json
dotnet tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll --mode validate --source artifacts/tp/absent-source --runtime artifacts/tp/r
./build/Invoke-RtsReferenceQualification.ps1 -Capture -OutputRoot artifacts/terrain-pbr/profile -RuntimeAssetRoot artifacts/tp/r -Scenes @('DaylightTerrain','EnvironmentKitBridge','EnvironmentKitYard') -Profiles @('Low','Medium','High') -Zooms @('CloseTactical','NormalGameplay','Strategic') -Displays @('1920x1080','3840x2160') -Warmups 32 -Samples 128 -RepeatCaptures 2
./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -OutputRoot artifacts/tp/pkg -RuntimeAssetRoot artifacts/tp/r
python tools/blender/terrain_qualification.py --output-root artifacts/tp/review
```

Each of seven masters was independently exported to `export-a/<family>` and
`export-b/<family>`. Native rejection/saved-edit evidence is `native3/`; the fresh
29-asset closure is `scoped/`. Dark cases add `-WorldDaylight .1 -CloudCover .6`,
wet cases add `-Wetness 1 -CloudCover .75`, Traverse adds `-CameraPath Traverse`.
The scoped compiler runs build then validate against an absent source root.
The wrapper `Test-TerrainPbr.ps1` reproduces those independent export/control/
compile steps; the actual executed directories above retain each phase.

Final source CI is recorded in the Draft PR. A successful local build or subset
is not the complete required `build-test` result. Optional provisioned native CI
is separate from actual local Blender/D3D12 evidence and is not claimed passed.


## Retained investigation and acceptance limits

Initial failures remain under `artifacts/tp/`: the saved graph's tuple/list JSON
comparison, a rejection harness retaining a live Blender array instead of a copied
value, an unset/stale shell exit-code check after successful authoring, and a host
Python image-inspection attempt without Pillow. Their corrected executions are
separate retained runs. The bundled analysis runtime supplies Pillow; production
Blender uses bundled bpy/numpy and no external image dependency.

Two read-only metadata hypotheses also failed before native capture: counting
only 21 changed containers omitted eight dependency build hashes, and comparing
the raw PNG hash to the compiler's combined definition-plus-PNG source hash was
incorrect. Inspection of the actual container fields and existing `AssetHashing`
resolved both. No payload difference, source mutation or numerical tolerance was
hidden by those corrections.

Static preservation does not approve the existing artwork or resolve preexisting
diagonal cliff projection, live LOD/shimmer, road/yard blending or physical-monitor
readability. Short source-bound hardware windows are functional observations.
Controlled full-client timing, thermal stability, complete physical memory,
long lifecycle/streaming/device recovery and explicit human art/motion approval
remain required. No sustained FPS or production promotion is accepted.


The first native migration matrix has 68 exact pairs out of 69. Dark
EnvironmentKitBridge at 3840x2160 High/Normal differs at exactly pixel
(3711,1435), maximum channel delta one out of 8,294,400 pixels. Camera, lighting,
profile, geometry and source payloads match. Both within-run readbacks match.
The fresh isolated paired rerun is RGBA- and PNG-byte-identical. The original
zero-tolerance comparison remains failed and retained; its cause is not proven,
no limit is widened, and the rerun does not erase that result. An earlier
intermediate status overstated 69 exact pairs before checking the final extra
cohort; the retained report and this correction distinguish all results.
