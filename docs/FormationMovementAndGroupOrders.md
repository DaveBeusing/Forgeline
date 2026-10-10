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

`MoveEntitiesCommand` accepts the requested `FormationTemplate`. The Windows development client starts with `Compact` and F3 cycles `Compact → Line → Column → Wedge`; the selected template is passed into subsequent movement commands. This exposes the required minimal formation control before a dedicated RTS command UI exists.

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

The group samples traversable navigation cells laterally around both its current centroid and the active shared-route waypoint, using the narrower result. Sampling the current group position prevents simplified long route segments from hiding choke points that lie between sparse waypoints.

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

Recovery-aware Retreat uses this same group path. `RetreatToRecovery` resolves one support destination for the accepted selection and submits a normal Column formation move, so large retreats retain one shared strategic route rather than creating a recovery-specific pathfinder or independent long-range searches per member.

The Simulation benchmark host contains `FormationRoutingBenchmarks`, comparing 10/50/100 independent strategic path searches with one shared formation route. Benchmark timing remains observational and is not a hardware-sensitive CI gate.

## Player combat groups

Persistent player combat groups are **client/presentation organizational state**, not simulation-owned movement or combat-group entities. `CombatGroupRegistry` owns ten session-scoped slots for the local player and stores full-generation `EntityId` values only. A session replacement clears all assignments, labels, and the active slot.

This is intentionally separate from simulation-owned `MovementGroup`, `CombatGroupMember`, and `CombatGroupIntent` state. Those simulation types are transient execution structures created by real movement/tactical commands. A player control group does not become a new ECS entity, does not grant buffs, and does not own formation, readiness, supply, or combat behavior.

The registry synchronizes against the completed-tick `CombatGroupOperationalSnapshot`. Only locally owned selectable Unit/Logistics entities copied into that snapshot are eligible. Destroyed entities, invalid entities, foreign entities, and stale entity generations are removed deterministically. Reuse of the same entity index with a new generation does not recreate group membership.

Default controls follow standard RTS conventions without replacing existing bindings:

- `Ctrl+0..9`: assign the current owned selection to a slot;
- `0..9`: recall the slot into the existing `SelectionSet`;
- `Ctrl+Shift+0..9`: clear the slot;
- `Shift+0..9`: intentionally performs no combat-group action.

Recall changes only the existing presentation selection. Subsequent movement, formation, action-dock, tactical, and minimap commands therefore continue through the same existing request/command path and per-unit validation. Combat groups do not submit commands directly.

The operational summary is derived from immutable completed-tick member data. It may aggregate Health, Combat Readiness Strength/Overall Readiness, Battlefield Supply, Fuel, Ammunition, current formation, and common/mixed tactical order state. Maintenance is **not** shown because there is currently no authoritative per-unit runtime Maintenance value to copy.

The production HUD uses the `SecondaryView` region for a compact overview of assigned groups. Active and exactly selected groups are visually distinct, and the minimap brackets members of the active group using the existing selected-group semantic without exposing enemy information.

## Current Boundaries

The current system deliberately does not include:

- role-aware combat slot placement;
- permanent named combat groups;
- convoy-specific road-lane discipline;
- artillery deployment templates;
- multi-army traffic scheduling.

Those behaviors can build on the movement-group identity, shared-route lifecycle, and stable-slot foundation without moving locomotion ownership out of `GroundMovementSystem`.

## Current-selection group card

Multi-selection replaces single-entity inspector details with a compact group card. GROUP is the current presentation selection count, UNITS is the number of copied owned live Unit/Logistics members in that selection, and COMBAT is the subset with the existing WorldTransform/Combatant tactical eligibility. Buildings remain part of GROUP but never enter unit composition or numeric averages. Unknown authored types appear under OTHER. Full-generation identities are indexed and deduplicated once per completed-tick owned-member snapshot.

HP and readiness are unweighted arithmetic means of members carrying those components. Fuel and ammunition are unweighted means over members with UnitSupplyState. Each percentage shows its contributing member count in parentheses; no coverage displays N/A rather than zero or full readiness. Missing components never contribute a fabricated value. These aggregates do not imply identical capabilities or acceptance by all selected members.

Click INF, ENG, SCOUT, TANK, ART, CARGO, SUPPLY or OTHER to replace the current selection with that copied owned subtype. These represent Rifle Squad, Combat Engineer, Scout Vehicle, Main Battle Tank, Mobile Artillery, Cargo Truck, Supply Truck and unknown identities. Zero-count cells are inactive. Filtering never rewrites saved control-group slots, simulation group ownership, routes or orders. Recall still restores the assigned group.

HP<=25% marks critically damaged members; SUPPLY! marks Critical/Unsupplied members. Their FOCUS buttons cycle through the currently eligible selected members in stable entity order. Camera focus revalidates owned live world instances and finite copied positions; it preserves yaw, pitch and zoom and neither changes selection nor issues an order. FOCUS GROUP centers on authorized selected instances. Hidden, foreign, wrecked or missing identities cannot be used as focus targets.

The F3/NEXT control cycles the existing intended formation (Compact, Line, Column, Wedge). It applies to subsequent normal movement/tactical requests and does not claim the army has already adopted it. Existing contextual tactical commands and advanced Combat dock remain the command path. LAST CMD shows the latest copied resolved command feedback, including exact accepted/rejected target counts and PARTIAL; this is historical command feedback, not a result attributed to the current filtered subgroup.

Rendering and input share CombatGroupCardLayout within the existing SelectionInspector region. Font/control size fits its width and height at high DPI/UI scale. Focus, modal, placement/targeting, pause, display and session transitions consume pending press edges. Session/tick mismatches disable the card. A selection-count mismatch while awaiting the next copied summary keeps the existing aggregate fallback rather than acting on stale controls.

Primary/advanced action controls wait until copied tactical selection identities exactly match the current presentation selection, including full entity generation. While a filtered selection awaits capture, primary controls show UPDATING SELECTION and advanced item actions remain unavailable. Matching counts alone cannot authorize a request to a different subgroup.
