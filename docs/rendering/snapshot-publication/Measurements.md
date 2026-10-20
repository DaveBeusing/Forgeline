# Snapshot publication measurements

## Qualified comparison scope

Product source is `6db272ce089b2a298e32ec14bbcd6b68ce8f1867` (0.1.420), baseline
`e42f4e7537b84c56c9dbdafe56f03f1e5e74270d` (0.1.419). The last product edits are
29bebfd for Presentation and 5ab7b3c for Client publication; subsequent changes are
probes, qualification helpers and evidence. The coordinated Release solution build
and publish pass with zero warnings/errors. Neither binary is relabelled as a later
reporting commit. Canonical catalog signature/fingerprint and hardware are recorded
in each raw cohort and native projection.

The coordinated baseline/candidate/candidate/baseline matrix completes all gameplay,
unchanged frame-hotpaths and combat-group-hotpaths invocations with exit 0. Its
32-warmup / 128-sample profiles have matching completed ticks, instance/member counts,
consumer checksums, probe SHA256 and catalog fingerprints. A separate 512-warmup
matrix measures ticks 513–640 using the same baseline-compatible probe bytes in both
builds. That probe is independently pinned by SHA256
`A64737B8DCFAB2BABDB1C570A3F389E16838E5119CB1B41B24F0579602CEAB2D` and committed
in a9f879f after measurement. Product assembly sources stay exactly at the two SHAs
above. It is gameplay-only: benchmark exit fields are null, not invented passes.

Two process observers cover the complete final comparison and the extended gameplay
runs: 105 and nine observations respectively, two-second spacing, zero detected
foreign build/test/renderer activity. This bounded observation cannot exclude every
short transient or unrelated OS/user activity. The final native matrix is also in the
105-observation interval. These reports qualify the stated measurement conditions,
not human image acceptance or universal performance.

| Dataset / render reads per tick | Capture mean bytes before → after | Capture median µs before → after | Consumer median µs before → after |
|---|---:|---:|---:|
| 32 warmups / 1 | 393,210 → 257,203 | 990.05 → 1041.15 | 790.00 → 596.95 |
| 32 warmups / 3 | 393,113 → 257,203 | 369.75 → 903.45 | 199.55 → 429.20 |
| 32 warmups / 7 | 392,955 → 257,448 | 299.60 → 572.55 | 250.95 → 626.65 |
| 512 warmups / 1 | 395,583 → 257,962 | 671.15 → 630.80 | 303.15 → 324.10 |
| 512 warmups / 3 | 395,453 → 258,085 | 267.35 → 235.00 | 184.55 → 147.85 |
| 512 warmups / 7 | 395,306 → 258,228 | 277.05 → 296.20 | 318.10 → 281.90 |

Capture saves about 136–138 kB per sampled tick. Most candidate consumer samples allocate zero bytes; the precise exceptions below
remain open and are not hidden by a tolerance. The longer warmup changes the timing distribution substantially; even
there the timings remain mixed. JIT promotion, ordering and elapsed-thread scheduling
remain relevant. No universal capture/consumer/publication latency or FPS win is
claimed, and performance acceptance remains open. The unchanged index-1000 p50 is
102.6/86.9 µs for the baselines and 80.3/87.9 for the candidates, all zero warm bytes.
It includes visibility and does not isolate correspondence work.


Three sampled candidate-A ratio-7 consumer windows have unattributed nonzero counters:
7,920 bytes at tick 103 in the 32-warmup cohort, and 3,528/6,712 bytes at ticks 583/635
in the 512-warmup cohort. Candidate-B and sampled ratios 1/3 are zero. Index/visibility
capacity remains 4,546 bytes in and around all three windows, with 550–551 instances;
there is no observed capacity growth explaining them. Aggregate ratio-7 candidate means
are 30.94 and 40 bytes/tick-window respectively. These windows include seven render
reads and their complete update/interpolation calls. The cause is **not established**;
allocation stacks are required to distinguish code/runtime/framework activity. Neither
JIT nor the historical 8,104-byte report is asserted as the cause. Existing focused
zero-warm-allocation contracts still pass; no global per-frame zero-byte claim follows.

The extended matrix's compound command initially exits 1 **after all four profile
processes pass**: PowerShell splatted a scalar option into characters during summary.
The error is retained. Explicit pure-data recovery exits 0 and validates sources,
probe/catalog identity, work shape and checksums without editing or rerunning samples.
The array splat is fixed; the guard now records process observation and command success
separately. No failed compound invocation is described as a successful complete run.

Portable evidence:
[coordinated CPU](evidence/coordinated/summary.json),
[extended gameplay](evidence/extended-gameplay/summary.json),
[recovery record](evidence/extended-gameplay/recovery.json),
[process observations](evidence/coordinated-process-observations.json),
[extended observations](evidence/extended-process-observations.json).
The adjacent jsonl files contain every observed sample. Evidence-local Git attributes
preserve raw JSON bytes and recorded hashes across checkout platforms.

## Final hardware output

Both fresh six-image matrices pass native structural qualification. All paired PNGs
are exact file matches and repeat pixels match, with the same catalog, driver,
1920×1080 High settings, fixed scene/tick/camera/visual time and raster DPI 96.
Resources and DXGI local usage plateau across all 32 samples per case; live resource
counts are flat and submission faults are zero. Full CPU/GPU frame samples, resource,
health and memory observations are retained in source-identified projections:
[baseline](evidence/coordinated-native-baseline.json),
[candidate](evidence/coordinated-native-candidate.json),
[comparison](evidence/coordinated-native-comparison.json).
Original manifests and PNGs remain in
`artifacts/snapshot-publication/coordinated-comparison/native-{baseline,candidate}/`.
This is frozen-state visual/resource qualification, not sustained live-game FPS.
GPU timestamps are fence-completed; unavailable passes remain null. Human review
remains pending and source changes do not inherit an accepted golden.

## Retained preliminary gameplay capture (overlapped)

The equal-work sequential order is baseline-a, candidate-a, candidate-b, baseline-b.
A subsequently reported concurrent full-solution run overlapped 10:12–10:27 UTC,
including these 10:12–10:17 UTC cohorts. Their CPU times are **unqualified**.
The raw observations are retained; thread-local byte counts and data/checksum contracts
are separate from timing isolation. The coordinated replacement above supersedes their timing qualification.
Baseline is `e42f4e7537b84c56c9dbdafe56f03f1e5e74270d` (0.1.419); candidate is
`29bebfdc6ca59c6b98789a9a6e110e4cf263fdb9` (0.1.420). All four use the identical
probe (SHA256 `9F9E3001C0FA1EE3AAB9154D83AFD89A7410CE2676115F7996E339F311CAE8A2`),
.NET 10.0.12, Release, default tiering and the same canonical runtime catalog.
The manifest SHA256 is `D1786170F593B6D32FE1CF4FCE85514CAB7681F52132D47F15EF2612A3D76870`;
content signature is `edcbaeaf79e9a93e907b9e4f62bbb3065f7c60b928b71d742e3f94c47a4f8136`.
The native catalog fingerprint uses a different definition and is recorded below.

Each process executes actual Central Divide gameplay fixed ticks, seed 9147, player 1,
with the default east computer participant and an intelligence-authorized extractor.
There are 32 warmup ticks and 128 sampled completed ticks, with 551 render instances
and three combat members. Consumer checksums, ticks and counts match in every cohort.
The render ratios are injected read rates, not wall-clock FPS. Capture sits inside the
complete producer tick; consumer includes RenderWorld update and interpolation. Neither
includes GPU work or Present. Thread bytes exclude observation storage; collection
counts in the raw report are process-wide, not attributed producer allocations.

| Render reads/tick | Capture mean bytes before → after | Capture median µs before → after | Consumer median µs before → after |
|---|---:|---:|---:|
| 1 | 393,117 → 257,203 | 944.65 → 751.95 | 757.85 → 752.40 |
| 3 | 393,197 → 257,203 | 640.15 → 446.45 | 372.15 → 494.15 |
| 7 | 393,116 → 257,523 | 321.55 → 268.50 | 255.75 → 231.75 |

The measured capture reduction is about 135.6–136.0 kB/tick. All candidate consumer
samples in this preliminary dataset allocate zero bytes; later coordinated exceptions
are reported above. The producer retains 246,112 bytes of scratch in these
workloads; render index plus unchanged visibility retains 4,546 bytes versus 2,654.
These are payload/capacity counters, excluding managed object headers, immutable
snapshot arrays, caller-retained snapshots and unrelated engine memory. Baseline has
no corresponding retained producer scratch. Candidate combat payload reuse is 159/159
successive captures including warmup, versus zero in the baseline. Fresh tick envelopes
and every other capture dependency remain validated.

Timing is observational and mixed. In particular the ratio-3 consumer is slower, and
the existing snapshot-index-1000 benchmark p50 is 90.7/69.8 µs in the two baselines
versus 111.7/77.8 µs in the two candidates. All four allocate zero in that benchmark.
It includes visibility rebuild and is not isolated correspondence time. The algorithm
has linear correspondence work, but these observations do not establish a consumer
latency win. Thermal/background conditions are uncontrolled; no global FPS, zero-byte
gameplay or overall CPU speedup is claimed. Performance acceptance remains open.

Portable raw reports, process metadata, unchanged frame/combat benchmark reports and
file hashes are in [evidence/gameplay](evidence/gameplay/summary.json). The summarizer
checks full source identities, equal probe/catalog signatures, completed data shape
and checksums before producing that summary. Both existing benchmark commands exit 0
in every cohort. Their existing assembly-version field is not a source fingerprint;
source attribution is supplied by run.json and the companion full informational identity.

## Actual host mailbox publication

The final equal-work host order is baseline/candidate/candidate/baseline. Candidate
source is `43631f025eba4d3da62396505ffff10986281dce`; its product implementation is
unchanged from `5ab7b3cea21c864e1825874086cb425d0002b35e`. Baseline remains e42f4e7.
Both compile the identical source-pinned host probe, with default tiering. These
pre-coordination host runs have no process observer; their CPU times are observational
and are not qualified as isolated latency comparisons. Thread-local allocation and
retained-reader contracts are reported separately. Each case
warms 2,048 batches × 32 publications and measures 256 × 32. Warmup bytes/time are
recorded separately, not silently moved out of the accounting.

This executes real ClientRenderHost.Publish: defensive capture, gate, mailbox counter
and signal. A deliberately blocked real render-owner reader retains its first frame
while publication continues. The fixed payload has 128 gameplay lines/32 labels,
32 debug lines/eight labels and eight selected identities. This is a mailbox workload,
separate from the real completed-gameplay capture above, with no GPU or FPS claim.
Changing mode mutates the first gameplay line on every publication; other channels
remain stable and every old-reader value is verified after shutdown.

| Payload | Mean bytes/publication before → after | Baseline cohort medians µs | Candidate cohort medians µs |
|---|---:|---:|---:|
| Stable | 8,184 → 0 | 1.000 / 0.984 | 1.653 / 1.066 |
| First line changing | 8,184 → 5,144 | 0.997 / 1.245 | 1.844 / 1.250 |

Allocation savings are reproducible; full-value checking has a CPU tradeoff at this
payload size. No publication latency win is claimed. The two baseline warmups allocate
536,346,752/536,346,624 bytes per stable/changing case; candidate warmups allocate
304/337,117,184 bytes. Stable baseline warmups take 104–105 ms versus candidate
140–211 ms; changing baseline 77–85 ms versus candidate 93–136 ms. Those total warmup
figures and all measured samples remain in [evidence/host](evidence/host/).

Preliminary 128-batch probes had not settled: the original equality cache reported
about 5.5 µs stable, and the unmanaged comparison prototype about 11.8 µs. The repeated
longer warmup exposes the substantially lower steady observations above without changing
tiering or widening any test bound. Preliminary raw runs remain in local
`artifacts/snapshot-publication/{matched-host,refined-host}/`; their timing is not blended
with the final cohort. Neither the measurement correction nor the bitwise implementation
is claimed independently to explain every timing difference.

## Retained preliminary native output and resource lifetime

The six baseline and six candidate captures cover IndustrialBase and
MainBattleTankProduction, High, 1920×1080, CloseTactical/NormalGameplay/Strategic,
raster DPI 96, seed/complete-tick/camera/visual time fixed by the existing fixtures.
Both use three warmups, 32 sampled frames and two repeated readbacks. The hardware is
NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, Windows
10.0.26300, .NET 10.0.12. Host-window DPI is 240 and distinct from raster DPI.
The common native runtime fingerprint is
`DDA6CF3F2A2DEB662A3190637A15D800F5D6B8D7DD8EAE66CEC7D3061EEBCE99`.

These native runs also fall in the reported overlapping CPU-load interval. Frame CPU
times are unqualified for performance comparison. Image identity, fenced readbacks and
resource observations remain separately attributable.

All six PNGs are exact file matches and all repeated pixels match. Structural checks
pass; the changed source does not inherit a golden's human visual acceptance. A spot
inspection of the normal production scene confirms the same existing output; the
full human review remains pending. This is a visual/resource qualification of fixed
states, not live-game sustained-motion performance.

Every case has a flat per-frame resource count over all 32 samples: 78 textures,
43,718,552 resident texture bytes and 85 descriptors at the two closer zooms; 82,
43,741,432 and 89 at Strategic. There are no texture binding/submission failures.
This bounded observation is not proof against all future leaks. Capture allocation
means are 56–152 bytes/frame; valid new snapshot arrays are outside this frozen-frame
window. GPU timestamps are available and fence-completed; missing pass times remain
null. CPU frame time includes reuse/Present and is not producer time. The raw CPU/GPU
frame values, memory, health and resource observations are preserved in portable
projections with original-manifest and image SHA256 values:
[baseline](evidence/native-baseline.json), [candidate](evidence/native-candidate.json),
[comparison](evidence/native-comparison.json). Full manifests and PNGs remain in local
`artifacts/snapshot-publication/native-{baseline,candidate}/`.

## Reproduction

Build the checked-out source and use the existing pinned SDK/runtime asset build.
Use isolated build output for the baseline. Copy the exact baseline-compatible gameplay
and host probe sources into its assigned test directory, build, then remove those
untracked copies before native capture. Published binary informational identities must
match the source used in each report. Do not relabel earlier binaries as a later head.

```powershell
./build/rendering/snapshot-publication/InvokeMeasurements.ps1 -Repository <checkout> -TestExecutable <built-test-executable> -RuntimeAssets <canonical-runtime-root> -OutputRoot <fresh-cohort> -RenderingBenchmark <built-benchmark-dll>
python build/rendering/snapshot-publication/SummarizeMeasurements.py <four-cohort-root>
./build/rendering/snapshot-publication/InvokeHostMeasurements.ps1 -Repository <checkout> -TestExecutable <built-test-executable> -OutputRoot <fresh-host-cohort>
./build/Invoke-RtsReferenceQualification.ps1 -Capture -ClientPath <published-client> -RuntimeAssetRoot <canonical-runtime-root> -OutputRoot <fresh-native-root> -Scenes IndustrialBase,MainBattleTankProduction -Profiles High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -RasterDpi 96 -Warmups 3 -Samples 32 -RepeatCaptures 2
python build/rendering/snapshot-publication/CollectNativeEvidence.py <native-root> <portable-report.json>
```

The candidate native command also receives `-BaselineRoot <baseline-native-root>`.
Use fresh output directories; do not overwrite evidence, run overlapping performance
jobs, disable tiering globally, reduce quality or accept images automatically.

## Guard protocol

Earlier overlapping evidence is never overwritten. A fresh comparison uses
`RunComparison.ps1` inside `InvokeQuietWindow.ps1`, after an explicitly coordinated
build/test-free window. The guard records foreign build/test/renderer process CPU and
flags an active foreign client or more than 0.1 CPU seconds between two-second samples.
It excludes the measurement process tree. This is a bounded observation, not proof
against every short transient or all operating-system/user activity. Full command lines
and private paths are not recorded. A violation leaves the cohort unqualified.
