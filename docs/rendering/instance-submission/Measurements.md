# Instance submission measurements

## Sources and definitions

The unchanged reference is master 47f4230f487e37f699b6572354e89f800476c513.
The corrected product is 63fada7afaf610ea91af3a8c6d283dd1d4093975,
version 0.1.421-dev. Runtime manifest SHA256:
D1786170F593B6D32FE1CF4FCE85514CAB7681F52132D47F15EF2612A3D76870.
Content signature: edcbaeaf79e9a93e907b9e4f62bbb3065f7c60b928b71d742e3f94c47a4f8136.

WorkMetrics.Interpolations counts GetInterpolatedInstance calls, including the
single-snapshot fast return. It does not claim that every call performs Slerp.
MeshResolutions and MaterialResolutions count actual asset-session resolution
calls; they are separate from static presentation-catalog operations. The
original single-snapshot mechanical fixture creates 1,000 roots and 9,750
submitted parts. Later interpolation coverage explicitly installs a distinct
previous/current pair, changing root rotation, translation and aim yaw.

Each CPU cohort uses the same adapter, sealed assets and unchanged existing
frame/culled hotpath gates, 256 adapter warmups and 1,024 raw samples. Raw
Stopwatch ticks, thread allocated bytes and actual output shapes remain retained.
The existing benchmark uses its own unchanged warmup/sample counts. Null graphics
proves CPU work, command shape and allocation properties; it is not GPU time.

## Initial observations and correction

The first implementation cfab93d1 produced equal output shapes and zero warm
allocations but regressed fully culled frames and new snapshot pairs. Those
cohorts remain under initial evidence. The correction removes the full root
scan after spatial rejection, avoids interpolation to read provably aligned
unique predecessors, returns prepared roots by readonly reference and keeps
pool growth outside the inline payload append. Unbounded/affordable shadows
skip unnecessary budget compaction and ordinary main draws omit shadow trace
indices. Tests cover bitset boundaries and duplicate/unordered correspondence.

The corrected two-pair run has no observed foreign workloads in 64 two-second
samples. All eight process exits are zero; all CPU shapes match and every raw
adapter allocation sample is zero. Median ranges in microseconds:

| Workload | Reference A/B | Corrected A/B |
| --- | ---: | ---: |
| 872 visible near roots | 239.3 to 245.4 | 150.5 to 193.4 |
| 5,000 fully culled roots | 4.0 to 4.2 | 1.6 to 2.7 |
| 10,000 distributed roots, pan/zoom | 1,187.0 to 1,237.1 | 602.1 to 655.0 |
| 10,000 distributed roots, new snapshot pair | 2,089.4 to 2,493.4 | 2,023.8 to 2,140.4 |
| Mechanical fixed 1,000 roots / 9,750 parts | 1,930.1 to 2,307.1 | 1,910.1 to 2,898.1 |
| Changing alpha, single snapshot, main + light | 3,696.1 to 3,884.5 | 3,619.6 to 4,862.6 |

Mechanical timings and CPU tails remain mixed. No general mechanical speedup,
sustained FPS improvement, stable-clock equivalence or complete performance
acceptance is inferred. Process observation excludes only its documented
classes and sampling interval, not all OS activity or thermal/frequency changes.

Human visual review remains open, especially the deliberately changed
budget-limited shadow selection.

Raw cohorts: [initial](evidence/initial/summary.json), [corrected](evidence/corrected/summary.json).
Each directory retains original samples, all eight process exits, observed quiet-window
results, source identifiers and original-file hashes. Adapter/controller source
archives use a .txt suffix and preserve the recorded original bytes.

## Final functional source and CPU cohort

Product binaries identify 60724f1204cffbc850cbbfbc0a381cd62b0f23a1,
including pose-page reclamation. The subsequent adapter-only fix reads the
fixture through the existing public RenderWorld API; adapter source and binary
hashes are independent of the measured product assembly identity. Both sides
use the identical deterministic adapter DLL. [Final raw observations](evidence/final/summary.json)
retain all seven workloads. All eight processes exit zero, all shapes match,
and every warm adapter sample records zero allocated bytes. The process guard
has 74 observations and no detected foreign workload.

| CPU workload | Reference A/B median us | Product A/B median us |
| --- | ---: | ---: |
| Original mechanical 1,000 roots / 9,750 parts | 2,121.2 / 2,292.8 | 1,728.7 / 1,725.2 |
| Near 1,000-root fixture | 200.3 / 281.5 | 178.1 / 171.1 |
| Fully culled 5,000 roots | 6.2 / 5.0 | 2.5 / 2.4 |
| Single snapshot, changing alpha, main + light | 4,441.9 / 4,596.7 | 3,551.2 / 3,568.4 |
| Distinct snapshot pair, main + light | 3,979.4 / 4,651.1 | 3,657.3 / 4,367.8 |
| Distributed 10,000 roots, pan/zoom | 1,194.2 / 1,320.3 | 606.3 / 735.0 |
| Distributed 10,000 roots, new pair | 2,758.0 / 2,491.9 | 2,083.7 / 2,428.0 |

The actual interpolated pair changes translation, root rotation and turret yaw.
Main performs 1,000 interpolation calls; light has zero calls and 1,000 cache
hits. Both accept 1,000 roots and submit 9,750 parts. Each pass resolves six
meshes/four materials and binds one pipeline; main binds 16 textures, light zero.
Prepared-root payload is 344,064 bytes; additional spatial payload is 38,848.
The separate 1,024-part budget submits 105 complete roots / 1,023 parts, confirmed
by the graphics-command counter. These counts do not redefine the device budget.
Medians improve in this cohort, but tails and earlier mechanical cohorts remain
mixed; the raw history is part of the result.

## Native hardware cohort

All six capture processes exit zero. Each source has 18 cases: seven High scenes
at tactical/strategic zoom, a High camera sweep, and Low repeated vehicles.
Each capture uses 1,920 x 1,080, raster DPI 96, 64 warmup frames, 128 measured
frames and two equal repeated captures. Physical host DPI is recorded separately
as 240. Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU,
driver 32.0.15.9653, D3D12 hardware; debug layer unavailable. Runtime fingerprint:
DDA6CF3F2A2DEB662A3190637A15D800F5D6B8D7DD8EAE66CEC7D3061EEBCE99.
The native quiet guard records 46 observations with no detected foreign workload;
completion is 2026-10-20T13:48:44.9668918Z. GPU timing contains distinct completed
fence observations, not CPU estimates. CPU frame timing includes frame reuse and
Present; pass timings, resources and memory are retained separately.

[Native summary](evidence/native/summary.json) links to unmodified portable frame
projections (losslessly compressed .json.gz). Original manifest hashes and both
compressed/uncompressed checksums are retained. Original full manifests remain
in local ignored qualification artifacts; the export removes machine paths and
redundant draw diagnostics. Unavailable GPU/memory fields are not converted to zero.

Fifteen image pairs are exactly pixel-equal. CombatSupportRepeated tactical
views differ: High fixed and sweep each change 49,577 pixels (2.3909%, maximum
channel delta 86); Low changes 107,628 pixels (5.1904%, maximum delta 100).
These three cases exceed the unchanged zero tolerance. The new selection
intentionally keeps complete shadow roots; human review is required. Original
[High reference](evidence/native/baseline-High.png) / [High product](evidence/native/candidate-High.png)
and [Low reference](evidence/native/baseline-Low.png) / [Low product](evidence/native/candidate-Low.png)
are byte-for-byte exports, never accepted goldens.

The frozen pixel comparator's aggregate imageFailures array is empty despite
three row-level WithinTolerance=false values. Its successful structural exit
without GateImages does not certify image equivalence. This report uses the
row values and hashes; repairing the shared comparator requires serial ownership.

The unchanged 15% performance gate fails: 31 fixed, seven sweep and seven Low
metric/statistic comparisons; zero unqualified cases. No threshold was changed.
For tactical repeated vehicles, CPU median is 3.80 to 5.79 ms (High), 4.69 to
5.55 ms (sweep), and 2.84 to 4.52 ms (Low). Their strategic counterparts improve
3.14 to 1.99, 3.03 to 1.78 and 3.44 to 1.80 ms respectively. Other CPU/GPU tails
also regress. A broad GPU or sustained FPS improvement is not established.

All measured frames have zero submission faults and texture-binding failures.
Texture count/bytes and descriptor use are constant over all 128 samples in 16
fixed/Low cases. Both sweep cases still load newly demanded assets (one and
seven textures); those cases do not prove a memory plateau. Render-owned page
and pool reclamation is covered by tests; process-pool retention and local GPU
usage remain separate fields in native data. Full native frames are not globally
allocation-free: the raw values include the unchanged capture path and asset
preparation, with additional allocations during sweep demand changes. The zero
allocation claim is limited to the warmed CPU submission workloads above.

## Longer warmup and reversed-order observation

[ABBA raw evidence](evidence/native-repeat/summary.json) keeps the same source
binaries, assets, hardware and fixed views; only this separate cohort's warmup
is 1,024 frames and samples 512. Order is reference A, product A, product B,
reference B. It covers IndustrialBase, MainBattleTankProduction and
CombatSupportRepeated at both zooms in High, plus repeated vehicles in Low.
All eight captures exit zero, with 99 quiet-guard observations and no detected
foreign workload. Completion: 2026-10-20T14:04:18.1205116Z.

Tactical repeated vehicles CPU medians in ms:

| Profile | Reference A / B | Product A / B |
| --- | ---: | ---: |
| High | 4.302 / 5.149 | 3.190 / 3.578 |
| Low | 4.149 / 4.259 | 3.312 / 3.663 |

The earlier tactical CPU slowdown is not stable across this longer warmup/order
cohort; its cause is not established. Neither thermal/clock equivalence nor a
runtime-policy explanation is inferred. The first failed gate remains retained.
The unchanged 15% gate still fails in all four comparisons: A High has two CPU
and five GPU metric/statistic regressions, A Low one CPU/one GPU, B High five
CPU/seven GPU, and B Low zero CPU/one GPU. There are no unqualified cases.
Therefore this cohort also does not satisfy complete performance acceptance.

All 16 product cases have stable texture count/bytes and descriptor use over 512
samples, with zero submission and binding faults. Twelve of 16 paired images
are exact; the same tactical High/Low shadow changes remain. Human approval,
sweep demand/plateau coverage and the full CI remain open. No extra native
quality reduction, runtime-policy change or new golden was introduced.

CPU raw sample streams are also losslessly compressed as .json.gz.
raw-file-hashes.json records the original decompressed bytes; hashes.json records
the committed files. Decompress a cohort to a temporary directory before running
SummarizeComparison.py; metadata, summaries and source archives stay directly readable.


## Integrated current-master hardware check

A final separate cohort compares current master
a1610d1a51d4107c4b8202e234ed33edd3cfc9c9 (0.1.421-dev) against integrated
b69e5f72a1444bbffce149c6a71b7551a27044a4 (0.1.422-dev). Both exact clients
were rebuilt from their own source; historical cohorts are not relabelled.
The renderer-owned implementation and owned test behavior are unchanged from 60724f12;
inherited logistics/alert integration and version metadata differ.

[Integrated native evidence](evidence/integrated-native/summary.json) repeats
all 18 paired cases with the original 64-warmup/128-sample/two-repeat protocol,
same sealed runtime assets, hardware, driver, DPI and settings. All six capture
processes exit zero. The guard records 44 observations without detected foreign
workload; completion is 2026-10-20T14:29:13.1250259Z.

Fifteen pairs are pixel-exact; the same three tactical High fixed/sweep and Low
shadow cases remain outside zero tolerance. The owned controller now records
row-level outsideTolerance counts 1/1/1 explicitly in run.json. The frozen shared
comparator remains unchanged. Review PNG byte identity is separately recorded
in review-image-identity.json; source attribution stays in each manifest.

The unchanged performance gate fails 31 fixed and two sweep metric/statistic
comparisons, with zero unqualified cases. The Low gate passes in this cohort;
its prior failed results remain retained. All frames have zero submission and
texture-binding faults. All 16 fixed/Low cases have constant texture/descriptor
resources; demand-changing sweeps still do not establish their full plateau.
There is no complete performance or human image acceptance.
