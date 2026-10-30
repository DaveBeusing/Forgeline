# Render-owner telemetry foundation

Execution baseline: remote:origin/master at
f34d695fcaa813616da8747a49d030e418081e04. This package adds observations;
rendering policy, quality, asset layouts, frame count, simulation authority,
resource budgets and fence deadlines are unchanged.

## Actual live stream

FORGELINE_RENDER_TELEMETRY_OUTPUT explicitly enables a bounded stream on the
actual ClientRenderHost render owner. FORGELINE_RENDER_TELEMETRY_WARMUPS and
FORGELINE_RENDER_TELEMETRY_SAMPLES predeclare counts (defaults 64 and 512;
maximum 4096 warmups and 65536 samples). Recording arms at the first gameplay
iteration, discards exactly the declared number of consecutive owner iterations,
then retains exactly the declared number of consecutive iterations. Loading,
suspended and terrain-pending observations before this window are retained in
a separate fixed 4096-entry prefix. Those states inside the measured interval
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

## Verification status

During implementation, Client and Client.Tests Release builds passed with zero
warnings/errors. Focused live tests initially passed 4/4; the fixed warmup/window
case was subsequently added and requires the final rerun. Graphics telemetry tests
passed 13/13, including real upload-fence separation, raw timing, logical bytes,
image parity and a same-binary off/on/on/off protocol (64 warmups, 256 samples per
cohort). Full raw overhead observations are written under artifacts/render-telemetry-overhead.
They describe the native clear-frame observer, not complete live-host overhead or
clock/thermal-controlled game speedup. Warm observer allocation guards remain zero.
Python qualification tests passed 59/59 after the new integrity counterexamples.

Initial attempts are retained as actual failures: three CA1822 data-property
analyzer findings were corrected; an early focused test invocation before a final
explicit build discovered no matching tests (exit 5), then the built tests passed.
Full canonical validation, exact clean-source live smoke/export validation,
independent final review and source-specific remote CI are separate required
execution records. No sustained FPS, complete physical memory, human art approval,
matched native/live fixture or hardware performance acceptance is claimed here.
