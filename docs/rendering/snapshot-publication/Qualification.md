# Snapshot publication qualification

## Baseline and scope

Source master: `f2be6532d759ab0840f2a4674c28d00446fe75ea`, version 0.1.418.
Branch: `performance/snapshot-publication`. The integrated foundation and explicit
parallel release are present through PRs 236, 237 and 241. No existing snapshot
implementation branch or open snapshot PR was found. Historical unmerged PRs 94,
98 and 100 concern session lifecycle or superseded material/terrain work.

Master CI run 39396697547 fails before restore/build/tests: its version check
expects 0.1.419 but receives 0.1.418 after adjacent merges. PR 256 independently
repairs serial versioning and HUD qualification; PR 257 owns cargo fuel service.
Neither branch overlaps the assigned snapshot files. Their pending CI is not a
passing master qualification. Shared workflows and foreign tests remain read-only.

## Identity audit

`EntityId.CompareTo` orders Index then Generation. ECS extraction uses stable
entity-index order; live entities, including wreck state, retain full generation.
Static dressing uses the distinct 0xE0000000 range; queued VFX use 0xF0000000 slots,
with emitter/generation metadata; decals use 0xD0000000 plus pool slot and its real
generation. Decals follow VFX in the public array, so complete snapshots can have
two ascending runs. Do not reorder public arrays or assume global monotonicity.
VFX and ground decals bypass interpolation; receiver decals retain full identity.

## Initial checks

Release restore/build at the baseline passed with zero warnings/errors. The initial
focused extraction/index/renderer/SelectedCombatGroup run passed 41 cases. Full
project and isolated allocation reproduction results will be recorded separately.
Raw local evidence: `artifacts/snapshot-publication/baseline/`.

Next: prove binary-reference correspondence and indefinite snapshot ownership,
then replace correspondence using render-owner scratch. Qualification is open;
no performance improvement, final CI, native acceptance or release is claimed.


## Correspondence checkpoint

The new mapping uses a stable two-pointer merge. Ordered snapshots use their
existing positions directly. The two production ascending runs are merged into
render-owner permutations; arbitrary caller order uses eight stable uint radix
passes, generation before index. Full EntityId equality remains authoritative.
All normalization and correspondence are O(n+m). For nonmonotone or duplicate
predecessors, a linear in-order traversal emits precisely the keys reachable by the
old binary search under strict ancestor bounds. Its misses and duplicate ties are
preserved; no hidden interpolation correction is introduced. Entity queries keep
the original raw search, then reuse the cached predecessor index.
Public arrays, interpolation and the independent visibility implementation are unchanged.
Scratch grows geometrically within 65,536 entries and trims after oversized populations;
four int arrays retain at most 1 MiB within that bound, excluding the unchanged legacy
visibility adapter. No borrowed/published instance array is reused as mutable scratch.

The initial correspondence check passes 21 new/existing mapping and foundation
boundary cases, including mixed presentation ranges, arbitrary order, generation
reuse, all interpolated TRS/pose/yaw values, caller mutation, retention, growth,
empty snapshots and warm zero allocation. Raw evidence:
`artifacts/snapshot-publication/correspondence-tests.txt`. A final recheck follows
the capacity-trimming correction. Producer and render timing remain separate.


## Integrated-base and compatibility checkpoint

PR 256 is merged at `e42f4e7537b84c56c9dbdafe56f03f1e5e74270d`, version 0.1.419,
and synchronized into this branch. Initial baseline suite failures are 64 display
marquee cases and one action-ring fixture case (addressed by PR 256), plus one each
in RtsInformationLayerTests.WorldMarkersUseDistinctGeometryForSelectionHoverAndInvalidTargets,
NotificationRowMotionTests.IdentityRevisionSessionClockAndReducedMotionDoNotRetainStaleVisuals,
and EconomicInterfaceSkinTests.NativeOperationsRejectsStaleFactsAndSelectionNeverCachesRetiredFacilityData.
These three existed before snapshot changes. Full current qualification remains open;
foreign UI tests/renderers are not changed to conceal the baseline failures.

The allocation failure reported historically is not reproduced in the baseline's
SelectedCombatGroup class, either alone or within the complete Presentation invocation.
The new warmed summary measurement retains the existing 512-byte-per-operation bound,
and a deliberately allocating 4-KiB control validates detection without disabling JIT
tiering or including fixture construction. This is not a newly established historical
root cause or a claim to have fixed an unreproduced failure.

The new compatibility set passes 26 cases, including 100 reproducible random unordered/
duplicate snapshots checked against every binary-search result, and direct effect,
receiver/ground-decal, wreck, generation, retained-reader and full interpolation contracts.
Unsigned identity extrema are included in the final rerun. Initial Client host contracts
pass 24 cases; added copy/failure and complete-gameplay probes pass 7 Client and 10
Presentation cases before the final compatibility additions. Source and raw command
logs distinguish each checkpoint; no failing whole suite is called green.
