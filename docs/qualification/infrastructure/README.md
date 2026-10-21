# Infrastructure qualification

Technical evidence for the road, bridge and crossing sources and presentation
integration. Human art approval, live motion, sustained/enclosing-frame
performance, thermal and renderer lifecycle acceptance remain open. The branch
is intended for a draft PR; no merge, release, tag or new golden is approved.

Current integration supersedes the historical source inventory below: master
`34c69dd97db417d1982fd7bf75cb1baa197082f6` includes world-prop PR #283
and version 0.1.438. This branch is now 0.1.439. The six shared bridge-apron,
road-edge and loading-apron High/Reduced definitions and their genuine masters
are reused unchanged from that integration. Redundant transition master/GLBs
from this branch were removed. Active infrastructure authoring covers two
unchanged qualified masters and twelve GLBs. Shared source positions, normals,
tangents, indices, bounds and materials match the historical seed exactly; UV
rounding is at most 2.981e-8 within the existing static contract. See
[SharedTransitions.json](SharedTransitions.json). Existing prop and vegetation
scene IDs are preserved before the appended infrastructure range. Historical
145-pair measurements and the three-master qualification below retain their
original measured identities and are not claims about the later prop base.

## Source boundaries

The ordered audit observed master `a524b0a8164390abe1e9cb38170e690f46147574`
(0.1.436), no related open PR/live branch, and reused infrastructure PRs
#49/#111/#181/#185. Render PRs #258/#260/#263-#268 are integrated, with physical
acceptance still open. Branch: `art/road-bridge-blender`.

Matched native measurements use terrain master
`8e486734383075c39a914b71adf34d8c3311b7be` (PR #284, 0.1.437):

| Boundary | Recorded revision |
|---|---|
| Before: shared real fixtures, previous source/layout | `fd71e5169fcc0fa24f6e5e6ec6103f70f850b0be` |
| After: curves, clearance and operational overlays | `214240bfdc890274eb3d562e8385a180015cf654` |
| Final integration base: vegetation PR #281 | `a2a88f06f24b6a4351b7f1f3c7a56819f0527d5d` |

Rebase preserved 45 of 46 measured production files exactly (text hashes
normalize checkout line endings). Only VisualReferenceScene.cs changed to keep
existing vegetation enum IDs/routing and append fifteen contiguous infrastructure
scenes. All three masters and eighteen GLBs remain byte-identical. Captured
revisions above remain the measured identities despite commit replay. Paired
timing evidence is not relabelled as vegetation-base timing. Version 0.1.438
is exactly one PATCH above the final base's 0.1.437.

## Native Blender qualification

Blender 5.2.2 x64 build `d13f752e3b9c`, executable SHA-256
`f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0`.
[SourceIdentity.json](SourceIdentity.json) binds three genuine packed-PBR masters
and eighteen actual exports. [BlenderQualification.json](BlenderQualification.json)
records saved-master reopening twice, source/compiled byte equality, source-less
validation, 24 invalid edit rejections and three positive saved edits affecting
only the intended runtime root. This is local real Blender execution. The opt-in
self-hosted workflow has not been dispatched.

[CurveRefinement.json](CurveRefinement.json) records the demonstrated disconnected
curve gap and bounded repair. Sixteen source meshes and the shared collision
retain original channels. Two closed ribbons retain exact bounds, pivots, atlas
rectangles and materials without increasing triangles/vertices. Compiled geometry
saves 1,200 bytes; container provenance differs from original glTF imports, so
all historical runtime containers are not claimed byte-identical. No texture,
material, sampler, LOD, collision or resource budget is expanded.

## Native images and sampled performance

[Comparison.json](Comparison.json): 145 paired cases, all repeat-stable, zero mesh
fallback/material binding failures; 33 unchanged-family pairs pixel-identical.
[before-samples.json.gz](before-samples.json.gz) and
[after-samples.json.gz](after-samples.json.gz) retain native measurements,
normalizing only checkout prefixes in derived copies. Original manifest hashes
and captured source identities remain recorded. GPU sample counts may be lower
than CPU counts and remain explicit per case.

NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, D3D12 FL12_1,
25,309,478,912 dedicated VRAM bytes, driver 32.0.15.9653; Windows 10.0.26300,
24 logical processors, .NET 10.0.12. Hardware rendering; debug layer unavailable.
Both sides use identical hardware/settings, eight warmups, 32 samples, two image
captures. Coverage: 1080p/4K, Low/Medium/High, 120/420/1000m, bright/dark/wet,
four junctions, real truck approach/deck/exit traces, 1/100/1000 instances,
30m detail, camera sweeps and ultrawide. Existing native validation rejects
640x360; that rejected request is excluded without weakening the contract.

Selected High/1080p/420m timings in milliseconds:

| Scene | CPU p50/p95/p99 before to after | GPU p50/p95/p99 before to after | Draw calls |
|---|---|---|---|
| Bridge deck | 13.55/21.54/23.68 to 12.61/27.59/32.72 | 11.37/13.67/14.36 to 11.75/14.78/14.87 | 11 to 10 |
| Ford deck | 13.91/25.29/27.53 to 13.41/23.87/26.36 | 9.48/13.30/13.68 to 12.40/13.09/15.54 | 8 to 8 |
| Road junctions | 8.18/23.28/27.52 to 11.53/24.26/26.31 | 6.98/10.65/10.67 to 8.50/11.57/11.74 | 7 to 7 |
| 1,000 instances | 14.10/17.93/24.23 to 16.66/20.30/20.61 | 12.31/16.71/17.47 to 15.04/16.65/16.68 | 3 to 3 |

These short sequential samples include Present/frame reuse and show variability
and some regressions. They are not sustained FPS or approved performance.
Approaches add 234 static display instances, while source mesh residency falls
1,200 bytes. Complete resource/submission data and quantiles remain available.
Thermal stability, long-session resource churn and enclosing-frame cost need
separate acceptance.

Original unmodified captures with hashes: [ImageIndex.json](ImageIndex.json).

| View | Before | After |
|---|---|---|
| Road gallery | [image](before/roads.png) | [image](after/roads.png) |
| North Bridge | [image](before/bridge.png) | [image](after/bridge.png) |
| South Ford | [image](before/ford.png) | [image](after/ford.png) |
| Long curve | [image](before/curve-long.png) | [image](after/curve-long.png) |
| Strategic 4K | [image](before/strategic-4k.png) | [image](after/strategic-4k.png) |

Captures sample real simulated movement with unchanged authoritative traces;
they do not certify continuous motion. Map transforms, graph, 12m navigation
width, collision and traversability remain unchanged. Operational bridge state
controls covered ground wear and drawn truck floor alignment.

## Integrated checks

Vegetation-base Release restore/build: zero warnings/errors, 1m42.41s. Portable
Blender contracts: 127 passed, 99.227s. Focused infrastructure cohort: 22 passed,
zero skips, including 27 typed bindings, 17 preserved compiled meshes, two
bounded curves, exact before/after traffic traces, unchanged map records,
dense terrain clearance, 12m lanes and millimetre approach/deck joins.
Full catalog: 1,363 assets, 309 meshes, 901 textures, 132 materials,
18 animations, two skins, one UI skin; 74,844,176 bytes total. Gameplay textures:
57,667,919 bytes, below unchanged 56 MiB gate. Runtime process contracts,
44-project reference graph and canonical map compilation pass (reachability 71;
two alternate navigation/two alternate road routes).

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
python -m unittest discover -s tools/blender/tests -p 'test_*contract.py' -v
./build/Build-RuntimeAssets.ps1 -Mode build -NoBuild
./build/Validate-ProjectReferences.ps1
./build/Validate-SemVer.ps1 -BaseRef a2a88f06f24b6a4351b7f1f3c7a56819f0527d5d
./build/Test-RuntimeAssetPipeline.ps1
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/map-qualification.json
```

The fixed-seed terminal comparison is retained in
[HeadlessComparison.json](HeadlessComparison.json): seed 2026, 80,000-tick cap,
required terminal result at tick 55,210, winner 1, 119 completed cargo orders,
zero failed cargo orders and zero route failures. Every authoritative match field
and the complete gameplay telemetry JSON are exactly equal. Only elapsed-time,
throughput, tick timing, allocation and GC observations are excluded. The
production-file rebase check is retained in
[RebaseSourceEquivalence.json](RebaseSourceEquivalence.json).

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --seed 2026 --ticks 80000 --require-terminal --diagnostics-output artifacts/headless-after.json --telemetry-output artifacts/headless-after-telemetry.json
```

## Retained failed attempts

Authoring initially exceeded the unchanged 120s deadline scanning the full
catalog; explicit audited family closure fixed it. A saved-edit provenance path
changed unrelated container bytes; corrected paths and the full real qualifier
passed. One presentation suite overlapped a clean asset rebuild and saw an absent
manifest; that cohort is invalid and requires a stable-runtime rerun. One build
hit an overlapping compiler DLL lock; later clean full builds passed. Economic
dispatch commandeered the initial traffic fixture; the replacement uses real
isolated navigation/movement and commands. A test filter selected zero tests;
that invocation is not counted.

Earlier repeat-stable previews exposed approach clipping and sideways wear.
Retained failures prompted shared raised nodes, solid support, dense independent
clearance tests, bridge-aligned cracks and operational terrain-wear occlusion.
Final paired evidence supersedes rejected previews. No budgets, authoritative
rules or acceptance gates were relaxed.
