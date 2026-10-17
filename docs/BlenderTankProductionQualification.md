# Directorate tank visual production qualification

Recorded 2026-10-17 for 0.1.397. The editable source and bounded workflow are
[documented here](BlenderTankProduction.md). All measured results, distributions,
source identities, resource counts, image hashes and failed performance gates are
retained in [qualification.json](evidence/directorate-tank/qualification.json).
The [original contracts](evidence/directorate-tank/baseline.json) precede the changes.

## Delivered source and boundaries

The saved rest source is `assets/source/units/directorate/authoring/main_battle_tank.blend`.
Seven GLBs are its reviewed compiler inputs; twelve deterministic glTF/kit seed
exports remain available for reconstruction. Existing IDs, collision/navigation
footprints, gameplay sockets, LOD thresholds and shared material/texture identities
are retained. No renderer, simulation, headless or runtime dependency was added.

LOD0 drops from 8,024 to 7,264 triangles: chassis 6,272 to 5,528; turret 1,752 to
1,736. Layered armor, service fittings, a second protected optical unit and moving
spokes use larger readable forms while reducing small repeated cylinder costs.
LOD1/2 stay at 892/244 triangles; damage/critical/wreck stay at 88/180/300.
Their positions and normals match the original seed exactly; maximum UV round-trip
noise is 0.00000002981, within the unchanged import tolerance.
Eight wheel bindings, cannon elevation/recoil and external turret aim retain their
existing copied-pose ownership. Symmetric tires and tread links remain static;
no physical suspension or circulating-track system is claimed.

## Sources and executed checks

Baseline master: `f12517c73dfe5863d7227b4f6c18e9139bcc5853`.
Original native captures: `ed1fec14` (version preparation and original contracts,
no model changes). Runtime/pipeline test binaries: `cba143ac`.
Final clean native and package source: `3834060b6ada6101625915495f114393f8e526ca`;
the final source change only adds a successful unmodified-scene control to the
native rejection tests. Subsequent evidence commits do not change runtime code.

Windows x64, .NET SDK 10.0.401, Blender 5.2.2 / bundled Python 3.13.13,
NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653.
Native D3D12, software false, debug layer disabled. Blender previews use CPU Cycles
and illustrate authoring; they do not establish engine shader parity.

- Release solution build and isolated final client build: zero warnings/errors.
- Project graph: 45 projects, no cycles or boundary changes.
- All 72 Python contracts, 13 asset and 114 presentation tests pass.
- Existing native motion comparison: three facts pass; two unrelated opt-in
  round-trip/surface facts skip because their inputs are not supplied in this run.
  All 616 matrices pass the unchanged 0.00002 bound; maximum error 0.0000035763.
  Across three default compiled exports, 57 meshes preserve geometry/contracts
  and mechanical batch sharing. The intentional edited pivot/rate/travel variant
  also preserves shared geometry.
- Saved tank project reopens twice with byte-identical exported source and compiled
  runtime trees. Nine invalid native edits are rejected against a successful
  unchanged-scene control. A saved positive static shape edit survives GLB export
  and compilation, changing only the intended damaged attachment.
- Independent reconstruction reproduces all twelve seed glTF exports byte-for-byte.
- Full forced catalog rebuild and integrity: 1,337 assets, 309 meshes, 900 textures,
  127 materials and one UI skin; 71,304,146 total runtime disk bytes.
  These disk totals are not device residency.
- Runtime process/cache contracts and a 64-tick, 1,000-entity headless smoke pass.

The existing manual provisioned Blender workflow now includes saved tank production.
Its local path was exercised; the remote workflow was not dispatched. This is
focused local coverage, not a claim that the entire solution suite was run locally.

## Native reference and resource results

Seven original/new image pairs use High, 1920x1080, identical lighting, camera,
DPI and settings: gallery and 1,000-tank formation at three RTS zooms, plus a 30m
gallery inspection. Eight warmups, eight requested samples and two repeated
captures per case; GPU distributions contain six or seven valid timestamp samples.
All captures are structurally qualified, repeat pixel-stable and have zero missing
mesh fallback or binding faults. Existing gallery normal/strategic and formation
strategic images are pixel-identical; formation normal differs by two pixels.
Near/detail images intentionally show the new geometry. No approved goldens or
human art acceptance is inferred.

| Case | GPU mean before / after ms | Instance draws before / after | Resident mesh bytes before / after |
| --- | ---: | ---: | ---: |
| Gallery close | 23.97 / 28.80 | 29 / 27 | 2,466,144 / 2,404,800 |
| Gallery normal | 15.90 / 15.02 | 28 / 28 | 598,224 / 598,224 |
| Gallery strategic | 19.07 / 17.27 | 29 / 29 | 298,608 / 298,608 |
| 1,000 tanks close | 347.34 / 303.07 | 11 / 9 | 1,562,448 / 1,501,104 |
| 1,000 tanks normal | 26.15 / 22.71 | 23 / 23 | 344,928 / 344,928 |
| 1,000 tanks strategic | 24.39 / 28.26 | 26 / 26 | 180,864 / 180,864 |
| Gallery inspection | 20.80 / 20.98 | 28 / 26 | 2,448,528 / 2,387,184 |

Textures and descriptors are unchanged in every pair. Paired 100/1,000-instance
canonical stress smokes both retain nine instance draws, 45,718,384 peak resident
texture bytes and 69 peak descriptors. Existing draw limits, 64 MiB scene texture
and 512-descriptor qualification limits pass without changes. These short timings
include the whole scene and are not sustained FPS or portable performance claims.

Review the [original 30m inspection](evidence/directorate-tank/before-MainBattleTankProduction-1920x1080-High-CloseTactical-Inspection30.png)
and [new 30m inspection](evidence/directorate-tank/after-MainBattleTankProduction-1920x1080-High-CloseTactical-Inspection30.png).
The [Blender idle](evidence/directorate-tank/blender-unit.directorate.main_battle_tank-idle.png)
and [firing](evidence/directorate-tank/blender-unit.directorate.main_battle_tank-firing.png)
previews show the editable source separately from the engine lighting.

The dense formation images are also retained: [before](evidence/directorate-tank/before-MainBattleTankFormation-1920x1080-High-CloseTactical.png) and [after](evidence/directorate-tank/after-MainBattleTankFormation-1920x1080-High-CloseTactical.png).

## Performance acceptance remains open

The unchanged default 15% performance gate fails the initial six-case comparison
on twelve CPU/GPU distribution statistics. The separate detail-camera gate passes.
These failed results are retained; fewer triangles/draws do not imply an all-green
timing qualification.

A fixed follow-up uses 32 warmups and 32 requested samples for the two suspect views in ABBA
order (old/new/new/old), two independent pairs, using the same clean client binary.
Gallery GPU means are 24.31 to 27.00ms in pair A and 23.85 to 23.56ms in pair B;
strategic formation means are 21.22 to 20.67ms and 21.65 to 20.42ms. Both mean
comparisons remain within 15%, but pair A fails gallery CPU/GPU tail statistics
and formation CPU p99. Pair B passes both gates. Thus two of four follow-up gates
fail. The data do not reproduce a consistent mean regression; tail variance and
attribution remain unresolved. There were no further repeats or gate relaxations.

The dense close formation still takes approximately 303ms GPU per frame, versus
347ms before, far above the 16.67ms analysis target. This existing practical
performance blocker is not resolved by the model work. Production acceptance
requires performance investigation and human visual review; neither is signed off.

## Distribution and corrections

The clean final source produces a reproducible `FORGELINE-0.1.397-dev-win-x64.zip`,
54,635,886 bytes, SHA-256
`04dad8411dd9a58cff7ccc422ee4e53e83cf63a415f028553e9e6651d0a6c089`.
All 1,461 extracted manifest hashes verify. Blender projects, Python and glTF files
are absent. UI/font/icon/audio packaging checks pass. The extracted 1,000-instance
smoke reaches Ready with empty stderr, no runtime override, an intentionally absent
Blender executable and an empty working directory. Its log confirms the installed
package runtime root, and the same resource budgets pass.

The first saved positive edit exposed Blender split-normal preview re-encoding;
explicit normals now deterministically re-establish the preview. A subsequent
native test hit the unchanged 120-second limit because writing preview attributes
repeatedly invalidated Blender's normal cache. Snapshotting computed values before
writes removed that repeated work; the complete native control/rejection/edit test
then passed. Failed logs remain under artifacts. Exact fixture triangle inventories
were reduced by 760 to match the new assets; existing resource ceilings were not
increased. One cohort preflight refused uncommitted review images; those generated
files were staged outside the tracked tree before clean-source captures.

## CI and review

[Initial PR CI](https://github.com/DaveBeusing/Forgeline/actions/runs/39068571801)
fails WARP `BattlefieldCombat-640x360-Low-NormalGameplay` with the ten-second GPU
fence timeout. The [exact fetched-master run](https://github.com/DaveBeusing/Forgeline/actions/runs/39066856516)
was still running when recorded. The same failure was previously observed on the
[earlier baseline](https://github.com/DaveBeusing/Forgeline/actions/runs/39062770122);
that history does not qualify the current base as passing. No CI retries or timeout
changes were made. New evidence commits trigger their own normal CI run. The PR
remains draft with CI, performance and human art acceptance explicitly open.
