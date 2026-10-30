# Vehicle clearance and continuous motion

[Documentation](../README.md) · [Complete catalog](../DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Implementation boundary](#implementation-boundary)
- [Concrete entry points and configuration](#concrete-entry-points-and-configuration)
- [Authoritative dimensions and coordinate conventions](#authoritative-dimensions-and-coordinate-conventions)
- [Physical clearance service](#physical-clearance-service)
  - [Whole-body sweep](#whole-body-sweep)
  - [Terrain over the footprint and center trajectory](#terrain-over-the-footprint-and-center-trajectory)
  - [Work, completeness and result semantics](#work-completeness-and-result-semantics)
- [Continuous ordinary-route planning](#continuous-ordinary-route-planning)
  - [Precise owners and progression](#precise-owners-and-progression)
- [Stable capture and simultaneous proposal arbitration](#stable-capture-and-simultaneous-proposal-arbitration)
- [Diagnostics, persistence and cache identity](#diagnostics-persistence-and-cache-identity)
- [Validation boundary](#validation-boundary)

</details>

## Implementation boundary

This implementation consumes navigation integration contract revision 1. It adds
physical clearance queries, an ordinary-route motion planner, read-only physical
capture and a final bounded motion veto. It does not register a simulation system
or change the existing movement, docking, queue, spatial-index, command, fuel or
save/replay callers. The runtime slots remain disconnected until central
integration explicitly installs them.

The starting integration revision is master
19e237d2e53c9a95abd2f8bb192ca19a4129c683. It already contains the shared navigation
contracts, logistics recovery and automatic conveyor connections. No competing
implementation of those features is introduced here.

Before final compilation this branch was synchronized with master
78e889d471435477c6b03ad866a572c64a3f262c, which also includes the independent
regional-update implementation. Its contracts and owned source paths do not
overlap this implementation.

The public algorithm interfaces remain INavigationGeometryQuery and
INavigationMotionPlanner. Concrete classes reporting Available means that their
operations are implemented, not that a running match uses them or that gameplay
has been accepted. See [the integration handoff](VehicleClearanceAndMotionHandoff.md)
for the remaining composition work.

## Concrete entry points and configuration

The existing shared interfaces remain the only public algorithm contracts. These
concrete implementations and internal Game helpers provide their integration:

| Implementation | Entry points and configuration | Lifetime |
| --- | --- | --- |
| NavigationVehicleGeometry | Constructor(snapshot, terrain, maximumSweepIntervals, maximumIntervalTravelMeters); Sweep(frame, movement, workAllowance); AlgorithmRevision = 1 | One pinned snapshot/terrain pair; no mutable query history. |
| NavigationMotionPlannerOptions | MaximumRouteWaypoints, MaximumLookaheadWaypoints, MaximumLookaheadMeters, PlanningHorizonSeconds, MaximumGroundedRefinements; Validate() | Immutable explicit configuration; refinements may be zero, remaining limits must be positive. |
| ContinuousNavigationMotionPlanner | Constructor(options); Propose(input, collision, geometry, workAllowance, out workConsumed) | Stateless; reusable with immutable inputs. |
| ContinuousNavigationMotionPlanner.ProposeCore | Internal overload additionally returns usedWaypoint, currentWaypointIndex and usedWaypointIndex | Per-call copied diagnostics/progression hints, never cursor authority. |
| NavigationAcceptedMotionProgress.TryGetNextWaypoint | Internal input, approved proposal, current/used indices, workAllowance; returns workConsumed and nextWaypoint | Post-arbitration, pre-commit calculation; no retained state. |
| NavigationMotionCapture.TryCaptureVehicle | Internal entities, entity, existing GroundMovementSystemOptions, out observation | Simulation-thread read only. |
| NavigationMotionCapture.TryCaptureBody | Internal entities, SpatialEntry, out collision body | Simulation-thread read only. |
| NavigationMotionCapture.CaptureCollisionFrame | Internal entities, world, tick, coverage, neighborhoodComplete, spatial entries, additional bodies, maximumBodies, workAllowance, out workConsumed | Bounded copied per-tick data. |
| NavigationMotionArbitrator | Internal constructor(maximumVehicles, maximumBodies); Constrain(collision, inputs, proposals, geometry, workAllowance) | Stateless final veto; bounded proposal/conflict output. |

All work allowances count deterministic operations. Capture, follower, progress
and arbitration consume separate caller-reserved portions of the fleet budget;
child geometry work is included in the corresponding call's reported work.
Configuration values are proposed operational limits until manually qualified;
this change does not install presets or claim measured capacity.

## Authoritative dimensions and coordinate conventions

NavigationMotionCapture.TryCaptureVehicle delegates to the existing
NavigationReadAdapter.TryCaptureVehicle. The profile comes from actual
WorldTransform.Scale / 2 and the existing GroundMovement maximum speed,
acceleration, deceleration and yaw rate. The clearance argument comes from
GroundMovementSystemOptions.ObstacleClearanceMeters. There is no second truck
dimension or balance table.

Poses describe the body's center. Local X is half-width, local Y is half-height
and local Z is half-length. Rotations must be finite and normalized within the
existing contract tolerance. The physical and movement paths use the same full
entity index and generation. Invalid dimensions, unknown identities, non-finite
values or stale source data cannot produce a movement approval.

The capture adapter uses actual ConveyorPlacement.Bounds for conveyors. Their
WorldTransform.Scale includes the authored mesh scale and is not a physical hull.
Other static objects use their authoritative spatial bounds. Mobile bodies retain
their actual orientation, dimensions and velocity. Neither faction visibility nor
zero health removes a physical obstacle; capture failure makes the frame
incomplete rather than silently dropping that object.

The collision frame is internal simulation data. It must never be sent directly
to presentation or reused as a faction-visible congestion map.

## Physical clearance service

NavigationVehicleGeometry implements INavigationGeometryQuery. Its constructor
takes the pinned NavigationWorldSnapshot, the same authoritative TerrainWorld
used to create that snapshot, a maximum interval count and a maximum interval
travel distance. Bounds must agree, and Sweep requires the exact snapshot
instance. The constructor copies the terrain chunk container once and retains
the existing immutable heightfields; it does not copy meshes per vehicle or tick.

Revision 1 exposes only point height/normal queries through the snapshot. Those
queries cannot prove that an unsampled ridge is absent under a whole vehicle.
The concrete constructor therefore also receives the original heightfields.
Matching world bounds alone cannot prove terrain provenance: the publication
owner must pass the same source to both objects and retain their paired lifetime.
No alternate terrain interface or modification of the frozen contracts is added.

### Whole-body sweep

Each sweep covers linear position interpolation and shortest-quaternion rotation
over its stated duration. It subdivides that motion using total translation,
rotational travel, the smallest body extent and the configured interval bound.
Every interval is enclosed by an oriented box expanded by projected translation,
the rotation chord bound, authored clearance and numerical padding. Consequently
the space between sampled endpoints is also checked.

An axis-aligned broad phase precedes an oriented 15-axis separating-axis check.
Static structures retain their vertical extent. A sufficiently high conveyor span
can therefore clear the vehicle while its supports and a lower span still block
it. A start or end pose that fits does not by itself authorize the turn between
them.

Other vehicles have a rotation-invariant enclosing body translated by the
captured linear velocity. This is deliberately conservative because revision 1
has no predicted angular velocity or acceleration bounds. It can reject a close
pass that a more precise future prediction would permit. It is a provisional
prediction; final same-tick proposal arbitration remains required.

### Terrain over the footprint and center trajectory

The terrain query visits native heightfield cells touched by the swept footprint,
including cell edges and chunk seams. For each bilinear cell it bounds the two
height derivatives using the original corner samples, and compares their combined
gradient with the snapshot capability's slope limit. Composition must provide the
actual applicable movement slope limit, not a different movement-class preset.

Missing chunks are unavailable geometry. Discontinuous chunk seams are rejected;
seams with different native resolutions are checked at the union of their sample
knots. These checks use the actual terrain and physical sources, not render meshes
or an assumed smaller global navigation grid.

The center trajectory is also clipped to each touched native cell. Its vertical
offset from the bilinear ground is a quadratic function along the proposed
segment. Endpoints and any interior extremum must retain the captured ground
offset within numerical roundoff. A long straight segment cannot pass through an
intermediate hill or bridge an unsupported valley merely because its two endpoints
have the same height.

The height offset is derived from the captured grounded pose and the same terrain
snapshot; half-height is not a substitute for the existing suspension/grounding
configuration. No new suspension, wheel-contact or chassis-pitch model is added.
Terrain curvature can require shorter grounded segments and can conservatively
limit a straight sweep. The caller must not silently reinterpret an approved
linear sweep as a different terrain-following curve.

### Work, completeness and result semantics

Interval visits, obstacle checks, terrain cells and seam work consume deterministic
work units. The configured maximum interval count is also a hard bound. There are
no wall-clock decisions or unbounded retry loops.

| Result | Meaning for the consumer |
| --- | --- |
| Clear | The entire specified interpolation is approved; fraction is 1. |
| Limited | A complete prefix was checked before an obstruction; only that prefix is usable. |
| BudgetExhausted | Only earlier fully checked intervals may be used; exhaustion does not mean unreachable. |
| Blocked | No positive prefix was approved. |
| Unavailable | Physical coverage or terrain is missing; no movement is authorized. |
| Stale / InvalidInput | The snapshot or request cannot be used; no movement is authorized. |

Zero allowance cannot approve movement. Work consumed never exceeds the caller's
allowance. A partial interval contributes no allowed fraction. Incomplete frames
and out-of-coverage paths are not treated as empty obstacle sets.

AllowedFraction is a prefix of the original interpolation and timing. Shortening
an endpoint and then traversing it over an entire tick changes the relative timing
against other vehicles; the actual resulting full-tick trajectory must be swept
again. Neither the planner nor the final arbiter treats a safe endpoint alone as
proof of a safe move.

## Continuous ordinary-route planning

ContinuousNavigationMotionPlanner implements INavigationMotionPlanner and has no
mutable vehicle history. NavigationMotionPlannerOptions explicitly bounds the
route scan, lookahead point count, lookahead distance, prediction horizon and
grounded refinement count.
These are operational limits, not measured defaults or vehicle balance changes.
The sweep duration is at least the actual fixed tick. Physical capture must cover
max(PlanningHorizonSeconds, DeltaSeconds), including configurations whose nominal
horizon is shorter than one tick.

The planner validates the complete order binding, current tick, entity, profile,
world revision, capability profile and knowledge scope. It requires an unambiguous
current direct-order waypoint belonging to the original route. A pending newer
search, a replaced order or an ambiguous route anchor cannot take over movement.

Within the bounded lookahead, every candidate receives a full-body sweep. A fully
cleared nearer path takes precedence over a farther shortcut obstructed by a
wall. Only the proven prefix of a limited result contributes to the free horizon.
Terrain-limited candidates may be shortened and re-grounded within the explicit
refinement cap; each retry starts at the original observed pose and time. There
is no unchecked spline or diagonal shortcut through a building corner. The actual
next movement step is checked independently from the lookahead.

The planned speed respects authored and effective maxima, the traffic speed cap,
curvature/yaw rate, remaining distance to the original goal and the approved free
horizon. Normal acceleration and braking use the existing rate limits. The
braking horizon includes the next simulation step rather than relying on another
future tick to prevent an overshoot. A hard fuel/tactical/safety stop or a newly
imposed limit can require an immediate hold; it cannot authorize temporary
overspeed.

The returned DesiredSpeed describes actual proposed horizontal displacement per
tick. Heading changes remain bounded by yaw rate. The planner preserves the
original gameplay target and its supplied goal tolerance. A projected navigation
endpoint is not proof of arrival at the original goal.

### Precise owners and progression

LocalApproach, required final heading, dock visits and queue-owned movement are
not smoothed by the ordinary-route follower. It returns an unavailable unchanged
proposal for those cases, leaving their existing precise controller responsible.
That is an explicit composition boundary, not permission for the old controller
to move outside the finalized batch.

The existing movement path retains its ordinary StopRadius + 0.01 m arrival
bound, the headed local approach's 0.1 + 0.01 m bound and its PI/90 heading bound.
VehicleDockPose.IsSettled still requires its existing 0.25 m position, PI/60 body
and movement headings, and at most 0.05 m/s. VehicleDockingService separately
requires at least two consecutive distinct settled ticks before service.
The caller supplies the matching ordinary goal tolerance and effective local
speed limits; this module does not alter those authorities.

Revision 1 does not carry the route cursor in NavigationVehicleObservation.
The concrete planner supplies internal planning/progress helpers for the central
Game caller. After final arbitration and before committing the transform, the
caller evaluates NavigationAcceptedMotionProgress.TryGetNextWaypoint. It derives
the next existing NavigationRouteState cursor only from the actually accepted
motion and an unambiguous forward route projection. An unresolved or budget-limited
progress calculation holds that participant and retains its previous route state.
Cursor and transform are then committed coherently. Selecting a distant lookahead point alone
must not advance the route. Otherwise a stateless follower could return to a
waypoint already passed on the following tick.

## Stable capture and simultaneous proposal arbitration

NavigationMotionCapture runs on the simulation thread before planning. Its caller
provides spatial entries and additional authored/reservation bodies, a coverage
box and an honest completeness flag. Coverage must include every body that could
enter the planned region during the supported horizon, not merely nearby centers
at capture time. Revision 1 has no horizon certificate to verify this for the
producer; insufficient or uncertain capture must be marked incomplete.

Capture checks source identity and current spatial bounds, bounds body capacity
and work, and orders complete output by stable source/entity/generation keys. It
does not query or update the shared spatial index itself. Truncation, stale source
entries, invalid geometry and duplicate identities make the frame incomplete.

NavigationMotionArbitrator is an internal final veto for same-tick proposals. It
uses the same immutable snapshot and stable full entity ordering. Each candidate
must pass its complete actual-duration sweep against the original occupants made
stationary, and then a conservative swept-envelope comparison against already
approved movements. Therefore rejecting a later candidate, or exhausting the
budget and holding it, cannot invalidate an earlier approval by removing an
assumed departure.

The result contains bounded constrained proposals and conflict identities. It
does not own fairness history, dock leases, queue order or traffic reservations.
Those remain with their existing authorities and the traffic module. Stable
ordering is a deterministic tie-break, not a second right-of-way policy.

The stationary fallback copies still carry their true Vehicle kind. Geometry
therefore conservatively encloses every possible body rotation even in this
check. Close parallel vehicles can be held although their exact initial boxes
would fit. Dense-lane capacity and maneuverability require manual acceptance;
the implementation does not claim optimal traffic throughput.

Every actual mover in the covered group must participate in final arbitration,
including precise dock/queue movement. Nonparticipants must hold for that tick;
an ordinary logical dock/queue lease is not a swept-volume certificate.
An unavailable ordinary follower cannot fall back to an independent later
movement loop after other proposals were finalized. A budget-limited result may
retain earlier approvals only if all remaining participants actually hold.

Only GroundMovementSystem, through the existing authoritative movement path, may
apply the approved transforms, update movement state/spatial presence and charge
propulsion fuel from actual movement. This implementation performs none of those
writes.

## Diagnostics, persistence and cache identity

The proposal exposes limiting reason, curvature, braking distance and approved
remaining distance. The concrete internal planning helper also returns the used
intermediate target and waypoint indices; the copied input profile supplies the
body envelope. There is no mutable LastResult field or per-tick mesh copy. Any
viewer-facing diagnostics must be copied and filtered by the owning Game and
presentation adapters before publication.

Geometry, follower, capture and arbitration have no persistent driving history.
Geometry retains a paired immutable snapshot/terrain source and immutable
configuration. Reconstruct it after world publication or restore; never restore
a query against a different snapshot. Per-tick proposals, conflict lists and
capture buffers are transient and must be recaptured rather than replayed as
authorization.

The existing route cursor/order, command ownership, dock occupancy, FIFO state,
cargo and fuel remain authoritative. Central integration must capture/restore the
actual command-generation counter and any existing route progress it changes.
No central save/replay schema is changed here. No history is hidden in a static
dictionary, object identity cache or clock-driven retry.

Any future clearance cache key must include algorithm revision, full vehicle
profile/clearance, both poses including direction, duration, geometry/capability
identity, collision capture tick and relevant physical body/reservation revisions.
A cell center or movement class alone is insufficient. Route/cost caches also
retain the original goal semantics and knowledge scope. This implementation adds
no such cache and never treats a soft-cost revision as geometric proof.

## Validation boundary

No automated tests are created, changed or manually executed for this change.
No benchmark, soak, smoke, headless run or visual-reference run is requested.
Existing automatically triggered CI remains unchanged; its results are separate
from local source compilation and final gameplay acceptance.

The compilation scope is limited to the Navigation and Game Release project
targets. Both attempted commands are blocked by three CA1512 diagnostics in
unchanged regional sources inherited from current master; see the handoff for
the exact locations and next build step. The source revision, exact commands,
compilation result and observed existing CI state are recorded in the pull
request. Compilation does not establish collision
correctness, gameplay smoothness, throughput or performance. Combined integration
and manual acceptance remain outstanding.
