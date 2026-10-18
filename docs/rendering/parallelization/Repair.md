# Render foundation timing and software admission repair

Status: implementation under qualification. Parallel release remains blocked.
Base master: `17810ee192fac495173a7e9da44c973595833f2b`, version 0.1.401.
The existing qualification follow-up is continued; one PR delta remains 0.1.402.

## Identified work and failure boundary

Enabled asset-frame measurement queried full device diagnostics twice inside the
timed frame, including adapter memory-budget queries and diagnostic allocations.
The existing collector already obtains the same snapshot outside that interval.
The repair consumes that observation instead; no measured render work is moved out
of the interval, phase timing remains active and unavailable observations are null.
A regression verifies zero device diagnostic reads with metrics enabled, correct
observed upload deltas, counter-reset handling and phase rejection.

Original CI 39098337001 failure.json locates the WARP deadline at WaitForIdle after
the measured frames, rather than an individual recording callback. Local two-CPU
affinity with unchanged 640x360 Low scene/samples/repeats completes, but GPU p95 is
4,567.830 ms. Three outstanding frames can exceed a ten-second queue-drain wait
without any one frame being stuck. Software admission is now bounded to one frame;
the deadline, hardware admission and rendered quality are unchanged. CI confirmation
is still required. A native four-buffer test covers prior-frame retirement before
callback admission and escaped-context rejection over 12 software frames.

## Fixed measurement protocol

Keep all failed historical evidence. Execute two new paired cohorts without changing
thresholds or selecting successful subsets. Each cohort uses tank and industrial
fixtures at all three fixed cameras, High 1920x1080, 32 warmups, 128 samples and two
readbacks. First run the integrated baseline then repair; second reverse the order.
Use the same immutable runtime assets, adapter/driver and settings. Record source
and assembly identities, memory/resource plateaus and GPU telemetry. Only one
qualification process uses the GPU. Do not change global power/driver settings.
The unchanged 15% gate must report every case/statistic in both cohorts; distinguish
frame elapsed time (which includes waits) from Prepare/Record timings and observed
GPU timestamps. Historical 8/32 observations remain visible and are not overwritten.

Run the unchanged WARP required capture on the repaired client, including the
two-logical-processor process-affinity diagnostic. Then run Release, full solution
tests, reference graph, ownership/factory contracts and unchanged window qualification.
Hosted CI and human image review remain independent required acceptance steps.

## Additional CI completion race

Repair CI 39104862208 stopped earlier than native qualification at the existing
`OwnerHeartbeatStopsNativeQueueAfterRenderPublicationStalls` test. Its output Stop
counter increments before the audio owner publishes updated mixer diagnostics.
Observing that independent counter is therefore not a completion barrier for the
following voice-count assertion. The bounded wait now observes both the native
stop and published zero voices before asserting the same zero value. The deadline
remains five seconds, all stop/disposal assertions remain, and no production audio
code is changed. All 23 non-explicit mixer tests pass; the real-device listening
test remains explicitly optional. This serial gate repair is added to the foundation
owner's exact test-file list; subsequent parallel work is still not released.

## Frame-time attribution

The first paired cohort passes all unchanged 15% comparisons. The second reports
IndustrialBase normal-camera frame-elapsed p95 at +15.948% and GPU p50 at +20.914%;
both complete cohorts remain retained. No case has a CPU/frame mean regression over
15%. Measured frame allocations consistently fall by approximately 2,768 bytes per
frame. A successful first cohort does not discard the second cohort's failed gate.

Add explicit frame-buffer reuse-wait and native Present CPU elapsed observations
to GraphicsFrameDiagnostics and populate the existing nullable asset-frame fields
from the supplied snapshot. These are measured native calls, never synthetic zero
GPU times. Other wait categories remain outside this specific admission metric.
This additive shared diagnostic change is serialized with the foundation repair.
A final diagnostic matrix will attribute waits without replacing either comparison
cohort or rewriting its results.

## Avoid duplicate presentation-only preparation

The preparation pass also computed per-instance color, vegetation/world transforms
and mechanical detail weights that it never consumed. Resolve the same unit turret,
unit state and building state attachments before leaving preparation; compute those
colors/transforms/weights only in the recording pass. Visibility, material and LOD
selection, attachment order, batching and GPU input formats remain unchanged.

This additional actual CPU-work reduction is qualified with two further predefined
paired cohorts, in opposite order, at 128 warmups/512 samples per case. Keep the
same six viewpoints/settings, two readbacks and unchanged 15% comparison. Retain all
earlier failed results; the larger cohorts do not overwrite them. Add native
DamageDestruction viewpoints to exercise state attachments. Run the final affected
Presentation/Client/Graphics suites after this change. The full solution at the
preceding diagnostic source passed 2,590 tests, zero failed, 16 optional skips,
2,606 total in 15m35.423s; unchanged asset/headless domains are not reclassified as
a new execution at a different source identity. Final CI runs the entire solution.

## Slow native smoke liveness

CI 39107228321 passes both the native WARP reference and GUI apphost completion
steps, then remains in window qualification. The two-second stale-window policy
of RuntimeMetricsSampler prevents any running FPS/TPS sample when every software
frame exceeds two seconds. Smoke completion previously waited indefinitely for
that sample. Keep the sampler policy unchanged: a smoke-only platform observer
samples actual published successful presentations and completed simulation ticks
while the render owner works. Cache only observed positive running windows, reset
on inactivity/session change/rollback, and export that observed window in the
smoke report. No configured FPS/TPS value, reduced scene or weaker budget is used.
A missing observation fails after one minute rather than hanging CI. Four
deterministic tests cover slow eight-second frames, missing frame/tick progress,
inactivity, session changes and rollback. The additional source paths are serial
foundation repairs; parallel release remains blocked.
## Final paired results and diagnostic limits

The final Prepare-work change at `5ecd0d9f8215448b172ea5d0095fa9cb91fe8b1b`
was tested with both predefined 128-warmup/512-sample cohorts. Both retain all six
viewpoints and two readbacks. All 24 paired images across the earlier and final
cohorts are pixel-identical. Per-frame texture/residency/descriptor/upload counts
plateau, with no pending retirements or submission faults. The final first cohort
passes; the reversed cohort fails four unchanged CPU/frame-elapsed tail gates:

| Final reversed-cohort case | Statistic | Baseline ms | Repair ms | Change |
| --- | --- | ---: | ---: | ---: |
| Tank / strategic | p99 | 27.113 | 33.486 | +23.50% |
| Industrial / normal | p99 | 26.866 | 32.066 | +19.35% |
| Industrial / strategic | p95 | 27.728 | 33.651 | +21.36% |
| Industrial / strategic | p99 | 33.236 | 70.203 | +111.22% |

No final CPU/frame mean or GPU statistic fails the 15% comparison. This does not
satisfy the required tail gates. The earlier two repair-cohort failures and original
seven foundation failures remain retained, without selecting successful subsets.
The initial diagnostic-query repair removes approximately 2,768 measured allocated
bytes per frame. It does not establish a sustained frame-rate improvement.

A separate six-view diagnostic run measures admission reuse-wait plus native Present
at 69-86% of mean frame elapsed. Other native waits can occur inside recording;
remaining elapsed must not be described as operating-system thread CPU time.
Active hardware probes still report P8/180MHz during rendering. No global power,
driver, affinity, quality or gate configuration was changed for hardware comparisons.

Separate GC observations cover complete diagnostic runs, including setup/export.
They do not establish which sampled comparison tails were caused by collection.
A user-authorized elevated kernel trace at final code source
`98dc4ba901e158a4da4f63add7cbee77ae1e5fae` captures only the own reference-window
owner thread: native exit 0, identified window thread, 6,952 completed off-CPU
intervals, zero dropped retained intervals. Across the whole run, Wait/UserRequest
accounts for 12,424.773 ms; its maximum interval is 78.518 ms. Ready intervals are
small in this observation. These are genuine blocked intervals, but without aligned
frame/phase markers or stacks the trace does not identify a specific native call
or prove the cause of every tail in a different comparison run. No performance
failure is waived on this basis. The probe uses temporary local tooling only;
no profiler dependency, elevated launch or trace collection enters the player.

## Final local validation and evidence

Final player source is `98dc4ba901e158a4da4f63add7cbee77ae1e5fae`, version 0.1.402.
Release build: zero warnings/errors. Full Client suite: 423 passed, zero failed,
two optional skips. After the Prepare change, Presentation: 1,272 passed/12 optional
skips; Graphics: 79 passed/two optional skips. The preceding complete solution run
passed 2,590 tests with zero failures and 16 optional skips; it is explicitly tied
to its earlier source, rather than claimed as a full rerun at the final source.
Required CI runs the complete solution at its actual checked-out identity.

Final native DamageDestruction captures pass all three cameras and match the
integrated baseline exactly. The final unchanged WARP case passes all eight repeat
readbacks and matches the integrated baseline exactly. The unchanged window-mode
script passes Windowed 1600x900 and BorderlessFullscreen 5120x2160, with real
positive running FPS/TPS observations, no binding failures and no submission faults.
The local debug layer is unavailable; these results do not certify debug-layer
execution. Texture budgets pass unchanged: gameplay 57,665,206 bytes below 56 MiB,
startup studio 1,786,417 bytes below 3 MiB.

Complete raw manifests are retained losslessly as `manifest.json.gz`, with original
and compressed SHA-256 values in `evidence/repair/integrity.json`; images and both
passed/failed gate reports remain present. Reproduce the summary directly from the
compressed evidence:

```powershell
python build/rendering/parallelization/SummarizeRepairEvidence.py docs/rendering/parallelization/evidence/repair
```

An initial GUI-launch orchestration attempt failed to wait for child completion,
so its concurrent `artifacts/foundation-pair*` runs are invalid and excluded. The
corrected serial and final cohorts use a waited native output pipeline, preserve
opposite run orders, and never replace a failed cohort. The invalid attempt's
local logs remain available; it is not performance evidence.

Status remains **implemented, not accepted**. CPU/frame-elapsed tail acceptance,
required final CI and independent human native-image review are open. The resource
lifetime/upload package and parallel owners remain blocked by the foundation gate.

## Integration with the newer master

While qualification was running, master advanced to
`d3f5c9cd` (vehicle docking integration, version 0.1.402). The repair branch
incorporates that upstream change and advances once above the current base to
0.1.403. All earlier evidence retains its original source/version; none is
relabeled as a combined-tree execution. Upstream changes the reference fixture
version from 3 to 4. Therefore earlier fixture-3 comparisons remain historical,
and combined-tree native/performance acceptance requires fixture-4 observations.
The foundation's actual original integrated SHA remains 17810ee; this newer base
is not a new foundation release or permission to start subsequent packages.

## Hosted debug-layer follow-up

Combined-tree CI 39112089267 passes WARP reference/apphost completion and now exits
window smoke normally, with actual positive running rates. Its Windowed report
contains four D3D12 debug warnings and zero errors, so the unchanged warning gate
correctly fails. Existing diagnostics counted and then discarded message text.
Retain the first 16 warning/error/corruption IDs and descriptions in diagnostic
stderr while continuing to count every message. No severity is filtered, warning
is waived or debug layer disabled. The next hosted run must identify and resolve
the actual warning before CI acceptance; the local debug layer remains unavailable.

## Combined-suite isolation failure

The first complete combined-tree execution retains five Client failures: an expired
single-cue audio-owner probe, allocation observation, decoder startup, branded frame
presentation and loading handoff. Presentation and Game complete successfully in
that run. Native Client classes previously ran concurrently with each other and the
allocation probe; desktop/video resources and time-sensitive observations require
exclusive process-local qualification. Assign these classes to one existing-runner
exclusive collection. No test, deadline, zero-allocation assertion or quality gate
is removed. Other test modules remain governed by the unchanged solution runner.
The audio-owner test now offers fresh actual-timestamp requests while waiting for
its blocking output to enter, within the same five-second bound. A request's real
150 ms expiry remains valid behavior; this ownership test no longer assumes that
one request survives arbitrary scheduling delay. Stop, disposal, owner-thread and
queue assertions are unchanged. These exact test paths are serialized foundation
CI repairs. The failed full run is retained independently of subsequent validation.

The current fixture-4 cohorts compare base `d3f5c9cd` with combined repair `4f4bf273`,
with 128 warmups/512 samples/two readbacks, before/after then after/before. All 12
comparisons are pixel-identical and all resources plateau. The unchanged gate
reports 3 failures in pair 1 and 8 in pair 2, with zero unqualified cases. Pair 1
fails tank-normal CPU p50 and GPU p50, plus industrial-close GPU p50. Pair 2 fails
tank-close CPU p95/p99; tank-strategic CPU mean/GPU mean; industrial-close CPU p95/
GPU p95; industrial-normal CPU p95/p99. These include mean and GPU regressions,
not only CPU tails. Earlier successful cohorts do not override these failures.
The two complete current cohorts and failed reports are retained in repair evidence.
Reproduce all three protocols with the same helper and repeated `--prefix` values:
`foundation-serial-pair`, `foundation-final-pair`, `foundation-current-pair`.

After exclusive Client qualification and the fresh-request ownership test change,
the entire Client suite passes: 423 successful, zero failed, two optional skips,
425 total, 37.108 seconds. The zero-allocation assertion passes unchanged. This is
affected-suite validation; it does not erase the five failed assertions from the
previous complete solution run or certify a complete solution rerun.

## Resolved warning identity and writer cleanup

Diagnostic CI 39113221933 identifies all four warnings as
`ClearRenderTargetViewMismatchingClearValue`: linear scene render targets were
created without any optimized clear value. Supply the engine's common clear color
at HDR resource creation, including the existing negative-alpha background encoding.
Use the same conversion for actual clearing so the hint and command cannot drift.
The per-frame caller-provided clear color, shaders and resulting pixels are unchanged;
other caller colors remain valid ordinary clears. No target recreation, extra idle
wait, resource scheduling feature or severity filter is introduced. A native
regression covers four buffers and full/half/full target recreation, with a required
debug-layer variant explicitly unavailable locally and exercised where installed.

The first combined complete solution run finishes with 2,686 passed, six failed,
16 optional skips, 2,708 total, 19m51.720s. The additional Assets failure masks an
entered-event assertion with workspace deletion while a writer still owns its lock.
The concurrency test uses dedicated long-running writer tasks to avoid blocking
its own thread-pool admission and always joins both writers in finally before
workspace disposal, including assertion/cancellation paths. Its five-second entry
bound, ten-second blocked qualification bound, blocking assertion and successful
qualification assertions remain unchanged. No compiler production code is changed.
The exact scene-target and Asset test files are added to the serial repair scope.

Targeted repair validation: Release builds with zero warnings/errors; the full
Graphics suite passes 80 tests with zero failures and three explicit skips (including
the unavailable local debug-layer variant), 83 total. All 25 AssetPipelineTests
pass. Full Client isolation previously passed all 423 non-explicit tests. Before
final acceptance, run two predefined opposite-order 128/512 hardware cohorts after
the actual optimized-clear change, preserving the same six fixture-4 views, settings,
two readbacks and unchanged 15% gate. Retain every previous failed cohort. Then run
the complete solution and native window/WARP/state comparisons at the actual new
source identity. No global power setting or performance threshold is changed.

## Cross-module native qualification policy

The optimized-clear cohorts at `11049693` retain exact pixels in all 12 comparisons.
Pair 1 fails one unchanged p99 gate (tank normal frame elapsed 25.505 -> 29.427 ms,
+15.38%); pair 2 passes every comparison. All mean and GPU comparisons pass in both.
This remaining failure is preserved and blocks performance acceptance.

The first full run with exclusive Client collections still reproduces a branded
startup failure while Graphics, Presentation and Game modules run concurrently;
the entire Client suite alone passes. Process-local xUnit isolation cannot isolate
native desktop/codec/timing work from other test modules. Set the full solution
runner's existing `--max-parallel-test-modules 1` option in CI and reproduction,
keeping every module, assertion, test filter, optional-test rule and time limit.
Focused earlier CI steps already run separate project commands. This is a serial
foundation CI repair to the exact workflow path; parallel implementation remains
unreleased. Retain the failed concurrent execution independently of the subsequent
complete serial-module run. No completed full success is inferred from a focused pass.

The known-failed concurrent optimized-clear solution run was stopped after its
branded startup failure; its log and explicit aborted-after-known-failure record
remain local. It is not reported as a completed full solution run. The replacement
executes every module serially with the same built source and unchanged tests.
CI 39114608164 at runtime source `11049693` now passes the previously failing
Windows window-mode/visual qualification step after the HDR clear correction.
Final workflow-head CI and the complete serial-module run still must finish.

## Retained hosted native proof and final acceptance boundary

The hosted native proof is retained under `ci-39114608164-*`. Its checked-out
synthetic merge is `ae066f896e12430742b22a1677dde179981d5e75`, with parents d3f5c9cd
and 11049693; the Git tree equals PR head 11049693 exactly. Native WARP, apphost
completion and both-window qualification steps pass. Both actual window reports
have debug layer enabled, zero warnings and zero errors, with 67 and 62 successful
presentations respectively. The overall run was superseded by the module-isolation
workflow update and is not labeled a successful full CI run.

The summary helper now includes every present predefined protocol by default and
reproduces the same complete result from raw and compressed data. All 48 paired
comparisons are pixel-identical. The final performance pair remains failed only at
tank-normal CPU/frame-elapsed p99 +15.38%; the reversed final pair passes. Neither
rounding nor successful subsets change that result. CPU/frame acceptance and human
native review remain open; subsequent rendering packages remain blocked. Full local
serial-module and final workflow-head validation were still running when this
native evidence snapshot was committed; their completed results belong to the PR
validation record and must not be inferred from this snapshot.

## Hosted default-adapter test assumption

Final-head CI 39116265186 completes the whole solution: 2,692 successful, two failed,
16 optional skips, 2,710 total, 18m21.508s. Every module except Graphics passes,
including all Client native/decoder/allocation tests and the Asset writer test.
Both failures are SceneCompositionMatchesLegacyTextureTransferAndPreservesOverlay
with ForceSoftwareAdapter=false, before its pixel comparisons. That configuration
has AllowSoftwareAdapterFallback=true; a machine without hardware correctly selects
WARP. The test incorrectly equates "not forced" with "hardware required".
Name the parameter forceSoftware and assert software identity when it is forced.
Keep all four test cases, all three exposure values, both tone mappers, full legacy/
composed pixel comparisons, overlay equality, resource and resize checks, and their
unchanged tolerances. Default-selection cases also run their full comparisons on
CI instead of being skipped or mislabeled as hardware coverage. No production
adapter-selection behavior changes. The exact test path is a serial CI repair.


## Required full-match and physical docking investigation

Hosted run 39119601649 at head 1dc0a1c6 passes all 2,694 tests with 16 optional
skips (2,710 total), the native steps, benchmarks, reproducible package and both
packaged startup checks. Its only failing step is the original seed-2026,
80,000-tick terminal match. All three separate quality workflows pass. The run
status is retained in `evidence/repair/gameplay-investigation` with lossless raw
reports and archive hashes. This is a failed overall CI run, not a green gate.

The same logical endpoint was reproduced on unchanged current master d3f5c9cd
and the renderer repair. A saved state exposes a vacant reserved approach whose
owner cannot advance past a staged truck. The staged truck cannot cross the
reservation to its waiting point. Normal route retries preserve this cycle.
The experimental repair only yields a physically vacant approach when its owner is Stuck and
another staged truck is TacticallyPaused on a route crossing that approach.
The existing release operation still checks full identities, visit generation
and actual owner/neighbor occupancy. An authoritative admission-yield component
keeps the former owner from reclaiming the lane until the crossing route clears;
cancellation or suspension clears that state. No occupied lane is released by a
timeout, and healthy approaches keep their existing behavior. Four new cases
cover vacant yield, occupied owner, occupied traffic and a non-stuck owner. One
case fails before the change; all four pass afterward.

A separate seed-17 observation at tick 30,000 exposes a precise exit orbit with
no nearby collider: the truck remains outside the 0.1 m arrival region at its
fixed crawl speed. Its exact recorded pose/velocity reproduces the failure at
10, 20 and 60 Hz. Runtime source 44a1e2e bounds turning speed by the remaining
approach distance and authored turn rate. All three cases then converge without
relaxed position/heading limits, unbounded rotation or positional snapping. Its
complete 511-test Game suite passes. The seed-2026 match still has the original
nonterminal endpoint, proving that this independent correction alone does not
resolve the reserved-approach cycle.

Exploratory broad staging-route and prospective power-planning variants are
excluded from the final implementation. Their completed failed suites and match
reports remain separately archived; a terminal result from one such variant does
not qualify the selected repair. Headless timings were collected during other
qualification work and do not constitute a throughput/performance comparison.
The targeted vacant-approach experiment passes all 515 Game tests, including
existing strategic deadlines, but its full 80,000-tick match still fails to end
(219 deliveries, 21,429 cargo units). It is not a qualified final implementation.

Master fb78998c then integrates PR #238 with physical FIFO admission, new queue
state, save schema 4 / authoritative schema 3, and native fixture 5. The previous
vacant-yield experiment is excluded when incorporating that authoritative queue
lifecycle. Retained pre-queue states and fixture-4 measurements are historical;
they cannot certify a fixture-5 binary. Revalidate current-master compatibility,
version delta, full tests/match and native comparisons on the combined source.

The next whole-solution execution at source 44a1e2e also exposes a remaining
Asset writer test dependency: TextureImporter acquires the process-wide
Bc7TextureEncoder.TextureSlot even for tiny fixtures. A concurrent large import
can hold that slot beyond the unchanged five-second qualification-entry wait.
LongRunning workers avoid thread-pool starvation but do not isolate that encoder
permit. Put AssetPipelineTests in an exclusive xUnit collection, preserving both
writers, their cleanup, the five/ten-second deadlines and the lock assertions.
All 25 focused pipeline cases pass after that policy change. A subsequent full
solution run must include the concurrent large-import classes as well.

The previous frame-elapsed p99 +15.38% failure and independent native review remain
open. None of these gameplay/CI results release the later parallel workstreams.


## Queue-enabled qualification and exhausted AI loading trips

Combined source c9230e4a integrates master fb78998c and advances the PR once to
0.1.404, above master's 0.1.403. Both combined and unchanged-master builds finish
without warnings/errors. The combined complete solution passes 2,730 tests with
17 explicit optional skips, 2,747 total, zero failures, 20m52.278s. This includes
the large-import classes and the now-exclusive writer collection. The graph has
47 projects with no cycles, and serial ownership/version validation passes.

The original seed-2026 match remains nonterminal on both queue-enabled binaries:
227 deliveries, 21,971 cargo units and 138 artillery shots. An endpoint snapshot
shows combat-ready units and partially stocked Supply Trucks still bound to old
SourceEmpty loading orders. Normal dispatch needs 50% cargo Fuel and 25% cargo
Ammunition, but reconsidering an exhausted trip attempts the full targets even
when no compatible source can supply all remaining cargo. That order excludes
the otherwise useful provider from reconnaissance/attack supply admission.

Finish this AI-planned exhausted trip when its existing cargo dispatch minimums
are met and no source can fill the remainder. Preserve full-refill selection when
a source can do so; below-minimum trucks retain loading/recovery. A dock visit or
queue registration still prevents release, and active recipient service retains
its existing ownership. Remove only the obsolete loading obligation; rescue
movement is preserved. The command-core candidate now propagates its actual
covers-cargo result, which this completion decision needs.

The new policy regression fails before the change. Six cases include exact
minimums, empty cargo, each insufficient commodity and an available full source.
The older no-replacement case now checks the completed partial trip, conserved
cargo and absence of dock/queue ownership. Original strategic deadlines and
readiness thresholds remain unchanged. All 551 Game tests pass afterward.
The required 80,000-tick terminal match remains to be rerun on this source.
Fixture-5 native pairs use the current unchanged master, retaining the same
128 warmups / 512 samples, B-A-A-B order, two readbacks and 15% gate. Earlier
failed fixture-3/4 protocols remain historical evidence and are not reclassified.


## Physical ammunition replenishment of the core loading source

Source 7b07ea8d still reaches Active/Running at the original 80,000-tick limit.
Its saved state contains ammunition in the ammunition factories, while several
Supply Trucks wait at the command cores with no cargo ammunition. Core stocks
contain plentiful Fuel but no Ammunition. The AI recognizes the core as a loading
source but installs no ammunition stock policy there. Thus consuming the initial
stock leaves no inbound physical distribution request for this loading source.

Add a demand-driven core ammunition policy from its own waiting loading trucks.
The critical minimum is the sum of their deficits below the existing 25% dispatch
threshold; the target is their remaining full cargo requirement. No demand means
a zero minimum/target, and ordinary surplus remains High. Actual deliveries still
require available producer stock, cargo trucks, physical docks and reservations.
No resource is created, no fleet bounds or readiness/deadline contract changes.
Three cases cover empty cargo and either side of the dispatch boundary, asserting
unchanged physical inventories. All 554 Game tests pass, zero failures/skips,
204.122 seconds. Client/headless builds have zero warnings/errors. Full terminal
qualification and final-source CI remain pending at this commit.

The broader processor-priority experiments are excluded. Prioritizing every
powered NoInput processor fails six existing strategic tests; promoting only
Electronics input fails two. Their complete results and the broad variant's
nonterminal full-match state remain archived. The initial experimental dispatch
test also had a wrong conservation expectation (200 instead of the actual 300
including source Fuel); it is excluded together with that global change. None
of these failed variants is part of the final runtime.


## Dispatch-complete exhausted trips

The 05ee10bf full match remains nonterminal. Its actual ammunition transfer rises
from 2,705 to 3,376 units; artillery shots rise from 79 to 158. East retains three
tanks, but multiple providers still take long full-cargo replacement trips after
a partial source is exhausted. The full-refill rule for an existing SourceEmpty
order disagrees with the ordinary 50% Fuel / 25% Ammunition dispatch rule.

Use the same existing dispatch requirement when reconsidering an exhausted trip.
After physical exit, finish that loading obligation once both cargo minimums are
met, before seeking another source solely for a top-up. Retain full-source search
for below-minimum trucks. The existing dock/queue guard is unchanged; active
rescue movement is preserved. The source-selection regression now uses a truck
below the Fuel minimum, so it still demonstrates required replacement loading.
The loaded replacement-source case now asserts completed dispatch instead of an
unnecessary replacement trip. A seventh case covers the below-minimum/full-source
boundary. All 555 Game tests pass, zero failures/skips, 143.413 seconds, retaining
all strategic deadlines. The original full-match qualification is still running
at this commit; no terminal success is claimed.

The earlier no-debug-capture CLI probe cannot provide a result: its report tries
to select a missing debug read model. The standard diagnosis-enabled run is used
for acceptance. A premature rebuild of its in-use Headless directory encountered
file locks; the subsequent build succeeds with zero warnings/errors. These are
not reported as successful match or source qualification. The 05ee10bf package
0.1.404-dev passes reproducibility and runtime asset/package validation; later
source identities must be recorded separately.


## Fixture-5 protocol and further supply lifecycle regressions

The fixed B-A-A-B protocol compares master fb78998c with source 7c2988d5 using
128 warmups, 512 samples and two readbacks. All 12 paired images are exact and
resources remain at steady plateaus, with zero submission faults. Pair 1 retains
one failure: IndustrialBase / CloseTactical frame-elapsed p99 is 28.1103 ->
33.0722 ms (+17.65%, unchanged 15% limit). Pair 2 has no regressions. All means
and GPU gate comparisons pass. The six slowest frames are now reproduced by the
summary tool with their aligned phase evidence. Frame 164 takes 33.7612 ms,
including 31.3246 ms waiting for the reusable graphics buffer; baseline wait
measurements are unavailable and remain null. This is a measured wait-phase
attribution, not an override of the failed gate. The separate idle power snapshot
shows a current 20 W limit and 115 W default; it is not aligned to a slow frame
and does not prove the cause of that frame. No power/driver setting changes.

Fixture-5 WARP has eight stable readbacks and exact baseline pixels; all three
damage views also match exactly. All 158 retained native PNGs match their actual
manifest hashes. All complete historical protocols remain reproducible. The
summary adds the fifth prefix and elapsed phase/tail observations without
changing the original gate calculation or replacing missing data with zero.

The required full matches through the 563-test source remain nonterminal. Further
recorded states expose two concrete lifecycle problems. Source ranking compares
full targets even for cargo already above its dispatch requirement, which can
send a truck past an adequate nearby depot. Rank source coverage by the existing
50% Fuel / 25% Ammunition deficits; six new cases cover both cargo boundaries and
a nearby source above/below the required minimum. The two observed unnecessary
far-source choices fail before correction. Physical loading still performs the
actual transfer and keeps original cargo targets.

A replacement scout can reuse a lower entity index and displace an existing
forward reconnaissance assignment. Preserve the existing reconnaissance combat
intent before applying the deterministic entity tie-break. Idle reconnaissance
selection uses that same reserved scout, so a second idle scout cannot draw away
the active escort. Two real index/generation reuse cases fail before correction
and cover multiple strategic decisions. No new authoritative state/schema.

The route-stage test distinguishes actual vanguard arrival from attack admission.
An attacking tank already inside the existing 180 m staging radius records route
progress before recovery, while the unchanged readiness, force-size, pressure and
supply gates still decide whether a new attack can issue. Five cases include no
attack intent, infantry-only arrivals, rear reinforcements and low Fuel. Three
positive arrival cases fail before correction. All 568 Game tests pass, zero
failures/skips, 96.242 seconds; the unchanged full-match gate is still running at
this checkpoint. No terminal result is claimed.

The earlier all-processor and electronics-only priority variants remain excluded.
The strict staging-quorum experiment passes 559 tests but does not change the
real failed match, and is excluded. Retrying prospective power planning on the
FIFO source fails the existing east 40,000-tick strategic test and remains
nonterminal at 80,000 ticks; it too is excluded. No deadline is relaxed.

Master has subsequently advanced through PR #239 (logistics setup UI), with
fixture 6 and product version 0.1.404. Integrate it before final qualification;
the next PR version must be exactly 0.1.405. Fixture-5 results above retain their
actual source identities and are not represented as fixture-6 acceptance.

### Current integration checkpoint (fixture 6, product 0.1.405)

Master eee4b603 (PR #239, logistics setup UI) is integrated at ad491690.
Product 0.1.405 is one PATCH above master 0.1.404. The full combined solution
build/test is in progress; the prior 568-case Game result predates that merge.
The vanguard source also fails the unchanged 80,000-tick terminal gate: Active /
Running, no winner. Its report, telemetry, save and log are retained, not counted
as acceptance. Hosted CI 39141378113 at 7c2988d5 fails the same full-match step;
CPU scalability, frame contracts and navigation/movement pass on that source.

The retained compressed evidence reproduces all 60 paired native images exactly
and preserves every performance failure from all five protocols. Fixture-5 p99
still fails once (+17.65% with a 15% limit); fixture-6 performance remains pending.
The user confirms AC operation with the performance profile. The idle 20 W reading
therefore remains an observation, with no proven causal attribution and no system
setting changes. Parallel release remains false and this PR remains draft.

### Reconnaissance with no available replacement escort

An isolated timeline observer records the fixture-6 source at fixed 2,000-tick
intervals. It uses seed 2026 and debug capture but leaves simulation diagnostics
disabled; it is exploratory evidence, not the required terminal qualification.
An orphaned reconnaissance route can keep advancing after its escort is lost.
A physical escort-destruction regression demonstrates 57.29 m of unescorted travel
within 100 ticks. With no eligible replacement, pause the scout at its current
position until the existing escort selection can assemble a supported route.
An available replacement keeps the previous pursuit behavior. No new component,
resource grant, readiness/cargo threshold or strategic deadline is introduced.

The broader unconditional pause passes the supply cases but fails two existing
strategic deadlines in the complete 570-case Game suite; it is excluded. The
refined condition passes all 82 supply and strategic tests (zero failures/skips,
2m39.938s), including those exact deadlines. The full Game rerun and unchanged
80,000-tick terminal check remain pending. Their eventual result must not be
inferred from the focused pass. Parallel release remains false.

### Completed local integrated and final-Game tests

The combined fixture-6 source corresponding to 7e485bf9 (the build preceded that
checkpoint commit, with the same source and product 0.1.405) completes the full
solution: 2,775 passed, zero failed, 17 explicit optional skips, 2,792 total,
25m15.321s. The build has zero warnings/errors (1m28.09s). Required source-contract
checks retain the original ownership and version rules.

The subsequent runtime source 25f40607 passes all 570 Game tests, zero failures or
skips, 3m41.713s, after the independent 82-case supply/strategy pass. This is an
affected-suite rerun after the final AI change, not a second complete-solution
run. Its separate headless build also has zero warnings/errors. The full-match
and fixture-6 native protocol are still pending at this checkpoint; they are not
implied by the green component/solution results.

### Fixture-6 native overlap and isolated protocol

Runtime source 25f40607 also fails the unchanged terminal gate locally and in
hosted CI 39147411406: Active / Running at 80,000 ticks, no winner. Frame contracts,
CPU scalability and navigation/movement pass; optional native-reference is skipped.
The complete local report, telemetry, save and log are retained.

The first fixture-6 B-A-A-B protocol, foundation-ui-pair, has four and three
performance failures respectively. Foreign native UI captures overlap both pairs,
confirmed by request/output timestamps and observed client process arguments.
The sanitized overlap record is retained. These measurements remain available,
but cannot qualify isolated source attribution. No foreign process was stopped.

Following user-authorized coordination, a separate fixed B-A-A-B protocol runs with
128 warmups, 512 samples, two exact pixel captures per case and external process
monitoring every two seconds. All 165 observations across four matrices have zero
foreign matching clients/headless/test workloads. Activity shorter than the polling
interval and unrelated operating-system activity are not excluded by this monitor.
The exact orchestration script and raw process observations are retained.

The isolated first pair fails only MBT Strategic medians: CPU 19.3791 -> 22.615 ms
(+16.6979%) and GPU 16.857248 -> 21.008224 ms (+24.6243%). The CPU mean/p95/p99 all
pass. The reverse-order second pair passes all CPU/GPU statistics and all six cases.
The unchanged 15% gate therefore remains open; the passing cohort does not override
the failures. No repeat-until-pass selection or threshold change is used.
All 84 paired images across seven complete protocols are exact; per-frame texture,
descriptor and upload plateaus and device health remain valid.

A separate temporary fence-timeout logging build captures one MBT Strategic case
under concurrent UI load. It records 512 samples, exact repeats, waits up to
147.5865 ms and no false-return timeout log entries. The source is 25f40607 plus
the retained diagnostic patch, not the unmodified qualification binary. The original
runtime source was restored byte-exact after this build. This experiment does not
qualify performance or establish a cause for every long wait. The original wait
polling interval and 10-second failure deadline remain unchanged.

### Mature cargo capacity investigation

The expanded-network regression reproduces the bootstrap cargo ceiling with a
complete four-stage processing chain and additional depots: the existing four
trucks never request more freight capacity. A bounded additive-demand variant
passes both focused cases but fails the existing eastern 40,000-tick offensive
objective test in the full suite (570 passed, one failed, no skips). That broad
variant is excluded and its runtime patch and full results are retained. The next
candidate preserves the existing assault and mature-supply minima before expanding
cargo capacity. It remains unqualified until its complete suite and terminal test
finish. Parallel release remains false and PR #237 remains draft.

The user's explicit scope extension, “Gameplay-Ursache hier mitbeheben”, also
covers the necessary production-policy regression test. Its exact path is added
to the serial foundation ownership list; parallel implementation remains disabled.
No additional production workstream is released by this ownership update.

The refined source is committed as fb6a0be6. It requires the existing scout,
objective-pressure tank, artillery and mature supply-truck minima before the
additional independent cargo routes can increase the target (hard ceiling eight).
Three focused queue cases pass: baseline logistics recovery, full industry without
an established assault fleet, and mature independent-route demand. The complete
Game suite passes all 572 cases, zero failures/skips, 2m36.487s. Its build precedes
the checkpoint commit with identical runtime/test source; the retained provenance
record distinguishes this from the freshly built headless binary. The headless
build has zero warnings/errors (59.16s). The strict terminal run is now pending.

The strategic GPU time series has two descriptive duration groups in every side
of both isolated pairs. At a descriptive 20ms split, first-pair baseline/repair
have 267/243 fast samples and 240/267 slow samples. Their group means remain close:
14.320/14.350ms and 23.265/23.169ms. The reverse pair also contains both groups.
Thus a small change in group proportions can move the median across the two
clusters. This describes the retained samples; it does not prove a hardware or
scheduler cause, remove samples, change the gate or turn a failure into acceptance.
The CPU Prepare/Record means decrease in the first pair (0.5240/0.8797ms to
0.3640/0.7225ms); elapsed frame time includes GPU/frame reuse waits. No additional
repeat of unchanged binaries is selected to obtain a passing median.

The fb6a0be6 full-match check also fails: 80,000 ticks, Active / Running, no winner.
Actual cargo completions rise from 224 to 312 and deliveries from 22,926 to
30,997.506878 units, but this is not terminal acceptance. The complete report,
save, telemetry and log remain retained. The western scout 83:2 is paused at
its own position with 0.307303 fuel, above its 0.18 continuing-reconnaissance
resupply threshold but below its 0.40 departure threshold. No resupply order or
movement group exists. A second healthy scout remains unassigned. The saved policy
and orders are retained in a focused endpoint record.

A physical regression uses a settled world, a 30%-fuel waiting scout and a supply
truck introduced 300m away. The unchanged source leaves it stranded at exactly
30% after 400 ticks; a continuing scout case passes. Earlier test setups placed
the provider in passive-transfer range and therefore did not isolate the decision
gap; they are not counted as before/after behavior qualification. The candidate
uses the existing departure threshold for paused/no-order reconnaissance, while
continuing missions retain their original threshold. No new numerical readiness
threshold or resource grant is added. Full-suite and terminal results are pending.

The first departure-recovery candidate changes the recovery classification but not
any configured numerical threshold. Its failure diagnostic at 400 ticks already
shows HoldPosition, the 0.46 policy and an active resupply order; the provider has
travelled toward the scout but remains outside transfer range. A 300m truck trip
needs more than 23s even at authored maximum speed, so the new physical test uses
one minute for travel and transfer (no existing qualification deadline changes).
Both original and candidate sources eventually transfer within that longer window.
Consequently, eventual fuel alone is not a discriminating regression. The test
must also observe timely explicit provider assignment after the existing planning
cadence; the long transfer window remains a separate physical follow-through.
The successful long-window before run is retained and is not relabelled as a
before failure. The candidate full Game suite is running before any test edits.

The exact departure regression now checks provider assignment within two existing
planning cadences, then allows a physical minute for the 300m trip and transfer.
Unchanged runtime fb6a0be6 fails the assignment assertion; the continuing-scout
control passes. The candidate passes both cases and real transfer (5.106s).
Its complete Game suite before that final additional assertion passes 574 cases,
zero failures/skips, 2m10.079s. The final assertion-only test refinement is rerun
against both runtimes; no runtime code changes follow the complete-suite run.
The fix is committed as fc86ce1e. Its strict 80,000-tick terminal check is pending.

The fc86ce1e strict match also remains Active / Running at 80,000 ticks. It delivers
31,005.006878 cargo units in 310 completed trips. The previous paused scout now has
0.920175 fuel, demonstrating that departure recovery occurs in the real run, but
that does not prove terminal completion. Both command cores remain at full 1,200 HP.
The complete report/save/telemetry/log are retained; no finish is forced.

The new endpoint also has loaded supply truck 194:3 actively rescuing own combat
unit 605:1 while every alternative truck is loading. The admission helper rejects
all trucks carrying SupplyRescueAssignment, even when that is real ongoing support
of the same combat force. A targeted regression is being prepared with logistics
and reserved-recon rescue controls; it is not yet an accepted runtime change.
The built local Client and Headless binaries both identify source fc86ce1e in their
full informational version. Client build: zero warnings/errors, 55.69s. No new GUI
start is performed during the other checkout's ongoing native captures.

### Active combat rescue and offensive support

The exact regression issues a real ResupplyCommand for a fuel-starved unit and
verifies its accepted provider and SupplyRescueAssignment after 12 ticks. Three
healthy tanks preserve the original combat admission minima. On fc86ce1e, active
own-tank rescue is incorrectly reported as missing forward supply; cargo-only and
reserved-recon rescues are correctly excluded. The candidate passes all three
cases (1.597s). It accepts only an assignment to a current owned combat unit with
a matching live ResupplyOrder, excluding the reserved scout. Loading/self-refuelling
trucks remain unavailable. All existing cargo and propulsion admission minima stay
unchanged, and MaintainForwardSupplySupport continues to leave rescue movement
untouched. The full Game suite and strict terminal run remain pending.

The active-combat-rescue runtime passes all 577 Game tests, zero failures/skips,
2m6.572s, after the independent three-case before/after regression. It is committed
as e0481eda; the test build precedes that checkpoint with identical runtime/test
source. No other runtime change follows that complete-suite result. The unchanged
strict terminal run on e0481eda is pending. This checkpoint does not grant parallel
release or native/performance acceptance. PR #237 remains draft.

The e0481eda terminal check remains Active / Running at 80,000 ticks with both
cores at full HP. Cargo completions/delivery are 296 / 29,164.5 units. The complete
failed run is retained. All western tanks in this endpoint are holding for fuel;
this is not corrected by merely recognizing active support in admission.

### Formation-bound providers between local movement orders

Existing SupplyRescueTravel.HasUnrelatedMovement already protects a provider with
an unrelated active movement intent. An initial test prototype therefore does not
establish a missing moving-provider guard; its same-group expectation conflicts
with that existing protection. The narrower regression models a live formation
awaiting its local route, with membership retained and no individual movement order.
On the unchanged source it incorrectly borrows that provider, including when an
uncommitted alternative is available (two failures, three passing controls).

The candidate treats membership in another live formation as ProviderBusy for
out-of-range reassignment. Same-group support and in-range transfers remain allowed;
existing unrelated-movement protection still applies. It introduces no component,
serialized state, numerical stock threshold or fleet increase. All five focused
cases pass (0.479s); the complete Game suite is pending. Under the user's existing
Gameplay-Ursache scope extension, BattlefieldResupplyPlanner.cs and its existing
ResupplyPlanningDiagnosticsTests.cs are added to serial foundation ownership.
Parallel release remains false.

The formation-provider candidate completes all 582 Game tests with zero failures
or skips (2m11.505s), after five focused cases reproduce two original failures and
pass after repair. It is committed as 728d5a1f. The build precedes the checkpoint
commit with identical runtime/test source. Its strict 80,000-tick terminal run is
pending. On pushed checkpoint 2d90cabe, frame contracts, CPU scalability and
navigation/movement pass; the main hosted build-test job is still running. These
checks do not replace qualification of the subsequent runtime source.


### Continuation, observation and subsequent hosted allocation failure

The 728d5a1f strict terminal check fails at 80,000 ticks, Active / Running,
with both cores at 1,200 HP. A separate read-only role observer reaches exactly
its state hash 08D895823770565ABBD530D4CBE07F4ABCC9C22ABC41FEBA0A5BAEF1A1FFE4FD.
The complete role timeline and saved state are retained. It is a causal diagnostic,
not a timed qualification. The actual first western reconnaissance escort 194:3
leaves its group at tick 28,451 with 54.9510515 fuel cargo and 120 ammunition;
the scout subsequently pauses at tick 28,991. Launch reserves should not be
applied as optional top-ups during an otherwise supported committed mission.

The broad continuation variant passes its focused cases but fails the eastern
40,000-tick offensive deadline (587 passed / one failed / 588 total). It is
excluded. Source 85285f78033f72577a4c4bdab1447eaf6db28992 limits continuation to
DeepOffensiveCommitted, keeps actual combined scout/provider resupply coverage
and retreat checks, and passes all 590 Game tests without skips or failures
(1m51.343s). Its strict terminal check still fails, Active / Running at 80,000
ticks. Western DeepOffensiveCommitted is true; eastern is false. This result
does not justify broadening the gate or weakening the terminal requirement.

Hosted main run 39155623741 on pushed source 2d90cabe fails in the test step:
RepeatedProductionStateResolutionDoesNotAllocateAssetNamesPerFrame(logistics_hub)
reports 3,968 bytes against its unchanged zero-byte expectation. Frame contracts,
CPU scalability and navigation/movement pass. The original 13-case allocation
test passes locally, so attribution to catalog runtime allocation is unproven.
Its steady-state resolver already uses precomputed names. The candidate moves
measurement to an explicitly awaited dedicated thread, suppresses inherited
execution context, and warms both resolver and allocation counter outside the
unchanged 1,000-call measurement. All 26 building presentation cases pass.

A complete Presentation module then exposes another zero-byte assertion in
UiTextBatchTests (3,368 bytes, one failed / 1,302 total / 12 optional skips).
It receives the same measurement isolation, retaining all 128 measured frames
and its zero-byte budget. The subsequent complete module passes 1,290 cases,
zero failures, 12 optional skips / 1,302 total (76.150s), build zero warnings
and errors. These local results require new hosted CI; they do not prove the
original allocation's stack origin. The two exact existing test files are
added to serial foundation ownership under the authorized CI-repair scope.

### Misordered arrival blocks its own physical dock evacuation

The 85285f78 endpoint provides a different long-lived physical blocker: all
three western supply trucks wait at depot 216:1, with loading orders accepted
at ticks 51,881 / 53,371 / 52,701. Its reserved owner 194:3 is blocked near the
entry by queue position three, 191:2. That arrival is physically ahead of its
predecessor and has a valid coarse route sideways to its assigned tail place.
It has fuel and no tactical or suppression movement prohibition. The queue's
no-passing step gate prevents the very first inward-facing steering step and
never permits it to turn out. Three physical movement regressions reproduce
zero movement on unchanged runtime at 10/20/60 Hz with the actual vehicle size.
A simplified unit-size prototype does not reproduce the real footprint case.

The candidate permits a misordered arrival's route intent away from the queue
axis while preserving FIFO membership, normal physical collisions and the
exclusive admission gate. A candidate checking only the immediate lateral step
still fails all three cases because the necessary initial turn faces inward;
it is excluded. The intent-based candidate passes all 79 ground-movement,
physical-queue and dock tests (3.121s). Complete Game and terminal qualification
are pending. VehicleDockQueues.cs and the existing GroundMovementSystemTests.cs
are included in serial ownership under the authorized gameplay repair scope.
All earlier failed gates remain retained; parallelReleased remains false.


The queue-evacuation runtime passes all 593 Game tests, zero failures/skips
(100.802s), including the original strategic deadlines. It is committed as
177d029e9d29469560e0337a16d25cb2651f56dd. The immediately preceding test build
has identical runtime/test source (format-only UTF-8 BOM removal follows).
The allocation-only checkpoint is d1032fde; its complete local Presentation
module result is recorded above. The current strict terminal run is pending;
no previous terminal failure or isolated performance failure is superseded.


The 177d029e strict gate also fails at 80,000 ticks (Active / Running), despite
the confirmed physical queue repair. Western strategic state reaches Attacking /
AttackObjective and eastern reaches Defending with one remaining combat unit;
this is progress, not a completed lifecycle. Total fuel transferred is
15,304.37297807682; delivered freight is 28,630 over 289 completed orders.
The full report/log/telemetry are retained. The additional old-save continuation
diagnostic was aborted: Restore reconstructs from tick zero and verifies the
prior state hash; it cannot directly continue that snapshot under changed code.
No hash validation was bypassed and no continuation success is claimed.

Master advanced to 617312bbf24314a1ee846d0d6096f85ef366ea26 (PR #240, logistics
onboarding) and is merged without conflicts. Its observational progress journal,
guidance changes and reference fixture seven are preserved. Product version is
now 0.1.406, exactly one PATCH over the new master's 0.1.405, replacing the prior
baseline-relative 0.1.405 increment. Earlier fixture-six native evidence remains
valid only for its recorded sources; it does not qualify this changed fixture.
Fresh integrated tests and a strict terminal run with an explicit save output
are required. The prior failed performance gate remains open.


### Current merged-master qualification and attackable-contact repair

Integrated source ae6865b005c074e79e96d7c0fcd18d529c4ba25d builds the complete
solution with zero warnings/errors (1m47.97s). Its complete serial-module suite
passes 2,810 tests, zero failures, 17 explicit optional skips / 2,827 total,
17m13.059s. Its unchanged strict 80,000-tick run still fails Active / Running;
both command cores have 1,200 HP. This run includes an explicit authoritative
save, retained for endpoint diagnosis. No save/replay verification is bypassed.

The endpoint shows western tank 143:159 at (2432.6096, 7.429023, 1282.683),
health 1, fuel 0.8516721487529358 and ammunition 1, repeatedly ordered to attack
power plant 95:2. That current identified contact has no Combatant, HealthState
or Targetable component. TacticalTestOpponentSystem allows that sensor contact
to replace a valid strategic advance; TacticalCombatSystem then correctly clears
the weapon target, prohibits movement and waits for legitimate intelligence.
Its last fire-ready tick is 69,257; the invalid order persists at tick 79,991.

The candidate applies the existing combat-target contract before an identified
contact can override strategic movement: living hostile combatant, non-depleted
health, target class, transform and compatible source weapon. This check occurs
only after current identification resolves the entity. Detected-only contacts
retain their coordinate behavior; no hidden information, building health,
weapon range or artificial damage is introduced. Both exact existing tactical
files are added to serial ownership under the user's gameplay repair scope.

Eight cases reproduce the original wrong target or lost strategic advance.
An initial additional damage assertion was unsupported by the pure tactical
fixture (four such assertions fail although target selection is corrected).
The final identical before/after fixture registers normal combat execution and
damage resolution: unchanged runtime fails all eight cases; corrected runtime
passes all eight, including real damage to a legitimate alternate target and
preserved strategic advance when only non-combat infrastructure is identified.
The complete updated Game suite passes all 605 cases, zero failures/skips,
105.018s. The strict terminal check for this candidate remains pending.

### Fixed fixture-seven B-A-A-B performance qualification

The new-master protocol compares unmodified 617312bb against captured renderer
source ae6865b0, both with reference fixture seven and identical runtime assets.
Both binaries are frozen with hashes. The predeclared protocol uses all six
original world cases, High 1920x1080, 128 warmups / 512 samples / two readbacks,
in B-A-A-B order. It starts only after local tests and headless work finish.
All four native matrices complete. Both per-cohort performance gates pass every
CPU/GPU mean, p50, p95 and p99 comparison at the unchanged 15% threshold, with
zero unqualified cases. The largest observed increases are 6.078949979936744%
and 9.846532142528737% respectively. All twelve paired images are exact, with
stable resource plateaus and healthy devices. The external two-second monitor
records 172 observations and no matching foreign workload; shorter activity
and unrelated OS work cannot be excluded.

This qualifies the captured renderer against the changed current master; the
older failed fixture-six comparisons remain intact and are not relabeled green.
The added tactical-contact candidate is a subsequent Game-only change; the
native manifest continues to identify ae6865b0, not an unmeasured later source.
The independent human review, final-head CI and terminal gate remain open.
The complete-summary reproducer includes all eight protocols and all 96 exact
paired images, preserving every historical failed gate. Parallel release
remains false until all required foundation gates are accepted.
