# Graphics resource scheduling qualification

Status: implementation under qualification; native performance, complete suites and
required PR checks remain pending. No integration, tag or release is performed.

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
validity. Only identical pipeline rebinding is cached, with the existing frame,
shadow, particle and pass invalidations; buffers, offsets, descriptors and frame
constants are not elided. Memory-budget queries are sampled at most every 250 ms;
internal upload diagnostics report sample age separately without changing public
metrics or querying DXGI for individual counters.

FORGELINE_DRED=1 opts into process-wide breadcrumb/page-fault configuration before
device creation. Unsupported interfaces are reported. On actual removal, bounded
HRESULT status telemetry is emitted once; no addresses, object names or memory
are dumped. Detailed breadcrumb operation traversal is not implemented. Run this
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
