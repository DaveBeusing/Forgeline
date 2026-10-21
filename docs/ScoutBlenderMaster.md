# Scout Blender master

## Current synchronization (0.1.436)

PR 280 is synchronized with master f151362883134eda0fe50f8fb70d9102b3635446 (0.1.435), including resource-deposit PR 279 and infantry PR 277. All 94 master scene enum IDs are preserved. Resource-deposit scenes retain 90–93, infantry scenes retain 84–89, and Scout appends at 94–98. The resource-deposit, infantry and Scout dispatch paths are retained. The branch contains exactly one PATCH increment to 0.1.436.

The merged working tree passes the Release build with zero warnings/errors, 97 Scout/infantry/resource/shared scene cases, 12 capture-request cases, the 44-project reference graph and the retained Scout master/export integrity check. Scout masters, exports and historical evidence remain unchanged. Native Blender, installed-package and image records below retain their original revisions and 0.1.434 package identity; they are not relabelled as new 0.1.436 measurements. Hosted checks must be evaluated at the synchronized PR head. Production promotion and human/render acceptance remain separate open gates.

## Result and integration decision

The retained Scout candidate contains a genuine editable rest master, a separate packed PBR/pose inspection project, and eight deterministic GLB exports for unit.directorate.scout_vehicle. Existing six-wheel art, all three LODs, turret, damaged/critical/wreck geometry and independent collision are preserved. The demonstrated gap was the missing saved native master; PR 186 already supplied the modular art and typed mechanics. No unsupported lighting or gameplay change is introduced.

Canonical source ownership remains held by the rendering residency lane in docs/rendering/parallelization/ParallelRenderOwnership.json. Accordingly, assets remain in assets/staged/scout. Complete sealed candidate catalogs are exercised by real installed players; this is review qualification, not production promotion. Render-product physical performance/memory/lifecycle acceptance and human art approval remain open; see [the product gate table](rendering/qualification/Qualification.md) and [missing integration records](rendering/qualification/IntegrationRequirements.md). Do not merge, tag, release or approve goldens on the strength of these observations.

## Original audit and measured 0.1.434 checkpoint

Before branch creation, remote default master, its SHA/version, related branches, PRs/reviews/CI, existing source contracts and qualification/governance documents were inspected in the mandated order. The initial base was a6fe118583be842e00416bcb100d1210f3240a00 (0.1.431); integrated PRs 186, 268 and 274–276 were reused. No unfinished Scout branch or open Scout PR existed. PR 273 was then a separate logistics owner and subsequently merged. Later Draft PRs 277–279 were inspected for overlap: the returned file lists contain no Scout or shared production-surface changes. PR 278 subsequently merged at 3c732aa5 and is included; 277/279 remain separate. Incoming tank source, fixtures and time-admission changes are preserved from master, not reauthored here.

At that measurement checkpoint, the integration base was 3c732aa519bb54bec17070bcdad378c232ea9de1 (0.1.433). Branch art/scout-blender-master contained exactly one PATCH increment to 0.1.434. All 84 existing scene enum positions, including nine incoming logistics scenes and three incoming tank scenes, are preserved; the five Scout fixtures append at 84–88. Incoming logistics files retain their original owner. The preceding logistics base a391a5bb (0.1.432) completed CI and Movement quality successfully. At the current integration checkpoint, master 3c732aa5 Movement quality passes and its main CI is still in progress at Rendering benchmark qualification. Raw job results remain separate from the original base failure.

The original base CI failed FrontendMenuBackgroundTests.DeferredLoadNeverBlocksFirstMenuAndCancellationCreatesNoGpuResource at the unchanged five-second entered.Wait assertion (3224 passed, 33 skipped, one failed). The cause was not isolated. All five unchanged class tests pass locally. No timeout or unrelated frontend implementation was changed and no inherited failure waives this PR's checks.

## Preserved source and presentation contracts

Engine (x,y,z) maps to Blender (x,-z,y), meter scale, using the established normalized source frames and physical instance scale (3,2.4,5.2). Height offset remains 1.2. Collision, unit pivot/bounds, turn radius, navigation, sight/sensor ranges and combat authority remain unchanged. All eleven root sockets and five turret sockets retain their exact definitions, including sensor_origin and weapon_muzzle.

LOD0/1/2 have 2512/736/284 triangles; turret 188, damaged 24, critical 80, wreck 116 and collision 12 (3952 total). The 57 original component selection groups survive. The body, six wheel partitions (rate 2.380952380952381), sensor partition (rate 0.6), outer turret and supported cannon travel remain typed rigid mechanics. Projected 90/28-pixel LOD selection and 12% hysteresis are unchanged; legacy 120/340 metadata is not substituted for the production selector.

The existing shared production atlas supplies Base Color (sRGB), Normal and ORM (linear). There is no Emissive binding or real sensor-light state in this material. Packed inspection maps have verified source hashes and color spaces; runtime textures and samplers are reused without duplication or budget changes. Geometry and texture provenance remain repository-owned with no third-party inputs. The independent inspection scene contains supported sampled poses for review; it is not an export input or a new runtime animation format.

Five family-only immutable fixtures cover idle/unselected, active/unselected, active/selected, 100 and 1000 Scouts. Repeated inputs distribute the actual four cardinal placements equally and alternate still/active cosmetic inputs. Dimensions/grounding derive from the real unit definition. Selection uses the existing capture indicator and read-only HUD context. The inherited eight-example ScoutVehicleProduction gallery remains unchanged. No sensor decisions, path simulation or new ECS animation is implemented.

## Native authoring and exact export

Blender 5.2.2 x64, build d13f752e3b9c, executable SHA-256 f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0, ran locally through the existing launcher. SDK 10.0.401 and C# 14 were used. The strict master SHA-256 is b70797a0e80b449a757703e925ef1969169485a8811297a4712097ac60dbf506.

Two independently saved native projects and a reopened master produce identical source and compiled runtime bytes. Five scoped catalogs each contain eight meshes, three textures and one material; all compile and qualify with an absent source root. A saved/reopened qualification-only vertex edit changes source Y by 0.0009999871253967276. Eleven actual native negative cases reject unsupported geometry, rates, frames, keyframes and weapon/sensor pivots. The retained checked-in rest master was reopened and all eight exports matched lineage byte-for-byte.

The original bounded native readback introduced source-float storage noise up to 2.9802322387695312e-8. The first installed image matrix exposed it: 69 of 91 pairs were exact, 22 differed, with 179 changed pixels total and maximum channel delta 44. Eight-repeat self captures remained exact and paired counters matched. A separately labelled controlled canonical-float diagnostic removed the strongest 4K/Low/1000 difference. Original failures and diagnostic provenance remain retained.

The family adapter now validates the actual native scene through the existing strict exporter, rejects deviations above 5e-8, retains original readback GLBs/reports, then serializes the unchanged canonical source floats. Final source channel deviations are zero. No shared importer, renderer, material, texture or tolerance was loosened. Intentional art changes still require reviewed source-kit/definition changes and reimport. A 1e-6 geometry change now rejects. Exact compiled RuntimeMeshVertex equality additionally covers generated tangents; the old rounded candidate fails this stronger assertion and the corrected three native exports pass. All 330 independent Blender/runtime pose matrices remain within the existing 2e-5 tolerance, with maximum error 0.0000038146973.

## Installed native observations

The corrected complete matrix on b8f6baba contains **91/91 pixel-exact pairs** (182 captures): 18 gallery, 54 single/100/1000 load, eight active/selected, dark, wet, five 20m inspections and four cardinal camera views. Changed pixels and maximum channel delta are zero. Every eight-repeat set is exact; all paired instance, material, submission and resource counters match; fallback, material/texture binding and submission failures are zero. [Full exact matrix](scout/evidence/exact-native/summary.json).

A separate installed integration subset on 9a2b60d7a046192de7aa663b0f7a83369919215a covers **9/9 pixel-exact pairs** (18 captures): gallery, idle/active/selected 20m singles, 100 at normal zoom, 1000 at 4K Low/close and High/strategic, dark and wet. Its inputs use the actual 0.1.434 installation; counters again match with zero fallback/binding/submission faults. [Current integration](scout/evidence/final-integration/summary.json).

Both corrected sets observe 22469216–44812076 tracked resident texture bytes and 53–93 SRV descriptors. Current integration whole-process DXGI local usage spans 410185728–738996224 bytes; these are scoped observations, not complete residency or lifecycle acceptance.

Current-source 4K/1000 timing observations, milliseconds as p50/p95/p99, canonical → candidate:

| Profile / zoom | Visible / draw calls | CPU ms | Derived GPU ms |
| --- | --- | --- | --- |
| Low / close | 307 / 11 | 35.394/163.107/1506.772 → 44.026/58.656/82.664 | 24.034/28.310/28.650 → 18.888/23.592/24.017 |
| High / strategic | 1000 / 1 | 31.946/73.796/82.403 → 32.949/84.768/85.784 | 32.150/34.600/35.836 → 31.367/35.230/36.534 |

There are 32 CPU samples per case. Available derived GPU counts are 30/31 for close and 28/27 for strategic (before/after). The close CPU p99 includes a 1506.772ms observation. It is retained without filtering; host conditions were uncontrolled and these timings cannot establish a causal speedup or performance pass.

Review PNGs are original unmodified outputs: [current single](scout/evidence/final-integration/images/states-ScoutSingle-1920x1080-High-CloseTactical-Inspection20-after.png), [current selected](scout/evidence/final-integration/images/states-ScoutSelected-1920x1080-High-CloseTactical-Inspection20-after.png), [current 4K/1000 strategic](scout/evidence/final-integration/images/thousand-strategic-ScoutRepeated1000-3840x2160-High-Strategic-after.png). Separate indexes bind every retained review PNG to its original manifest SHA-256.

The device was a physical NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, D3D feature level 12_1, 25309478912 dedicated bytes, software=false, debugLayer=false. Driver 32.0.15.9653 was reported alongside Intel 32.0.101.8724. Mesh/ray capability flags report availability, not use. Window DPI was 144, raster DPI 96, UI scale 1.

Every pair uses the same installed executable, source revision, device, camera, seed/tick, lighting, profile and warmup. Eight warmups, 32 timing samples and eight exact readbacks are requested per case. Original CPU/GPU distributions, instance/material/submission counters, resource residency/descriptors and whole-process DXGI observations remain in losslessly compressed raw manifests, bound by original and compressed SHA-256. Selected review PNGs remain unmodified and checked against their manifest hashes.

These are short sequential snapshots with uncontrolled host load, clocks and thermal state. There are no adjacent live-motion images. No causal speedup, sustained FPS, moving-LOD/judder, leak-free lifecycle, complete VRAM or live sensor/wheel animation acceptance is claimed. Static sampled matrices establish mechanical contract consistency only. Descriptor capacity remains 4096 and the separate existing qualification ceiling remains 512; neither is increased.

## Build, package and verification

At clean integration source 9a2b60d7a046192de7aa663b0f7a83369919215a, Release builds with zero warnings/errors. All 114 selected presentation/compiled/pose cases and 20 incoming physical-queue cases pass. The checked-in master is reopened again and produces all eight exact exports. The 44-project reference graph and one-PATCH check against freshly fetched master 3c732aa5 pass. The own diff touches no canonical source, compiler, renderer, simulation, navigation or headless implementation.

The current canonical development compilation updates 36 records and verifies 1327 unchanged records, qualifying 1363 assets / 74844665 payload bytes. The fully sealed Scout candidate qualifies the same 1363 identities / 74844790 bytes without sources. The +125 bytes are mesh-container/provenance metadata; exact compiled vertex sequences and bindings are unchanged. Both have 59453925 texture bytes, including 1786417 studio-splash bytes; gameplay content is 57667508 against the unchanged 58720256-byte (56MiB) cap. Compiled content bytes are distinct from physical GPU residency.

The canonical publisher with VerifyReproducible completes successfully for FORGELINE-0.1.434-dev-win-x64.zip, SHA-256 5f1ae0f84441a65de0d3752ba7d3700091b699d3750dbed139f5fd3d820d1750. Final package checksums/provenance and the publish log are retained; temporary reproducibility trees are cleaned by the publisher. The source tree was clean. A fresh external installation verifies all 1487 package checksums, contains zero .blend/.py/.glb/.gltf or AssetCompiler files, and starts with fresh settings from an empty working directory (exit 0, no working-directory files) with child PATH limited to System32 and .NET. This is runtime independence, not a claim that Blender was uninstalled. All 1363 installed assets qualify with an absent source root. The generic startup stress request is labelled separately from the actual Scout fixture workloads.

Earlier checkpoints include 102 portable Blender contracts, compiler process-lock/interruption/corruption contracts, the 44-project reference graph, 22 affected scene cases and 117 shared presentation/rigid-motion regressions. Release builds report zero warnings/errors. A fixed-seed headless run on 20b00ace completed 55210 ticks with Ended/Completed/Victory/CommandCoreDestroyed, winner 1 and zero failed cargo/route failures. No subsequent change touches simulation or headless source; this historical run is not relabelled as a newer revision. Final full solution coverage comes from PR CI, not a claimed local full-suite run.

The initial nullable test-access build failure, native report NameError, protected staging-copy failure, diagnostic destination collision, old-rounded exact-vertex regression and inherited base-CI failure are recorded separately from corrected attempts. The initial staging collision has a recorded outcome but no retained full process log; no log is reconstructed. No failed attempt is counted as passing.

## Source epochs and evidence

| Revision | Observation |
| --- | --- |
| d54e8376b551f5c68a0287e86132dc47c5044d80 | Initial complete native authoring and preview PNGs |
| 20b00ace08089ad44798941a4cfc5668ea2ef06c | First retained native reopen, package, 91-pair matrix and terminal headless run |
| fa503a2b62b15fc26f08a7bbff9b472f117abed1 | Exact native export qualification, eleven negative cases and 330 matrices |
| b8f6baba7b0d97040537edf9544b8231fe02e15c | Checked-in exact master reopen, complete catalog, second installed package and full 91-pair matrix |
| 9a2b60d7a046192de7aa663b0f7a83369919215a | Current-master integration, 0.1.434, final build/catalog/package and installed integration subset |

Evidence under scout/evidence retains distinct authoring, exact-authoring, initial-native, exact-native and final-integration epochs. Reports retain actual absolute execution paths and source hashes. No old capture is relabelled as a current-source capture. Documentation-only commits do not change measured binaries or assets. The PR records final hosted check results; optional provisioned native jobs remain separate from locally observed real-Blender/device runs.

## Reproduction

1. Restore/build ForgeLine.sln in Release. Run build/scout/Qualify-Master.ps1 with a fresh short OutputRoot; optional RenderPreviews produces Blender inspection PNGs. An optional QualifiedRuntimeSeed must name an already qualified scoped catalog and does not bypass mesh compilation or source-less validation. Default provisioned CI cold-builds all scoped catalogs.
2. Run build/scout/retained.py to validate the two projects/eight exports against canonical sources, then build/scout/Test-RetainedMaster.ps1 with a fresh root to reopen the actual checked-in master. --refresh is an explicit qualified replacement that first preserves the old candidate; it does not promote canonical assets.
3. Set FORGELINE_SCOUT_MASTER to the actual native qualification root. Set FORGELINE_SCOUT_RUNTIME_SEED to a complete qualified canonical runtime and run ScoutBlenderMasterTests/ScoutBlenderPoseTests. The complete sealed overlay is assembled through RuntimeAssetIntegrity.SealManifest and qualifies without sources.
4. Run Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot <qualified-overlay>. Extract into a fresh directory outside the checkout, verify manifest.sha256, and use the actual installed client for startup/native captures.
5. build/scout/Capture-Review.ps1 accepts explicit BaselineRuntime, CandidateRuntime, ClientPath and a fresh OutputRoot. It executes all 91 before/after pairs with zero pixel/channel tolerance. ComparePixels records differences; it does not approve images or goldens. Retained analysis and integration protocols describe the separate measured epochs.
6. Validate-ProjectReferences.ps1 and Validate-SemVer.ps1 -BaseRef origin/master enforce architecture and the single PATCH. Full CI, native authoring, installed visuals and human/render acceptance remain distinct gates.
