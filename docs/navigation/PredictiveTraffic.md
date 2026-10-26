# Predictive logistics traffic

GroundMovement is the sole owner of transform and order commits. The simulation captures current physical bodies, authored obstacles, service reservations and bounded per-vehicle neighborhoods before planning. Missing, truncated, stale or exhausted captures hold controlled vehicles. Precise docks and queues retain their original admission and release owners.

## Bounds and persisted authority

| Limit | Value |
| --- | ---: |
| Controlled vehicles / historical actors | 128 / 512 |
| Global / per-vehicle physical bodies | 4096 / 128 |
| Route waypoints / passage leases | 4096 / 128 |
| Tick / ordinary search work | 262144 / 32768 |
| Workspace | 16 MiB |
| Planning horizon / prediction intervals | 3 seconds / 16 |
| Spawn endpoint candidates / ordinary retry | 25 / 60 ticks |

Integration contract revision is 3, geometry algorithm revision is 5, traffic checkpoint revision is 3 and authoritative snapshot schema is 9. The sorted navigation.traffic domain stores root bindings, history, recovery targets and passage leases. Save/replay envelopes and map identities are unchanged. Local search guidance and physical wait observations add no saved authority or hidden cache.

## Prediction, ownership and actual movement

Canonical full entity identities and waiting age determine traffic priority. Acceleration, braking, yaw, selected-route waypoints and terrain shape sixteen copied prediction intervals. Confirmed clear prefixes can permit a bounded braking speed before a later obstruction. Shared-work exhaustion invalidates the batch before history commits.

Pinned-grid probes follow the actual forward route. Directed passages and crossing envelopes provide exclusive admission. Occupied leases survive command changes, death and wrecks until the complete rear footprint clears; unentered approaches expire after 80 ticks. Release requires complete physical coverage. Within the same owned queue, FIFO position takes precedence for a new crossing only when the captured root still targets that queue's waiting pose. Entered leases are never preempted.

Far unentered reserved/busy/blocked visits remain coarse travel. Exact slot, dock and queue generations bind the handoff to precise movement. Runtime spawns use the authored terrain offset. Only accepted unambiguous forward projection advances a waypoint; a turning arc or local detour retains its original waypoint and command.

Actual proposals independently check current body geometry, earlier accepted swept motions, service and passage ownership, terrain/world coverage, acceleration, yaw and real fuel/tactical constraints. Captures include physical bodies regardless of presentation visibility. Paid output-blocked reservations remain owned. No route, reservation or priority grants translation through a blocked sweep.

## Recovery without replacing orders

The wait graph selects at most one stopped reverse escape per cycle. Verification simulates at most 128 intervals of .125 seconds, checks the complete arc and compares other selected escapes. Continuation keeps the root command and checks its next prefix again; speed stays at most 1 m/s. Same-tick physical veto evidence may connect a precise dock owner to a coarse blocker only after pose, profile, full identity and service generations are revalidated. A logical service wait alone cannot invent a physical cycle.

For a verified mixed precise/coarse physical cycle, recovery first checks a short forward prefix against geometry, all service reservations and held passages. If clear, copied guidance at at most 1 m/s lets the coarse waiter make room without taking its root route or service owner. The normal motion planner, arbitration and actual commit check that guided step again. A native-terrain two-cargo reproduction remains stationary for 400 ticks before this change, then clears the dock and releases it only after physical departure. Stale or purely logical waits do not enter this path.

The existing legacy reverse arc also serves a non-logistics FormationLocal member when its live group owner, original submission tick and acceptance order match. It still requires actual blocked motion (or an already checked backing continuation), preserves group membership and the current order, and checks every physical sweep. A foreign group, service owner, movement restriction or stale binding does not gain this authority.

A foreign dock reservation denies new admission. An existing occupant may evacuate strictly outward without centre crossing, inward motion, rotation in place or reentry. Prediction and actual commit use the same predicate. An exiting dock owner does not wait for an obstacle wholly behind its current centre; the full bay remains reserved until physical release, and every actual departure translation/rotation retains its collision veto. Obstacles in the remaining forward lane still stop departure.

## Bounded local detours

A slow non-precise vehicle with an unchanged original command at least 40 ticks old may search around an observed stationary vehicle or its service reservation. Stationarity uses actual captured velocity, never the frozen geometry flag. Search uses 4 m spacing, a 24 m axial radius, 256 nodes, 64 expansions and at most 16384 work, also bounded by ordinary per-search and remaining tick limits. At most four attempts share at most 32768 work or one quarter of the remaining tick budget. The first identity rotates deterministically each tick.

Every edge is grounded, swept and checked against service/passage ownership. Priority-queue bookkeeping is charged. Only the first short forward target is advisory; its copied prediction and actual step are checked again at at most 1 m/s. No complete route, owner change, free fuel or forced displacement is produced. The future general footprint-routing, scheduler and regional module slots remain honestly disconnected.

Geometry algorithm 5 makes fixed-body prefiltering an explicit opt-in on NavigationSweep. Only these additional detour searches request it. It charges an exact union of all existing interval envelopes, scans and validates every fixed body once, and retains bodies intersecting that conservative union for the original interval checks. Moving-body frames and ordinary queries keep their previous processing. The same work caps, physical envelopes, terrain and endpoint checks remain mandatory.

## Numerical envelope continuity

Algorithm 4's endpoint fix is retained. Double-reference centre, interval-projection and endpoint interpolation errors are projected nonnegatively onto enclosure axes and rounded outward. Maximum endpoint padding is preserved through rotation. The protected-body coarse guard uses 20e-6S to cover two padded bodies, the padded rotational chord and interpolation error before skipping a full query.

The exact former scout/Supply transition at tick 13751 passed a sweep but failed the following stationary query. It is rejected by the corrected envelope; the 2 mm-clearance control remains clear. Both endpoint cases also run with optional prefiltering. Symmetric .26 m versus .24 m gap controls retain the authored .25 m required clearance.

## Qualification status

Current focused evidence: 47/47 Navigation tests, including 120 optional-filter equivalence queries; 52/52 dock tests; the dense fixed-body detour case under unchanged work limits; and the original natural player flow plus four formation/body controls (5/5). The latter passes funding, paid production, the original Supply staging/loading trip, scout refueling and grouped reconnaissance without extending its deadlines. The foreign-owner control and the current mixed-cycle departure, traffic, funding and original-player selection pass together (88/88). Before the last mixed-cycle and configurable infantry-demand changes, the complete Game suite passed 1058/1061 with the two natural raid cases and paid research case still failing. These remaining natural cases are being checked on the current candidate.

The raid fixture now places its two exposed trucks 12 m apart instead of stacking their physical bodies at one coordinate. Its cargo, damage, rerouting, original deadlines and success assertions are unchanged. The previous geometry-4 integration and the later formation-recovery candidate both failed terminal acceptance at tick 80000. The latter completed 25697.1 cargo and caused 1805 damage, but remained Active; its elapsed time under parallel tests is not a hardware performance result. These earlier failures are not accepted qualification, and no merge-readiness or hardware performance claim follows from the focused results.

When raid configuration demands more live infantry than the main assault complement, initial input priority includes MinimumUnits + HomeDefenders. Four enabled/disabled and completed-roster controls preserve missing paid inputs and release the priority once the roster is satisfied. Standard Gameplay already used a four-infantry complement; this configurable-demand correction does not itself explain its remaining natural failures.
