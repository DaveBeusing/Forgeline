# Infantry skeletal qualification

Status: implemented and reviewable; performance and human motion/readability acceptance remain open. One paired CPU-tail gate fails. No acceptance, merge, release or normal-power FPS claim is made.

## Source and executed validation

The ordered repository precheck started from freshly fetched master `a381bda9d08685250c570d1c29d4e625f2c3d206`, containing the integrated clip foundation. The separate vehicle-docking branch/PR has no overlapping infantry skin owner. Version 0.1.399 is exactly one PATCH above master 0.1.398. Final native code evidence uses `da261246765fceb5c31de01afaf6d26c0e789b96`.

Blender 5.2.2 reopened both committed four-figure/44-joint masters twice, exported all nine actions per role, matched every compiled skin/clip payload, compiled a real key edit with a changed idle payload and validated all eight resulting catalogs with their source directories unavailable. [Payload identities](review/infantry-skin/blender-payloads.json) retain the result. Evaluated Blender joint positions match runtime within 0.00002 metres. Sampling/deformation allocate zero managed bytes after caller-owned storage is supplied. The initial allocation failure was retained and fixed by removing a hot-path interface enumerator. Opposing weighted rotations also retain finite orthonormal vertex bases.

The sealed production catalog has 1357 assets: two skins (3,630,411 container bytes), eighteen clips (189,114 bytes), 309 meshes, 900 textures and 127 materials. Total runtime assets: 75,122,812 bytes. Mesh/material/texture counts and infantry triangle counts are unchanged. [Catalog report](review/infantry-skin/assets.json).

| Validation | Executed result |
| --- | --- |
| Release solution and reference graph | Zero warnings/errors; 45 projects, no cycles |
| Final pipeline/rigid/production tests | 51 passed |
| Final focused pose/cache/LOD/Operations tests | 113 passed |
| Final complete Presentation suite | 1261 passed; 12 explicit opt-ins skipped; zero failures |
| Complete Game suite | 410 passed |
| Complete Client suite | 418 passed; two explicit native-audio opt-ins skipped |
| Headless | 64 ticks; 1000 entities; seed 12345; no pending commands |
| Runtime pipeline process contracts | Locking, cancellation/recovery, source-less and integrity contracts passed against the isolated current compiler |

The first broad Assets run retained five failures caused by stale 1337-asset expectations; all five production contracts were updated to 1357, and the infantry rigid-range assertions were migrated to actual skin/joint/clip assertions. The final 51-test run includes those five contracts and passes. The first broad Presentation run retained an Operations zero-allocation failure (624 bytes, count 1000). Its assertion and implementation were not relaxed; the final focused suite and the complete final suite pass, including that case. These earlier failures are not recorded as successful runs.

## Native poses, distances and batching

Both roles rendered all nine actions without skin/static fallback. Initial fixed phases 0, .125, .375 and 1 were captured at `7ce49b6b`; after restoring established LOD behavior, phases 0 and .375 were rechecked on the final native code. The [complete final two-image pose set](review/infantry-skin/poses/manifest.json) preserves its original image hashes. Fixed images prove sampled poses, not smooth video playback or human approval.

The actual camera-distance matrix uses no inspection override: 120 m selects static LOD1, while 420 m and 1000 m select static LOD2. The repeated scene contains 1000 squads/4000 cosmetic figures; 972 squads are visible at 120 m and all 1000 at the other distances. No skin poses are evaluated there. [Recorded cameras and LOD counts](review/infantry-skin/distance-lods.json).

At the separate 40 m inspection workload, 551 squads/2204 figures are submitted in eight infantry draws (the bind comparison uses two). Skinning shares six action/time/detail variants, retains 11,451,456 upload bytes across the frame slots and reports zero static fallbacks. This stays within the sixteen-variant/16 MiB-per-slot/four-slot limits. Collision, navigation, entity count and selection bounds remain unchanged.

An initial 18/6-pixel infantry geometry cutoff unintentionally changed existing detail policy. It was restored to the existing 90/28-pixel selection and matching 90-pixel motion fade. The initial cost run was stopped while making that correction; its completed static samples and [failure record](review/infantry-skin/initial-cost-failure.json) remain retained. This restoration did not remove the hardware slowdown, and no speedup is attributed to it. The initial inspection override also made three zoom-labelled captures use the same 40 m camera; they are not claimed as three physical distances. The final distance matrix above corrects that qualification gap.

## Paired cost and remaining blocker

The [fixed plan](review/infantry-skin/performance-plan.json) runs identical geometry/materials in bind, skin, skin, bind order at 40 m, High, 1920x1080, 32 warmups, 128 timed samples and two repeat readbacks. All four native cohorts are structurally qualified with repeated identical pixels, valid bindings and no fallback meshes. Samples, budgets and timeouts were not relaxed. [Cohort inputs and distributions](review/infantry-skin/cohorts.json).

| Pair | CPU mean | GPU mean | Gate result |
| --- | ---: | ---: | --- |
| skin-b versus bind-a | +2.21% | +2.42% | Fails CPU p95 +38.26%, p99 +17.09% |
| skin-c versus bind-d | +0.63% | +2.69% | Passes all mean/p50/p95/p99 limits |

Both use the unchanged 15% repository gate. [First report, including failure](review/infantry-skin/performance-a.json); [second report](review/infantry-skin/performance-b.json). The overall performance requirement is **not passed**. The two static cohorts also drift substantially in CPU tails; that is evidence of variance, not a waiver or proof that skinning caused none of the observed tail cost.

During 100% GPU utilization, the RTX PRO 5000 remained at P8/180 MHz. The driver reports an active software power cap of 20 W versus a 115 W default, with no thermal slowdown. [Power-state record](review/infantry-skin/gpu-power-state.txt). These settings were not changed. Absolute static and animated GPU means around 529-544 ms cannot qualify normal-power RTS frame budgets. Normal-power hardware cost and human motion/readability review remain required; the failed pair must stay visible when that work resumes. Existing software-fence/inactive-path failures in [rigid qualification](RigidClipQualification.md) are separate evidence, not an automatic baseline waiver for this feature.

## Installed player and remote checks

Final archive identity, every-file verification, source exclusion and installed/development pixel parity are recorded in [package integrity](review/infantry-skin/package-integrity.json), [package provenance](review/infantry-skin/package-provenance.json) and [installed parity](review/infantry-skin/installed-parity.json). The player is tested from an empty working directory, with no runtime-root environment override and an invalid Blender path. Only compiled assets ship.

The PR remains a draft while the performance gate and human review are open. Remote CI status is reported on [PR 234](https://github.com/DaveBeusing/Forgeline/pull/234); running checks are not represented as passed. No merge, tag or release is performed.
