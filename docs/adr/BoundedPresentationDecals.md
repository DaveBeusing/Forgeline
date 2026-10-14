# Bounded presentation decals

## Decision

Presentation owns a shared fixed-capacity DecalPool for ground and receiver marks. Requests contain copied transforms, a kind, bounded strength/lifetime and optional full receiver identity with a normalized local anchor. Handles include slot generations; cancellation cannot affect a reused slot, including across session resets. No request enters simulation, saves, collision, navigation or combat resolution.

Defaults are 512 retained marks, 16 per 256m anchor cell and 128 published marks per snapshot. Capacity pressure drops new cosmetic requests. Receiver migration updates cell counts and retires a mark if the destination is full. Expiry and rewind/session reset release slots without allocating another resource pool. Copying uses caller-owned scratch storage; published snapshots own their immutable instances.

Ground footprints require current visibility at their center and all four corners. Hidden ground marks can remain retained until expiry but are not published. Receiver marks retire when their full generation no longer resolves to a live, visible World actor; they never reconstruct hidden positions. Vehicle tracks are sampled only for the viewing faction. Receiver movement and rotation use copied transforms, with normal render interpolation between snapshots; ground marks remain fixed.

Graphics reuses the existing compiled decal quad, atlas materials and synchronized instance streams. Marks have no selectable identity, never cast shadows, cap opacity at 0.65 and fade during their final 20% lifetime through ordered alpha coverage. Missing mesh/material or development-fallback material skips the mark and increments diagnostics. No decal-specific native texture, target or lifetime owner is added.

## Limits

Ground marks are terrain-normal-aligned planes; they can intersect curved terrain. Receiver hits use dominant-axis box-proxy faces and normalized local anchors, not exact collision surfaces or projected decal volumes. Cell accounting uses anchors, not every cell crossed by a footprint. Interpolation follows copied world poses rather than an exact curved parent-anchor trajectory. Capacity is a presentation budget, not world streaming persistence. Broad authored terrain wear stays in its existing immutable control-profile path.

## Validation

Tests cover invalid requests, generation-safe cancellation/reset, negative cells, receiver movement/cell pressure, destruction/visibility, expiry/fade, missing resources and allocation-free warmed copying after 10,000 spawn/cancel operations. Extraction tests cover real combat impacts and sampled owned tracks; production parity and existing headless tests preserve authority separation. Native reference captures exercise the shared ground carrier and compiled atlas; object alignment is covered by copied-transform tests and remains subject to live visual review.
