# Formation Movement and Group Orders

## Purpose

Formation movement turns a multi-unit move command into one simulation-owned movement group with shared strategic navigation and formation-relative local locomotion targets.

The system exists for two reasons:

- keep large selections visually coherent and reduce unnecessary movement micro-management;
- avoid one long-range hierarchical path request per selected unit when the group can share the same strategic corridor.

Formation movement remains a game/simulation concern. Presentation only visualizes extracted debug state and never owns formation state.

## Command Boundary

Player interaction still enters simulation through `MoveEntitiesCommand`.

For a valid move command:

- non-ground or otherwise non-formation-capable controllable entities retain the existing individual `MovementOrder` behavior;
- one formation-capable unit retains the individual route behavior;
- two or more formation-capable units create one movement-group entity;
- members receive `MovementGroupMember` state rather than independent strategic `MovementOrder` instances.

The movement group owns:

- issuer and destination;
- selected formation template;
- accepted/submitted ticks;
- centroid and bounds;
- current forward direction;
- member count;
- representative footprint/speed state;
- compression/split state;
- shared path lifecycle.

A later move command detaches affected units from their previous group before creating the replacement group or individual movement order. Empty groups are retired automatically.

## Formation Templates

The initial templates are:

- `Line`
- `Column`
- `Wedge`
- `Compact`

`MoveEntitiesCommand` accepts the requested `FormationTemplate`. The Windows client currently uses the command default (`Compact`) for normal multi-selection move orders. This keeps formation selection available at the command boundary before a dedicated game UI exists.

Templates generate local two-dimensional slot offsets around the group anchor. Offsets are projected into world space from the group travel direction:

```text
local lateral axis -> formation right
local longitudinal axis -> formation forward
```

Spacing is derived from the largest member radius plus a configured margin so mixed-footprint groups do not assume one fixed vehicle size.

## Stable Slot Assignment

Slot assignment is deterministic and designed to avoid unnecessary crossing.

For each group update:

1. valid existing slot assignments are preserved;
2. duplicate or out-of-range assignments are released;
3. only unassigned members are matched to remaining slots;
4. assignment uses the nearest remaining slot around the current group centroid;
5. stable entity iteration and slot-index tie breaking keep the result reproducible.

A member loss therefore does not trigger a complete reshuffle. Only assignments that can no longer remain valid are repaired.

The same slot index survives normal route turns and temporary local avoidance. The world-space slot target moves and rotates with the shared route while the member identity remains stable.

## Shared Navigation

`FormationMovementSystem` runs in `NavigationRequests` before `HierarchicalNavigationSystem`.

A group schedules one `NavigationPathRequest` from its centroid, or from the closest traversable member position when the centroid is not a valid navigation start.

Mixed movement classes use a conservative representative capability. The member with the lowest supported maximum slope defines the shared route capability.

The shared `HierarchicalPathfinder` instance is also used by normal individual navigation. Its versioned high-level caches remain available to both systems.

When a shared route succeeds:

- the group stores one `MovementGroupRoute`;
- the centroid advances through the route waypoints;
- members receive formation-local `MovementOrder` targets for their assigned slots;
- those orders are marked `MovementOrderKind.FormationLocal`.

`HierarchicalNavigationSystem` explicitly bypasses strategic path generation for formation-local orders. This is the key boundary that prevents a shared group route from degenerating back into one global path request per unit.

Navigation-version changes invalidate stale group routes and pending requests using the same version boundary as individual pathfinding.

## Locomotion Ownership

`GroundMovementSystem` remains the only system that mutates unit transforms for formation movement.

Formation logic provides:

- local desired slot position;
- effective group speed limit.

Ground locomotion continues to own:

- acceleration/deceleration;
- turn limits;
- terrain following;
- slope validation;
- local separation;
- static-obstacle steering;
- penetration correction;
- arrival at local targets.

Temporary local avoidance may pull a unit away from its slot. Because the group republishes the desired slot target, the unit gradually reforms after the avoidance condition clears.

## Speed Harmonization

The group baseline speed is the minimum `GroundMovement.MaximumSpeed` among current members.

Each member receives `FormationMovementConstraint`, and `GroundMovementSystem` clamps its normal speed target against that value.

If the maximum member-to-slot error exceeds the configured stretch threshold, the group applies an additional slowdown factor. This gives lagging members time to recover instead of allowing faster members to permanently abandon them.

The constraint is removed when the unit leaves the group, the group completes, or invalid membership is cleaned up.

## Choke-Point Handling

The group samples traversable navigation cells laterally around the active shared-route waypoint.

If both sides close within the configured scan range and the available corridor is narrower than the desired formation width, the system applies controlled fallback:

1. lateral offsets are compressed toward the route centerline down to the configured minimum compression scale;
2. if the required compression would exceed that limit and the group is large enough, the layout switches temporarily to a column;
3. the column is divided into longitudinal cohorts with deliberate gaps;
4. once the route opens again, the original requested formation is regenerated and units reform around their existing slot identities.

Slot targets that still land on blocked navigation cells are projected progressively toward the shared route anchor until a traversable target is found.

The fallback preserves one shared strategic route. It does not create independent long-range path requests for the cohorts.

## Arrival and Member Removal

At the final shared waypoint, completion requires every current member to be within its local slot-arrival tolerance.

On completion:

- formation-local movement orders are removed;
- speed constraints are removed;
- membership components are removed;
- the movement-group entity is retired;
- cumulative completion diagnostics are incremented.

Destroying an entity automatically removes its ECS components. The next group update recalculates the member set, centroid, bounds, footprint, representative capability, and slot validity without keeping stale entity references.

## Diagnostics

`FormationMovementDiagnosticsSnapshot` exposes:

- active group count;
- active member count;
- largest group size;
- compressed group count;
- shared path request count;
- completed group count;
- failed group count;
- slot reassignment count;
- compression event count;
- split event count;
- blocked-slot projection count.

These counters make it possible to confirm that a 100-unit selection issued one shared strategic route rather than 100 individual route requests.

## Debug Visualization

When F2 world debugging is enabled, the client captures formation debug state.

`FormationMovementDebugVisualization` renders:

- group centroid;
- current group bounds;
- forward direction;
- active shared waypoint;
- shared route waypoint chain;
- slot targets;
- member-to-slot assignment lines;
- formation name;
- member count;
- compression scale;
- split cohort count.

Debug rendering consumes copied snapshot state and does not mutate simulation data.

## Validation

Game tests cover:

- multi-unit command conversion to one group;
- one shared strategic request for 10, 50, and 100 units;
- no fallback to individual hierarchical path requests;
- recognizable line-slot geometry;
- stable slot identity across updates;
- entity destruction while a group is active;
- replacement commands and old-group retirement;
- narrow-corridor split fallback;
- concurrent movement of multiple groups.

The Simulation benchmark host contains `FormationRoutingBenchmarks`, comparing 10/50/100 independent strategic path searches with one shared formation route. Benchmark timing remains observational and is not a hardware-sensitive CI gate.

## Current Boundaries

The current system deliberately does not include:

- role-aware combat slot placement;
- permanent named combat groups;
- attack-move;
- retreat behavior;
- convoy-specific road-lane discipline;
- artillery deployment templates;
- multi-army traffic scheduling.

Those behaviors can build on the movement-group identity, shared-route lifecycle, and stable-slot foundation without moving locomotion ownership out of `GroundMovementSystem`.
