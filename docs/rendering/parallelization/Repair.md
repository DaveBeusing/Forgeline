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
