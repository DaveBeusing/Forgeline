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
