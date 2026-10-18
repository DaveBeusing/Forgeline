# Rigid clip runtime qualification

## Scope and source identity

The bounded optional clip runtime is implemented. Release compilation and 180 focused tests pass; final performance/human visual acceptance remains open. The production catalog retains mechanical motion and contains no animation references. Clip opt-in is exercised in isolated source/runtime clones.

Base: `9e679451d5fd04c29ee5f95df3a4b603f5cf73df`; product version `0.1.398`, compiler `1.11.0`. Native six-phase and paired cost evidence uses clean source/binary `e13ab4d29ac7e03403f2faf8085b7932ede76df0`. Final source `b7526cf259709de4b4fe76e1647d19ebe797d71a` additionally rejects null animation-reference metadata and passes the complete focused suite. The final installed package reproduces the earlier phase-1 image exactly; that guard does not change valid clip rendering.

Environment: Windows x64, SDK 10.0.401, pinned Blender 5.2.2 (build d13f752e3b9c), NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653. Evidence is fixed native frames, not a claim of human visual approval.

## Functional and compatibility results

| Check | Result |
|---|---|
| Release solution | Pass, zero warnings/errors |
| Asset compiler/payload/legacy mesh tests | 60 passed, zero skipped |
| Playback/resource/lifecycle/reference tests | 117 passed, zero skipped |
| Capture request/output tests | 3 passed, zero skipped |
| Original source mesh reimport | 309 rebuilt, 1028 unchanged; complete 1337-asset qualification passed |
| Production disk footprint | 71,304,146 bytes; existing texture/material/LOD/socket identities retained |
| Dependency/process/integrity contracts | Canonical runtime pipeline process gate passed |
| Architecture/headless | 45 projects, no cycles/boundary violations; 64 ticks, seed 12345, 1000 entities passed |
| Actual Blender exports | Committed clip plus two independent exports produce equal compiled payload hashes; all three validate without source trees |
| Repeated instance tests | 100 and 1000 visible instances use two instanced part draws; shared clip identity and buffer disposal asserted |
| Frame evaluation | 1000 compositions allocate zero managed bytes; seven Blender-evaluated poses match compiled samples |

The [Blender payload result](review/rigid-clips/blender-payload.json) records hash `E22F45F9839C0BC8424E46C02C54CCDF8B50E8C547970F73F1125CDCFBA3FDB0`. Parser negatives cover missing targets/ranges/pivots, duplicate channels, hierarchy cycles/mismatch, unsupported cubic/scale/skin/rest data, accessor/count bombs, invalid times/duration/quaternions/translations, incompatible code motion, swept bounds, null references and corrupt/truncated/versioned payloads. Incremental external-buffer edits rebuild the clip and dependent mesh while preserving the unchanged material.

## Native frames and resource cost

Six phases (0, .5, 1, 1.5, 2 and 3 seconds), 1920x1080 High, inspection distance 20m, eight warmups/eight samples, two pixel-identical captures each: all structural passes, no missing meshes or material/texture binding failures. Intermediate hashes differ; loop endpoints 0/3 are identical. One clip is shared: 232 payload bytes, 931 container bytes. [Phase hashes](review/rigid-clips/phases.json).

The review catalog replaces only the tank hull LOD0 with the synthetic frame/arm fixture. Existing turret and damage attachments remain visible; these images qualify the pipeline and are not proposed tank artwork. The production sources are unchanged.

[Phase 0 native image](review/rigid-clips/phase-0/MainBattleTankProduction-1920x1080-High-CloseTactical-MechanicalT0-Inspection20.png), [phase 1 native image](review/rigid-clips/phase-1/MainBattleTankProduction-1920x1080-High-CloseTactical-MechanicalT1-Inspection20.png). Original capture manifests accompany each image.

A predetermined ABBA sequence compares the same isolated fixture mesh with/without clip binding: neutral A, clip A, clip B, neutral B. Each run uses the 1000-tank formation, two zooms, 32 warmups/32 samples and two repeated captures. No limits or timeouts changed, no conditional reruns to obtain a pass.

| Cohort/view | CPU mean neutral → clip (ms) | GPU mean neutral → clip (ms) | Existing 15% gate |
|---|---:|---:|---|
| A close | 25.95 → 23.89 | 27.10 → 25.41 | Pass |
| B close | 24.62 → 24.29 | 26.27 → 25.81 | Pass |
| A normal | 24.83 → 34.42 | 25.90 → 31.90 | Fails mean/p50 |
| B normal | 22.81 → 24.48 | 23.88 → 25.80 | Fails p95/p99 |

Close view has 331 visible LOD0 entities and four object draws in both catalogs. Normal view has 1068 visible objects and 23 draws; reduced tank LODs mean **zero clips loaded in both catalogs**, and their normal-view images are identical in both cohorts. All runs retain 47,912,856 peak texture bytes and 89 descriptors (unchanged 4096 capacity; existing qualification budgets unchanged). No clip GPU buffers, textures or descriptors are introduced. The inactive-path timing failures remain qualification failures; the data does not establish a clip-evaluation regression or general frame-time acceptance. [Cohort A](review/rigid-clips/performance-a.json), [cohort B](review/rigid-clips/performance-b.json), [images/resources](review/rigid-clips/resource-comparison.json).

## Source-less deployment

Final review archive: `FORGELINE-0.1.398-dev-win-x64.zip`, 54,549,065 bytes, SHA256 `855f315a69326ae30d5b2436d83607b93fa6256ecd93377b1e197d211037228f`. The canonical publisher's two builds compare reproducibly, UI/audio package contracts pass, and all 1462 installed manifest checksums verify. This is an isolated clip-review catalog (1338 assets), not a change to the production tank sources or a release/tag.

The extracted player starts from an empty working directory, with no runtime-root environment override and an invalid Blender executable path. The 1000-instance stress session exits successfully. An installed 1920x1080 High phase-1 reference then matches the development phase-1 image exactly, with one loaded clip, 232 clip bytes, zero missing meshes/binding failures and stable repeat pixels. No `.blend`, `.glb`, `.gltf`, Python authoring script or compiler executable is packaged. [Installed package evidence](review/rigid-clips/installed.json).

Full local raw logs, all six frames, paired cohorts and the archive remain below `artifacts/clip-*`. Reproduction commands and supported limits: [rigid clip assets](RigidClipAssets.md).

## Open acceptance and CI

The PR remains a draft because normal-view timing qualification and human visual acceptance are open. Do not label the aggregate performance gate green. Existing master already fails its software WARP `BattlefieldCombat-640x360-Low-NormalGameplay` capture on the unchanged ten-second GPU fence deadline: [exact master run](https://github.com/DaveBeusing/Forgeline/actions/runs/39070803086), source `9e679451d5fd04c29ee5f95df3a4b603f5cf73df`. This is independently evidenced rather than inferred from an older branch. Limits, software handling and deadlines were not relaxed.

[Current PR checks](https://github.com/DaveBeusing/Forgeline/pull/232/checks) are authoritative for the final head. Local focused tests and hardware captures do not assert a fully green hosted/software run; the manual Blender workflow remains an explicit dispatch on the pinned runner.
