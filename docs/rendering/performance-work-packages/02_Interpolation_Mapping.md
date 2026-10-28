# 02 Interpolation mapping

## Outcome and dependency

Validate and improve the prepared correspondence already implemented by RenderSnapshotIndex and consumed by RenderWorld. Depends on the ownership checkpoint in 01; no new graphics dependency.

## Ownership

Own RenderWorld, RenderSnapshotIndex, SnapshotIdentityOrder where assigned, dedicated correspondence tests and a dedicated CPU benchmark class. Do not edit InstanceVisibilityIndex, snapshot publication/lifetimes, renderer files, Graphics, shared benchmark dispatch or reference collection. Coordinate exact files with the snapshot-publication owner in 01.

## Tasks

1. Preserve rebuilding only on accepted snapshot changes and O(1) previous-index access during per-index interpolation. Do not restore a per-object binary search.
2. Audit entity ordering and generation-aware matching for sorted/unsorted snapshots, sparse IDs, equal ticks, skipped ticks and session changes. Reuse existing ordering helpers; published arrays remain immutable.
3. Check FindCurrent lookup on unsorted snapshots against the ordering fallback used for correspondence. Fix a demonstrated mismatch inside this ownership boundary, preserving duplicate-ID policy.
4. Preserve transform, mechanical-pose and aim interpolation plus existing VFX/decal exceptions. Never interpolate a recycled entity index or a new session against an old identity.
5. Measure correspondence preparation, capacity growth, retained bytes and repeated frame lookup separately. Optimize redundant sorting/copying only after identifying its contribution.
6. Use two distinct populated snapshots in CPU benchmarks; a first-snapshot-only fixture bypasses the correspondence work. Include churn and differing render-frames-per-tick ratios.

## Acceptance

- Reference-result tests cover alpha endpoints/intermediate values, first/empty snapshots, additions/removals, generation reuse, reordered entities, stale/equal ticks and session reset.
- GetInterpolatedInstance and TryGetInterpolatedInstance agree for valid identities even when supported input ordering differs.
- Measure 1,000 / 10,000 / 100,000 entities with stable and changing populations; report preparation versus repeated-frame cost and allocations.
- Preserve simulation isolation and immutable snapshot ownership. Pooling/copy-elimination is a separate lifecycle change, not part of this package.
- If the existing mapping already meets the contract, report that result and close missing tests instead of replacing it.

Suggested commit: `Validate prepared render snapshot correspondence`
