# Particle ordering CPU qualification

Status: same-output CPU improvement observed in this bounded local experiment;
complete preparation, native images and hardware GPU acceptance remain open.

## Identity and method

- Reference algorithm: unchanged radial-distance heap sorter copied from
  `ParticleVfxRenderer` at `cdf97fb9a8ef277cf75d451476a21128d398007c`.
- Measured candidate and fixture: committed source
  `8331e384d1fc7b89c9b40ade7590ce9a5ed6f99a`, followed by a complete Release build.
- Host: Linux x64, AMD EPYC 9V74 host model, eight logical processors visible to
  .NET; SDK 10.0.401, runtime 10.0.12. This is not a Windows or physical-GPU run.
- The raw report fingerprints the actual Presentation and test assemblies.
  Source-file hashes and command exits are retained in local-validation records.
- Six fixtures: 512, 2,048 and 4,096 particles, each with Spark and Smoke branches.
  Stationary unique depths along camera -Z and identity view make both algorithms
  produce byte-identical complete 64-byte particle payload order. Smoke executes
  the zero-age curl branch. Moving/off-axis image corrections are excluded.
- A fixed mixed warmup required at least 3 seconds and 256 calls to each
  implementation per fixture. It actually performed 692 complete sweeps and 8,304
  sort calls in 3,003.6106 ms. First implementation alternated per sweep.
- Timed order is reference, candidate, candidate, reference for every fixture;
  each cohort adds 64 warmups and retains all 256 timed samples. Array reset is
  outside timing and inside allocation windows. Runtime tiering/PGO/ReadyToRun
  settings were unchanged; their recorded override variables are all null.

The reference is the frozen algorithm compiled into this test assembly, not a
separate whole baseline product executable. No external-process isolation,
specific JIT tier, stable clock, driver effect or GPU result is inferred.

## Observed warm results

Each implementation contributes 512 observations per row. Times are milliseconds.
Means combine both equal-sized cohorts; p95 uses nearest rank. The same raw data
also retain p50, p99, individual cohort means and UTC intervals.

| Particles | Visual | Reference mean | Candidate mean | Reference p95 | Candidate p95 | Mean reduction |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 512 | Spark | 0.07126 | 0.01871 | 0.10270 | 0.02040 | 73.7% |
| 512 | Smoke | 0.07123 | 0.02007 | 0.08610 | 0.02040 | 71.8% |
| 2,048 | Spark | 0.35186 | 0.09179 | 0.43460 | 0.13520 | 73.9% |
| 2,048 | Smoke | 0.36576 | 0.10134 | 0.45990 | 0.17070 | 72.3% |
| 4,096 | Spark | 0.87388 | 0.40643 | 1.17790 | 0.55630 | 53.5% |
| 4,096 | Smoke | 0.76685 | 0.38573 | 0.79540 | 0.41570 | 49.7% |

All 24 cohorts preserved exact particle payloads. Every measured current-thread
and all-thread allocation window reported **0 bytes**. This observation includes
the test host's all-thread window; it is not a claim that an entire frame, process
or native renderer allocates zero. The final run also executed the 21 portable
regression cases: **22 total, 22 passed, zero failed/skipped**, including this
opt-in qualification case.

The candidate mean is lower in both paired candidate cohorts than in either
reference cohort for each fixture. Residual host variation remains visible,
particularly the 4,096-Spark reference cohort means (0.97322 and 0.77455 ms).
The evidence supports the reusable key/permutation sorter on these inputs; it
provides no FPS percentage or full Prepare/upload/particle-pass speedup.

## Retained earlier run

[particle-ordering-initial.json](evidence/cpu/particle-ordering-initial.json) is the
first completed run from source `68d62dbe8d788bb4e29516a69655e74b2ccdd2f8`.
It used only 64 per-cohort warmup calls. Its first 512-Spark reference mean changed
from 0.52151 to 0.03245 ms between the two reference cohorts, and 512-Smoke appeared
slower in the candidate. That pronounced transition is consistent with incomplete
runtime warmup, but no JIT trace was collected to prove a sole cause. Those timing
results are not accepted as a stable speedup or slowdown estimate.

The follow-up changed only the benchmark's fixed, result-independent process
warmup and provenance. Production code, payload assertions, quality settings,
runtime tiering and sample counts stayed unchanged. The original run was neither
overwritten nor removed, and no observation was filtered for being slow. No
additional run was selected to improve the displayed result.

## Raw evidence and applicability

- [Warm raw report](evidence/cpu/particle-ordering-warmed.json): all 6,144 timed
  observations, all 24 allocation windows, assembly hashes and warmup details.
- [Derived summary](evidence/cpu/summary.json): exact unrounded aggregate results,
  raw-file hash and percentile definition.
- [Build/test records](evidence/local-validation/index.json): commands, exits,
  elapsed times and source-file identities; absolute validation paths normalized.
- [Reproduction](Reproduction.md): same fixture and a new evidence path per run.

The later reconciliation with master `e42ae48346bbb1331b7a548a72fc0905ae6d184a`
retains these particle-ordering and fixture source blobs unchanged. These reports
keep their actual measured source identity; they are not relabeled as new whole
product measurements. Admission priority, fallback rejection, dormant-state
replacement and evolved view-depth ordering still require separate native visual
review, including overlapping sprites and sustained resource plateaus.
