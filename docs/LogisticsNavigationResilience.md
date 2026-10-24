# Logistics navigation resilience and CPU qualification

## Scope and provenance

Review started from master `42ac542f3c7d8b2d793d8dffd6930f772ad95186`
(0.1.469-dev) and synchronized with master
`3c261dca3182d9973cce07ea64f2b7826c11f6c4`. The intervening visual-asset
compatibility changes do not modify the reviewed navigation or Game sources.
The five production changes were committed as
`2b191cbca783ef51e57b58f23a34993e3805d728` after qualification.
The PR has one PATCH increment to 0.1.470; no schema or public component layout changes.

This document supplements [hierarchical navigation](HierarchicalNavigation.md)
and updates the current recovery/workspace behavior described by the historical
[movement qualification](NavigationMovementQuality.md). It does not establish
full-match, native graphics, visual or release acceptance.

## Corrected movement intent and arrival

A pending path result for an obsolete world version previously removed its
pending component and discarded the remaining movement intent. Valid, still-owned
intent is now restored to the ordinary navigation phase. There is at most one
replacement per affected entity in that tick; results are not recursively
resubmitted while draining the completion queue. This is not a fleet-wide budget.

Scheduling removes the previous MovementOrder. Any subsequently present order
supersedes that pending request, including another command in the same tick.
Active-route invalidation also respects a replacement strategic or local docking
order. Owned waypoints now require matching issuer, submitted tick, accepted tick,
strategic kind, absence of a precise target heading, and waypoint position.
Coincident local docking orders cannot be mistaken for the old strategic waypoint.
Canceled or destroyed entities do not regain abandoned intent.

The nearby-stuck shortcut remains available only for intermediate route corners.
It no longer completes a final destination outside the normal stop tolerance.
A stuck rescue-route replacement also redirects its existing ownership token
through SupplyRescueTravel, retaining recipient and fuel-budget fields.

The loaded-truck regression fixtures preserve 40 Steel during world changes,
detours, and an unreachable destination. They isolate navigation with the actual
CargoTruckFactory; they are not a claim of a completed physical delivery test.

## Behavior-preserving CPU changes

NavigationGridBuilder now finds each static obstacle's affected X/Z cell ranges
and marks their row spans. It no longer scans every obstacle for every cell.
Binary range searches evaluate the same inclusive floating-point cell edges as
the old overlap predicate. Terrain sampling, slope limits, clearance and boundary
touching semantics remain unchanged. The tradeoff is one transient Boolean per
cell when obstacles exist; no mask is allocated for an empty obstacle collection.

The existing exclusive local-search queue pool now also owns costs, predecessors
and corridor sets. Collections are cleared in the same finally-protected return
path. At most eight workspaces are retained; a workspace exceeding 16,384 active
entries in any tracked collection is discarded. These are entry limits, not exact
byte limits. Concurrent searches never share a live workspace, and returned paths
own their output arrays independently.

NavigationWorld uses a cache-hit fast path before constructing the capturing Lazy
factory. Concurrent graph publication remains unchanged. No movement-class costs,
A* neighbor ordering, deterministic tie breaks, diagonal guards or route geometry
were relaxed to produce the measurements.

## Executed qualification

[Paired qualification run 40134443631](https://github.com/DaveBeusing/Forgeline/actions/runs/40134443631)
used Ubuntu 24.04 X64, .NET SDK 10.0.402 and runtime 10.0.13. The repository's
10.0.401 SDK selection permits latest-patch roll-forward.

The checkout was `fb294efddc753c5cf375eaec020b7d4afea8fb35`. It included the new
tests and probe but the original production sources. Baseline binaries were
preserved, exact reviewed source patches were applied in that worktree, and
candidate binaries were then built. Both test JSON records therefore report the
checkout in `sourceHead`; that field is not a clean candidate commit identifier.
Only the five qualified source files were subsequently committed as `2b191cbc`.

| Executed tests | Before | After |
| --- | --- | --- |
| New recovery, ownership and changed-blockade cases | 2 passed / 12 failed | 14 passed / 0 failed |
| Complete Navigation project | 34 passed / 0 failed | 34 passed / 0 failed |
| Complete Game project | 882 passed / 21 failed | 894 passed / 9 failed |
| Skipped Game tests | 0 | 0 |

The complete Game project is **not green**. The nine remaining failures have the
same test identities and reported assertion evidence before and after:
six CompiledNavigationParityTests cases (cell sizes 16/32 with no closure, north
bridge, or south ford), ProductiveCompiledStartPreservesLegacyCheckpointsAndGeography,
and two RejectedWritePreservesLastReadableFileAndCleansTemporaryFiles cases.
Their root causes and Windows outcomes are not inferred from this Linux comparison.
No existing test, golden, required CI step or threshold was changed or skipped.

New coverage includes same-tick command replacement, coincident docking targets,
repeated world revisions, cancellation/destruction, rescue ownership, final-goal
precision, actual newly inserted walls with a legal detour or explicit failure,
and cargo conservation. Rasterization is compared with the original exhaustive
predicate using individually tested seeded obstacles, exact and adjacent float
boundaries, negative origins, fractional cell sizes, clearances, an empty set,
and a world-spanning obstacle. Existing Navigation tests also exercise route
alternatives, fallback and concurrent searches.

## Paired performance observations

[Retained raw measurements and provenance](qualification/logistics-navigation/paired-cpu.json)
contain all six process reports, not only the most favorable pair.
Three rounds alternated baseline/candidate execution order on the same host.
Every process warmed 256 route queries and three grid builds, then measured
512 route queries, twelve grid builds, and 10,000 warm graph-cache lookups.

The route workload used two alternating wheeled destinations on an unobstructed
512-metre square. The separate grid-build workload used 16,384 cells and 1,024
small static obstacles. It is not a dense moving-convoy workload.

| Metric | Baseline | Candidate | Interpretation |
| --- | ---: | ---: | --- |
| Mean grid build | 29.359 ms | 13.241 ms | 54.9% less time, about 2.22x throughput in this workload |
| Temporary allocation per warmed route | 155,688 B | 7,624 B | 95.1% less allocation |
| Mean warmed route query | 0.5561 ms | 0.5591 ms | Essentially unchanged; no route CPU speedup established |
| Allocation per grid build | 221,288 B | 237,696 B | 16,408 B more, the temporary raster mask tradeoff |
| Allocation for 10,000 warm graph lookups | 1,760,000 B | 0 B | Cache-hit factory allocation removed, not an allocation-free pathfinder |

Route signatures, blocked-cell signatures and expanded local-node totals are
identical across all six reports. Three microbenchmark pairs do not establish
statistical generality, frame-rate gains, truck delivery throughput, native
hardware performance, or retained heap bounds. Vehicle maximum speed and the
physical steering/braking rules were not changed.

The downloaded artifact is ID `12576557527`, SHA-256
`abf0744bb2f095b32d14d93aeb0264aa84ebd221422f8c36a7cc046bca2cead3`.
It retains baseline/candidate build and test logs, test-status JSON, and six
measurement reports. The five qualified source blob identities are:

| Source | Git blob |
| --- | --- |
| NavigationRouting.cs | db313e7d1a4ad9eaefac975d0f4927087dd36c21 |
| LocalPathFrontier.cs | eddde84ef897df297bafb89b4a338dfb8303da7e |
| LocalPathSearch.cs | 4c383d478c6470e88304f759e072681a5553b7aa |
| NavigationGrid.cs | 2c5e65d88430bad9b773456b62856f6b74865758 |
| NavigationWorld.cs | 7645f5f01976d38fb4c85773159fc900b610552a |

## Reproduction and integration boundary

The retained workflow is read-only and cannot push source changes. The one-time
source-writing migration, its exact patches and the temporary baseline-failure
comparison script are removed. The normal required CI workflow is untouched.
The independent Windows job runs the complete Game project without exclusions.
See [probe commands](../build/navigation/README.md) for the current-source checks.

Required full CI, native movement/docking inspection, and product acceptance
remain necessary before merge. Historical successful scoped qualification is not
substituted for a current required build-test check.

## Remaining movement work, not implemented here

Global routing still excludes moving-vehicle congestion. Local avoidance is not
predictive reciprocal avoidance or a narrow-passage right-of-way system; a replan
can therefore find the same route around unchanged static geometry. A future
traffic layer should distinguish a short queue from sustained blockage, apply
stable yielding/reservations, and expire congestion penalties deterministically.

Route clearance is not calculated from an actual oriented truck footprint and
turning envelope. Continuous mobile-versus-mobile collision, speed-aware lookahead,
and continuous intermediate-corner following require separate measured work.

The existing bounded general fallback does not bound both initial corridor
searches or total fleet requests per tick. Global request budgeting, bounded
per-world route caching, connectivity-aware endpoint alternatives and incremental
regional rebuilding remain separate improvements. No new obstacle sensor,
visibility exception, free transport, teleport, fuel bypass or vehicle speed
balance change is introduced.
