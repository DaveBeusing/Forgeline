# Render foundation qualification

Status: **integrated; technical gates passed; human native review approved**. The
parallel release record takes effect when PR #241 is integrated into master. The current acceptance
checkpoint is [Acceptance](Acceptance.md); the measurements below retain their
original historical source identities and failed results.

## Identity and immutable behavior

Baseline: master `3c61f47274f03a0ab6e3b2022f40ac8205a72eb2`, 0.1.400.
Changed native binary: `2dfd86d318271decfc87c5234ef461e1e7714bb4`, 0.1.401.
Both use .NET 10.0.12/SDK 10.0.401, Release, Windows x64 and the same sealed assets.
Runtime-directory fingerprint:
`EE9B03122F04718874D7B2CDBBEDB4B74F6D907082D56C1A399C01545B2D9590`.
The standard development build reports zero compiled and 1,357 unchanged records.

All three factory method bodies retain their baseline hashes after only the private
-> internal access change. This includes exact shader literals, defines, target
profiles, layouts, material/texture bindings and pipeline flags. The checked-in
factory-source regression passes. The frozen ABI and initialized-buffer transfer,
failed replacement, phase/owner errors, suspended callback recovery, large snapshots,
conservative bounds and retained publication readers have dedicated executable tests.

Release solution build: zero warnings/errors. Acyclic project graph: 45 projects.
New namespace: 13 boundary contracts pass. Ownership/factory source: four tests pass.
Complete affected suites: Presentation 1,271 passed/12 optional skips, Client 419/2,
Graphics 78/2. After final read-interface wiring, the complete solution passes:
**2,588 successful, zero failed, 16 explicit optional skips, 2,604 total** (11m16.537s).
This includes the full combat-group allocation suite; the historical full-suite
allocation failure is not reproduced. Optional skips remain explicitly optional
Blender/hardware qualification, not silently reclassified passes.

The existing null-graphics frame-hotpath benchmark completes ten cases with 1,024
warmups and 8,192 samples each. Snapshot indexing and culled rendering allocate zero;
other warmed rendering cases report 0.05859375 bytes/operation. Extraction and copied
publication intentionally retain their existing owned allocations (241,182.11 and
15,456 bytes/operation). These are CPU observations, not GPU or FPS qualification.
The separate combat-group benchmark passes its allocation contracts in all 24 cases:
eight retained HUD/input cases allocate zero, while eight summary and eight owned
snapshot/index cases satisfy their existing bounded-storage contracts. Its unchanged
128 warmups/256 samples and observational timings are retained in the raw report.
Both benchmark reports retain the harness assembly identity 0.1.0.0; the executed
Release binaries came from the changed source identity above, without relabeling
that existing harness metadata as a product version.

## Native equality and bounded resources

Actual NVIDIA RTX PRO 5000 Blackwell laptop GPU, feature level 12_1,
driver 32.0.15.9653; hardware adapter, debug layer unavailable/disabled. Native High
1920x1080, eight warmups, 32 samples, two readbacks, fixed actual 120/420/1000m
bookmarks. Baseline and changed tank/industrial matrices each complete six cases.
Every before/after image has the identical pixel SHA256. Repeated images are exact.
No mesh fallback, material/texture binding failure or submission fault is recorded.
This proves sampled native image equality, not independent art approval or every
possible scene/camera/driver state. No golden image was regenerated as an approval.

| Scene | Camera | Instance draws before/after | Logical texture bytes before/after |
| --- | --- | ---: | ---: |
| Tank | 120m | 27 / 27 | 47,912,856 / 47,912,856 |
| Tank | 420m | 28 / 28 | 47,912,856 / 47,912,856 |
| Tank | 1000m | 29 / 29 | 47,935,736 / 47,935,736 |
| Industrial | 120m | 23 / 23 | 47,912,856 / 47,912,856 |
| Industrial | 420m | 37 / 37 | 47,912,856 / 47,912,856 |
| Industrial | 1000m | 41 / 41 | 47,935,736 / 47,935,736 |

Mesh-cache counts also match per case (42/35/28 and 49/45/40). Across all 32 warmed
samples per case, texture count, resident bytes, descriptors, upload count and live
resource count are constant. Live counts are 368/354/352 and 382/374/376 respectively;
pending retirement and submission faults remain zero. This is a measured warmed
plateau, beyond a successful single frame. It is not an unlimited-session leak proof.

The initialized Upload-buffer fallback is active for real fallback, runtime mesh
and terrain geometry in these captures. CPU-input copy and failed-candidate disposal
have separate tests; existing native resource lifetime rules remain in the device.
Cold tank and three BattlefieldCombat VFX viewpoints also pass native structural,
repeat-readback and binding checks. The cold run reconstructs its renderer with zero
warmup frames; it does not claim OS/driver cache eviction. Masks, decals, particles,
main/shadow passes and offscreen coverage retain existing production paths.

The unchanged window-mode script passed on the baseline and the integrated code,
including borderless 5120x2160. Compiled gameplay texture bytes are 57,665,206 under
the unchanged 56 MiB limit; studio startup is 1,786,417 bytes under 3 MiB. No texture
source/profile change, quality reduction or larger limit was needed.

## CPU preparation and performance acceptance

The unchanged 15% comparison **fails seven CPU distributions** across the six cases;
zero cases are unqualified under the comparison tool. Failures include tank-close
mean/p95/p99, tank-normal mean/p99 and industrial-close mean/p99. The failed report
is retained. There are no GPU distributions over this comparison threshold, but
that is not a sustained GPU performance guarantee. No rerun was used to replace a
failed cohort, and no unsupported FPS improvement is claimed.

| Scene / camera | CPU mean before -> after ms | GPU mean before -> after ms | Prepare mean ms |
| --- | ---: | ---: | ---: |
| Tank / 120m | 38.007 -> 56.008 | 19.430 -> 14.616 | 11.632 |
| Tank / 420m | 15.725 -> 25.991 | 17.267 -> 11.934 | 0.512 |
| Tank / 1000m | 18.885 -> 11.267 | 19.960 -> 12.292 | 0.325 |
| Industrial / 120m | 19.618 -> 28.162 | 20.671 -> 14.726 | 0.324 |
| Industrial / 420m | 16.365 -> 11.096 | 16.920 -> 12.119 | 0.349 |
| Industrial / 1000m | 18.638 -> 11.937 | 20.523 -> 13.058 | 0.465 |

Initial synchronous preparation repeats conservative selection before recording.
Its real additional CPU work is measured separately; the first tank-close cohort
also has large timing tails. The data do not establish a single cause for every
outlier. Telemetry records P8/180MHz and an active 20W software power cap against
a default 115W. Global power/driver settings were not changed. Native CPU/frame-time
acceptance stays open; this foundation is not represented as a render speedup.

Prepare and Record times are sampled explicitly. Submit/Retire fields measure CPU
phase bookkeeping after actual device submission. Present/wait times and upload
byte counts are unavailable/null; no invented zero GPU timing is introduced. DXGI
usage, logical bytes, texture counts and device health remain separate observations.
The disabled-meter regression performs 1,024 frame sequences without per-frame
allocation or diagnostics/clock queries.

## Existing gate failures and release condition

Historical CI 38928699407 failed at 99,608,050 compiled gameplay texture bytes; the
integrated content-budget fix already resolves that exact unchanged local gate.
Older master CI 39086554073 failed the audio heartbeat test; the complete local
Client suite now passes, without an audio change or claim of fixing its timing.
Current-master CI 39094375378 instead fails native WARP at its unchanged ten-second
GPU-fence deadline. Full logs identify this pre-existing software-adapter timeout.
It is not attributed to this refactor and its deadline is not raised. PR CI must
complete and any introduced failure must be repaired before integration.

The manifest records a functional implementation SHA and the actual integrated SHA.
Only a checked, authorized integration into master and resolution/acceptance of the
open required gates can update that status and release the eight parallel owners.
Human native review, the CPU-performance failures and required CI remain explicit.

## Reproduction

Exact scope and commands are in [contracts](Contracts.md). Executed isolated build
and test outputs were under `artifacts/render-foundation`; the corresponding native
capture request includes all settings, source identities and command exit status.
Raw manifests, all reference images, per-frame resource/health/CPU/GPU observations,
failed performance comparison and GPU telemetry are retained in [evidence](evidence/).
Metadata evidence retains its original bytes through a directory-local attributes
file. Runtime source files and compiled asset formats are unchanged.

## Integrated qualification follow-up

The foundation was merged by PR #236 into master at
`17810ee192fac495173a7e9da44c973595833f2b` (version 0.1.401). Integration is now
recorded explicitly in the ownership manifest, independently of acceptance.
`parallelReleased` remains false. Subsequent resource-scheduling implementation
has not started; acceptance of this foundation remains its prerequisite.

Final PR CI 39098337001 failed its native WARP reference step at the unchanged
ten-second fence deadline. CPU-scale 39098337009, frame-contracts 39098336987 and
navigation 39098336983 passed. The exact software-adapter failure matches the
pre-foundation master CI 39094375378; this identifies it as a prior failure,
without exempting the required gate or proving a cause.

The integrated master was rebuilt in isolated output on Windows x64 with the
pinned SDK: zero warnings/errors, exit 0. The exact WARP scene/profile/camera/
display/sample/repeat parameters were then reproduced locally, with only the
explicit client/output locations changed:

```powershell
dotnet build ForgeLine.sln -c Release --artifacts-path artifacts/foundation-acceptance
./build/Invoke-RtsReferenceQualification.ps1 -Capture -Software -Scenes BattlefieldCombat -Profiles Low -Zooms NormalGameplay -Displays 640x360 -Warmups 3 -Samples 8 -OutputRoot artifacts/foundation-acceptance-warp-baseline -ClientPath artifacts/foundation-acceptance/bin/ForgeLine.Client/release_win-x64/ForgeLine.Client.exe
```

Both commands exit 0. The software device is Microsoft Basic Render Driver,
feature level 12_1, debug layer disabled. The resulting single image passes
structural checks and all eight repeat readbacks are pixel-identical. The same
runtime fingerprint is retained. This local result does not replace hosted CI,
hardware performance acceptance or human image review. Software GPU mean time is
1,098.706 ms and is not a hardware performance result. The unmodified raw manifest
and comparison are retained under `evidence/integrated-warp/`.

The seven earlier CPU/frame-time comparison failures remain unchanged and open.
In the tank-normal and industrial-close cases, measured Prepare means are 0.512
and 0.324 ms while total frame means increase by 10.266 and 8.544 ms. Those elapsed
frame samples include device reuse/presentation waits, so the data alone do not
attribute the whole increase to Prepare CPU work. No failed cohort is replaced,
threshold relaxed, deadline extended or performance acceptance inferred from this
observation. Isolated attribution and reproducible performance acceptance remain
required, together with successful required CI and explicit native image review.

Master CI 39103788914 was still running at this follow-up. Next: inspect its final
native result, resolve the required CI failure without suppressing it, complete
performance attribution/acceptance and human review, then release the ownership
manifest in a separate authorized serial integration. A merge alone does not
release subsequent rendering work.
## Foundation repair qualification

The follow-up repair and full retained results are documented in [Repair.md](Repair.md).
The diagnostic-observer allocation and unnecessary Prepare work are removed;
software backlog is bounded without raising the fence deadline. An audio test
publication race and slow-software smoke completion are also repaired. Source
`98dc4ba901e158a4da4f63add7cbee77ae1e5fae` passes final local native damage/WARP
comparisons and both window modes. CI 39107228321 at the preceding implementation
passed native WARP and GUI apphost completion, then exposed the smoke liveness issue;
final required CI must confirm its repair and complete the full solution.

Two larger opposite-order 128/512 hardware cohorts retain exact images and stable
resources. The first passes; the second has four CPU/frame-elapsed p95/p99 failures.
Earlier failures remain retained. Native wait/Present and scoped kernel observations
provide attribution evidence, without turning blocked elapsed time into measured
thread CPU or overriding the failed gate. All complete datasets and failed reports
are in [repair evidence](evidence/repair/). Human native-image review, tail-performance
acceptance and final required CI remain required. No subsequent package is released.

The optimized-clear repair retains all four predefined protocols, including the
final remaining tank-normal CPU/frame-elapsed p99 failure (+15.38% against 15%).
Both reversed-order final cohorts retain exact images and stable resources; one
cohort fails that single tail while the other passes. No threshold is rounded away.
Hosted native proof in `evidence/repair/ci-39114608164-*` verifies actual debug-enabled
zero-warning/error output in both window modes at the equivalent runtime-source
tree. The superseded overall run is not represented as full CI success. Final
workflow-head/serial-module suite results are recorded in PR #237; performance
acceptance, independent image review and authorized integration remain prerequisites
for releasing later owners.
