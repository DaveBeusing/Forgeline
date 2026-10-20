# Remaining native evidence integration

## Decision

Keep the frozen product interfaces and existing reference tools intact. The
qualification collector consumes their real exports and marks missing required
observations as open. Complete acceptance requires the serial integration owner
to bind additional measured records and independent gate results to exact source,
runtime and hardware identities. No synthetic zeros, pass-time addition or
historical data substitution can close these gaps.

This document describes a required extension, not an implemented product API.
The strict supplemental record validators already exist in `qualification.py`;
the current native capture entry point does not produce these complete records.

## Enclosing live-client frame

The native reference's measured CPU interval includes terrain preparation and
`RenderFrame`, including native Present/frame-reuse work. It does not capture the
whole live gameplay host, event processing, camera work and surrounding update
cost. `RenderAssetFrameMetrics` reports scoped phase observations; its Submit and
Retire fields are CPU bookkeeping and do not substitute for native wait/present.

The serial producer must observe the full interval at the host owner boundary
and export `kind: frame-envelope`, `scope: live-client`, a positive CPU
`frequencyHz`, ordered enclosing `startTick`/`endTick`, and the exact required
phase names from the validator. Each phase interval must lie within the enclosing
frame. Phases may overlap; only the enclosing tick difference determines the
total. Include process-wide and measured-thread allocation deltas over that same
interval. Bound recording storage before measured work so the observer does not
introduce an unreported allocation path.

The phase names are `prepare`, `host`, `renderFrame`, `present` and `fenceWait`.
Declare `gpuStreamScope: independent-live-client` and a nonempty `streamId`;
the separate live run cannot reuse the native fixture's fence namespace as if
those measurements came from the same submitted frames. Retain its actual
command, source, duration, exit code and hashed stdout/stderr.

Each record is bound to `sourceSha`, role, pair, native case key, runtime and
fixture fingerprints, and the actual hardware identity digest. It contains the
same number of measured frames as its native case. The cohort adds a `{kind,
path, sha256}` supplemental reference without rewriting the original native
manifest. A record outside the matching source/case, repeated frame indices,
out-of-envelope phase, unequal sampling count or process allocation smaller than
its included thread allocation must fail.

Relevant frozen files requiring explicit serial coordination:

- `src/ForgeLine.Client/ClientRenderHost.cs`
- `src/ForgeLine.Client/VisualReferenceCapture.cs`
- `src/ForgeLine.Client/ReferencePerformanceSamples.cs`
- `src/ForgeLine.Presentation/RenderContracts/RenderAssetFrameMetrics.cs`

## Raw completed GPU timestamps

The current native manifest preserves derived GPU durations, sample fences and
timestamp frequency. It omits the original begin/end timestamp values and an
explicit completed-fence value for each readback. Distinct derived fence IDs are
a useful integrity check; they cannot prove the omitted observations.

Add optional per-frame GPU observations with queue identity, device/resource
generation, submitted fence, completed fence, raw start/end ticks and the actual
queue timestamp frequency. The readback must have `submittedFence <=
completedFence`, a valid nonsentinel fence, and an increasing unique submission
within its own stream/generation. A new device can restart fence numbering;
separate pass observations of the same submission are not duplicate whole-frame
samples. Missing GPU data remains null and cannot satisfy hardware acceptance.
Delayed particle/pass samples must retain the fence they actually describe.

Relevant frozen files include `src/ForgeLine.Graphics/D3D12GraphicsDevice.cs`,
`src/ForgeLine.Graphics/GraphicsDiagnostics.cs` and the client reference exporter.
The current owner-thread, queue, readback and retirement contracts remain binding.
No additional queue, GPU layout change or render/global idle is implied.

## Moving lifecycle and complete memory accounting

Capture an actual 30-minute workload for both baseline and candidate, with a
retained script hash, at least two load, unload, session and resize transitions,
and at least 30 distinct timed camera states. A stationary image cannot qualify.
At least five time-ordered, equivalent settled checkpoints must extend to the end
of the soak. The validator conservatively flags any later settled counter above
the initial comparable checkpoint; it does not invent slack for unexplained
growth or discard peaks.

Camera changes must span the entire soak with no observation gap over 60 seconds;
session and resize events must identify different actual states. Declare
`checkpointPolicy: same-workload-after-warmup-no-exclusions` and
`counterSemantics: live-or-outstanding-at-settled-checkpoint`. Retain the script
itself as hashed evidence and the actual command record.

The record needs `liveResourceCount`, `liveResourceBytes`,
`retiringResourceCount`, `retiringResourceBytes`, `textureMipBytes`,
`stagingBytes`, `descriptors` and `pendingUploadBytes`, according to the
validator's `MEMORY_COUNTERS`. These are live/outstanding checkpoint values;
cumulative uploads cannot serve as a memory plateau. Process versus thread
allocations are separate measured interval counters. Current
`GraphicsDiagnostics.Resources`, `Health`, `Memory` and the upload owner provide
some of these observations, but logical resident texture bytes and DXGI local
usage are different metrics. Resource count is not resource byte size. Missing
staging, retiring-byte or outstanding-upload fields cannot be reconstructed by
treating them as zero. Preserve actual raw checkpoints and temporal events.

Native dense-effect, Fixed, Sweep and Traverse reference cases remain useful
workloads. They do not replace live-session and resize instrumentation. Serial
coordination is needed across the client lifecycle and the graphics/resource
owner; no frozen producer is modified by the qualification change.

## Observed hardware conditions

The existing `Get-QualificationConditions` in
`build/rendering/qualification/Qualification.Commands.psm1` records the active
power plan and observable battery state. It always leaves
`thermalState: unavailable` because portable Windows CIM cannot establish
physical GPU temperature, clock or throttling stability. Machines without a
battery record also retain `powerSource: unavailable`; the absence of a battery
does not become a measured AC-power observation.

The current collector therefore cannot satisfy the hardware gate solely by
receiving a valid reviewed calibration file. That file is admission and identity
evidence; its claims do not override the conditions observed before and after
each run. The independent evidence imports cannot replace those observations.

Serial integration must provide a real hardware observation source bound to the
actual adapter and timestamps. Retain physical temperature, clock, power and
throttling observations over every native run, the raw acquisition logs and
previously reviewed stability bounds. Populate before/after conditions only from
those measured records. Failure or absence of that provider remains explicit
unavailability, including on a runner whose labels and capacity review are valid.

## Independent mandatory acceptance records

The report separates compiled content, physical GPU memory, allocations, native
reference timing, full live-client frame timing, raw GPU timestamp integrity,
lifecycle stability, human visual acceptance and required CI. A green capture
process is insufficient evidence for the other gates.

The validator implements strict imports for these independent gates. Every
imported record has `schemaVersion: 1`, its declared `kind`, `baselineSha` and
`candidateSha`, and is included through a `{kind, path, sha256}` reference in
`cohort.validationEvidence`. The record's file references are relative to its
directory; all referenced bytes are preserved. The collector does not invent a
passing result when a record is absent. Generic `{status: pass}` inputs are not
accepted.

| Imported kind | Required evidence and interpretation |
| --- | --- |
| `required-ci` | Both roles retain every original REST run page with `total_count` / `workflow_runs` and every original latest main-CI job page with `total_count` / `jobs`. Page counts and IDs must agree. The latest exact-source `ci.yml`, `frame-hotpaths.yml` and `scalability.yml` results are checked; missing workflows stay open. Main `build-test` must belong to that run, attempt and source and complete successfully. An older green run cannot replace a newer failure. |
| `content-budget` | Both roles retain the actual command/logs and a source/runtime-bound measurement whose metric is `compiled-gameplay-texture-container-bytes`, with integer `bytes` and unchanged `limitBytes: 58720256`. |
| `allocation-budget` | Both roles retain actual command/logs, source/runtime/hardware-bound `live-client-full-frame` observations, at least 64 warmup frames and 512 raw indexed samples of `processAllocatedBytes` / `threadAllocatedBytes`, and a previously reviewed common budget policy. |
| `gpu-memory-budget` | Both roles use the same command/identity/sampling contract and a previously reviewed common policy for every complete live/outstanding memory counter listed above. |
| `visual-review` | Explicit reviewer, UTC review time, rationale and accepted/rejected decision; every paired native case is bound to both exact image and manifest hashes, with visibility, geometry/material and motion decisions. Both source-bound live-animation media files, durations and review decisions are required. Fixed camera-pose images are insufficient. |

Budget policies use `schemaVersion: 1`, the matching kind, reviewer, rationale,
`reviewedAtUtc` and integer per-counter `maximumPerSample` values. The review
predates measurement; the identical policy file hash applies to both sources.
It must reflect the existing approved budget rather than values chosen after
looking at a failed candidate. Command records retain `sourceSha`, `executable`,
literal `arguments`, `startedAtUtc`, `elapsedMilliseconds`, integer `exitCode`
and hashed stdout/stderr. A failed command is a failed gate even when its output
file contains plausible numbers.

The remaining serial work is to emit the missing measured product data and bind
the actual CI, budget and review artifacts to these contracts. Hosted readiness
is only admission; its preflight summary is not itself a final required-CI import.
Absent or failed required records remain visible in final acceptance.

For compiled textures, retain the exact compiler/container metric and its
unchanged 56-MiB budget independently of GPU resident or allocated bytes. For
interaction allocations, retain complete-suite and isolated class observations,
warmup, TieredCompilation/GC settings, processor count and source SHA. Do not
weaken the zero-allocation expectation to explain a historical failed suite.

For visual candidates, retain native-resolution images and deterministic motion
sequences at identical scene/camera conditions. The review record must identify
the exact baseline and candidate, artifact hashes, reviewer, date and accepted
scope. Pixel equality and source-less packaging remain separate checks. Existing
golden files are not replaced automatically.

## Integration validation

Regression tests must reject moved Prepare/host costs, invalid clock frequencies,
non-finite intervals, repeated or incomplete GPU fences, mismatched source and
runtime fingerprints, fabricated unavailable values, shortened soaks, unmatched
settled checkpoints and missing mandatory gate inputs. Controlled positive tests
should use known raw intervals where phase times overlap, and independent device
generations legitimately reuse fence values.

Then execute the source-pinned complete solution, existing content/allocation
gates, source-less native matrix and moving lifecycle workload. Record all failed
attempts. A reviewed immutable baseline must remain unchanged; no timing
threshold, image approval or integration permission follows from these tests.
