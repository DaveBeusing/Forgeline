# Terrain geometry and residency results

**Implemented, not yet accepted.** Product source
`d8abff95e1134f9986d2b3f90de0eef846340527`, version 0.1.424; unchanged reference
`bfc16a953d731a844321a0848a2ebadc71c5875f`, version 0.1.423. Later evidence-only
commits do not change the measured renderer. Master was checked again against the
remote and remains the reference. Versioning is one PATCH per PR, with no tags,
releases or merge.

## Functional result

The production constructor enables immutable bounded preparation, optional
CPU-only neighbour prefetch, owner-only publication, independent payload budgets,
retained GPU LRU, shared initialized static index ranges, palette reuse and
independent conservative main/light geometry selection. Every LOD retains the
original unit boundary segments. Authoritative terrain, navigation, shader
profiles, vertex/root layouts and graphics retirement contracts are unchanged.
Interior normals retain the original heightfield kernel bit for bit; shared
edges sample both immutable neighbours. See the
[API/lifetime/error decision](BoundedTerrainPreparation.md).

| Check | Observed result |
| --- | --- |
| Final Release solution | Exit 0; zero warnings/errors; 2m10.46s |
| Owned terrain cases | 34 pass, including a reference-negative normal control |
| Required existing classes | 26 pass; also included in the full suite |
| Unchanged native WARP residency | 2 pass; also included in the full suite |
| Full solution at product source | 3,131 total: 3,107 pass, 6 fail, 18 explicit skips; exit 2 |
| Additional Graphics observation | One atmosphere capture rejected an unsettled surface; isolated class 5/5 passes |
| Presentation, Client, Assets | Complete project suites pass |
| References / ownership | 46-project graph valid; functional paths confined to the reserved lane |
| Runtime development validation | 1,357 unchanged / zero compiled; isolated cache; exit 0 |

Five full-suite failures match pre-change Game failures. The sixth is the
unsettled-surface Graphics observation, retained separately without asserting a
cause. Assertions, timeouts, thresholds and test inclusion are unchanged.
[Final functional observations](evidence/corrected-functional-checks.json) retain
the original log hashes; [binary identities](evidence/qualified-product-binaries.json)
pin all 20 compiled application assemblies. The
[normal regression control](evidence/normal-regression-control.json) fails exactly
the expected interior-normal assertion against the earlier candidate and passes
with the correction. The initial direct-runner argument error (exit 3) is recorded
separately from that expected regression failure (exit 1).

## Measurement conditions and qualification

Windows x64, SDK 10.0.401 / .NET 10.0.12, 24 logical processors; hardware D3D12 on
NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, Windows driver
32.0.15.9653 (596.53), debug layer unavailable. Runtime manifest SHA-256:
`D1786170F593B6D32FE1CF4FCE85514CAB7681F52132D47F15EF2612A3D76870`.
No GC/tiering or quality settings were changed for measurements.

Each predefined experiment runs reference/candidate/candidate/reference in four
separate processes. All invocations exit 0. CPU uses the same immutable 81-chunk
representative map and cap four. Native terrain uses the compiled 144-chunk map,
cap 128, High shadows, 1280x720, three sites at actual 120/420/1000 m zoom, fixed
and moving cameras: 18 cases per process, 128 warmups, 256 samples. GPU samples
are unique completed fences; missing timing remains null. CPU frame time includes
Present/reuse and is separate from Prepare and GPU execution.

The complete process guard **rejects the combined window**: an existing foreign
`VBCSCompiler.exe` process contributes one CPU-second at 18:39:36 UTC. Ownership
and cause are not established. No limit is waived. The original
[guard result](evidence/quiet-window-summary.json) and
[every observation](evidence/quiet-observations.jsonl) are retained.

The independently invoked complete CPU protocol ended at 18:32:03 UTC; the
complete direct GPU protocol ended at 18:36:34 UTC. Both precede the violation
and have zero busy/violation observations across their own full four-process
intervals (5 and 100 samples). The
[temporal association](evidence/phase-quiet-observations.json) assesses those
predefined whole protocols, not selected frames. It does not override the
combined rejection or establish client acceptance. Bounded process observation
cannot rule out all OS/user activity or prove equivalent thermals.

## Measured costs and residency

CPU-only owner p50 values, microseconds; pairs A and B are reported independently:

| Operation | Reference A / B | Candidate A / B |
| --- | ---: | ---: |
| Stable Prepare | 32.0 / 29.3 | 2.2 / 1.5 |
| Repeated map-teleport Prepare | 3,188.4 / 2,722.7 | 39.6 / 34.1 |
| Warm single-chunk main recording | 3.5 / 3.2 | 5.3 / 4.6 |

Stable Prepare and warm recording allocate zero owner-thread bytes. Over the 512
measured teleports, owner allocations fall from 61,546,392 bytes to
418,192 / 417,160 bytes; optional worker allocations are not included in this
owner-only counter. Accepted/harvested mesh/splat work, cancelled optional work,
uploads and asset lookup have separate raw metrics. The extra 1--2 microseconds
of warm single-chunk recording and mixed startup/cold results are real tradeoffs,
not hidden by the preparation gain. Null graphics does not establish GPU speed.

Five further map laps retain 1,905,184 prepared payload bytes, four GPU chunks,
256,224 geometry bytes and 22,880 control bytes. Post-collection managed heap
remains approximately 3.77 MB; process working set decreases from 59.6 to 49.5 MB.
These observations complement the bounded-count/byte tests; payload is not a
claim about total heap or physical GPU allocation.

The native route plateaus at 128 GPU chunks: 6,737,952 geometry and 732,160 control
payload bytes, with 8,234,160 prepared bytes. For the same 128 full vertex payloads,
the old per-chunk index representation uses 9,836,544 geometry bytes. The shared
index representation therefore reduces that logical geometry payload by 31.5%.
All current required coverage is admitted; optional prefetch adds no GPU chunks.

Strategic fixed views submit 22.66% fewer main triangles at the ridge/road,
23.85% fewer at the bridge and 24.15% fewer at industry. Close protected cases
retain their full triangle count. Shadow counts are separate. These are actual
indexed draw ranges, not estimated LOD ratios.

GPU times are **not a reproducible general improvement**: even the two identical
reference binaries produce world-time mean ratios B/A of 0.520--0.888 and
shadow-time ratios 0.568--0.946. Candidate comparisons contain both improvements
and regressions. This same-binary variance prevents causal attribution; no FPS
percentage or general GPU performance acceptance is claimed.

## Production client and images

Twelve complete matrices contain 68 cases: High/Low, tactical/strategic,
daylight/logistics, Fixed/Sweep, and one RendererCold case per process. Fixed and
Sweep use 512 warmups/256 samples; the minimal cold case uses 3/8 and does not
establish stable percentile acceptance by itself. All native case repeats are
pixel-identical within their capture. Source, runtime, requested/actual dimensions,
material/fallback and submission contracts pass structural checks.

The frozen performance comparator retains its 15% threshold. Its six advisory
pair/mode reports contain eight CPU statistic alerts: 1/1/1 for A Fixed/Sweep/Cold
and 0/4/1 for B. There are no client GPU alerts. Advisory exit 0 is not acceptance:
the client interval is in the rejected combined process window, cold distributions
are small, and some timing limits are exceeded.

The frozen pixel comparator is run at its original zero tolerance. Each of the
34 matched source pairs changes pixels; maximum changed fraction is 4.4409%,
maximum channel delta 89. The detailed `WithinTolerance` rows are checked rather
than trusting the comparator's empty aggregate `imageFailures` field. Human
geometry/lighting, seam, bridge/road contact and shadow review remains pending;
no new goldens are accepted. A close High view changes only 16 pixels by at most
one channel value, but this is not a substitute for the complete review.

## Evidence and remaining gates

- [CPU raw cohorts](evidence/cpu-pairs.zip), [CPU statistics](evidence/cpu-summary.json).
- [Native terrain raw data and original RGB exports](evidence/terrain-native-pairs.tar.xz),
  [native statistics](evidence/terrain-native-summary.json).
- [Client observations and exact RGBA pixels](evidence/client-pixels.tar.xz).
  The frozen exporter supplies portable per-frame observations. Every decoded
  pixel payload is verified against the native `pixelSha256`; original PNG hashes
  are also retained. No resampling, colour transformation or image approval occurs.
  Original PNG files remain in the immutable local capture directories.
- [Client comparator summary and alerts](evidence/client-comparisons.json).
- [CI observations](evidence/ci-observations.json).

Archives are standard ZIP or tar/xz and have adjacent SHA-256 files. Matching
camera cases are grouped for compression; every reconstructed archive member is
hash-verified. The earlier full PNG/independent-ZIP copies are retained locally
instead of adding redundant 100+ MB objects to Git. Reproduction helpers live in
`build/rendering/terrain-residency/`; all original local captures are under
`artifacts/terrain-residency/qualified-*` and are never overwritten.

For viewable client images, run
`python build/rendering/terrain-residency/ExportClientPixels.py <client-pixels.tar.xz> <fresh-directory>`.
Optional `--case <exact-key>` selects a view. The exporter verifies the native
RGBA hash and its own PNG decode round-trip. PNG containers may be re-encoded;
the pixels remain exact. This export does not approve a baseline.

Required gates still open: successful complete CI, a clean and repeatably stable
client timing protocol under unchanged limits, investigation of GPU same-binary
variance and cold/recording cost tradeoffs, and human image/contact approval.
Current source CI passes frame contracts, navigation and CPU scaling; its main
job fails five Game tests, the previously observed 8,486,912-byte submission-pool
allocation case, and an additional existing frontend deferred-loader wait timeout.
That frontend class passes locally in isolation (5/5); its cause is not asserted.
These files are outside the terrain write scope and are not silently modified.

The existing client still calls synchronous `Prepare`, which drains bounded
steps. A completely cold jump may therefore block until its union is ready.
Hard per-frame admission requires the serial host change described below; the
4 ms limit remains soft and cannot interrupt an atomic chunk or asset upload.

## Serial host integration request

`src/ForgeLine.Client/ClientRenderHost.cs` currently calls `terrainRenderer.Prepare`
immediately before world recording. A serial owner can replace this adapter with
`TryPrepareTerrainFrame(TerrainRenderer, RtsCamera, int, int,
GraphicsDirectionalShadowFrame?) -> bool`, implemented using the existing
`PrepareStep(..., maximumNewChunks: 2)` API. A false result must retain a Preparing
state, process UI/input/cancellation, and omit **all** new-view main/shadow recording
and Ready publication. Camera/shadow/viewport changes restart preparation; resize
must not render the old camera with new dimensions. A true result admits the
complete union before recording. Simulation and authorized snapshots continue
independently. Tests belong in the serial Client test scope and must exercise
cold jumps, resize while pending, cancellation/session replacement and no false
Ready publication. No such frozen host file is changed in this PR.

Central documentation integration should link the decision from Architecture /
Development and describe one shared index buffer, separate payload budgets and
the synchronous adapter versus incremental API. Those central files remain with
their serial owner.
