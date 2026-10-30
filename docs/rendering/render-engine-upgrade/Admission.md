# RenderEngine baseline admission contracts

[Documentation](../../README.md) - [Complete catalog](../../DocumentationIndex.md) - [Baseline status](Baseline.md) - [Measurement protocol](MeasurementProtocol.md)

This is the contract implementation for a future controlled baseline. It does **not** establish a performance baseline, approve policy numbers, accept the current hardware conditions, or authorize wave 2. The separate measurement package requires these contracts reviewed and integrated on master first. Historical ownership records and final optimization qualification gates remain unchanged.

## Supported evidence producers

The native reference runner and real `ClientRenderHost` share one immutable World recipe: actual current/history snapshot identities, camera settings and all sampled poses, terrain/surface marks, effective quality/lighting/atmosphere, selected entities, HUD/readmodel state, DPI/scale, frozen interpolation and visual/mechanical/particle time. Recipe bytes are retained as Base64 with their SHA256. Interactive UI/material-reference variants are explicitly unsupported by this matched-host contract. Ordinary smoke output cannot be renamed into a native fixture.

Each authored host frame is requested once and acknowledged only after an actual successful submission and presentation. Failed, skipped or unpresented attempts fail the cohort; they cannot consume a warmup/sample slot and then retry. Final owner CPU, camera ACK and completed GPU records join one-to-one on actual device generation, resource generation and shared-queue submission fence. Queue upload fence gaps are allowed; CPU ordinal is never treated as the actual fence. Repeated readbacks occur outside timing, preserve all eight results and require identical bytes. Native/host PNG file hashes and decoded RGBA hashes are independently checked by admission.

The optional D3D12 identity exports the actual device LUID and queue node outside the historical `GraphicsDeviceInfo` digest. An early producer-generated checkpoint identifies actual PID, process creation time, source, runtime bytes, client assembly, window handle and device session. Bounded query-only installed CUDA Driver API calls map the same live LUID/node to UUID/PCI and then the sensor inventory. Both independent process observations bracket the query inside that exact child command. Archived LUID equality, adapter name, caller hash, stale checkpoint, PID reuse or multiple candidates cannot establish the binding. Multi-node devices remain explicitly unsupported. No CUDA context/kernel or hardware settings are changed.

Both opt-in producers require the actual loaded catalog to have valid integrity metadata and verify every listed file size/hash. The manifest must remain byte-identical across loading/verification. Full runtime directory fingerprints and complete executable payload inventories are captured before/after the command. Legacy unsigned catalogs remain inspectable through the ordinary Assets API; they cannot support these evidence producers.

## Real session and observer controls

An explicitly observed new-game session receives immutable provenance from the actual `ClientApplication` composition: scenario/profile/seed, participants, settings, load origin and smoke/interactive mode. Every owner sample retains the expected session, actually consumed snapshot session/tick, and real simulation callback session/completed ticks/running state. Both simulation and consumed snapshots must progress. This proves a real advancing session, not gameplay success. The default unobserved path does not construct this proof. Loaded-save admission remains unavailable: a file reopened after loading cannot attest the precise bytes previously loaded.

`CollectorOn` and `CollectorOff` are separate authored-host experiments. Both run the same preallocated boundary-clock/presentation probe, identical recipe and contiguous 64/512 window, and the same completion/stop sequence. OFF never constructs `LiveRenderTelemetry`, enables its completion ring, samples its precise allocation counters, snapshots its resource memory or drains its GPU stream. OFF exports no invented owner/allocation/GPU samples. Ordinary native GPU timing, production diagnostics and the common ACK/content probe remain enabled in both arms. The result measures the optional wave 0 collector overhead, not removal of all instrumentation. It is not an allocation-budget substitute.

Mixed coverage requires actual simultaneous consumption, not a label or loaded cache: at least two valid nonfallback runtime material identities, skinned-buffer variants and composed mechanical main parts, plus active particles and particle draw calls in the same measured frame. The optional renderer recorder is allocated only for evidence producers, uses bounded preallocated material storage, records its scan ticks, and resolves a pending recording epoch to the actual successful submitted/presented tuple. Prepare invalidates it. It describes the last successfully recorded frame under Prepare/record/Present; wrappers must reject reusing an old tuple after a failed attempt. The normal renderer owns no recorder. Existing content is not changed to manufacture a positive mixed case.

## Acquisition and frozen controls

The supported acquisition API is `capture_runner.run_capture(client, request, fresh_output, sensor, cuda_driver, host=..., gameplay=...)` in [the owned tooling](../../../build/rendering/render-engine-upgrade/capture_runner.py). `host=True` runs `--reference-host-fixture-request`; the default runs `--reference-capture-request`; `gameplay=True` runs the existing `--smoke-test` with child-only telemetry/binding environment. Absolute existing executables are required. No launcher activates a window or changes driver, power, GC, fence, sample-count or timeout policy.

The append-only sensor journal retains exact argv/stdout/stderr, actual OS AC status and HWND/PID/foreground/visibility/minimize facts. A constant-size atomic progress snapshot identifies the raw prefix. Missing/failed observations, gaps over five seconds, changed binaries, foreground loss, unknown/active clock-event masks, failed commands and interrupted cleanup remain failed evidence. Startup/after-exit observations are preserved with their actual unavailable status. Explicit pre-window facts and ongoing observations must bracket the whole measured window. Point observations do not prove continuous physical unobscured visibility.

`baseline.py plan` remains an incomplete protocol proposal, not an approved experiment. Before capture, freeze and review the complete exact experiment record on trusted master. `baseline.py admit --cohort <cohort.json> --repository <checkout> --trusted-master-source <sha> --output <new.json>` verifies that record's exact committed bytes. Missing evidence produces a retained nonzero blocked result.

The accepted protocol contains:

- `kind=accepted-baseline-control-protocol`, exact execution source, freeze timestamp and identical-source A/A comparison; the complete binary inventory and actual canonical hardware identity;
- all canonical scene/quality/zoom/display Warm Fixed/Sweep/Traverse and separate RendererCold Fixed cases, plus named scaled Fixed/Sweep and actual mixed coverage, raster DPI 96, exact serialized recipes/effective profiles and sampled cameras;
- at least five pairs, unchanged 64 warmups/512 samples; RendererCold has zero applied warmups/128 samples with setup separate;
- separate actual-gameplay origin and collector-on/off cohorts;
- an ordered `attemptSchedule` including every pair/arm/case/producer, retained directory and original launch-output path, and its fixed `attemptInventoryRoot`;
- previously reviewed hardware, allocation and logical GPU-memory policies committed before capture. Review rationale and finite maxima must be real, never generated by the collector.

A cohort's hash-bound `runs` cover that exact schedule, including supplemental producers. Every `attempt.json` under the frozen inventory root must be represented; failed/omitted/reused/extra attempts cannot be replaced by a winning subtree or rerun. Actual chronological command keys must equal the ordered schedule, with contiguous reversed A/B blocks and no overlap. Both arms use the same source, executable/runtime/seal/provider/hardware identities and policies. Raw arrays drive mean, lower median, nearest-rank p95/p99 and maximum. The inclusive 15% A/A control boundary is unchanged and symmetric; inclusive phase/pass intervals are not added together.

Allocation budgets use exact enclosing-frame process and owner-thread counters from separate warm Full observations. The whole process includes real platform/audio/background owners, including time spent in waits. A retained GPU capacity is not a managed allocation bound. Memory budgets consume exactly the eight existing logical/outstanding counters: live count/bytes, retiring count/bytes, texture mip bytes, staging bytes, descriptors and pending upload bytes. Their overlapping values are not total physical VRAM. Driver-private/heap/query/allocator physical coverage remains unavailable. Existing scoped scene checks remain independent.

Verified controls return `baselineControlsAccepted=true`. Overall `wave2Ready` remains false until a separately accepted review binds the original cohort and measured phase/pass/wait/allocation/cold distributions, sampled owner sites and both original wave 2 lanes' measured hypothesis or honest no-change disposition. Future optimized-candidate, lifecycle and human art gates are not imported into baseline-only control acceptance.

## Separate sampled allocation diagnostics

The isolated [allocation parser](../../../build/rendering/render-engine-upgrade/allocation-trace/AllocationTrace.csproj) references only centrally pinned TraceEvent 4.0.0. It is outside production/headless references. The nested tool manifest pins dotnet-trace 10.0.750501; no global installation is required. Build with the repository-selected SDK:

```powershell
dotnet tool restore --tool-manifest build/rendering/render-engine-upgrade/allocation-trace/.config/dotnet-tools.json
dotnet build build/rendering/render-engine-upgrade/allocation-trace/AllocationTrace.csproj -c Release
dotnet build build/rendering/render-engine-upgrade/allocation-trace/fixture/AllocationTraceFixture.csproj -c Release
python build/rendering/render-engine-upgrade/allocation_diagnostics.py --tool <absolute-pinned-dotnet-trace.exe> --fixture <fixture.dll> --parser <AllocationTrace.dll> --repository <checkout> --output <fresh-directory>
```

The tiny NoInlining fixture verifies real sampling/stack resolution and retained command/payload/trace/report hashes. It is explicitly synthetic diagnostic tooling validation, not render-owner allocation-site evidence. Original raw NETTRACE and fresh ETLX stay retained. Invalid input, wrong PID, absent stacks, loss/truncation and changed payloads fail or remain unavailable.

The parser preserves legacy `GCAllocationTick` and modern `AllocationSampling` separately, including payload version, types, OS thread/PID, timestamps, raw stacks and separate loss observations. Sampled amounts are never exact total bytes and the two families are not summed. A managed thread ID cannot substitute for the owner's OS thread ID. Original documented Trace4/min4 FastSerialization and v6 header bytes/hash, typed tags/lengths/footers and little-endian clock/UTC are checked; unknown formats remain unavailable. Clock-format support does not provide independent producer identity.

A reviewed owner-site diagnostic additionally needs the actual separate attach command, pinned executable/version and parser before/after inventories, exact trace/report input-output hashes, original independent process/image/creation observations, matching source/runtime/device session/native owner, and resolved owner stack samples within the original measured intervals using compatible original clocks. Startup-only, outside-window, ambiguous or unbound samples cannot grant warm-frame attribution. This analysis readiness is separate from A/A controls and precise allocation budgets. Never profile during qualification timing.

## Capacity-derived proposals - not approved policies

The user requested capacity-derived proposals for review. These are **PROPOSAL ONLY**; they do not supply approved JSON policy or a reviewer/date. Both allocation maxima and hardware stability ranges remain unresolved.

| Logical counter | Proposed maximum | Limitation |
| --- | ---: | --- |
| liveResourceCount | 8192 | Conditional bounded fixture/catalog generations |
| liveResourceBytes | 2147483648 | Conditional 2 GiB logical-data ledger |
| retiringResourceCount | 4096 | Existing retirement capacity; bytes still independently bounded |
| retiringResourceBytes | 2147483648 | Assumes at most one superseded bounded population |
| textureMipBytes | 268435456 | Conditional 256 MiB including pending releases |
| stagingBytes / pendingUploadBytes | 146800640 each | Three 4 MiB slots and one 128 MiB slot; actual used/inflight ranges differ from capacity |
| descriptors | 4096 | Shader-resource heap capacity |
| processAllocatedBytes / threadAllocatedBytes | UNRESOLVED | Needs complete transient/pool-miss/UI/background-owner allocation ledger |

The conditional ledger assumes three frame buffers, at most 100000 **expanded submitted parts** rather than roots, declared bounded host/UI populations and one superseded resource population. Actual per-frame counters must verify those assumptions; no content may be trimmed to fit. The 4K/High target textures alone account for 533102592 bytes before shadow constants and other resources. Existing capacities in `GraphicsResourceRetirement`, `UploadArenaLimits`, descriptor allocation, asset residency, terrain residency and skin buffers support local bounds; they do not prove the whole fixture ledger or all-driver physical allocation.

Observed hardware mask `0x4` remains conservatively active software power cap. A retained query observed enforced power limit 20 W, default 115 W, maximum 175 W; these are time-varying discovery facts, not approved operating conditions or causal diagnosis. No settings were changed and no stability policy is inferred from a pilot result.

## Retained failures and current limits

Functional attempt01 at `68388de2be82cc57a82ede607e2a603c2c494327` failed before sampling on a span-bearing UI proof serializer. The explicit World DTO fixes preserve consumed UI/vector/matrix/identity data. Attempt02 at `ef6e03ad13f170ea56cbb469586f011ef8e9088f` retained a zero-tolerance one-channel delta of one at pixel (260,48), green 137 to 138, on repeated native readback2; host was not launched. Cause is unproven. The single shadow diagnostic03 at `95792f762edd2931875eddb1fd5e96dc1f62b3a5` failed foreground precheck before sampling; no attachments were produced and it was not retried. All attempts remain retained. A 1.5x capacity preflight reserved about 1.208 GB against 22.61 GB free before diagnostic03.

Local native WARP tests establish only bounded renderer/fixture functionality. The two-file BattlefieldStatus test repair preserves marker count/privacy/pixel repeat/font assertions and constructs the complete marker label within the actual HUD clip with an unclamped test-only distance1000. The original hosted failure did not reproduce locally at1600x900; a small800x450/high-DPI CPU case demonstrated fragile original clip assumptions. Production camera default260 and renderer behavior are unchanged.

No current full physical native/host matrix, positive mixed scene, accepted hardware/allocation policies, accepted baseline ranking or optimization result is claimed. Full exact-source canonical validation and hosted CI are separate delivery records. Measurement acceptance stays OPEN; original wave 2 stays NOT READY.
