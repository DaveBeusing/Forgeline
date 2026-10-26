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

A coarse waiter can also depart strictly outward from its own currently entered passage while a service owner blocks its root. This requires the same-tick retained lease, matching full identity, current physical pose and dimensions, and actual passage occupancy. Every short prefix is checked against geometry, service reservations and other passages at at most 1 m/s; the original 40-tick recovery age remains. Only this verified outward departure can continue at that slow speed; reverse recovery still requires a stopped vehicle. The lease remains owned until complete physical clearance. Logical waits without this physical ownership do not qualify. A native-terrain recorded occupied-crossing/dock pair and its inward-direction rejection control pass with unchanged 400-tick bounds.

A blocked, unentered reserved queue head can be held by LaneClear before it has a movement order. Optional capture now probes its grounded physical service path against the current frozen neighborhood. Only an actual moving-body veto naming the coarse occupant, with matching owner, queue, bay generation and pose, is copied into the existing same-tick physical-wait checks. A logical reservation alone still supplies no evidence. At most four probes share16384work or one quarter of the remaining tick allowance; candidate order rotates each tick. The existing grounded-query depth,40tick waiting age, full reverse-arc verification and actual movement gates are unchanged. The recorded remote-depot Supply/Cargo pair stays stationary1000ticks before the correction. It then clears the physical lane and refuels the Cargo from existing stock; the stale-generation control remains blocked and retains the bay. The focused traffic/dock/native selection passes143/143.

The existing legacy reverse arc also serves a non-logistics FormationLocal member when its live group owner, original submission tick and acceptance order match. It still requires actual blocked motion (or an already checked backing continuation), preserves group membership and the current order, and checks every physical sweep. A foreign group, service owner, movement restriction or stale binding does not gain this authority.

A foreign dock reservation denies new admission. An existing occupant may evacuate strictly outward without centre crossing, inward motion, rotation in place or reentry. Prediction and actual commit use the same predicate. An exiting dock owner does not wait for an obstacle wholly behind its current centre; the full bay remains reserved until physical release, and every actual departure translation/rotation retains its collision veto. Obstacles in the remaining forward lane still stop departure.

## Bounded local detours

A slow non-precise vehicle with an unchanged original command at least 40 ticks old may search around an observed stationary vehicle or its service reservation. Stationarity uses actual captured velocity, never the frozen geometry flag. Search uses 4 m spacing, a 24 m axial radius, 256 nodes, 64 expansions and at most 16384 work, also bounded by ordinary per-search and remaining tick limits. At most four attempts share at most 32768 work or one quarter of the remaining tick budget. The first identity rotates deterministically each tick.

Every edge is grounded, swept and checked against service/passage ownership. Priority-queue bookkeeping is charged. An exhausted optional search stops before a zero-allowance geometry query, so exhausted advisory work cannot falsely invalidate the captured geometry. Only the first short forward target is advisory; its copied prediction and actual step are checked again at at most 1 m/s. No complete route, owner change, free fuel or forced displacement is produced. The future general footprint-routing, scheduler and regional module slots remain honestly disconnected.

Geometry algorithm 5 makes fixed-body prefiltering an explicit opt-in on NavigationSweep. Only these additional detour searches request it. It charges an exact union of all existing interval envelopes, scans and validates every fixed body once, and retains bodies intersecting that conservative union for the original interval checks. Moving-body frames and ordinary queries keep their previous processing. The same work caps, physical envelopes, terrain and endpoint checks remain mandatory.

## Projected endpoint continuity

When blocked start and destination project onto the same traversable cell, the legacy pathfinder emits that shared waypoint once. The mandatory duplicate-binding rejection in continuous motion remains unchanged. The same-cell regression fails before the fix; the distinct-cell control retains its projected start and the original requested destination.

Lookahead does not blend an appended original-target leg that reverses the final route segment back onto the current vehicle position. It reaches the projected endpoint before the existing local-approach handoff. This prevents a clear but sub-float lookahead chord from producing endless rotation with zero translation. Native terrain, physical bodies, original goal, source stock and actual propulsion costs remain enforced. The recorded low-fuel Supply reaches existing depot fuel within the original200ticks; the zero-fuel control stays stationary.

## Numerical envelope continuity

Algorithm 4's endpoint fix is retained. Double-reference centre, interval-projection and endpoint interpolation errors are projected nonnegatively onto enclosure axes and rounded outward. Maximum endpoint padding is preserved through rotation. The protected-body coarse guard uses 20e-6S to cover two padded bodies, the padded rotational chord and interpolation error before skipping a full query.

The exact former scout/Supply transition at tick 13751 passed a sweep but failed the following stationary query. It is rejected by the corrected envelope; the 2 mm-clearance control remains clear. Both endpoint cases also run with optional prefiltering. Symmetric .26 m versus .24 m gap controls retain the authored .25 m required clearance.

Algorithm 6 additionally checks a fixed body's whole translation directly on separating axes when the rounded midpoint enclosure reports contact. Projection endpoints are evaluated in double precision; their minimum separation over the entire interval must exceed both padded bodies, the unchanged physical clearance and numerical tolerance. Rotation is conservatively enclosed about the starting body by its full angular chord. Unknown moving-body frames keep their original processing. Terrain, interval limits and actual movement arbitration remain mandatory. The recorded parallel-gap regression fails before this correction; closer-body and turning-contact controls remain blocked. All53 Navigation contracts pass.

A native three-cargo reproduction keeps the precise dock owner stationary through600ticks before the correction. With it, the coarse physical blocker moves under its original root and the owner completes its straight-plus-outward departure within the same600tick bound. Full bay generations and physical release assertions remain intact; the existing400tick two-vehicle fixtures are unchanged. These three-vehicle and existing mixed/passage/batch controls pass62/62.

## Construction and active production budgets

Automatic construction source selection preserves one live, unpaused and uncancelled NoInput unit request's unpaid costs in that facility's actual input inventory. It can spend surplus beyond those costs. Already reserved production inputs are protected by InventoryStore and are not counted twice. Paused, cancelled, stale, mismatched and unrelated-inventory claims do not invent a hold. This changes only the opponent's source selection; authored prices, actual construction/payment and player commands remain unchanged.

The recorded cadence40 bootstrap delivered120Steel and80Electronics to the Barracks, then reserved all of it for Radar construction from tick3202 through3311. Before the correction it produced only one Rifle by the original20000tick deadline. With the budget guard the same setup produces three by12606. The policy regression fails before the fix; ten budget controls and the original27 production-policy cases pass. No fixture deadline or stock assertion changes.

## Qualification status

Navigation contracts passed53/53 on unchanged geometry6. The last service-capture traffic/dock/native selection passed143/143, including stale-generation rejection and finite-stock refueling. Ten construction-budget controls and the original27 production-policy cases now pass. Paid inputs, original damage and time bounds, physical clearances and work caps remain enforced.

The last complete Game on service-capture traffic9285102d passed1075/1078; initial-force preparation, paid artillery and validation supply/contact readiness failed. Integration641d185f strict seed2026/80000tick/require-terminal also failed Active, delivering28323.54cargo and81artillery shots. Parallel-run timings are not hardware performance qualification. Full Game, the six natural scenarios and strict qualification of the construction-source guard remain pending. No merge-readiness claim follows from the focused results.

Raid target engagement uses any currently identified member reaching actual three-dimensional weapon range, letting shorter-range members close under normal tactical control. Home-defender selection consistently uses configured home radius. All 30 raid-planner tests pass, including the formerly failing 95%-range and configured-radius controls. The raid fixture places its two targets 12 m apart instead of stacking their physical bodies; cargo, damage, rerouting and original success assertions are unchanged.

When raid configuration demands more live infantry than the main assault complement, initial input priority includes MinimumUnits + HomeDefenders. Four enabled/disabled and completed-roster controls preserve missing paid inputs and release the priority once the roster is satisfied. Standard Gameplay already used a four-infantry complement; this configurable-demand correction does not itself explain its remaining natural failures.
