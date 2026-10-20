# Spatial candidates and instance submission

## Decision and active path

SimpleInstanceRenderer queries a private Morton-ordered sidecar built from the
current/previous immutable snapshots. Bounds union both endpoints, enclose
rotation with a sphere, and include infantry and vegetation wind envelopes.
The sidecar sorts indices, never public instances. Its query marks clusters;
the renderer consumes candidates in original source order. Camera and light
queries are independent. Nonfinite cluster or object bounds are conservative.
No simulation, visibility authority, picking, particle order or graphics layout
changes are introduced.

The existing RenderWorld visibility interface remains compatible. The new
renderer sidecar needs the exact pair of snapshot identities, including session
identity, before reuse. Bounds are refreshed on every new pair. The sorted
permutation can survive a new pair only after all live 32-metre cell keys are
recomputed and prove unchanged, with the same session and count. Crossing a
cell or session boundary sorts again. No static revision inference is used. A 262,144-root ceiling bounds this additional
index to about 11 MiB of array payload. Larger snapshots use the existing swept
contiguous-cluster path, still visiting every relevant root.

A lazy root preparation cache retains at most 256 pages of 256 entries. Its key
is exact current/previous snapshot identity, alpha and presentation time. A
renderer lifetime fixes lighting, quality, vegetation settings, texture detail
and the asset-catalog/session generation. Native handles never enter this cache.
A new renderer is required for a new asset generation or quality construction.
Session changes clear pages. Overflow roots are prepared directly without
extending retained pages. Only interpolated pose, conservative bounds and base
transform are shared: camera projection, LOD/hysteresis, distance density, wind
LOD amplitude and mechanical-detail weight remain per pass. LOD histories are isolated by weak RenderWorld and camera
identity, and reset for a changed session; another camera cannot seed hysteresis.

Per-pass string-to-mesh/material maps retain stable resolved handles only inside
one prepare or recording traversal. Every new traversal clears these maps and
resolves each distinct demanded asset through RenderAssetSession. The session
continues to own loading, validation, generation and native lifetime. Preparation
and recording retain their original ordering; there is no new loader or queue.
Bindings follow unchanged first-encounter batch order. Pipeline constants and
textures are rebound when their pipeline/material changes. Mesh slices retain
FirstIndex, index count, skin variant, material and instance-buffer offsets.

## Scratch and budgets

The normal CPU payload ceiling remains 65,536 staging/combined batch instances.
Oversized staging and batch arrays use synchronous ArrayPool leases, returned
before reuse and at Dispose. Stable large frames reuse warmed pool buckets;
return to a small frame releases the oversized leases. No snapshot or GPU buffer
uses this pool. The process pool may retain returned storage, so render-owned
capacity and process-memory plateau must be reported separately. Unmanaged
payload avoids cross-session references. Root-index payload adds four bytes per
part for exact root accounting. Empty batch metadata has an escape band of
1,024 slots (four legacy groups of 256); payload retention remains governed by
the original combined-instance ceiling. Larger batch peaks clear metadata at
the next reset. GPU upload replacement and fence retirement are unchanged.

The frozen device budget counts submitted parts. Shadow selection now accounts
all parts of each root, excludes roots requiring unavailable draw slots, and
selects affordable whole roots in stable hashed entity/generation order. A
costly root cannot block affordable roots. Geometry and transforms of selected
roots are complete. The first allowed batch slots retain existing draw priority;
no frame can exceed maximum parts or draws. This deliberately changes the
budget-limited shadow selection and requires visual approval. Under budget,
all roots and their original per-batch order are preserved. Drawn roots, accepted
roots, uploaded parts and drawn parts are separate WorkMetrics fields.

## Validation and integration

Tests in RenderSubmission compare exact opaque uploads with uncullled reference,
exercise swept/nonfinite bounds, separate light queries, cache key changes,
65,535/65,536/65,537 roots and simultaneous 257-batch leases. Existing renderer,
LOD, animation and asset tests remain unchanged. Null graphics proves CPU and
command properties only. Native frame, memory and image evidence is required.
The measurement adapter compiles against existing output assemblies without
new project references or changes to shared benchmark/CI files.

This decision supersedes the per-frame oversized-rebuild description for
SimpleInstanceRenderer in the shared scratch ADR. Serial documentation follow-up:
link this decision from docs/adr/RenderFrameScratchAndSnapshotOwnership.md and
the rendering section of docs/Architecture.md after integration. Those shared
files are intentionally outside this change's write ownership.
