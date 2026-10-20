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
