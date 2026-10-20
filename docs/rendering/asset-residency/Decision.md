# Closed visual residency and explicit device ownership

Use a closed visual dependency plan from the selected immutable runtime catalog. All mesh and
material roots are a conservative superset of the selected match; dependencies include every
LOD, material texture, rigid clip, skin and payload-declared reference. UI/audio-only content is
not a root. The current catalog resolves 484 assets, so a bounded pinned working set is preferable
to an open streaming service. Missing/corrupt required assets never become Ready; the pre-existing
explicit development fallback remains available to legacy direct material callers.

CPU reads, integrity checks and typed payload decoding run in bounded startup steps (up to sixteen
assets, 4 MiB and four milliseconds between assets), before the first rendered frame. One bounded
asset may exceed a step; sealed containers above 16 MiB and total encoded payload above 128 MiB
are rejected. Startup drains required steps synchronously rather than blocking a frame on Task
Wait/Result. There is no background GPU work or asynchronous result that can outlive its owner.
Cancellation is checked at every publication boundary. No unsupported worker integration is claimed.

GPU publication uses the existing initialized static-buffer and texture contracts, on the render
owner, in similarly bounded steps. Required readiness means the complete plan is published. The
96-MiB geometry plus texture-mip policy is logical data, not physical heap allocation; skins/clips
retain their separate existing bounds. Allocator padding, staging and DXGI usage require native
observations and remain separate metrics. Initial readiness/cold construction costs are measured,
not removed from the evidence by shifting the warmup start.

A weak device registry admits explicit reference-counted catalog/profile scopes, at most two live
profiles during reconstruction. It is not a process-lifetime resource singleton. The immutable
catalog signature, asset ID, decoded shape/format/color space, effective mip profile and device
identity determine a binding. Every facade pins the closed working set; the last owner releases all
wrappers through the existing native fence-retirement implementation. Active resources are never
evicted. There is no optional prefetch/LRU working set in this closed-catalog implementation; an
oversized pinned set fails explicitly instead of evicting visible objects. Restart creates a fresh
generation after last-owner release. A retiring wrapper failure cannot double-decrement a lease
or prevent remaining wrappers from receiving disposal.

No public ABI, runtime container format, source master, global quality cap or shader changes.
