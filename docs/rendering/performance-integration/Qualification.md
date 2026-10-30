# Rendering performance integration qualification

Functional validation passes and all 80 native captures qualify structurally with unchanged pixels. Performance acceptance remains open: the 10k High Sweep CPU p95 is higher in both matched repeats (+32.1% and +81.8%), with substantial within-source variance and unmeasured thermal conditions. The report retains these regressions instead of declaring a combined speedup.

This serial integration retains the four merged wave-02 packages as the production baseline. Instancing, default-memory geometry, prepared correspondence and the native scaling collector were already present in named master `af8aaac0f9425b077d3a4497271495c1fce00d87` (0.1.534). Their historical reports remain source-specific and are not relabelled as combined acceptance.

The measured candidate is `968e0db1683ff30e728e20560350ebd9ea0f0242` (0.1.535). Production code changes initialize the six immutable particle indices (12 bytes) through the existing initialized static-buffer arena. The dynamic particle streams remain Upload memory. The reference script now restores an originally absent static-memory control as absent, including after a failed child process. Native and process tests reproduce both issues; no new queue, frame layout, renderer feature or acceptance threshold is introduced.

## Integration and ownership

| Package | Merged PR | Actual merge source | Interpretation |
| --- | --- | --- | --- |
| Foundation | #439 | a761969668136f32ce99438e4b6a407094c9567e | Shared contracts already integrated |
| Instanced submission | #440 | 92acfc9dbf15adf18274227301196940c2630db7 | Existing production instancing qualified; no legacy toggle invented |
| Static geometry | #441 | d1b9bcfd28bba6e318ea164ac762e3a9dfd369d4 | Existing arena qualified; particle-index handoff accepted serially |
| Snapshot correspondence | #442 | f4a5e8c573f13c3eea43ce94d61c8035ddaec6aa | Existing prepared permutation now used for unordered unique IDs |
| Native scaling | #443 | af8aaac0f9425b077d3a4497271495c1fce00d87 | Seven synthetic scenes and schema-2 collector; not a renderer optimization |

[Ownership.json](Ownership.json) defines the explicit serial write set after all package owners completed. The historical foundation ownership manifest remains unchanged. Graphics contracts, the 112-byte instance layout, separate main/shadow storage, owner-thread affinity, Prepare/Record/Submit/Retire, queues/budgets, authoritative simulation state and player authorization remain unchanged.

## Measurement protocol and limits

Windows x64, Intel Core Ultra 9 285HX (24 logical processors), NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU (25,309,478,912 declared dedicated bytes), NVIDIA driver 32.0.15.9653; Windows 10.0.26300 and .NET 10.0.13, SDK 10.0.402. The Intel secondary adapter is not the measured device. Driver, actual adapter, native surface, controls, build and payload are also recorded per manifest. Balanced power was observed; thermals and background load were not traced or fixed.

The same freshly compiled sealed runtime payload is shared by both immutable clients. Binary and payload inventories before/after every matrix bind all input bytes to SHA-256. These source-labelled clients use the existing native reference entry point; no substitute collector or falsified build identity is used. Timing runs follow all builds/tests and disable debug validation, DRED and software rendering.

The matrix uses Medium, native 1920×1080, seed 2026, 128 discarded warmup frames, 512 measured CPU frames and two deterministic readbacks per case. Seven scaling scenes (1k/10k/100k High/Low plus terrain-only) run Fixed and Sweep version 1 at the normal 420m bookmark. IndustrialBase and BattlefieldCombat run at CloseTactical 120m and Strategic 1000m. Independent reversed-order repeats provide baseline/current and within-source variance. Four production cohorts compare the current static-memory Default/Upload/Upload/Default control with all other inputs held equal. This control changes all existing static geometry paths; it does not isolate the 12-byte particle handoff.

CPU whole-frame times include present and frame reuse. Preparation/record/submit/retire, present and waits are observed subsets that overlap the whole frame; they must not be summed. GPU times use distinct completed fences and actual timestamp frequency; the latest-completion API may skip intermediate completions, so actual valid counts are retained. GPU pass distributions also overlap. DXGI process usage/budgets, declared adapter capacity and logical payload bytes are distinct; no total-VRAM reduction is inferred. Allocation observations, instance upload traffic, retained skin capacity, terrain uploads and startup/setup are separate records.

The named baseline already contains all wave-02 optimizations. Historical one-feature CPU evidence remains in its original package reports; it cannot establish an isolated FPS gain for the integrated source. No legacy instancing switch or synthetic parent speedup is fabricated. The historical correspondence parent failed reversed-ID correctness, so its fast incomplete work is not a performance baseline.

Native readbacks use immutable authorized presentation fixtures, frozen visual time and the existing main/shadow paths. They do not measure a natural animated match or simulation throughput. Exact image differences and structural checks do not grant human art acceptance. Debug layer/PIX/DXGI live-object instrumentation is unavailable on this host; native tracked-resource plateau and fence tests are evidence of bounded lifetime, not a debug-layer live-object report.

Historical lane evidence remains separately attributable: [instanced submission](../instance-submission/), [static geometry and scheduling](../resource-scheduling/), [correspondence qualification](../snapshot-publication/CorrespondenceQualification.md), and [native scaling qualification](../qualification/ScalingQualification.md). Their parent/current sources and sample scopes must be read in those records; they are not additional repetitions of the matrix below.

Image review of the synthetic 10k High native readback shows a very small central crate-grid footprint on the terrain. These repeated-crate fixtures exercise populations, correspondence, culling and submission; their high root counts do not imply representative screen coverage, diverse materials, mechanics, skinning, wind or an army-sized visual workload. Production IndustrialBase/BattlefieldCombat views remain a separate matrix. High/Low labels are supplemented by actual main and shadow counters, including the existing shadow-caster budget. The host renders alpha=1 over populated immutable history; intermediate-alpha correspondence performance belongs to the historical CPU lane.

Native candidate readbacks reviewed directly: IndustrialBase CloseTactical retains textured structures/terrain, selection rings and directional shadows; BattlefieldCombat CloseTactical retains vehicles/infantry, effects and selection overlays; its Strategic view reduces screen coverage/LOD and exposes the existing terrain boundary. No fallback mesh or texture-binding failure is reported. These observations complement the full presentation privacy/visibility tests; the images alone do not prove authorization filtering or grant art acceptance. Exact baseline/current and repeat pixel reports remain the auditable comparison.

This report does not close the broader historical [complete-acceptance handoff](../qualification/IntegrationRequirements.md). Full live-host envelope/process-wide allocations, original GPU begin/end ticks and completed-fence readbacks, complete live/retiring/staging/upload byte accounting, a 30-minute moving lifecycle soak, physical thermal/clock stability logs, previously reviewed common budget policies and explicit live-animation/art review remain separate evidence requirements. The bounded native lifecycle tests, derived fence-qualified reference timings and DXGI usage reported here are not substitutes for those missing records. Required CI is also source-specific and is reported separately from local validation. Historical SDK/command examples in that document remain source-specific; this run uses the actual pinned SDK and CI command recorded here.

## Canonical and native validation

All production code is identical between canonical source b232a6a7e3f06b9f10c46fe1c489d0c4a93e6b88 and measured source 968e0db1683ff30e728e20560350ebd9ea0f0242; the later commit adds the native surface recovery test and corrects the documented CI command. The final complete solution gate runs at the measured source.

| Check | Result |
| --- | --- |
| Version against origin/master | 0.1.534 → 0.1.535, exactly one PATCH increment |
| Project references | 46 projects, valid graph |
| Complete Release build | Passed, zero warnings/errors |
| Compiled Central Divide and scenario contracts | Passed |
| Clean runtime build and source-less runtime contracts | Passed, 2,055 assets, compiler 1.13.0 |
| Native client smoke and headless smoke/stress | Passed; native render-stress 1,000, headless 1,000/10,000 entities |
| Reproducible local package | Passed; no external release |
| Natural terminal match | Passed; seed 2026, winner 1 at tick 59,098 within 80,000 ceiling |
| Complete solution, unchanged CI scheduling | 4,882 total: 4,801 passed, 81 skipped, 0 failed; 29m17.598s |
| Focused graphics lifetime | 60 passed, 3 debug-layer skips at canonical source; later recovery test also passes |
| Focused correspondence/submission/collector | Passed; original logs retained |
| Actual Win32 minimize/restore + 320×240→640×360→320×240 | Passed; no extra static transfers/allocations, suspended callback excluded |
| Partial particle initialization and early disposal | Passed; device reusable, resources retire at actual fences |
| Required remote CI on final PR | Pending exact-source CI; local results do not substitute |

The first solution invocation followed the stale unbounded command in Development.md: 4,881 total, 4,798 passed, 81 skipped, two Client bounded-wait failures. Both tests pass when the complete Client module runs alone (610 passed, two skipped), and the final unfiltered full suite passes with the existing CI command. CI already used serial test modules; only the documentation changed. No assertion, deadline, test selection or terminal condition was relaxed. Initial failed attempts, theory/debug skips and final successes remain together in the validation archive.

The new particle regression tests fail against the original Upload index construction (three failures, one debug skip), then pass after the production change (three passes, one debug skip). Five process-level script cases cover absent and existing environment controls through actual script failure. Capacity growth, delayed fence reuse, session restart and shutdown are additionally exercised by the complete native/client/presentation modules and focused lifecycle cases. Debug-layer unavailability remains explicit.

## Completed matrix and valid samples

80 cases retain 40,960 CPU frames and 40,742 distinct completed GPU observations. 11 unique original PNGs cover the content-addressed image archive. Qualification errors: 0. CPU/GPU/frame/pass sample counts remain explicit in summary.json and the original manifests.

| Cohort | CPU samples | GPU samples | Process startup ms | Startup managed allocation MiB |
| --- | --- | --- | --- | --- |
| baseline-a-production | 2048 | 2040 | 3476.010 | 173.161 |
| baseline-a-fixed | 3584 | 3572 | 2797.953 | 443.381 |
| baseline-a-sweep | 3584 | 3559 | 2705.298 | 443.188 |
| candidate-a-fixed | 3584 | 3543 | 3336.255 | 443.272 |
| candidate-a-sweep | 3584 | 3568 | 2499.261 | 443.189 |
| candidate-a-production | 2048 | 2038 | 3119.798 | 173.177 |
| memory-upload-a | 2048 | 2038 | 3616.805 | 173.010 |
| memory-upload-b | 2048 | 2038 | 3343.456 | 173.235 |
| candidate-b-production | 2048 | 2040 | 4293.703 | 173.036 |
| candidate-b-sweep | 3584 | 3571 | 2626.688 | 443.281 |
| candidate-b-fixed | 3584 | 3562 | 2690.304 | 443.171 |
| baseline-b-sweep | 3584 | 3565 | 3055.817 | 443.244 |
| baseline-b-fixed | 3584 | 3568 | 2758.995 | 443.237 |
| baseline-b-production | 2048 | 2040 | 3474.486 | 172.977 |

## Matched whole-frame p95

All times below are milliseconds. Each cell is CPU / GPU p95, kept separate. Complete mean/p50/p95/p99 distributions, valid counts and per-frame chronology remain in the retained JSON. The A/B values are independent runs; they are not pooled into a confidence interval.

| Case / camera | Baseline A | Current A | Current B | Baseline B |
| --- | --- | --- | --- | --- |
| production/IndustrialBase-CloseTactical | 17.741 / 14.109 | 17.869 / 14.406 | 17.739 / 14.389 | 17.927 / 14.379 |
| production/IndustrialBase-Strategic | 19.317 / 13.887 | 19.141 / 13.575 | 19.175 / 13.334 | 19.516 / 13.791 |
| production/BattlefieldCombat-CloseTactical | 20.443 / 16.845 | 20.588 / 16.951 | 20.391 / 17.029 | 19.709 / 16.840 |
| production/BattlefieldCombat-Strategic | 20.238 / 16.299 | 20.873 / 16.608 | 20.243 / 16.192 | 19.977 / 16.146 |
| fixed/Scaling1000High-NormalGameplay | 16.580 / 13.025 | 16.455 / 12.961 | 16.339 / 12.789 | 16.350 / 12.886 |
| fixed/Scaling1000Low-NormalGameplay | 16.378 / 12.548 | 16.048 / 12.751 | 15.966 / 12.551 | 16.174 / 13.016 |
| fixed/Scaling10000High-NormalGameplay | 28.893 / 7.742 | 19.190 / 10.769 | 26.947 / 10.200 | 24.027 / 9.000 |
| fixed/Scaling10000Low-NormalGameplay | 16.029 / 12.735 | 16.250 / 12.841 | 15.851 / 12.597 | 16.097 / 12.570 |
| fixed/Scaling100000High-NormalGameplay | 263.273 / 17.726 | 293.827 / 18.062 | 292.108 / 17.706 | 315.838 / 17.974 |
| fixed/Scaling100000Low-NormalGameplay | 42.637 / 6.776 | 38.543 / 6.735 | 39.632 / 6.729 | 45.664 / 6.694 |
| fixed/ScalingTerrainOnly-NormalGameplay | 15.768 / 12.243 | 16.101 / 12.649 | 16.046 / 12.726 | 15.866 / 11.807 |
| sweep/Scaling1000High-NormalGameplay-Sweep | 16.555 / 12.969 | 16.097 / 12.831 | 15.949 / 12.487 | 16.191 / 12.756 |
| sweep/Scaling1000Low-NormalGameplay-Sweep | 15.983 / 12.811 | 16.422 / 12.983 | 16.235 / 13.004 | 16.309 / 13.044 |
| sweep/Scaling10000High-NormalGameplay-Sweep | 20.802 / 10.046 | 27.481 / 7.538 | 48.490 / 9.356 | 26.668 / 8.581 |
| sweep/Scaling10000Low-NormalGameplay-Sweep | 16.318 / 12.964 | 16.495 / 13.011 | 16.512 / 13.037 | 16.673 / 13.020 |
| sweep/Scaling100000High-NormalGameplay-Sweep | 251.333 / 17.700 | 258.096 / 17.967 | 287.667 / 18.185 | 294.882 / 18.329 |
| sweep/Scaling100000Low-NormalGameplay-Sweep | 43.440 / 6.743 | 45.551 / 6.764 | 44.707 / 6.700 | 36.230 / 6.700 |
| sweep/ScalingTerrainOnly-NormalGameplay-Sweep | 16.096 / 12.808 | 15.875 / 12.117 | 16.028 / 12.682 | 15.991 / 12.207 |

## CPU phases and blocking

Current A p95 in milliseconds. Present/reuse and Prepare/Record/Submit/Retire overlap whole-frame CPU time and must not be added. These are observed phase boundaries of this collector, not a simulation or worker-utilization profile.

| Case / camera | Prepare | Record | Submit | Retire | Present | Reuse wait |
| --- | --- | --- | --- | --- | --- | --- |
| Fixed/Scaling1000High-NormalGameplay | 0.859 | 1.503 | 0.000 | 0.001 | 0.200 | 14.693 |
| Fixed/Scaling1000Low-NormalGameplay | 0.104 | 0.218 | 0.000 | 0.001 | 0.205 | 15.231 |
| Fixed/Scaling10000High-NormalGameplay | 6.963 | 11.605 | 0.000 | 0.001 | 0.204 | 0.000 |
| Fixed/Scaling10000Low-NormalGameplay | 0.912 | 1.582 | 0.000 | 0.001 | 0.215 | 13.775 |
| Fixed/Scaling100000High-NormalGameplay | 109.234 | 181.603 | 0.000 | 0.002 | 1.069 | 0.001 |
| Fixed/Scaling100000Low-NormalGameplay | 9.307 | 24.878 | 0.000 | 0.001 | 0.282 | 0.001 |
| Fixed/ScalingTerrainOnly-NormalGameplay | 0.003 | 0.007 | 0.000 | 0.000 | 0.206 | 15.603 |
| Sweep/Scaling1000High-NormalGameplay-Sweep | 0.878 | 1.485 | 0.000 | 0.001 | 0.208 | 14.187 |
| Sweep/Scaling1000Low-NormalGameplay-Sweep | 0.106 | 0.221 | 0.000 | 0.001 | 0.204 | 15.611 |
| Sweep/Scaling10000High-NormalGameplay-Sweep | 9.355 | 16.810 | 0.000 | 0.001 | 0.284 | 0.001 |
| Sweep/Scaling10000Low-NormalGameplay-Sweep | 0.910 | 1.589 | 0.000 | 0.001 | 0.205 | 14.433 |
| Sweep/Scaling100000High-NormalGameplay-Sweep | 97.063 | 160.420 | 0.000 | 0.001 | 1.001 | 0.001 |
| Sweep/Scaling100000Low-NormalGameplay-Sweep | 11.223 | 29.150 | 0.000 | 0.001 | 0.304 | 0.001 |
| Sweep/ScalingTerrainOnly-NormalGameplay-Sweep | 0.003 | 0.007 | 0.000 | 0.000 | 0.196 | 15.505 |
| Fixed/IndustrialBase-CloseTactical | 0.203 | 0.419 | 0.000 | 0.001 | 0.235 | 16.357 |
| Fixed/IndustrialBase-Strategic | 0.302 | 0.573 | 0.000 | 0.001 | 0.227 | 17.699 |
| Fixed/BattlefieldCombat-CloseTactical | 0.199 | 0.426 | 0.000 | 0.001 | 0.248 | 18.870 |
| Fixed/BattlefieldCombat-Strategic | 0.599 | 0.977 | 0.000 | 0.002 | 0.378 | 18.840 |

## GPU pass observations

Current A production p95 in milliseconds. Null means unavailable/no eligible pass samples. Passes overlap; atmosphere/composite can describe the same work.

| Case | World | Overlay | Composite | Shadow | Particle | Atmosphere |
| --- | --- | --- | --- | --- | --- | --- |
| IndustrialBase-CloseTactical | 10.099 | 2.358 | 2.956 | 3.207 | n/a | 2.956 |
| IndustrialBase-Strategic | 11.038 | 1.498 | 4.182 | 0.641 | n/a | 4.182 |
| BattlefieldCombat-CloseTactical | 9.227 | 5.246 | 5.183 | 3.287 | 0.096 | 5.183 |
| BattlefieldCombat-Strategic | 8.802 | 2.954 | 5.855 | 3.287 | 0.094 | 5.855 |

## Upload and memory observations

Current A fixed/production views. Visible roots, actual reduced-LOD roots and last observed shadow casters are distinct populations. Instance bytes are per-frame traffic; skin upload fields retained in the raw records are capacity observations. DXGI values are local process usage, not total VRAM. All per-frame budgets, loaded textures, live/retired resources, setup allocations and terrain upload counters remain in the original manifests.

| Case | Visible roots | Reduced LOD | Shadow casters | Instance upload B/frame max | Terrain uploads first/last | DXGI min MiB | DXGI max MiB | Managed B/frame p95 | Renderer setup ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Scaling1000High-NormalGameplay | 1000 | 1000 | 1024 | 112000 | 24 / 24 | 288.617 | 289.617 | 56.000 | 4025.342 |
| Scaling1000Low-NormalGameplay | 100 | 100 | 124 | 11200 | 24 / 24 | 289.617 | 289.617 | 56.000 | 2896.683 |
| Scaling10000High-NormalGameplay | 10000 | 10000 | 4096 | 1120000 | 24 / 24 | 289.617 | 289.617 | 56.000 | 2864.689 |
| Scaling10000Low-NormalGameplay | 1000 | 1000 | 1024 | 112000 | 24 / 24 | 289.617 | 289.617 | 56.000 | 2620.498 |
| Scaling100000High-NormalGameplay | 100000 | 100000 | 4096 | 11200000 | 24 / 24 | 289.617 | 289.617 | 458832.000 | 3454.598 |
| Scaling100000Low-NormalGameplay | 10000 | 10000 | 4096 | 1120000 | 24 / 24 | 289.617 | 289.617 | 56.000 | 3208.046 |
| ScalingTerrainOnly-NormalGameplay | 0 | 0 | 24 | 0 | 24 / 24 | 289.617 | 289.617 | 56.000 | 2350.159 |
| IndustrialBase-CloseTactical | 34 | 0 | 218 | 4256 | 24 / 24 | 289.617 | 289.617 | 56.000 | 4754.146 |
| IndustrialBase-Strategic | 260 | 256 | 214 | 29120 | 41 / 41 | 291.543 | 291.543 | 56.000 | 3013.042 |
| BattlefieldCombat-CloseTactical | 32 | 4 | 278 | 10976 | 24 / 24 | 289.680 | 289.680 | 440.000 | 3372.934 |
| BattlefieldCombat-Strategic | 258 | 254 | 216 | 29344 | 41 / 41 | 291.605 | 291.605 | 56.000 | 2555.717 |

## Static-memory production control

Same current source, payload, four production views and frame counts: Default / Upload / Upload / Default. This control affects the existing static-memory paths together. It does not isolate the 12-byte particle handoff, prove a memory-system gain, or qualify synthetic Upload scaling. Each value is CPU / GPU p95 milliseconds.

| Case | Default A | Upload A | Upload B | Default B |
| --- | --- | --- | --- | --- |
| IndustrialBase-CloseTactical | 17.869 / 14.406 | 28.030 / 25.534 | 28.289 / 25.492 | 17.739 / 14.389 |
| IndustrialBase-Strategic | 19.141 / 13.575 | 25.172 / 20.943 | 24.787 / 20.878 | 19.175 / 13.334 |
| BattlefieldCombat-CloseTactical | 20.588 / 16.951 | 36.470 / 33.742 | 36.911 / 33.908 | 20.391 / 17.029 |
| BattlefieldCombat-Strategic | 20.873 / 16.608 | 24.631 / 23.282 | 24.346 / 23.282 | 20.243 / 16.192 |

## Regressions, repeats and image differences

The existing advisory threshold remains 15%. The following counts are individual CPU/GPU mean/p50/p95/p99 metrics above that threshold, not failed frames or independent statistical trials. Within-source repeat changes disclose noise/order dependence. No portable performance acceptance or isolated optimization gain is granted from these observations.

| Comparison | Metrics >15% | Unqualified cases |
| --- | --- | --- |
| baseline-b-fixed-vs-baseline-a-fixed.performance.json | 3 | 0 |
| baseline-b-production-vs-baseline-a-production.performance.json | 0 | 0 |
| baseline-b-sweep-vs-baseline-a-sweep.performance.json | 6 | 0 |
| candidate-a-fixed-vs-baseline-a-fixed.performance.json | 4 | 0 |
| candidate-a-production-vs-baseline-a-production.performance.json | 0 | 0 |
| candidate-a-sweep-vs-baseline-a-sweep.performance.json | 3 | 0 |
| candidate-b-fixed-vs-baseline-b-fixed.performance.json | 1 | 0 |
| candidate-b-fixed-vs-candidate-a-fixed.performance.json | 4 | 0 |
| candidate-b-production-vs-baseline-b-production.performance.json | 0 | 0 |
| candidate-b-production-vs-candidate-a-production.performance.json | 0 | 0 |
| candidate-b-sweep-vs-baseline-b-sweep.performance.json | 3 | 0 |
| candidate-b-sweep-vs-candidate-a-sweep.performance.json | 5 | 0 |
| memory-upload-a-vs-candidate-a-production.performance.json | 32 | 0 |
| memory-upload-b-vs-candidate-b-production.performance.json | 32 | 0 |
| memory-upload-b-vs-memory-upload-a.performance.json | 0 | 0 |

| Strict image comparison | Cases with changed pixels | Image-gate failures |
| --- | --- | --- |
| baseline-b-fixed-vs-baseline-a-fixed.images.json | 0 | 0 |
| baseline-b-production-vs-baseline-a-production.images.json | 0 | 0 |
| baseline-b-sweep-vs-baseline-a-sweep.images.json | 0 | 0 |
| candidate-a-fixed-vs-baseline-a-fixed.images.json | 0 | 0 |
| candidate-a-production-vs-baseline-a-production.images.json | 0 | 0 |
| candidate-a-sweep-vs-baseline-a-sweep.images.json | 0 | 0 |
| candidate-b-fixed-vs-baseline-b-fixed.images.json | 0 | 0 |
| candidate-b-fixed-vs-candidate-a-fixed.images.json | 0 | 0 |
| candidate-b-production-vs-baseline-b-production.images.json | 0 | 0 |
| candidate-b-production-vs-candidate-a-production.images.json | 0 | 0 |
| candidate-b-sweep-vs-baseline-b-sweep.images.json | 0 | 0 |
| candidate-b-sweep-vs-candidate-a-sweep.images.json | 0 | 0 |
| memory-upload-a-vs-candidate-a-production.images.json | 0 | 0 |
| memory-upload-b-vs-candidate-b-production.images.json | 0 | 0 |
| memory-upload-b-vs-memory-upload-a.images.json | 0 | 0 |

Image gates were not requested: structural checks, source-labelled comparisons and exact-pixel observations are retained; human visual acceptance remains pending. The images are original native PNGs with a case/cohort-to-SHA mapping, not resized or edited derivatives.

Across baseline/current matched pairs, CPU p95 deltas span -33.6% to +81.8% and GPU p95 -25.0% to +39.1%. The 10k High Sweep CPU regression appears in both pairs; visible roots remain 10,000, instance uploads 1,120,000 bytes/frame, and managed allocation p95 56 bytes/frame. Its Prepare/Record tails both increase; Present/fence waits remain small. The cause is not established by these records, so it is not dismissed as noise. Baseline and current repeats also vary, and clock/thermal traces are unavailable. The four production views show no metric over the unchanged 15% threshold in either baseline/current pair.

Upload production controls are consistently slower in these paired observations: CPU p95 +18.0% to +81.0%, GPU p95 +40.2% to +99.1% relative to adjacent Default cohorts. All paired control images are exact matches. This is an observation of the existing static-memory diagnostic on this adapter, not a newly introduced instancing/default-geometry capability or an isolated 12-byte particle performance gain. Existing optimized defaults remain enabled.

## Reproduction and retained evidence

[Summary.json](Summary.json) contains all 80 case summaries, phase/pass distributions, counters and all 15 advisory comparisons. [Run-Comparisons.ps1](Run-Comparisons.ps1), [RequestTemplate.json](RequestTemplate.json), [Summarize-Comparisons.py](Summarize-Comparisons.py), [Generate-ReportTables.py](Generate-ReportTables.py), [Archive-Validation.py](Archive-Validation.py) and [Validate-IntegrationScope.py](Validate-IntegrationScope.py) preserve the executable protocol and retention method.

Build the two full named source checkouts in Release with the pinned SDK, restore/runtime assets as required, and copy their complete Client output directories to the frozen-baseline/frozen-current paths below. Both DLL informational identities must end with their exact source SHA. The runtime root is the same freshly compiled sealed payload for both roles: fingerprint 55827DADD0271F6F74F0FB66E2B2774C924C38FC106B6EA351C43BF9A694EBBA, content signature 135b44877d4882ac539073738a43af7b4794ff35819cc2c1f55c353771743020. Per-file binary/runtime SHA-256 inventories are retained before and after the entire matrix and compare unchanged.

The original invocation used artifact copies of the published runner/template. The raw archive retains their original bytes and hashes. Copy the published files to those paths to use the same invocation on a clean checkout:

```powershell
Copy-Item docs/rendering/performance-integration/Run-Comparisons.ps1 artifacts/performance-integration/Run-Comparisons.ps1
Copy-Item docs/rendering/performance-integration/RequestTemplate.json artifacts/performance-integration/RequestTemplate.json
pwsh -NoProfile -File artifacts/performance-integration/Run-Comparisons.ps1 -RepositoryRoot . -BaselineClientDirectory artifacts/performance-integration/frozen-baseline -BaselineCommit af8aaac0f9425b077d3a4497271495c1fce00d87 -CurrentClientDirectory artifacts/performance-integration/frozen-current -CurrentCommit 968e0db1683ff30e728e20560350ebd9ea0f0242 -RuntimeAssetRoot assets/runtime -RequestTemplate artifacts/performance-integration/RequestTemplate.json -OutputRoot artifacts/performance-integration/cohorts-v1 -Warmups 128 -Samples 512
```

The original capture runner completed all 14 native processes with exit 0 and verified unchanged inputs, then exited 1 because its Compare function collided with PowerShell’s Compare-Object alias. This postprocessing defect did not change any native record. The published runner uses the unique Invoke-MatchedComparison name. The original runner, corrected runner, failed postprocessing log and successful recovery log remain in raw-evidence.zip/protocol. The actual recovery command was:

```powershell
pwsh -NoProfile -File artifacts/performance-integration/Complete-Comparisons.ps1 -RepositoryRoot . -OutputRoot artifacts/performance-integration/cohorts-v1
```

Recovery completed all 15 performance and strict zero-tolerance pixel comparisons with exit 0, zero unqualified cases and zero changed pixels. Performance comparison was advisory, image gates were not requested, and human visual acceptance remains pending. No original native manifest, PNG or measurement window was rewritten or selectively rerun.

The canonical command script and every original step log are in validation-evidence.zip. The actual final CI-equivalent gate and retention commands are:

```powershell
dotnet test --solution ForgeLine.sln -c Release --no-build --max-parallel-test-modules 1
python artifacts/performance-integration/Summarize-Comparisons.py --repository-root . --cohorts artifacts/performance-integration/cohorts-v1 --output artifacts/performance-integration/report-v1
python artifacts/performance-integration/Archive-Validation.py --root artifacts/performance-integration --output artifacts/performance-integration/validation-report-v1
python artifacts/performance-integration/Generate-ReportTables.py artifacts/performance-integration/report-v1/summary.json artifacts/performance-integration/ReportTables.md
```

Request copies in the raw archive replace only absolute output/runtime paths with portable macros; original request hashes remain recorded. Native manifests and comparison records retain their original bytes. ZIP indexes bind every retained record to SHA-256, while the images archive maps cohort/case to 11 original full-resolution PNG hashes without editing. Archive regeneration can change ZIP metadata; record/image hashes identify original evidence.

| Archive | Bytes | SHA-256 |
| --- | --- | --- |
| [raw-evidence.zip](evidence/raw-evidence.zip) | 7330035 | cd76f11ded00a1927e6f5d16915a6a6f2937a2a76c4107426f2d597025a0d887 |
| [images.zip, eight verified parts](evidence/Images.json) | 51080078 | abf51d814f5d958edf49354028dbceb9996d1c758d996f6f49743af8bf78621c |
| [validation-evidence.zip](evidence/validation-evidence.zip) | 804553 | 0e21f7fea53c6f335df58ae91b582d9f61be47f9ff8783217cdb69a3e28d5b9a |

GitHub rejected the 51,080,078-byte ZIP through its blob API as too large. The identical ZIP is therefore stored as eight byte-range parts listed in [Images.json](evidence/Images.json). [Join-ImageEvidence.py](Join-ImageEvidence.py) verifies every part and the full original SHA-256 while restoring it; no PNG or ZIP bytes are edited. Reassemble before normal ZIP extraction:

```powershell
python docs/rendering/performance-integration/Join-ImageEvidence.py --manifest docs/rendering/performance-integration/evidence/Images.json --output artifacts/performance-integration/restored-images.zip
```

The separately published Summary.json uses LF text line endings. The original generated summary bytes, original native manifests and original comparison records remain hash-bound inside raw-evidence.zip.
