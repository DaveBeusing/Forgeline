# Material timing recovery protocol

The follow-up includes strict timing failures; no 15% threshold, metric or scene
is waived. Previous raw observations remain retained, including the 13 client
CPU and 25 focused CPU/GPU exceedances. Unchanged caster code also varies between
cohorts, so the prior percentages alone cannot identify a receiver-shader cause.

Before changing rendering code, run the existing 64-warmup/256-sample world
protocol with the *same frozen baseline executable and source in both arms*.
This control uses independent runs, the same B-A-A-B ordering, scenes, quality
profiles, runtime assets and completed-fence series as the original protocol.
All control results must be retained, including failures; an identical-source
control cannot qualify the candidate or waive an existing gate.

If this control reveals instability, investigate it explicitly before a new
candidate acceptance run. Any changed sampling/warmup protocol must be declared
before execution and applied equally to both arms. Keep the original threshold,
all distributions, raw samples and failed historical cohorts. Do not select a
winning rerun or substitute CPU/Present timing for GPU execution.

No renderer workload is started during the reserved terrain qualification
window. Functional gameplay investigations proceed from existing source/logs.

## Component-probe responsiveness defect

Source inspection found that the original separate world/shadow probe never
pumped the native window message queue during its eight cases. The real client
runner pumps before every timed frame and holds a thread-scoped display/system
execution requirement. The component probe now does both, with event dispatch
outside the unchanged CPU bracket and restoration of the prior requirement.
Its report records schema 2 and both explicit conditions. No production renderer,
shader, settings or metric definition changes. This is a measured-methodology
candidate, not yet proof that missing event dispatch caused the prior timing deltas.

Retain an identical-source control with the original frozen probe first. Then
freeze and compare the revised probe against the same baseline source in both
arms, keeping 64 warmups, 256 samples and every original gate. Only after that
control can a corrected-probe B-A-A-B candidate run support a new acceptance
statement. The full-client CPU failures require their own unchanged-runner
verification; this component fix does not explain them automatically.

## Distribution guard correction

The focused analyzer now matches the repository's nearest-rank percentile
contract (`ceil(n*p)-1`, including lower median). Its previous p50 selected the
upper middle sample for even counts. It also rejects nonfinite, negative and
zero/unavailable distributions instead of allowing a null percentage to escape
the gate. Boundary tests cover ranks, input ordering, insufficient counts and
invalid samples. New reports use schema 2. Raw observations and historical
reports remain intact; this correction does not itself establish a performance
pass. The unchanged full-client analyzer is used as before.

## Processor-dependent submission pool

The completed CI log at source d3bfa9a3 reproduces 8,486,912 bytes in
BatchEscapeBandUsesBoundedReusablePayloadLeases. Its 257 simultaneous int[512]
leases exceed the shared pool's retained partitions on the four-core runner.
128 missing arrays * (512*4+24 header bytes) * 32 measured passes = 8,486,912,
matching the observed allocation exactly. More processor partitions locally
explain why the same test passed there; no threshold change is appropriate.

Small submission payloads (up to 512 elements) now use a dedicated configurable
pool bounded to 1,024 arrays per bucket, matching the existing metadata escape
band independently of processor count. Larger payloads keep the shared pool.
Release still removes all list-owned capacity; native resources, snapshots,
submission order and fences are unchanged. Returned process-pool storage remains
distinct from render-owned capacity and has a finite upper bound. Validation
must include the original frozen test binary and repaired binary under
DOTNET_PROCESSOR_COUNT=4, followed by the existing submission/full suites.
The frozen original test reproduces exactly 8,486,912 bytes under DOTNET_PROCESSOR_COUNT=4 (one failure, exit 2). The repaired build passes all 21 SubmissionSafetyTests under the same setting (exit 0), including the unchanged zero-allocation test and large-submission cases. The full isolated Release solution builds with zero warnings/errors. Full-suite and native qualification remain separate.

## Quiet-window compiler guard

Probe preparation and the final build disable shared compilation and MSBuild
node reuse. Before native measurements, shut down remaining build servers and
verify no compiler/build/test/asset process remains. Both measurement drivers
now also reject csc, VBCSCompiler, MSBuild, asset compilation, Blender and the
own gameplay diagnostic, including dotnet-hosted compiler/build forms. This
prevents an otherwise idle build server's delayed work from silently entering
a measured window. It does not exclude any bad timing observation from a
completed valid cohort.

## Original identical-source control

The original frozen baseline probe completed all four independent cohorts:
16 paired cases, six 15% metric/statistic exceedances (two world GPU, one shadow
GPU, two total GPU, one CPU), zero missing series. Both arms use the identical
baseline executable/source. This demonstrates residual timing variability; it
neither approves the candidate nor proves missing event dispatch was its cause.

The responsive-control attempt stopped before its first capture because the
regular player started after the original control's last monitor observation.
The completed original control remains separate; no responsive result exists
for that aborted attempt. The player must be closed before a fresh control root.

## Final isolated controls and candidate observations

After the owner confirmed the regular player closed, the fresh responsive A/A
control completed four cohorts without a competing workload. Both arms use the
same baseline source/executable: 16 paired cases, 17 exceedances (four CPU, four
world GPU, five shadow GPU, four total GPU), zero unavailable series. Pumping
events and holding the scoped execution requirement did not remove variability.
This rejects the hypothesis that this probe correction alone resolves the gates.

The predefined final ef83d2d5 candidate comparison completed independently:
16 world/shadow pairs, 18 exceedances (ten CPU, two world GPU, three shadow GPU,
three total GPU), zero unavailable series. The complete client matrix contains
56 captures with 103 exceedances: 53 CPU and 50 GPU. Pair 1 fixed has one GPU
exceedance and pair 1 sweep passes; pair 2 fixed has 39 and pair 2 sweep 63.
All original limits, samples and raw failed observations remain in force.
The strong process monitor records no competing work during these completed
cohorts. Per-launch telemetry records idle P8 before launches; it does not
establish the clock during rendering and is not evidence of a specific cause.

These observations do not qualify native performance. Identical-source control
variability prevents attributing every difference to shader cost, but does not
waive any candidate failure. No additional repeat was selected for a green
result. A reproducible timing environment and a separately declared stable
control are required before accepting the unchanged 15% gates.

The independent hosted CI is green, including render benchmarks and the complete
solution, so its former 8,486,912-byte submission-pool allocation is resolved.
That CI pass is distinct from the still-failed physical-GPU timing qualification.
