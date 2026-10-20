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

## Exact compiled-content metric

At qualification source
`8d4748d3228c49e50ae26600c1c65938687ef9dd`, the unchanged 56-MiB assertion
is in
[`Invoke-WindowModeQualification.ps1`](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/build/Invoke-WindowModeQualification.ps1),
before its Windowed/Borderless smoke calls. It requires a successful asset report
(normally `artifacts/asset-qualification.json`), positive total texture bytes,
and studio-splash bytes between zero and that total. Its exact calculation is:

```text
gameplayTextureBytes = textureRuntimeBytes - studioSplashTextureRuntimeBytes
gameplayTextureBytes <= 58,720,256 bytes (56 MiB)
studioSplashTextureRuntimeBytes <= 3,145,728 bytes (3 MiB)
```

[`RuntimeAssetQualification.Run`](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/src/ForgeLine.Assets/RuntimeAssetQualification.cs)
iterates the manifest records, resolves each runtime file and uses its entire
`FileInfo.Length`. For texture records it sums those compiled container lengths
into `TextureRuntimeBytes`. Only texture IDs starting with the exact ordinal
prefix `branding.undefined_behavior.splash.` also enter
`StudioSplashTextureRuntimeBytes`. Other UI textures are not generally excluded
from the gameplay calculation. These values are not just unwrapped pixel/mip
payload bytes. The asset compiler serializes this report for the downstream
window qualification step.

The same window script separately checks `peakResidentTextureBytes` against
67,108,864 bytes (64 MiB) inside its native smoke validation. That scoped resident
texture counter is distinct from compiled content and from complete physical GPU
allocation. Neither limit was changed by this qualification. Retain the actual
printed `Qualified texture budgets` line and original report for each source;
the historical 99,608,050-byte report is not a current measurement.

## Historical interaction-allocation context

The reported 8,104-byte suite discrepancy is historical context for analysis
source `874b1652bc272ef648120a0b1de9bc85edf5f70c`. That analysis anchor does
not establish the original measurement source. It is not a new measurement from
the qualification source `8d4748d3228c49e50ae26600c1c65938687ef9dd`.
The original failing source SHA, theory row, complete process environment and
executed isolated/suite command lines have not been recovered. No specific GC,
tiering or renderer cause is established by the reported byte count.

At the historical source,
[`SelectedCombatGroupTests.RenderAndControlsShareBoundedGeometryWithZeroWarmAllocation`](https://github.com/DaveBeusing/Forgeline/blob/874b1652bc272ef648120a0b1de9bc85edf5f70c/tests/ForgeLine.Presentation.Tests/SelectedCombatGroupTests.cs)
tested `(width, height, DPI, UI scale)` values `(1024, 720, 96, 1)`,
`(1024, 720, 192, 2)`, `(1600, 900, 144, 1.25)`, `(1920, 1080, 96, 1)` and
`(3840, 2160, 192, 2)`. Setup, geometry checks and the first recording render
preceded the measured interval. A separate retained graphics device and
`SelectionInspectorHudRenderer` received 128 warmup renders, followed by exactly
256 renders between two `GC.GetAllocatedBytesForCurrentThread` observations.
The assertion required an exact zero-byte delta. That method did not request
`GC.Collect`, a no-GC region or a tiering setting.

Current coverage uses different rendering code and must retain its actual scope:

| Current contract at `8d4748d3228c49e50ae26600c1c65938687ef9dd` | Measured boundary and unchanged assertion |
| --- | --- |
| [`HoverTooltipRenderingTests.WarmSelectionStatusUsesRetainedBuffersWithoutManagedAllocations`](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/tests/ForgeLine.Presentation.Tests/HoverTooltipRenderingTests.Status.cs) | Current `WorldHoverTooltipSurface`, active selection status/action panel, 1600 by 900 at 96 DPI; 128 warmups, 256 render calls, exact zero thread-local bytes, positive vertex count and one retained buffer. This is related current coverage, not the old renderer/display matrix. |
| [`SelectedCombatGroupTests.RebuildingSummaryAllocatesBoundedStorageIndependentOfArmySize`](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/tests/ForgeLine.Presentation.Tests/SelectedCombatGroupTests.cs) | 1,000 selected members; 128 summary-construction warmups, 256 constructions, at most 256 times 512 bytes. The 131,072-byte bound is a distinct summary-storage contract, not a relaxed reproduction of the old zero-allocation renderer assertion. |
| [`CombatGroupMeasurements`](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/benchmarks/ForgeLine.Rendering.Benchmarks/CombatGroupMeasurements.cs) | 1, 10, 100 and 1,000 members, pure and mixed groups, 128 warmups and 256 samples per domain. HUD plus idle input requires zero bytes; summary construction allows 512 bytes per sample; snapshot copy uses its existing count-dependent bound. Its null-graphics HUD frame includes panel update, view creation and gameplay HUD rendering, not the whole live client or a GPU/Present/fence interval. |

The old renderer name and method no longer occur in the 152 direct current
Presentation test source files inspected at that source. This does not imply
that all related zero-allocation coverage disappeared: the tooltip/status test
above exercises the current rendering path. Current class success alone is not
evidence that the exact old theory matrix was rerun.

The current SDK is `10.0.401`, with Microsoft.Testing.Platform and
`xunit.v3.mtp-v2`. The
[`Presentation` test project](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj)
explicitly sets `ConcurrentGarbageCollection` to `false`; its comment cites
runtime allocation-counter accounting issues. This configuration is observable,
but does not prove the cause of the historical 8,104-byte result. Main CI has no
explicit tiering or concurrent-GC environment override. In
[`frame-hotpaths.yml`](https://github.com/DaveBeusing/Forgeline/blob/8d4748d3228c49e50ae26600c1c65938687ef9dd/.github/workflows/frame-hotpaths.yml),
`DOTNET_TieredCompilation=0` and `DOTNET_gcConcurrent=0` apply only to the
alert-map and gameplay-UI benchmark steps. They do not apply to the combat-group
class selection or the separate `--combat-group-hotpaths` process. The benchmark
project does not set `ConcurrentGarbageCollection`.

After the normal Release build and runtime-asset preparation, the current
isolated-method and complete Presentation reproduction commands are:

```powershell
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-method '*WarmSelectionStatusUsesRetainedBuffersWithoutManagedAllocations'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build
```

The actual required full-solution command in current main CI is:

```powershell
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
```

Use the old `*RenderAndControlsShareBoundedGeometryWithZeroWarmAllocation`
method filter only on the historical checkout. The
[MTP method filter](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
is distinct from VSTest filtering. A separately labeled diagnostic project run
may add `-- --parallel none`; it cannot replace the required suite result.
These are reproduction recipes, not recovered proof of the original failing
commands. Preserve the exact theory row, process/runtime settings, raw delta,
command, exit code and full log if the failure recurs. Keep the existing
assertions unchanged and route any production or central-test correction through
the Presentation/integration owner.

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
