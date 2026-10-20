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


## Startup stop and retained CPU data

An owner-thread preparation scope is prepared to consume host stop requests at each CPU read
and GPU publication boundary, including the boundary immediately before Ready. Nested rebuilds
restore their predecessor scope in reverse order; foreign-thread disposal cannot remove it.
The real client/reference host bindings require a separately approved serial edit because both
host files are outside this area's file ownership. Until integrated and tested, cancellation in
the running client is an explicit acceptance blocker rather than a claimed product feature.

Final native retirement also clears the prepared typed dictionaries, even if a resource wrapper
throws during disposal. The encoded-payload cap bounds admitted source data, not exact managed
heap usage: typed arrays, dictionary overhead and transient decode copies are separate CPU costs.
No actual heap allocation is inferred from this cap or the logical GPU-data policy.

## Measurement interpretation

The declared native sequence is two fixed B-A-A-B warm pairs followed by a separate RendererCold
pair, with identical source-independent fixtures, cameras, profiles and runtime fingerprint.
Raw setup time/allocations include renderer construction and initial terrain preparation;
whole startup, catalog checks and later CPU/GPU samples remain separate observations. Failed
cohorts remain evidence and are not replaced by RendererCold or changed regression thresholds.
A three-cycle load/warm/unload probe records shared cache IDs, last-owner resource return,
upload-arena capacity/backpressure and DXGI process usage. Staging capacity is not committed
Default-heap occupancy; the frozen public interface exposes no exact Default-heap byte counter.
Cross-source zero-delta pixels are a technical comparison, with human image approval still
required; the existing same-source accepted-golden gate is not repurposed for different code.
