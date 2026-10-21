# Logistics Blender qualification

Three saved Blender masters now supply all 33 canonical Storage Depot, Supply
Depot and Logistics Hub LOD/state meshes. This preserves the already integrated
artwork; it does not establish new high-fidelity art approval. Docking, queue,
inventory, throughput, navigation and collision authority are unchanged.

[Production/export contract](LogisticsBlenderProduction.md) ·
[Exact master/export bindings](qualification/logistics-blender/SourceBindings.json) ·
[Machine-readable results](qualification/logistics-blender/Qualification.json) ·
[Lossless native raw samples](qualification/logistics-blender/NativeSamples.json.gz).

## Original qualification source and commits

Fetched integration/default branch `master`:
`b32bf04671fb579e01a7e282eac76ff91c5b2f1d`, version **0.1.428**.
That source was used for the comprehensive measurements below. The branch
initially advanced PATCH once to **0.1.429**. Concurrent integrations required
the current-base reconciliation recorded at the end; no tag or release is created.

| Boundary | Commit |
| --- | --- |
| Saved-source contracts and fixtures | `7d74cfed` |
| Three genuine modular masters | `8c22f6ec` |
| Instance workloads and real dock/FIFO regression | `1219936c`, `d2f83467` |
| Family-scoped native mutation targeting | `5411cf4a` |
| Exact UV preservation and source mutation guards | `fb0fd5cf` |
| Isolated single-building views | `880e0d15` |
| 33 canonical saved GLB exports and source bindings | `eda79f7a005d8970147cf289dab255f17622065b` |

The first 77 baseline cases use client `5411cf4a` with the sealed current-master
asset catalog. Their after cases use `eda79f7a`. The only subsequent production
code change adds optional single-building diagnostics; existing scene fingerprints,
camera/settings and pixels match exactly. Workload and inspection pairs use the
same `eda79f7a` client with each sealed catalog. Installed evidence also binds
`eda79f7a`, `0.1.429-dev`, and a clean source tree. Later evidence/documentation
does not change executable or asset content.

The starting master has a failed `build-test` check; successful pre-merge feature
checks are not substituted for it. On the actual integrated implementation head
`eda79f7a`, hosted `build-test`, `frame-contracts`, `cpu-scale`,
`navigation-movement` and `contracts` all pass. Optional `saved-masters` and
`native-reference` are skipped; real local Blender/D3D12 results below are separate.
Final evidence-head CI is verified separately in the PR.

## Executed source and functional checks

| Check | Observed result |
| --- | --- |
| Actual pinned Blender 5.2.2 x64 | Three saved/reopened masters; three saved module-edit positives |
| Native invalid edits | 36 rejected, including a real UV edit inside the general round-trip tolerance |
| Native source edits | Six rejected: changed ordered material bindings and changed material values |
| Portable Blender contracts | 103 passed, zero failed |
| Independent post-adoption export | All 33 GLBs byte-identical to canonical saved exports |
| Complete Release solution | Zero warnings/errors; 1m25.08s |
| Complete serial C# suite | **3,240 total: 3,214 passed, 26 optional skipped, zero failed**; 26m26.799s |
| Project graph / SemVer | 44 projects, no cycles; exactly one PATCH |
| Scoped source-less catalogs | Three official 16-asset catalogs compile and validate with an absent source root |
| Full source-less catalog | 1,363 assets; official integrity and typed asset validation pass |
| Physical queue regression | Every fleet size 2–10 × four rotations × three real family docks: 108 combinations |
| Natural headless before/after | Seed 2026, Victory / CommandCoreDestroyed, winner 1 at tick 55,210 |

Headless lifecycle, entity counts, pending commands, cargo deliveries/failures,
route results, distribution, Fuel/Ammunition transfer, artillery and participant
results match exactly. No simulation result is granted by artwork.

All source position, normal, UV and available tangent values, indices and semantic
metadata are preserved exactly. The compiler generates runtime tangents normally.
Of 1,363 compiled payloads, 1,362 are byte-identical. Storage Depot LOD0 has 64
different sign bits on zero-valued float components, with **zero numerical
difference**; indices, sections and materials remain identical. All 101 pixel
comparisons also pass the unchanged zero tolerance. No meaningful source edit is
silently snapped: only exact unchanged float32 UV preview encoding is recovered.

The compressed masters reopen as genuine Blender data (`BLENDER17-01` after
decompression). Their packed source references are relative; decompressed audits
find zero local workspace path markers. No geometry regeneration replaces a saved
master during normal export. Shared atlas/material sources and historical kits,
component glTFs and generator remain intact.

## Geometry and unchanged resource limits

| Family | LOD0 / LOD1 / LOD2 triangles | Authoritative instance scale |
| --- | --- | --- |
| Storage Depot | 3,436 / 348 / 176 | 14 × 8 × 14 m |
| Supply Depot | 1,860 / 484 / 300 | 16 × 8 × 16 m |
| Logistics Hub | 5,856 / 432 / 276 | 18 × 9 × 18 m |

Every mesh retains one indexed material section. No extra per-truck material,
texture or descriptor binding is introduced. Current bounds, sockets, metre-scaled
vehicle deck heights, LOD distances/hysteresis and collision references remain.

The full catalog is **74,844,581 bytes**, versus 74,844,350 before. The **231-byte**
increase is longer source-path metadata across 33 containers; geometry payload
lengths and GPU geometry allocation sizes do not increase. Mesh containers total
11,307,598 bytes. Texture containers remain 59,453,925 and materials 163,881 bytes.
The exact gameplay texture gate excludes only the 1,786,417-byte studio splash:
**57,667,508 / 58,720,256 bytes (56 MiB cap)**. No threshold or budget is raised.

Across all source capture cases, both catalogs reach the same maximum observed
texture residency **44,812,076 bytes** and **93 / 4,096** descriptors. The separate
64-MiB resident-texture and 96-MiB shared working-set limits remain unchanged.
DXGI usage and registered resource counters do not establish exact physical
Default-heap ownership or a complete lifecycle memory proof.

## Native hardware evidence

Device: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, Direct3D 12
feature level 12_1, 25,309,478,912 dedicated bytes, driver 32.0.15.9653;
hardware adapter, debug layer disabled. Windows build 26300, x64, 24 logical
processors. All requested native backbuffer dimensions are verified.

| Paired cohort | Pairs |
| --- | ---: |
| Three families × Low/Medium/High × three zooms × 1080p/4K | 54 |
| Real Loading/Transit/Unloading/Sustained × 1080p/4K | 8 |
| Dark, daylight .1 / cloud .6, High normal, 1080p/4K | 6 |
| Wetness 1 / cloud .75, High normal, 1080p/4K | 6 |
| Existing moving camera Sweep, High normal, 1080p | 3 |
| 1/100/1,000 instances per family, Low/High strategic, 1080p | 18 |
| Single buildings, High, 35 m inspection, 1080p/4K | 6 |

**202 source captures / 101 pairs: every pair is RGBA- and PNG-byte-identical.**
Every case repeats exactly within its own two readbacks, after eight warmups and
32 measured CPU samples. GPU records retain their actual available sample count.
Zero fallback meshes, texture/material binding failures and submission faults.

At 1,000 authored instances, Storage/Supply show 964 visible and 36 culled roots;
Hub shows 972 visible and 28 culled. These are not claimed as 1,000 simultaneous
visible buildings. Low uses LOD2 for 964 Storage, 859 Supply and 622 Hub instances.
The visible building meshes submit in one or two indexed draws per family case.
The four authored headings are present, while real fleet/dock rotations are
separately exercised in the 108 physical simulation combinations.

The camera Sweep translates by 20/10 m and varies yaw, returning to its fixed pose
before still readback. It does not vary distance or supply human motion approval.
Full moving-LOD popping, all-rotation live traffic display review and long lifecycle
acceptance remain distinct gates. Diagnostic single/repeated snapshots intentionally
have no player-experience HUD; the waiting-data banner does not mean a missing mesh.

Raw per-case CPU/GPU frames, phase timings, allocation observations, resource/DXGI
samples, settings, source commits, image hashes and exact case results are retained
in the linked JSON/gzip. Original local manifests and all original PNGs remain
under `artifacts/logistics/`; no image is altered or golden approved.

The maximum *per-case* CPU p99 is 554.3064 ms before and 521.8542 ms after; GPU p99
maxima are 36.076384 and 35.564608 ms. These are not pooled percentiles or a speedup
claim. Captures share the workstation with other work; thermal stability and a
quiet measurement window are not verified. Current-thread allocations, timed
renderer subphases and short DXGI observations are not complete-client guarantees.

### Selected untouched native views

| Family | Gallery before | Gallery after | 35 m inspection |
| --- | --- | --- | --- |
| Storage Depot | [Before](images/logistics-blender/before-StorageDepot.png) | [After](images/logistics-blender/after-StorageDepot.png) | [Inspection](images/logistics-blender/inspection-StorageDepot.png) |
| Supply Depot | [Before](images/logistics-blender/before-SupplyDepot.png) | [After](images/logistics-blender/after-SupplyDepot.png) | [Inspection](images/logistics-blender/inspection-SupplyDepot.png) |
| Logistics Hub | [Before](images/logistics-blender/before-LogisticsHub.png) | [After](images/logistics-blender/after-LogisticsHub.png) | [Inspection](images/logistics-blender/inspection-LogisticsHub.png) |

[Real loading](images/logistics-blender/LogisticsLoadingProduction.png) ·
[Sustained deliveries](images/logistics-blender/LogisticsSustainedProduction.png) ·
[1,000 Hub instances, Low](images/logistics-blender/LogisticsHub1000Low.png).

## Reproducible installed player

`Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible` succeeds with
the sealed runtime catalog. Archive `FORGELINE-0.1.429-dev-win-x64.zip` is
55,137,668 bytes, SHA256
`22c86c07b0331ed6b56f46347eceeef93935f68ffc59b19451c40ac3985d0f58`.
All **1,487** extracted file hashes verify; all 1,363 runtime assets are present.
No `.blend`, `.gltf`, `.glb`, Python, PowerShell or AssetCompiler file ships.

Fresh extraction is outside the source checkout. The installed child runs with
only Windows system/.NET directories on PATH; Git is resolved explicitly by the
parent capture harness. Seven gallery/real-traffic and three close inspection
cases pass on hardware and exactly match development RGBA and PNG bytes.
The full evidence set contains **212 native cases**, including these ten installed
cases. This is a development package, not a release publication.

## Reproduction, retained failures and decision

Exact native inputs are stored per manifest. The source/export entry points are
in the production contract. The executed final commands include:
Use fresh output directories when reproducing; the paths below identify retained
evidence and should not overwrite it.

```powershell
dotnet build ForgeLine.sln --configuration Release --no-restore -m:1 -nr:false
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
python -m unittest discover -s tools/blender/tests -p 'test_*.py'
./build/Validate-ProjectReferences.ps1
./build/Validate-SemVer.ps1 -BaseRef origin/master
./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -OutputRoot artifacts/lg/pkg -RuntimeAssetRoot artifacts/lg/r
python tools/blender/logistics_qualification.py --output-root artifacts/lg/review
```

Earlier isolated attempts remain in `artifacts/lg/`: the initial output preflight,
the inappropriate planar-normal assumption on existing smooth cylinders, two UV
mutation harnesses that selected the collision mesh, and a build overlapping its
own compiler. Each was diagnosed; corrected native cohorts and a clean complete
build pass. The initial strict payload-byte assertion exposed the 64 zero sign
bits recorded above; they are not relabelled as byte-identical payloads. No geometry
limit, normal/UV tolerance, timeout, pixel threshold or golden was loosened.

Implemented and technically reviewable in the draft PR. Controlled physical
performance/thermal/full-memory/lifecycle acceptance and explicit human art and
live traffic/motion approval remain open. Integration of renderer optimization
code or its tooling alone does not close those gates. Do not promote, merge, tag,
release or approve new goldens from these source and static image results.


## Current-base reconciliation: 0.1.432

Concurrent PRs #274, #275 and operational Blender work advanced `master` to
`a6fe118583be842e00416bcb100d1210f3240a00` / **0.1.431**. Merge commit
`382a99cc9996f2137747a2500fa33f878f81aca8` preserves all master scene enum
values, appends logistics diagnostics and reconciles exactly one PATCH to
**0.1.432**. Historical commits and measurements above are retained unchanged.
The three saved masters and all 33 exports still match their recorded hashes.

[Current-base machine results](qualification/logistics-blender/CurrentBaseQualification.json)
and [eight raw native cases](qualification/logistics-blender/CurrentBaseNativeSamples.json.gz)
record the following fresh checks on that merge commit:

- Complete Release build: zero warnings/errors; 44-project graph and exact-PATCH checks pass.
- 110 portable Blender contracts, 81 visual scene tests and 20 physical queue tests pass.
  The latter still exercise 108 actual logistics dock/fleet/orientation combinations.
- Official development compile and source-less validation: 1,363 assets,
  74,844,665 bytes. Only the twelve incoming Cargo/Supply Truck runtime payloads
  differ from the earlier sealed catalog. All logistics building payloads remain
  identical; texture/material sizes and the 56 MiB gameplay texture cap are unchanged.
- Three High/Normal/1080p family galleries and real loading production on the
  native hardware adapter exactly match the original RGBA and PNG bytes, with
  repeat-identical readbacks and zero fallback/binding/submission faults.
- Reproducible player ZIP: **55,139,506 bytes**, SHA256
  `065c0c309f3b45849e6a68e585dbf6d3057bb402269e44c79f72e03e6bae9873`.
  All 1,487 manifest hashes verify in a fresh sibling installation outside the
  checkout. No authoring/compiler files ship. With system/.NET-only child PATH,
  the same four installed scenes exactly match development and original pixels.

These eight new captures supplement the original 212; they do not attribute all
historical measurements or the original full-suite result to the merge commit.
CI is re-run after synchronization; its current check state is recorded in the
Draft PR. Human art, live motion and controlled physical performance gates remain open.

Commands use fresh retained paths:

```powershell
dotnet build ForgeLine.sln --configuration Release --no-restore -m:1 -nr:false
python -m unittest discover -s tools/blender/tests -p 'test_*.py'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.VisualReferenceSceneTests
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.VehicleQueuePhysicalTests
dotnet tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll --mode development --source assets/source --runtime artifacts/lg/r432 --qualification-output artifacts/lg/qualification432.json
dotnet tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll --mode validate --source artifacts/lg/no-source-432 --runtime artifacts/lg/r432 --qualification-output artifacts/lg/source-less432.json
./build/Invoke-RtsReferenceQualification.ps1 -Capture -OutputRoot artifacts/logistics/current432 -RuntimeAssetRoot artifacts/lg/r432 -Scenes @('StorageDepotProduction','SupplyDepotProduction','LogisticsHubProduction','LogisticsLoadingProduction') -Profiles @('High') -Zooms @('NormalGameplay') -Displays @('1920x1080') -Warmups 8 -Samples 32 -RepeatCaptures 2
./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -OutputRoot artifacts/lg/pkg432 -RuntimeAssetRoot artifacts/lg/r432
```
