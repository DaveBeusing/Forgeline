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
