# Graphical interface qualification

## Decision and fixes

The graphical interface is not merge-ready or accepted as AAA quality. Required
resource, allocation, debug-layer and real human/hardware gates are recorded
separately below. No golden, timeout, assertion or resource limit was relaxed.

Original native images exposed two compact-layout defects. Onboarding could
cover the lower command area; its shared layout now reserves that area and fits
wrapped objective, binding, context and next-step text before choosing type size.
Operations previously scaled its font with column width. It now uses available
vertical space independently, fits horizontal controls in the same shared input
geometry, and retains a readable text floor at normal/larger requested scales.
The six-row page, navigation, filters, route focus and existing actions remain.
Closed Operations entry text is positioned inside its existing control.

No artwork, reference originals, runtime payloads, simulation, commands, camera
rules, intelligence permissions, shader or gameplay feature changes were made.
Dynamic world/fog/placement geometry, diagnostics, native pointer ownership and
bootstrap/emergency drawing remain intentional independent paths.

## Attributable revisions

| Scope | Revision |
| --- | --- |
| Integrated master / fresh baseline | `5bfd8a14ceaea60368ded7bad0b0a6e2f79a5be8` |
| Complete pre-fix native cohorts | `726315b70d81e2fa7c0c9ffd7b9be815162553a9` |
| Corrected application, package and native candidate | `705e63297748fc1fd675d3fe331fbd94e8c20c34` |
| Whole-runtime/state-bank guard | `a73698d46a27d5d834d56ae773fc88af85e31b7a` |
| Retained strict protocol / final code checks | `a2c29525564a2ff5530d508fd790c685978ec6ac` |

Product version is 0.1.380-dev, exactly one PATCH above base 0.1.379.
Subsequent documentation and qualification-only commits must not relabel native
images, tests or packages with another source identity.

## Surface coverage

The [surface inventory](UiSurfaceCatalog.md) maps normal rendering to
`FrontendOverlayRenderer` and the six gameplay owners: resource/power,
selection inspector, action dock, information/minimap/guidance, hover tooltip and
Operations. Normal frontend and HUD art uses compiled pages through prepared
shared leases. Text, quantities, progress, queues, orders and permitted contacts
remain copied live presentation data. Static image fixture values (including
FPS 60 / TPS 20) are authored test inputs, not gameplay or performance results.

Native coverage includes all 58 named frontend/HUD fixture states: menu and
control feedback, loading/failure, new/load/empty/broken saves, every Settings
section and long/conflicting bindings, pause/settings/Credits, resource/power/
supply pressure, empty/single/mixed/long selection, guidance, tooltips/groups,
queue blockage, production/logistics/supply/technology, Operations empty/missing,
contacts, targeting and strategic map. Fixtures never dispatch gameplay input.

The matrix is 58 states × five outputs × three raster DPIs × three UI scales:
2,610 distinct cases / 5,220 exact fixed-state readbacks. Outputs are 1280×720,
1920×1080, 2560×1440, 3440×1440 and 3840×2160; DPI 96/144/192; scales .75/1/2.
Thirty independently complete requests keep all existing distinct-state pairs
together without exceeding the native 30-state request limit. Original native
PNG/RGBA hashes, text/skin/binding diagnostics and exact case unions are checked.

The pre-fix root `artifacts/interface-native-final` retains four complete
requests (348 cases). Its following request was closed normally after discovery;
the failure marker and 49 partial cases remain excluded. The initial dirty-tree
capture rejection produced no native evidence. Corrected installed evidence
uses a fresh `artifacts/interface-native-installed` root; do not overwrite either.

## Tests and CI

| Executed source | Total | Passed | Failed | Optional skips |
| --- | ---: | ---: | ---: | ---: |
| Fresh master `5bfd8a14` | 2,100 | 2,090 | 1 | 9 |
| Earlier candidate `5b216f7f` | 2,100 | 2,091 | 0 | 9 |
| Corrected application `705e6329` | 2,201 | 2,190 | 2 | 9 |
| Retained strict protocol `a2c29525` | 2,201 | 2,190 | 2 | 9 |

Fresh master failed the unchanged building-name allocation test for
`electronics_plant` (1,984 bytes, expected zero). The corrected-source run failed
that same test family for `supply_depot` (2,288) and `radar` (1,984). The passing
intermediate run and isolated runs do not establish a resolved cause. Strict
zero budgets remain. Any later scheduling/counter diagnosis requires separate
attributable evidence rather than retry-until-pass selection.

The final retained-protocol run at `a2c29525` takes 8m 52s 382ms and fails the
unchanged SimpleInstanceRenderer zero-allocation test (480 bytes) and
`AssetPipelineTests.RejectsIncompatibleOrDamagedDeploymentManifestAndCompilerRecovers`
for missing-integrity (`recovered.Success` is false, line 352). A separate,
unaltered seven-case recovery control passes 7/7; it does not erase that failure
or identify its cause. Assets/compiler/test bodies match the baseline. This
recovery failure is newly observed here, not claimed to be reproduced on fresh
master. Both failures remain open, separately from the native/published negative
cases that pass. The final Release build has zero warnings/errors.

The corrected-source Release build has zero warnings/errors. All 196 focused
guidance, Operations, economic, skin and tactical contracts pass. Expanded
geometry tests include all supported output/DPI/scale combinations plus retained
1024×720 / 1600×900 cases. Restore, 43-project boundaries, version contracts,
source/runtime integrity, font/audio, native-image/performance rejection and
package exclusion/process contracts pass.

Source CI run 38880981301, `build-test` job 116907444148, passes the UI/matrix,
native typography, shared-skin lifetime, economy/privacy, tactical/native-pointer,
frontend/cinematic and WARP contracts before failing step 37 in window-mode
qualification: 99,608,050 compiled gameplay texture bytes exceed 58,720,256.
Later hosted benchmark, diagnostics, stress, recovery, full-suite, publish,
installed startup and terminal steps are skipped. Local results do not turn
these skipped hosted steps green. Frame hot paths 38880981290 and Scalability
qualification 38880981332 pass; optional hardware scopes remain separate.

The final code checkpoint `a2c29525` is also checked in
[CI 38894338401](https://github.com/DaveBeusing/Forgeline/actions/runs/38894338401),
job 116951368488: all preceding UI/runtime/native contracts pass and step 37
fails with the same 99,608,050-byte / 56-MiB catalog overage. Later required
benchmark/diagnostic/stress/recovery/full-suite/publish/startup/terminal steps
remain skipped there. [Frame hot paths](https://github.com/DaveBeusing/Forgeline/actions/runs/38894338363)
and [Scalability qualification](https://github.com/DaveBeusing/Forgeline/actions/runs/38894338382)
succeed at that source. Current-head checks remain authoritative on
[PR 214](https://github.com/DaveBeusing/Forgeline/pull/214); documentation does not
change or supersede these executed source identities.

## Assets and package

All five original reference hashes and all 852 catalog sprite hashes pass;
836 sprites are runtime-required. Source IDs, page dependencies/regions and
all 1,337 sealed containers qualify (900 textures, 309 meshes, 127 materials,
one skin). Container bytes total 113,308,083. The sealed content signature is
`5cb87ff663e609ff3c0842be2671e3eabc9a9389e3562a66e0fba46733700932`.

Development runtime fingerprint:
`ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`.
Installed runtime fingerprint:
`B8ACA01894C15F98C09ACB5A1A80EA173B7FCD73C51D4C7E7BFF3701B61F523F`.
The installed tree additionally contains the four UI PCM cues and studio MP4/WAV
playback sidecars. Do not normalize these different whole-tree identities.

Reproducible archive: `FORGELINE-0.1.380-dev-win-x64.zip`, 39,997,668 bytes,
SHA-256 `c366d3452fe6b1ddf6e14ae3f5943ed5459484e6bb87f14857643218b12595fa`.
All 1,403 published-file checksums and all 1,337 asset payload hashes pass.
The package excludes source/reference artwork, PNG/SVG masters, authoring tools,
capture tools and asset-authoring compilation dependencies. Existing DXC runtime
shader dependencies remain present; no absence of shader compilation is claimed. Its 844 UI payloads and PCM identity
are independently checked. No GitHub release or uploaded player is claimed.

Installed menu startup, two independent fresh-settings matches, direct windowed
and borderless process smokes, save/load/replay recovery and a natural seed-2026
match pass. Terminal result is CommandCoreDestroyed / player 1 / tick 42,922
within the unchanged 80,000 limit. No resource grants or forced result are used.
Missing manifest, invalid seal, missing core UI page and corrupt command-core
mesh each exit 1 with diagnostics; damaged/missing runtime files remain unchanged.

## Lifetime, displays and acceptance boundaries

Native shared-page/component tests exercise repeated sessions, selection changes,
cancellation/partial preparation and last-owner release. The opt-in recorded-
resource lifetime control passes without the debug layer; its debug-required
variant fails because the layer is unavailable locally. Zero debug counters
without the layer are not certification. Current direct/capture shutdowns do
not reproduce the historical fence timeout; an earlier failure is not declared
fixed merely because another gate now fails first.

The capture HWND and requested output are separate: its initial 1280×720 window
reports actual Windows DPI 144 while the backbuffer is explicitly resized to each
requested output and layout uses the requested raster DPI. Installed direct
smokes report 1600×900 windowed and 5120×2160 borderless, both at DPI 144. These
are native process observations; monitor transitions, physical legibility,
pointer feel, live animation/stalls and listening quality still require review.

Static images, transformed inspection aids, repeatability and nonuniform regions
cannot approve typography, contrast, icon semantics, occlusion or AAA quality.
Every exported review remains pending and tied to the exact manifest/PNG hashes.
No reviewer name, time or accepted rubric field is manufactured.

## Performance and resource gates

The installed warm pair uses fixed source/runtime, Medium 1920×1080, DPI 96,
scale 1, 64 warmups and 256 CPU samples per case. Distinct completed GPU samples
are 252–254. Two processes cover Menu, Onboarding, Operations and Contacts.
No observed mean/p50/p95/p99 trend exceeds the existing 15% advisory limit.
Menu CPU p50 is 4.22–4.29 ms / GPU p50 1.22–1.23 ms; HUD/world GPU p50 is
23.22–23.78 ms. These frozen whole-frame measurements do not satisfy a sustained
16.67-ms gameplay target and do not isolate individual skin shader cost.

Skin draw/vertex/upload and logical page/descriptor counters are kept separately
from whole-world timing and DXGI memory. Capture preparation also constructs
unused HUD/world resources for ordinary menu cases; its resident bytes are not
the normal frontend-only resource cost. Per-case release observations must be
reported independently from cumulative peaks and from the full 64-MiB scene
texture gate. Shared skin pages remain 28 MiB; their existing 32-MiB ceiling,
zero warm geometry allocation budgets, 56-MiB catalog and 64-MiB scene limits
remain unchanged. Retained individual UI containers and atlas pages are counted
honestly rather than excluded to obtain a pass.

All 30 installed cohorts and all 2,610 original PNGs pass independent PNG and
decoded-RGBA SHA-256 checks (12,825,001,603 original PNG bytes). All 5,220
fixed-state readbacks agree. One source, installed runtime, pair of assemblies
and driver identity spans the full union. No sampled texture uploads, missing or
dropped text/skin content, texture/material binding failures, submission faults
or pending retirements are observed. Before every case, loaded textures and
resident texture bytes return to zero; these per-case observations do not replace
a live same-process menu/match/restart journey.

The independent verifier initially assumed every UI case exposed HUD diagnostics;
frontend cases legitimately expose `ui.skin` with `ui.battlefieldSkin` null.
The corrected verifier requires a populated actual owner (frontend textures and
leases, or prepared HUD skin) and preserves all missing/dropped-content checks.
The initial schema error and the successful verification logs are retained;
no original native assertion, image, manifest or reviewed baseline is altered.

Across the full short matrix, logical texture residency is 40,894,648–89,891,412
bytes and used SRV descriptors range from 26 to 96. Active HUD skin draws span
12–39 and skin vertices 2,226–4,608. Whole-frame allocation medians span
56/88/128/144/296/368 bytes; these are not isolated UI geometry allocations.
The shared skin pages stay at 29,360,128 bytes. Resource baselines, cumulative
peaks, page totals, reserved upload buffers and DXGI budgets remain distinct.

Four actual pixel-comparison reports cover 348 before/after cases: 96 are
pixel-identical and 252 differ. Both complete outputs are retained at 720p and
1080p, DPI 96, all three scales. Hardware is comparable, but source changes from
`726315b7` to `705e6329` and runtime tree from development ADE to installed B8;
those identities are explicitly different. The reports request pixel comparison,
not an approved image gate. Empty image-failure arrays do not approve changes.
A real GateImages attempt with a pending baseline review correctly rejects it.

The four 4K/DPI192/scale2 half-world cases preserve native output, glyph/skin
counts and named UI region bounds against their matching full-world cases.
Menu pixels agree exactly; the three World/HUD images differ. Transparent HUD
regions contain differently resolved world pixels, so perceptual sharpness still
needs review. The overcast and WARP supplements remain separately scoped.

## Matched before/after performance

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver
32.0.15.9653, D3D feature level 12_1; Windows build 26300, .NET 10.0.12,
x64 and 24 logical processors. The enumerated Intel adapter is not the measured
render device. Captures run with VSync off; physical live motion remains pending.

Both source-less archives and every published-file checksum are independently
verified before measurement. The baseline application is
`0cb9b8089f84abfdce0e754e9176be5c4628c16b` (0.1.379); its src/tests/tools/build
bodies match integrated master. The candidate is frozen `705e6329` (0.1.380).
Both installed runtime fingerprints are B8, with the same sealed payloads.
A clean detached baseline checkout validates the old package's source guard;
no capture request or manifest identity is rewritten. Two processes per side
and workload alternate before/after, with 64 warmups and 256 CPU samples.
All second-run GPU distributions below contain 254 distinct completed samples.

| Workload / state | Before CPU p50/p95/p99 ms | After CPU p50/p95/p99 ms | Before GPU p50/p95/p99 ms | After GPU p50/p95/p99 ms |
| --- | --- | --- | --- | --- |
| 1080p / Menu | 6.36 / 20.34 / 21.43 | 6.13 / 19.32 / 21.85 | 1.23 / 5.91 / 6.41 | 1.96 / 6.05 / 6.44 |
| 1080p / Onboarding | 22.18 / 25.06 / 26.52 | 21.61 / 24.74 / 26.17 | 20.99 / 24.07 / 24.50 | 20.92 / 24.19 / 24.42 |
| 1080p / Operations | 21.65 / 24.83 / 28.71 | 21.38 / 25.73 / 28.15 | 20.81 / 24.02 / 24.36 | 20.78 / 24.25 / 24.85 |
| 1080p / Contacts | 22.36 / 24.62 / 32.15 | 23.26 / 25.63 / 30.08 | 21.82 / 24.19 / 28.45 | 23.38 / 24.51 / 25.25 |
| 720p compact / Onboarding | 12.69 / 23.89 / 27.83 | 12.03 / 23.30 / 28.15 | 11.29 / 20.77 / 24.13 | 11.27 / 20.41 / 24.07 |
| 720p compact / Operations | 11.47 / 23.34 / 27.85 | 10.80 / 20.74 / 27.61 | 11.03 / 20.40 / 24.03 | 10.74 / 20.13 / 24.04 |

Normal is Medium/1920×1080/DPI96/UI1; compact is Medium/1280×720/DPI192/UI2.
The unchanged 15% advisory comparator flags normal Menu GPU p50:
1.234752 → 1.956064 ms (+58.42%, +0.721312 ms). The candidate's own repeated
normal runs also flag that same median (+58.36%); baseline Contacts CPU p99
varies +21.58%. All six reports have zero unqualified cases; compact before/after
and both compact variance reports have no flagged trend. No outlier is discarded
or rerun until green. Power/thermal telemetry is not controlled or recorded, so
these trends require investigation rather than a causal performance claim.
The prior current-only warm pair with no flagged trend remains a separate pair.

Whole-frame allocation p50 stays 144 bytes for Menu and 296 for these HUD cases.
Normal Operations skin draws stay 22, vertices move 2,682 → 2,790 and upload
85,824 → 89,280 bytes. Compact Operations stays 22 draws, vertices
2,724 → 2,868 and upload 87,168 → 91,776 bytes. Skin pages, batch/buffer counts
and asset payloads are unchanged. These counters isolate skin submissions;
the CPU/GPU distributions include the world and are not isolated skin timings.
The unchanged 32-MiB shared-page ceiling passes; the 56-MiB catalog, 64-MiB scene
and sustained 16.67-ms targets remain open. No budget was raised.

The comparison captures all finish. Git's initial cleanup fails on a long Windows
path after unregistering the clean detached checkout. The original error is
retained; a containment-checked native PowerShell removal completes cleanup.
This filesystem failure does not invalidate completed captures or count as a
successful first cleanup. No temporary baseline worktree remains.

## Allocation-counter controls and retained test gate

An independent package-free .NET 10.0.12 / Windows 26300 / x64 / workstation-GC
probe uses no application methods in its measured idle intervals. It records
32/32 exact-zero idle controls and 8/8 positive 64-byte allocations. Under a
separate allocating/collecting thread, 7/192 idle intervals report 7,920 bytes
each, across 167 collection requests. Warmup and anchors are outside the measured
window. The raw samples, source and build log are retained. This reproduces a
counter artifact locally; it does not prove the cause of every failing application
test. See the related [runtime investigation](https://github.com/dotnet/runtime/issues/134724).
No runtime, GC environment or compiler setting is overridden.

An attempted narrow nonparallel collection with blocking collections before
measurement still fails `SimpleInstanceRendererTests.WarmSubmissionDoesNotAllocatePerFrameScratch`
at source `02be58d9`: 480 bytes, expected zero (39/40 focused cases pass).
The attempt is therefore reverted by `a2c29525564a2ff5530d508fd790c685978ec6ac`.
All C# test bodies/scheduling again match the frozen application's strict
protocol; no tolerance, skip, warmup increase or retry-until-pass is adopted.
The diagnostic prototype's initial analyzer failure and subsequent build/run
are retained independently from production qualification. A later passing
full run does not erase the intermittent failures listed above.

Qualification guard source `a73698d46a27d5d834d56ae773fc88af85e31b7a` adds dynamic
29-state banks, missing-skin rejection and whole-runtime fingerprints in schema-2
plans. Synthetic additions beyond two banks, absent/duplicate cases and added or
mutated playback sidecars are rejected. The PowerShell whole-tree hash matches
the native B8 fingerprint. Existing schema-1 native records remain unchanged;
no later script or documentation revision relabels the frozen source.

## Retained evidence and reproduction

The [evidence index](qualification/graphical-interface/evidence.json) hashes the
[record archive](qualification/graphical-interface/qualification-records.zip)
and selected native originals. The archive retains all complete manifests,
exact-union/checkpoint/validation records, actual pending reviews, before/after
and performance reports, original failed/passed logs, package/source identities,
negative cases, terminal/recovery outputs and reproduction/verification scripts.
The 12.83-GB full original image set stays in its immutable local capture roots;
it is not claimed to be uploaded to GitHub. The local native-pixel HTML index
provides separate grayscale and linear-RGB colour-vision inspection aids.
No transformed image replaces an original or grants accessibility acceptance.

| Selected unchanged original bytes | Evidence |
| --- | --- |
| Onboarding before / after, 720p/UI2 | [Before](qualification/graphical-interface/onboarding-before.png), [after](qualification/graphical-interface/onboarding-after.png) |
| Onboarding after, 720p/DPI192/UI2 | [Original](qualification/graphical-interface/onboarding-dpi192.png) |
| Operations before / after, 720p/UI2 | [Before](qualification/graphical-interface/operations-before.png), [after](qualification/graphical-interface/operations-after.png) |
| Menu / audio settings | [Menu](qualification/graphical-interface/menu.png), [audio](qualification/graphical-interface/audio-settings.png) |
| Controls long, 720p/DPI192/UI2 | [Original](qualification/graphical-interface/controls-compact.png) |
| Selection 4K / contacts ultrawide | [Selection](qualification/graphical-interface/selection-4k.png), [contacts](qualification/graphical-interface/contacts-ultrawide.png) |
| Overcast Operations | [Original](qualification/graphical-interface/operations-overcast.png) |

Use the committed source, pinned SDK and [review handbook](UiVisualReview.md)
for capture, resume and review export. Build/compile first, then run native lanes
sequentially in fresh output roots. For this package, pass its installed
`ForgeLine.Client.exe` and `assets/runtime` explicitly; a development-runtime run
has a different whole-tree fingerprint. The exact executed scripts and arguments
are retained in the archive. Pending reviewer fields must remain pending until a
real reviewer inspects and accepts the exact source/manifest/image identities.

## Explicit remaining acceptance work

| Area | Status and next evidence |
| --- | --- |
| Static native matrix / fixed-state repetition | Structural result is separate from human visual decisions. |
| Keyboard/pointer settings, rollback, selection, command, queue, filter, alert and map contracts | Automated Client/Presentation contracts pass; an end-to-end packaged live journey is not recorded. |
| Monitor migration, physical DPI/input alignment, minimize/restore and alt-tab | Not run as a physical interactive journey; requested raster DPI is not monitor migration. |
| Repeated menu/match/restart | Repeated renderer/session ownership contracts pass; packaged matches use independent processes. A same-process live restart journey remains pending. |
| Bright/overcast terrain | Native fixed-state evidence exists; daylight .2/cloud 1 is overcast evidence, not a certified night scene. True-night human review is not run. |
| UI motion / sound | Existing motion/preference and PCM/settings contracts pass; live motion and listening quality remain pending. |
| Grayscale / protan / deutan / tritan | Separate native-pixel inspection aid; no accessibility reviewer decision or in-game filter is claimed. |
| Hardware scope | One NVIDIA adapter/driver; WARP is a low-resolution functional fallback. Other physical GPU tiers are not run. |
| Device-fault/recovery | Existing deterministic contracts and normal native shutdown observations; no physical device removal is injected. |
| Debug layer | Required opt-in case fails locally because the layer is unavailable. |
| Full suite | Allocation failures retained with exact source and raw logs; cause and later diagnostic controls must be assessed separately. |
| Resources / sustained performance | Catalog exceeds 56 MiB, scene textures exceed 64 MiB, and warm whole-frame HUD timing exceeds 16.67 ms. Limits remain unchanged. |
| Human approval | Every rubric, physical-monitor, motion and GPU-performance decision is pending. No accepted golden is created. |

These gaps prevent merge readiness. The next acceptance session needs the exact
source/package, available D3D12 debug layer and physical monitor/input/audio
review. The resource overage requires a separately measured texture-ownership or
asset-footprint correction; retained individual containers and shared atlas pages
cannot be excluded from accounting. CI must then finish every required hosted
step, including the unchanged natural terminal match.

Selected 1280×720 originals were inspected at their native pixel dimensions,
including ControlsLong at DPI 192/scale 2 and corrected Operations at that same
output. The long Controls fixture intentionally ellipsizes oversized values;
Operations retains its detail and action regions. Larger-image previews can be
rescaled by the display tool and are not asserted to be a full 4K pixel review.
The separate 100%-zoom inspection index provides unchanged originals for the
remaining full-resolution, contrast, edge/atlas and accessibility decisions.

The nine full-suite skips are the two opt-in D3D12 resource-lifetime variants and
seven optional particle, atmosphere, quality-profile, scene-pass, shadow and
terrain/vegetation performance cases. Their skip remains a skip; the matched UI
benchmark does not implicitly qualify those separate hardware matrices. The
explicit lifetime run belongs to source `5b216f7f3e87b8e7bbfb90f7ac53392e5b6f374b`;
its Graphics implementation and test bodies are unchanged at `705e6329`.
