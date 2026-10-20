# Snapshot publication measurements

## Retained preliminary gameplay capture (overlapped)

The equal-work sequential order is baseline-a, candidate-a, candidate-b, baseline-b.
A subsequently reported concurrent full-solution run overlapped 10:12–10:27 UTC,
including these 10:12–10:17 UTC cohorts. Their CPU times are **unqualified**.
The raw observations are retained; thread-local byte counts and data/checksum contracts
are separate from timing isolation. A coordinated replacement is required.
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
samples allocate zero bytes. The producer retains 246,112 bytes of scratch in these
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
Both compile the identical source-pinned host probe, with default tiering. Each case
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

## Coordinated replacement

Earlier overlapping evidence is never overwritten. A fresh comparison uses
`RunComparison.ps1` inside `InvokeQuietWindow.ps1`, after an explicitly coordinated
build/test-free window. The guard records foreign build/test/renderer process CPU and
flags an active foreign client or more than 0.1 CPU seconds between two-second samples.
It excludes the measurement process tree. This is a bounded observation, not proof
against every short transient or all operating-system/user activity. Full command lines
and private paths are not recorded. A violation leaves the cohort unqualified.
