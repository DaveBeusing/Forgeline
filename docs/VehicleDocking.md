# Vehicle Docking

Building cargo pickup, cargo delivery and Supply Truck loading share one
simulation-owned loading lane. Logical logistics transit nodes retain their
existing movement semantics.

## Geometry and ownership

`BuildingLoadingDock` is authored in the existing C# building definition catalog.
Current loading buildings use the local +Z facade with travel along +X. The
service point is 4.5 m outside that facade at ground level; placement rotation
transforms the entire lane. Vehicle length and width come from the instantiated
vehicle transform, including the Directorate definitions. No AABB-axis heuristic
chooses a loading side.

Current definitions provide 12 m of clear longitudinal maneuver distance plus
half the actual vehicle length on either side of service. Runtime terrain,
swept footprint and departure checks still apply to this authored space.

The Supply Depot service center spans its authored `supply_transfer` and
`cargo_load` socket positions with both Directorate truck lengths. The Ammunition
Plant service point follows the authored `loading_access` offset along its facade.

Placement previews show the required loading corridor and reject occupied or
out-of-map corridors. Nearby construction also preserves existing loading
corridors. These spaces are not added as larger global navigation blockers.
Runtime admission checks terrain and spatial obstacles, including rotated truck
footprints. A blocked coarse entry cell uses a separately validated approach
start outside the facade or behind the entry. The reserved corridor includes
that connecting segment. Local movement reaches the authored entry before
service; neither authored entry nor service position is projected onto the grid.

Automatic construction prefers a loading facade facing the base access area
and validates every selected rotation through normal placement. Extractor
orientation retains its deposit-edge authoring. Barracks and Vehicle Factory
production offsets remain on the authored output side, beyond the loading lane,
so newly produced idle units do not block the next input delivery. Tests check
this separation against the full unit catalog in all four rotations.

`VehicleDockSlot` belongs to the building; `VehicleDockVisit` and a monotonically
increasing `VehicleDockGeneration` belong to the truck. Full entity identities
and visit generations authorize service and release. Ownership includes approach,
stationary service and confirmed exit. Other trucks receive `Busy` and stop
without searching a path into the occupied lane. There is no FIFO queue.

Strategic arrivals first reach a validated point outside the entry without claiming the
slot. Concurrent arrivals choose separated outside maneuver points, including
their vehicle and separation margins, so they cannot converge on one unreachable
shared stop. A generation-zero visit describes this approach only; it authorizes no
service or release. The actual claim happens at the outside maneuver area. Nearby outward and diagonal candidates are checked when a neighbor blocks the backward approach. A safe waiting point remains stable when another truck leaves, so releasing the bay does not send the waiting truck on another coarse detour.
If another truck claims the slot during strategic travel, the second truck
stops outside and reports `Busy` without replanning into the occupied lane.

A validated dock approach takes motion ownership from the truck's previous
formation membership and formation speed constraint. Other formation members
keep their orders. A denied owner cannot detach the requested truck.

Cancellation revokes the job and movement. It does not release a physically
occupied corridor. Cleanup checks actual occupancy; neither a timeout nor a stale
release can give another truck the same occupied lane. Destroyed or cancelled
vehicles remain obstacles while their physical presence remains registered.

## Movement and service

Strategic navigation reaches the validated entry maneuver area. Local movement
then targets the exact entry, service position and heading, keeping collision and terrain
constraints. Straight dock movement uses the vehicle's authored speed and
braking limits. Precision movement is limited to 2 m/s within 1 m of the target
and 1 m/s during heading corrections above 6 degrees.
Final heading changes remain bounded by the vehicle's turn rate.

Authorization requires position error at most 0.25 m, heading error at most
3 degrees and speed at most 0.05 m/s for two consecutive observed ticks. Both
the body transform and movement heading must agree. Radius proximity, arrival
status and completing a coarse path cannot authorize inventory transfer.

Cargo batches contain at most 20 units; supply batches contain at most 20 Fuel
and 20 Ammunition. A rate of 100 aggregate units per second determines a positive
tick-based service duration. A batch books only after its service time and valid
pose checks. The same truck cannot book another batch in that tick. Pose loss
resets service time; completed batches remain committed.

Supply loading confirms completion on the following tick, after nearby field
distribution has used the previous batch. A completed order is finite: later
field consumption requires another explicit loading order or the existing
opponent loading planner.

Cargo reservations are reduced by the successful batch quantity. Transfer failure
restores any reservation released for that attempt. Existing inventory operations,
cargo-loss reporting and distribution observers retain stock ownership. Empty
sources and full targets expose waiting reasons. After service, the slot remains
owned until the vehicle clears the exit. Pure transit waypoints retain their perimeter navigation and do not reserve a bay.

After the straight exit section, departure turns away from the facade. Admission
chooses a free departure strip from eight stable candidates and freezes that
choice for the visit. The vehicle clears the through lane sideways before its
job ends. Collision and terrain restrictions remain active; an obstructed or
fuel-blocked departure retains ownership and reports `Exit blocked`.

An empty loading source or full destination yields its bay after a safe exit.
The order remains pending outside, allowing another truck to replenish or drain
that same inventory. Supply retries stock/capacity admission at one-second tick
intervals without searching a path on each waiting tick. A rescue temporarily
suspends the visit while preserving the interrupted order; its old slot remains
occupied until physical clearance. A changed logistics route likewise exits any
active visit before replanning. Already loaded building anchors do not require
another service visit merely to recompute the route.

## Supply commands and propulsion

Use the existing **Resupply** action on a Supply Truck to request cargo loading
from a compatible owned depot or Command Core. The opponent planner creates the
same authoritative loading order. Ordinary movement past a source never loads
the supply cargo compartment.

A loading truck completes its physical exit before escort assignment.
Existing field distribution remains independent. Automatic remote rescue dispatch
waits for an exclusive loading visit and its exit to finish, even while that
stationary visit has no movement order. A partial first batch cannot preempt its
own loading trip; nearby recipients may still receive physical field supply.
Remote recipients also skip a truck actively traveling to load, so they can use
a stocked stationary provider instead of chasing its subsequent departure.
A source-empty truck waiting outside the bay can still serve a remote recovery.
An unaffordable retreat waits for physical assistance in every strategic phase.
Strategic escorts and local tactical responses use their existing combat command
paths while the loading planner prepares physical supply.
The opponent buys a third cargo truck as three processing sites open; optional
expansion to four waits for its first objective-pressure force. Both use its
existing two-to-four-truck fleet bounds and normal paid production. Replacement reserves use
the same target, so finite dock cycles do not leave a mature industry on the
initial two-truck throughput. Vehicle prices and inventory targets are unchanged.

Propulsion Fuel remains a separate inventory. A correctly docked truck can load
with an empty drive tank. An empty tank outside the dock requires the existing
physical rescue/refueling path. Cargo travel budgets include local approach and
exit; a fuel-blocked exit retains its slot. Carried Fuel does not become free
propulsion Fuel.

Optional propulsion top-ups respect the delivery fuel policy during dock service
and exit; required recovery still takes precedence. Outstanding distribution
requests track their current policy priority even while their projected quantity
covers the minimum. Physical recovery requests compete as High bulk work once
their configured minimum is covered; the configured policy remains intact. Refinery
urgency also respects the available propulsion reserve; inventory targets remain
unchanged. This prevents persistent recovery priority from starving production
during longer physical service cycles.

For physical building deliveries, dispatch prefers the largest affordable,
capacity-admitted load before comparing route cost and stable source identity.
This avoids repeatedly visiting a nearby source with a tiny surplus while a
larger valid load is available. Logical-node dispatch retains its existing cost
policy. Construction urgency follows the next building's actual missing costs;
unit-production urgency follows missing costs of the next unit. Neither changes
stock targets or throughput limits.

During a power shortage, unrelated construction waits while partial deliveries
accumulate for the next required power plant. Its input policies remain Critical
through cost reservation, preventing another dispatch from claiming a completed
construction load before the build command books it. Tactical support continues.
Home placement candidates leave 96 m between neighboring sites so coarse navigation
cells retain usable access between the authored loading lanes.

Within the Critical dispatch class, Command Core construction shortages,
powered unit factories blocked on inputs or building an initial vehicle pipeline,
and depot Fuel with an actual loading/resupply recipient precede passive depot
buffers and ordinary processing stock. Age and stable request identity still resolve
ties within each group. Factory policies retain total physical stock semantics,
including reserved material. The production planner includes both the active
recipe and its real accepted successor in the required input budget.
Factories without power retain the normal recovery ordering so construction
inputs can restore power first.

A waiting vehicle cannot newly enter another truck's reserved lane. If its body
already overlaps the lane, ordinary movement remains available to clear it; the
owner still waits for a physically clear corridor. Terrain and collision checks
remain active, and the slot is not released by a timeout.

Automatic supply planning reconsiders an exhausted loading source after the
truck has physically left the bay, on its existing strategic decision cadence. Another stocked owned
source may complete the same full cargo targets. Partial cargo is retained;
active dock visits and other waiting reasons are not redirected. Explicit player
loading orders keep their selected source.
The physical loading retry timer stays independent of source selection, so
retries between planning decisions cannot postpone a new source indefinitely.

## Persistence and presentation

Dock components are authoritative ECS state. Save payload schema 3 and
authoritative checkpoint schema 2 mark the changed simulation semantics.
Pre-docking saves and replays are rejected explicitly before reconstruction;
historical fixtures and checksums remain unchanged. Envelope format 1 remains
supported. See [Save, Load, Replay and Recovery](SaveLoadReplayAndRecovery.md).

Selected owned trucks expose copied activity, block reason and service progress
through the existing player work summary. Hidden or foreign inventory contents
are not exposed. Simulation ownership remains independent of rendering and
animation.

## Reference scenes

Visual fixture version 4 runs the real shared docking service, collision index,
navigation and supply-loading order. Loading captures require stationary cargo
service progress and a completed supply batch. A second cargo truck must be stationary in `Busy` outside the current owner's reserved corridor. The sustained case captures after
at least two completed physical deliveries and tick 1024, within the existing
4000-tick fixture limit. Historical version-3 captures retain their original
identity; they are not silently requalified against the changed fixture.
Native images and a human review of entry, stationary wheels, aligned body,
physical exit and the waiting second truck remain separate acceptance evidence.
Loading and unloading inspection targets follow the Cargo Truck; the transit
inspection follows the concurrently loading Supply Truck. The sustained view
keeps its traffic overview. Targets participate in the fixture fingerprint.
See [qualification](VehicleDockingQualification.md) for source-bound evidence
and remaining gates.
