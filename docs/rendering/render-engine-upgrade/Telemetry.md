# Render-owner telemetry foundation

Execution baseline: remote:origin/master at
f34d695fcaa813616da8747a49d030e418081e04. This package adds observations;
rendering policy, quality, asset layouts, frame count, simulation authority,
resource budgets and fence deadlines are unchanged.

## Actual live stream

FORGELINE_RENDER_TELEMETRY_OUTPUT explicitly enables a bounded stream on the
actual ClientRenderHost render owner. FORGELINE_RENDER_TELEMETRY_WARMUPS and
FORGELINE_RENDER_TELEMETRY_SAMPLES predeclare counts (defaults 64 and 512;
maximum 4096 warmups; samples plus warmups, 4096 reserved startup observations
and eight in-flight completions must fit a total budget of 65536). Recording arms at the first gameplay
iteration, discards exactly the declared number of consecutive owner iterations,
then retains exactly the declared number of consecutive iterations. Loading,
suspended and terrain-pending observations before this window are retained in
a separate prefix sized to 4096 startup entries plus the declared warmups.
More than 4096 pre-arm startup observations explicitly invalidates the run.
Unsupported aggregate settings are rejected before allocation instead of being
silently clamped; 4096 warmups permit at most 57336 samples. Those states inside the measured interval
remain measured states and fail gameplay-window validation; they are not skipped.
Completed windows stop observation and native completion admission at the actual
last submitted queue fence. Later gameplay cannot overwrite or overflow them.
Uncompleted windows and any bounded-storage overflow stay invalid.

The CPU envelope starts after the deliberate idle mailbox wait and before mailbox
acquisition, surface/camera/snapshot/input-geometry/audio work. It ends after
submission, retirement, existing diagnostics, qualification publication and
production logging. Platform event processing and simulation execute on their
independent owners and are explicitly excluded: this is the complete active
render-owner iteration, not the sum of all application thread CPU time. Prepare,
renderFrame, present and fenceWait retain actual monotonic intervals; overlap is
allowed and must not be added. Process-wide precise GC and current-thread
allocation observations conservatively bracket the same work; their counter
reads and telemetry storage/serialization are separate observer work. Serialization
happens after execution, not inside a measured frame.

The host emits kind=live-render-owner-telemetry, an independent stream ID, actual
assembly source revision, fingerprint of the executing runtime payload, native
adapter description, raw frames/completions, prefix, exact window and overflow.
Runtime hashing and all fixed storage allocation happen before measured work.
An optional FORGELINE_RENDER_TELEMETRY_CONTEXT JSON must match actual source and
runtime before execution. It cannot turn an ordinary live match into a native
reference fixture. nativeFixtureBindingAvailable remains false.

After building Release and runtime assets, a fresh evidence path can be used:

```powershell
$priorOutput = $env:FORGELINE_RENDER_TELEMETRY_OUTPUT
$priorWarmups = $env:FORGELINE_RENDER_TELEMETRY_WARMUPS
$priorSamples = $env:FORGELINE_RENDER_TELEMETRY_SAMPLES
try {
    $env:FORGELINE_RENDER_TELEMETRY_OUTPUT = [IO.Path]::GetFullPath('artifacts/render-live-new/owner.json')
    $env:FORGELINE_RENDER_TELEMETRY_WARMUPS = '64'
    $env:FORGELINE_RENDER_TELEMETRY_SAMPLES = '512'
    dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000
    if ($LASTEXITCODE -ne 0) { throw 'Live telemetry smoke failed; retain its original output.' }
} finally {
    $env:FORGELINE_RENDER_TELEMETRY_OUTPUT = $priorOutput
    $env:FORGELINE_RENDER_TELEMETRY_WARMUPS = $priorWarmups
    $env:FORGELINE_RENDER_TELEMETRY_SAMPLES = $priorSamples
}
```

With telemetry explicitly enabled, existing smoke completion waits for this fixed
window; its one-minute failure limit and ordinary disabled behavior remain intact.
This is the existing bounded smoke fixture and its existing completion behavior,
not a natural match, 1000 active-unit battle or performance acceptance run.
Use qualification.validate_live_owner_telemetry on this actual output for strict
structural validation. A valid result explicitly reports nativeFixtureAcceptance
open. No command/snapshot is relabelled as IndustrialBase or another native scene.

## GPU and resource observations

IGraphicsRenderTelemetry is an optional owner-thread source; existing
IGraphicsDevice and command contracts are unchanged. Raw readback is admitted
only after a real completed nonsentinel direct-queue fence. Device generation,
resource generation, actual submitted/completed fence, frequency and original
frame/world/composite/overlay/shadow/particle ticks are retained. CPU submission
ordinals are distinct from queue fences (uploads also consume queue fences).
Live records join exact device/resource-generation/fence tuples, including delayed
completions. The bounded queue retains out-of-order completions without duplicate
submission acceptance. Endpoint errors or overflow invalidate evidence.

Native reference captures retain their original derived series and schema 2;
rawGpuTiming is additive. Every serialized new raw series is strictly validated
when present, including duration agreement with existing derived frames. Older
manifests remain readable as historical evidence without acquiring missing raw
proof. Raw world intervals include shadow work; the existing derived world value
subtracts shadow time. Neither pass series is a disjoint timing partition.

Logical data-resource accounting includes wrapper buffers/textures/readbacks,
internal swap-chain/scene/depth/shadow targets, constant/readback buffers, retained
upload staging, descriptors and actual outstanding unsubmitted/inflight upload
ranges. Live and pending-retirement populations are disjoint. Pending upload bytes
come from batch state/fences; cumulative upload traffic is not reused as a plateau.
Texture mip payload can include a pending release until its actual fence.

PhysicalAllocationBytes stays null: native heaps, descriptor/query/command and
driver allocation sizes are not exposed by logical data-byte counters. DXGI
process usage, adapter declared capacity and logical bytes remain separate.
These observations do not establish complete physical GPU-memory acceptance.

## Collector trust and physical hardware seam

Historical ParallelRenderOwnership.json and its release/ancestor checks remain
unchanged. -InitiativeOwnershipPath selects the additive Ownership.json only with
an explicit OwnershipBaseSource, a committed byte-identical record in the selected
AnalysisMasterSource, master ancestry, valid package waves/disjoint ownership and
scoped diff checks. Directory.Build.props remains a serial version-only exception.
This package cannot self-approve its new ownership record: initiative acceptance
remains unavailable until the record is accepted on master. Standalone structural
ownership checks grant no integration or runtime approval.

New cohorts retain Git-command evidence for each exact source global.json and
matching source-less package SDK provenance. The earlier validator hardcoded SDK
10.0.401 despite the current repository pin having moved; new sourcePins remove
that discrepancy without hardcoding the replacement. Historical cohorts without
this evidence retain their original qualified pin and limitations.

HardwareObservationPath, HardwarePolicyPath and ExpectedHardwareIdentitySha256
are an ingestion seam, not a sensor implementation. The next package owns provider
acquisition. Samples must be actual physical temperature/clock/power/throttling
observations, explicit AC/battery status, exact adapter/driver/SDK identity, strictly
ordered timestamps, no gap above five seconds and a recent final sample. A common
reviewed policy must predate observations and provide physical bounds. Parse,
hash and retained base64 all derive from one immutable byte read and one acquisition
cutoff. Before/after summaries are recomputed from those raw bytes and bound by
the report to actual native identity and command interval. The after trace must
cover the complete command, including its beginning and final sample. Mismatch,
throttling, battery, invalid/future/stale data or missing provider cannot become
stable hardware evidence. No power or driver configuration is changed.

Bind-LiveTelemetry.py preserves original stream/context/command bytes and refuses
to promote the current ordinary live producer. Matched native-fixture live
acceptance remains OPEN until an actual measured workload identity/producer exists;
caller-supplied caseKey or fixtureFingerprint is insufficient. Strict final
qualification and all independent CI/budget/lifecycle/human-review gates remain.

The optional resource-memory struct flows directly from IGraphicsRenderTelemetry
into explicit native reference samples. It is not added to GraphicsDiagnostics,
so default production diagnostic objects retain their existing allocation size.

## Verification status

| Exact product source | Observed validation |
| --- | --- |
| dead0c5ad23d141b76f10718f75342d62afbe72c | All 17 canonical stages passed; Release build zero warnings/errors; 2055 clean runtime assets/contracts; native and headless smoke/stress; reproducible local publisher; full serial suite 4900 total, 4819 passed, 81 explicit skips, zero failed (28m35.987s). Natural seed-2026 match ended player-1 Victory/CommandCoreDestroyed at tick 59098, 343.382 seconds, within 80000. |
| ec4f961f2855bebf93774355407eb9c1931d0780 | Aggregate/prefix-budget and default-diagnostic-layout corrections: 20 focused Client/reference cases and 13 Graphics/native cases passed; both builds zero warnings/errors. |
| 9cf3491256e9ef18718a43af5611054465735f45 | Optional-context environment correction: 24 focused Client/context/window/reference cases passed; build zero warnings/errors. |
| 02dc24f9549d90748ad3771af54f54214db9b1ec | Observer/service attribution: 25 focused Client cases, 60 Python qualification tests and 36 PowerShell orchestration assertions passed; build zero warnings/errors. Actual live attempt 05 completed the unchanged 64-warmup/512-sample window with 512 CPU and 512 completed GPU observations, no overflow, successful CLI exit and strict diagnostic validation. |

The full canonical run belongs to its original exact revision; it is not relabelled
as later corrections. The final documentation-only commit changes no product code.
Final exact-source remote CI is observed separately after PR creation. No extra
PATCH is allocated against the unchanged origin/master base.

Canonical commands, exits and raw logs are retained under
artifacts/wave0-canonical-01/results.json. Native observer protocol artifacts retain
all four same-binary off/on/on/off cohorts (64 warmups, 256 samples each) under the
Graphics test output artifacts/render-telemetry-overhead directory. They measure
the native clear-frame observer, not whole live-host overhead or a game speedup.
Warm observer allocation guards remain zero. Independent read-only reviews passed
with the explicit remaining qualification warnings below.

## Retained live attempts and corrected diagnosis

All attempts use the declared 64 warmups, 512 samples and unchanged one-minute
smoke deadline. No samples, workload, image tolerance, fence timeout or CI gate was
reduced to obtain a pass. Every failed output remains in its original fresh folder.

- Attempt 01, source ec4f961f: a cleared optional context environment variable
  arrived as an empty string and was opened as a filename. Startup failed and
  produced no owner output. The later parser treats null/empty/whitespace as
  absent while preserving supplied-file source/runtime validation. Logs remain
  under artifacts/wave0-live-01.
- Attempt 02, source 9cf34912: startup succeeded but the deadline expired with
  217/512 CPU and GPU samples, no overflow and an incomplete window. The 217-frame
  span was 46.866 seconds. An initial comparison of median payload 10.535 ms with
  average cadence was invalid; it did not establish work outside the payload.
  The original data remains under artifacts/wave0-live-02.
- Attempt 03, source 02dc24f9: the unchanged deadline expired at 220/512 samples.
  The added intervals disproved the observer-gap hypothesis. Payload mean/p95
  were 218.848/738.430 ms; observer-inclusive service mean/p95 were
  218.896/738.471 ms, with inter-service gaps averaging 0.005 ms. Begin/end precise
  counter means were 0.010/0.023 ms, memory snapshot 0.011 ms and GPU drain below
  0.001 ms. Fence-reuse wait mean/p95 were 208.414/661.986 ms; Present p99 was
  approximately 486.29 ms. Actual GPU work mean/p95 was only 8.586/9.290 ms.
  Outstanding uploads were zero and logical bytes/descriptors constant. These
  observations locate the long tails in native reuse/presentation scheduling,
  not the small observer operations; they do not establish a driver cause.
  Frame-exact process counting was retained. Data remains under artifacts/wave0-live-03.
- Attempt 04, same source 02dc24f9: PTY transport alone also timed out. External
  read-only Win32 observations established Visible=true, Iconic=false and
  Foreground=false for the actual primary client window at the observed time.
  Cached focus was not proof of visibility or continuous foreground state.
  Data remains under artifacts/wave0-live-04.
- Attempt 05, same source 02dc24f9: the actual window completed after an activation
  attempt, with CLI exit 0 and strict diagnostic validation. Payload mean/p95
  were 21.505/30.960 ms; service mean/p95 21.758/31.599 ms; GPU mean/p95
  8.005/8.946 ms. Exact process and thread series each contain 512 samples.
  The activation API reported failure after approximately eight seconds, and
  the retry found the already completed client gone. Continuous foreground was
  not verified. Environment sensitivity remains uncontrolled; this successful
  structural collection is not a causal driver fix or selection of a winning
  performance rerun. All failed attempts remain alongside it under
  artifacts/wave0-live-01 through artifacts/wave0-live-05.

A separate ignored synthetic .NET 10.0.13 counter probe used eight allocation/
sleeping workers, eight warmups and 64 calls in a predetermined precise/
approximate/approximate/precise order. Precise medians were 0.004 ms, p95
0.0043/0.0106 ms; approximate median/p95 was 0.0001 ms. It did not reproduce the
actual native scheduling tails. No approximate process allocation counter was
substituted into live observations.

The observer retains fixed-storage BeginGC/EndGC/Memory/Drain and signal-wait
intervals. Service begins before the first precise process query and ends after
all observation/drain work; payload remains separately named. Reports expose
both distributions, operation costs and remaining inter-service gaps. Correct
means and tails, not a median-versus-average comparison, support diagnosis.

Initial implementation failures are also retained as actual failures: three
CA1822 data-property analyzer findings were corrected; a focused test invocation
before the final explicit build discovered no matching tests (exit 5), after
which the built cases passed. Neither attempt is counted as a passing result.

## Remaining acceptance

Native fixture/live workload matching, complete physical GPU allocation coverage,
controlled hardware clocks/thermals, previously reviewed common budgets,
30-minute moving lifecycle qualification and human motion/art acceptance remain
OPEN. The live stream is the real smoke render owner, including development
stress, not a natural battle or the native IndustrialBase fixture. Per-frame
process/thread counters are precise observations, not an approved allocation
budget pass. PhysicalAllocationBytes remains unavailable, not zero.

Exact-source remote build-test remains required before integration. No sustained
FPS, generic performance improvement, driver/synchronization repair, broader
hardware support, product release or Product Owner approval is claimed.
