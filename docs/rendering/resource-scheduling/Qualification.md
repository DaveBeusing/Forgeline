# Graphics resource scheduling qualification

Status: implemented, not accepted. The final fixed performance cohort retains
an open CPU-tail gate. The repaired complete local suite passes; actual debug-layer,
human visual and required final PR checks remain open. No integration, tag or release is performed.

## Baseline and scope

Remote master f2be6532d759ab0840f2a4674c28d00446fe75ea, product 0.1.418.
Foundation acceptance PR #241 is integrated with successful required checks and
explicit native equality review. Its manifest releases eight disjoint owners.
No existing resource-scheduling branch or PR required continuation. Existing
cargo/version branches have separate ownership. A pre-existing local ignore-file
edit is excluded from this change.

Master CI 39396697547 fails only at version validation: expected 0.1.419, received
0.1.418. Later build/native/test gates are skipped; no current-master runtime
failure is inferred. Its complete failed-step output is retained locally. The
required one-PATCH delta for this PR is prepared against then-current master.

An unchanged Release Windows x64 reference client is built from this source into
an isolated artifacts root before product edits. Baseline and changed clients
must use the same sealed runtime root, cameras, driver and quality for comparisons.

## Implemented boundaries

A render-owner upload arena owns three 4 MiB slots and one on-demand large slot,
rounded to 4 MiB and capped at 128 MiB. Maximum retained staging is 140 MiB,
four open/in-flight batches and 32 transfers per batch. Requests beyond 128 MiB
fail before allocating the destination; they are an explicit authoring/runtime
budget error. No pending slot resets before its actual completed fence. Pressure
waits for the oldest relevant slot with the unchanged ten-second fence bound.

Textures copy rows directly into mapped native staging, using device-provided
footprints, 512-byte placements and format-specific row sizes, including BC7
block rows. RGBA8/R8/BC7 mip semantics and descriptors remain unchanged. Deferred
transfers batch until space/count pressure, first resource binding, frame execute
or idle/shutdown. CPU input is consumed before return. No command context, Span
or staging pointer escapes its owner. Immediate texture uploads still wait.

Initialized static buffers now use Default memory and CopyDest-to-vertex/index
barriers behind the existing interface. Dynamic CreateBuffer/upload callers stay
unchanged. FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK=1 selects the existing Upload
implementation on device creation for explicit comparison/recovery.

Execute-before-Signal failures mark the batch completion unknown and fault the
session. Pending destinations and pool objects remain owned until proven queue
idle or confirmed removal. Existing sticky-fault shutdown and process-rooted
retention remain in force. Device disposal releases the pool only at the same
safe shutdown boundary as other native objects.

Context objects remain per callback: reusing one would let stale references regain
validity. Identical pipeline and texture-table bindings are cached, with frame,
root, shadow, particle and pass invalidation. A bounded sixteen-reference texture
cache is device-owned; a context retains only a heap-bound flag. Ownership and
lifetime checks precede cache lookup. Buffers, offsets and frame constants are
not elided. Memory-budget queries are sampled at most every 250 ms;
internal upload diagnostics report sample age separately without changing public
metrics or querying DXGI for individual counters.

FORGELINE_DRED=1 opts into process-wide breadcrumb/page-fault configuration before
device creation. Unsupported interfaces are reported. On actual removal, bounded
HRESULT status and bounded copied breadcrumb/allocation-type metadata are emitted
once; no addresses, object names, native objects or memory are dumped. Traversal
is capped at sixteen nodes and sixteen operations per node. Run this
opt-in in a separate process; it is excluded from performance baselines.

## Executed checks

The first 20-case scheduling run reports 19 passes and one fixture accounting
failure: it expected zero live objects while its pipeline was intentionally still
alive. The fixture comparison now uses the pre-upload live-object baseline.
The corrected run passes all 20 cases, zero failures/skips (6.645 seconds).
This includes actual NVIDIA hardware readbacks comparing initialized Default and
Upload buffers, CPU-input mutation after return, RGBA8/R8/BC7 odd/padded rows and
small mips, two sets of 100 load/unload cycles, failed staging growth and injected
Close/Execute/Signal upload failures. Failed and corrected logs remain distinct.

These checks do not yet replace native before/after timing, full suites, debug-layer
qualification, large-transfer pressure, pass changes, resize or final PR CI. No
universal FPS improvement or device-loss recovery is claimed.


## Integration and native root-state repair

Master advanced to e42f4e7537b84c56c9dbdafe56f03f1e5e74270d during work and was
integrated without conflicts. Its changes are version/workflow/HUD-test/documentation
changes; the production source, assets and compiler trees are identical to f2be6532.
The PR therefore has one PATCH from the new master 0.1.419 to 0.1.420.

All 28 expanded scheduling contracts passed, zero failures/skips (9.921s).
The complete solution Release build on 0cc31d62 passes with zero warnings/errors
(23.69s), and runtime source compilation/qualification succeeds.

A subsequent full Graphics run on that source aborts at WARP ExecuteCommandList
with 0xC0000005 in the existing perspective-depth particle test. The new pipeline
cache incorrectly moved ValidateParticlePipeline before SetGraphicsRootSignature;
that method also binds a root descriptor table, so it requires the new root first.
The original order is restored. Repeated identical pipeline bindings still execute
the particle validation/binding against the already active matching root. A native
particle regression covers the opaque-to-particle transition and exact repeat image.
The complete failed log is retained and is not treated as a passed run.

After repair, the focused particle/native selection passes all 22 cases. The full
Graphics module passes 109 cases, zero failures and three optional skips / 112 total
(20.964s). Two lifetime cases require explicit opt-in; the debug clear case reports
the unavailable D3D12 debug layer. This is not a claim of debug-layer acceptance.
The final regression fixture also binds a real vertex buffer for its opaque draw.

The validated original-binary upload probe records 67,531,680 managed bytes for
256 uploads and 14,336 bytes for 256 empty frames (56 bytes/frame). This preliminary
host check is not a paired performance acceptance. Fixed clean paired measurements
will use the committed probe, preserve chronological samples and separately record
CPU recording, final drain and unavailable GPU-copy time. No frame-context lifetime
is weakened to remove the semantically necessary context allocation.


## First fixed performance protocol and binding refinement

The first predeclared B-A-A-B sequence uses baseline f2be6532 and repaired source
40cf69a0. All twelve paired native images are exact; every structural/resource
case qualifies. Both unchanged 15% gates fail: pair one has four CPU regressions
(all strategic tank CPU mean/p50/p95/p99), pair two has three CPU regressions
(tank close p95, tank normal p50, industrial strategic p50). No GPU metric
regresses. Complete before/after matrices, per-frame observations and monitors
are preserved; these failures are not overridden by the upload gains.

Both paired upload runs reduce managed bytes from 67,531,680 to 157,696 for 256
uploads. Recording windows are 287.198 -> 95.981 ms and 242.724 -> 130.767 ms;
final drain is recorded separately. The native pool plateaus at 8 MiB. Empty
frames remain 56 bytes each. This is a measured upload workload, not a universal
FPS or GPU-copy timing claim.

Phase data show variation across Prepare, Record and overlay work; they do not
establish one cause for all CPU tails. The next source refinement elides redundant
shader-heap and identical texture-table bindings. Its bounded sixteen-reference
cache is device-owned, reset for every frame, new root and pass/native transition.
Owner/lifetime checks precede lookup; disposed/recycled wrappers cannot hit by
descriptor index. Constants, offsets and buffer bindings remain unconditional.
The context object is still per callback, without an additional cache array per
frame. An inactive upload arena also avoids a hot call into the submission path.

The complete refined Graphics suite passes 111 tests, zero failures, three
optional skips / 114 total (26.652s). Tests include root changes, pass changes,
exact shared-texture output and disposed cached-texture rejection. A test-loop
Analyzer failure was corrected before this passing build; no rule was suppressed.
A new paired protocol is required for this changed source, preserving all first
protocol results. No timer, sample, image or regression threshold is relaxed.


## Extended fault metadata and isolated baseline failures

The opt-in DRED collector now records at most sixteen breadcrumb nodes, with a
sixteen-operation window around each completion marker, plus sixteen existing and
sixteen freed allocation types. Page-fault presence is a boolean. It serializes
only selected numeric metadata, never names, native objects, pointers or memory.
Unsupported interfaces/HRESULTs remain explicit. Actual device removal is not
forced; ordinary CPU tests verify bounded, copied windows and missing history.
This fault-only change is inactive in the timed production configuration.

The first refined protocol attempt stops after its first baseline upload probe:
a foreign snapshot-publication CPU benchmark is observed by the mandatory quiet
window guard. No changed upload or native matrix is accepted from that attempt.
Its data, foreign-work observation and error remain distinct. A complete isolated
protocol must start with fresh evidence roots after all competing tests finish.

The complete solution on source 79fea4c6 finishes with 2,960 passed, four failed,
17 optional skips / 2,981 total (10m11.669s). The failures are Controls count
(expected 49, actual 53), notification reset opacity (one versus zero), selection
marker line count (24 versus eight), and the Operations native opaque sample.
Hosted CI 39405830327 fails the same Operations assertion at line 171.

All four exact unchanged tests are independently reproduced against the original
f2be6532 production binaries. Their production source/assets/compiler trees equal
current base e42f4e75, and these four test files are unchanged by this PR. Both
original-binary processes fail with the same assertions (three Presentation, one
Client). These are demonstrated baseline failures, not waived gates. Their files
were outside resource-scheduling ownership. Explicit serial repair authorization
was granted for these four test files; no UI product source change is necessary.


## Current fixed cohort and serial UI contract repair

The coordinated B-A-A-B cohort uses the original f2be6532 production binaries and
09eb021bf810f58cd5162717aef64203e2c7c493, with the same six cases, 128 warmups,
512 samples, two readbacks, High quality and 1920x1080. All twelve paired images
match exactly and all structural/resource cases qualify. The competing workload
was explicitly paused; the process monitor records no observed competing workload
within these captures. This does not exclude unrelated operating-system activity.

Pair one passes every unchanged 15% CPU/GPU mean/p50/p95/p99 threshold. Pair two
fails IndustrialBase NormalGameplay CPU p95 (2.6423 -> 3.9429 ms, +49.22%) and p99
(3.2563 -> 6.0598 ms, +86.09%). Its CPU mean improves 1.687842 -> 1.669008 ms and
median 1.5967 -> 1.2561 ms; spikes occur across world, overlay and shadow recording.
These observations do not establish a scheduling or product-code cause. No GPU
frame metric fails. Neither cohort is replaced or discarded. Performance
acceptance therefore remains open despite the measured upload benefit.

The two upload pairs allocate 67,531,680 -> 157,696 and 67,531,680 -> 159,280
managed bytes. Recording is 302.0631 -> 95.6000 and 310.1906 -> 136.3535 ms;
final drain is separately 0.0052 -> 1.9520 and 0.0050 -> 3.1740 ms. Changed runs
retain two slots / 8 MiB with no backpressure waits and return registered live,
retired and descriptor counts to zero. Empty frames remain 56 bytes each. GPU
copy time remains unavailable, not zero. These are workload observations, not
universal FPS claims.

Four serially authorized test repairs restore current documented UI contracts:

- Controls include four placement/ring paging rows, now checked explicitly as
  well as the complete row count, unique labels and registered gameplay prompts.
- Notification gaps up to and including 250 ms are continuous; a newly visible
  row begins its fade. Gaps above 250 ms settle, as do reversed clocks. The test
  now exercises both sides of the existing threshold without changing product code.
- Selection uses four oriented L-corners (eight segments); hover uses four diagonal
  notches. Invalid targets retain two diagonals and the invalid color contract.
- Operations uses the documented 0.30-opacity background. Native captures verify
  both world contribution and panel contribution, an untouched exterior pixel,
  stale-data rejection, selection refresh and no repeated texture uploads.

The exact additional paths are MainMenuModelTests.cs in Client.Tests and
NotificationRowMotionTests.cs, RtsInformationLayerTests.cs and
EconomicInterfaceSkinTests.cs in Presentation.Tests. No global ownership manifest,
UI source, golden image or performance threshold is modified. The explicit
Directory.Build.props serial exception still provides exactly one PATCH.


## Final local verification

Source 6350b0cf5bbd38f643d90087ba216dcc907abf22 has the same production
src/assets/tools/benchmarks trees as timed source 09eb021b; only tests, build
measurement helpers and qualification records differ. Release restore/build pass
with zero warnings/errors (build 31.08s). All affected classes pass: 38 Presentation
and thirteen Client tests. The complete serial solution suite passes 2,969 tests,
zero failures and seventeen optional skips / 2,986 total (9m09.347s). This replaces
none of the retained earlier failed logs. All 119 Graphics cases are included;
optional debug/lifetime cases retain their explicit opt-in policy.

Source-less runtime validation passes with an explicitly absent source path.
The native DRED opt-in process reports configured=true before device creation and
passes its initialized-buffer image parity test. Actual removal is not forced.
The explicit lifetime opt-in process passes the non-debug case and fails only the
requireDebugLayer=true assertion because the local D3D12 debug layer is unavailable.
This is an unmet environment gate, not a passed debug qualification or a disabled
assertion. No SDK/driver/system installation or configuration is changed.

The original local command wrapper incorrectly carries that optional native exit
code into the succeeding PowerShell-only reference check, whose own log reports
success. An independent check with explicitly reset native exit state confirms
47 projects, no cycles and preserved architecture boundaries; the one-PATCH rule
also passes at 0.1.420. Both the original wrapper status and the independent corrected
results are retained. No complete suite or timing cohort is rerun to conceal this.

The frozen ownership validator passes all ordinary changed paths; its four own
contract tests pass. Five exact serial exceptions are separately enumerated in the
retained scope record: version props and the four authorized UI test files. The
shared ownership manifest, project references, public contracts and central workflows
remain unchanged. The unrelated pre-existing local ignore-file edit is not staged.

A post-hoc analysis of the retained IndustrialBase NormalGameplay second-pair frames
places most changed-source peaks near indices 250-305; all phases show wider tails.
The first/last 128-frame block means are 1.207/1.347 ms, versus 2.446 ms in the
256-383 block. These descriptive slices do not alter the full-cohort gate and do
not prove a cause. Raw chronological observations remain authoritative.

Human review of the six original native PNGs is requested and pending. Current
required PR checks can be followed at PR #260; no green final-CI result is inferred
from the local suite. Final status is **implemented, not accepted**: retain the
draft until CPU-tail, actual debug-layer, human visual and required CI gates are
resolved. No merge, tag or release is performed.
