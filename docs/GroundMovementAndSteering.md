# Ground Movement and Local Steering

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Building service uses the [physical loading queue](VehicleQueues.md). Assigned
local row motion preserves predecessor spacing and stable individual targets.
Local admission transfers motion ownership to the existing exclusive dock;
service retains its settled position, heading and speed checks. Ordinary passing
remains available outside the queue strip. Queue membership does not teleport
vehicles or replace physical exit clearance.

<details>
<summary>On this page</summary>

- [Purpose](#purpose)
- [Simulation Ownership](#simulation-ownership)
- [Components](#components)
- [Fixed-Tick Semantics and Arrival](#fixed-tick-semantics-and-arrival)
- [Heading and Turning](#heading-and-turning)
- [Terrain Following](#terrain-following)
- [Neighborhood Queries and Local Separation](#neighborhood-queries-and-local-separation)
- [Static Obstacle Steering and Swept Clearance](#static-obstacle-steering-and-swept-clearance)
- [Progress and Stuck Detection](#progress-and-stuck-detection)
- [Spatial Occupancy and Chunk Crossing](#spatial-occupancy-and-chunk-crossing)
- [Diagnostics and Debug Visualization](#diagnostics-and-debug-visualization)
- [Parallelism Policy](#parallelism-policy)
- [Validation](#validation)
- [Current Limitations](#current-limitations)

</details>

## Purpose

Ground movement is the authoritative fixed-tick locomotion layer for standard RTS ground units.

It converts accepted `MovementOrder` state into simulation-owned `WorldTransform` updates while preserving the engine boundaries between commands, simulation, world queries, spatial indexing, presentation, and rendering.

Strategic pathfinding and formation planning are intentionally separate. This subsystem moves an entity toward the current local target or route/formation slot target; it does not compute long-range routes or formation geometry.

## Simulation Ownership

The canonical flow is:

```text
Input / UI
    ↓
MoveEntitiesCommand
    ↓
MovementOrder
    ↓
GroundMovementSystem
    ↓
WorldTransform + GroundMovementState
    ↓
SpatialIndexSystem
    ↓
post-tick presentation extraction
```

`GroundMovementSystem` executes in `SimulationPhase.Movement`.

The client registers it before `SpatialIndexSystem`. Local steering therefore reads the spatial occupancy produced by the preceding completed tick, movement writes authoritative transforms, and the spatial synchronizer then publishes the new occupancy before later simulation phases run.

Rendering never mutates movement state. `PresentationExtractor` continues to copy completed simulation transforms into immutable presentation snapshots for render interpolation.

## Components

`GroundMovement` is the data-driven locomotion profile. It contains maximum speed, acceleration, deceleration, turn rate, unit radius, stop radius, separation radius, obstacle look-ahead, maximum terrain slope and terrain height offset.

`FormationMovementConstraint` may additionally provide a temporary group speed ceiling. Supply and suppression constraints can reduce mobility. These rules do not change because a renderer or optional steering effect is disabled.

`GroundMovementState` contains current velocity, yaw heading, movement status, observed accepted-order tick, best target-distance observation and consecutive stalled ticks.

Movement status is one of `Idle`, `Moving`, `Arrived`, `Stuck`, `OutOfFuel` or `TacticallyPaused`.

## Fixed-Tick Semantics and Arrival

Movement uses only `SimulationContext.TickDuration`. Render delta, frame rate, camera update rate and presentation interpolation never influence simulation displacement.

Unconstrained acceleration, deceleration and heading changes are bounded per fixed tick. Braking speed is derived from remaining target distance and configured deceleration. Final arrival and collision safety can stop or shorten a step rather than preserving momentum through an obstacle or beyond a small target.

`GroundMovementStep.ClampArrival` examines the proposed horizontal movement segment. When its closest point to the target lies within the stop radius plus the existing 0.01 m completion tolerance, the segment can be shortened to that point. It never moves the unit sideways to an unvisited target. This protects even a zero stop radius against crossing the entire arrival region between two ticks.

Collision correction and terrain validation run before final arrival acceptance. A target behind a blocking wall must not be accepted merely because the unconstrained step could reach it.

An arrived unit stops with zero authoritative velocity, reports `Arrived` for the completion tick and consumes its `MovementOrder`. On the following tick, with no active order, it returns to `Idle`.

Dock orders additionally carry a target heading. They use a 0.1 m movement stop
radius and complete the bounded heading adjustment before consuming the order.
The shared [vehicle docking contract](VehicleDocking.md) separately verifies two
stationary ticks before permitting service. Dock entry and exit use local speed
limits without changing general strategic movement speeds.

This does not change intermediate waypoint handoff into continuous corridor following. Navigation still owns waypoint publication and completion.

## Heading and Turning

Ground movement uses yaw around +Y. The horizontal forward vector is derived from the authoritative heading. Heading moves toward the desired steering heading by at most:

```text
TurnRateRadiansPerSecond × TickDuration
```

The transform rotation is written from that yaw value. Ground units do not use rigid-body angular dynamics. Vehicle turn-radius policies and class-specific reverse manoeuvres are separate design work, not implied by arrival correction.

## Terrain Following

When an `ITerrainQuery` is supplied, each accepted movement candidate samples authoritative height and normal. The normal determines slope; excessive slope or missing terrain rejects the candidate. Accepted Y is sampled terrain height plus the unit height offset.

Terrain following remains headless and has no graphics dependency. It still validates the candidate rather than continuously sweeping every heightfield feature along a very long step; swept static-obstacle clearance does not establish continuous terrain collision.

## Neighborhood Queries and Local Separation

`QueryMovementNeighborhood` gathers mobile and static candidates into reusable spatial-query buffers with stable entity ordering. Mobile queries cover the greater of separation range and the possible tick travel plus unit radius. Static queries cover the greater of look-ahead and possible travel, plus unit radius and static clearance.

These queries are independent of `SeparationWeight`, `ObstacleSteeringWeight` and zero obstacle look-ahead. Disabling steering changes avoidance preference, not collision eligibility. A missing spatial index still means no spatial collision service is configured.

Mobile neighbors contribute short-range steering away from congestion. Candidate positions are additionally projected out of overlapping mobile radii. This remains endpoint-based local conflict resolution against preceding-tick occupancy, not a swept multi-agent collision guarantee or a simultaneous crowd solver.

Shared-route formation movement supplies formation-relative slot targets through the existing locomotion contract. Separation may temporarily pull a member away from its slot; subsequent formation updates guide it back without bypassing steering.

## Static Obstacle Steering and Swept Clearance

Static spatial entries are treated as short-range obstacles. Optional steering evaluates a look-ahead probe and adds deterministic lateral steering when an obstacle blocks the approach. Endpoint penetration correction projects candidates outside the configured clearance.

After these corrections, `ConstrainStaticStep` checks the complete horizontal segment against static bounds expanded by the moving circle. Side intersections and circular corner caps preserve rounded clearance rather than treating every corner as an oversized box. The earliest contact clips the step with a 0.001 m contact skin. If penetration correction has enlarged the original travel envelope, a further static query covers the corrected segment.

Already-overlapping starts retain the existing depenetration policy so a newly spawned or displaced unit is not trapped permanently by a zero-time sweep hit. This is a recovery exception, not proof that invalid initial overlaps are safe content placement.

Thin static obstacles cannot be skipped just because both the start and unconstrained endpoint lie outside the obstacle. Tangential travel and rounded corner clearance remain regression-tested.

This is not a general path planner. Deep concave traps, maze routing, bridges and long detours belong to navigation. Swept contact prevents penetration; it does not itself generate a collision-free detour.

## Progress and Stuck Detection

Progress is measured against the best observed horizontal distance to the active target. Small forward motion accumulates until it exceeds the default 0.025 m progress epsilon. Back-and-forth motion alone does not count as forward progress.

The default stuck threshold is 40 consecutive stalled ticks. At 20 Hz this is approximately two seconds; the authoritative definition remains tick-based. A new accepted order resets progress observation.

Stuck state is diagnostic and does not silently invent a strategic reroute inside ground movement. Higher-level navigation owns replanning decisions.

## Spatial Occupancy and Chunk Crossing

`SpatialIndexSystem` runs after ground movement in the same movement phase. It upserts new entity bounds into the chunk-aware spatial grid. Crossing world chunk or spatial-cell boundaries therefore requires no locomotion-specific migration path.

Lifecycle cleanup remains owned by `SpatialIndexCleanupSystem`.

## Diagnostics and Debug Visualization

`LastDiagnostics` exposes per-tick ground, ordered, moving, stuck, arrived and terrain-blocked unit counts, plus neighbor and static-obstacle steering adjustment counts. Steering adjustment counts do not represent the number of swept collision contacts.

Opt-in `DebugCaptureEnabled` publishes immutable positions, velocities, targets, radii and movement statuses. `GroundMovementDebugVisualization` draws velocity vectors, targets and separation neighborhoods. Presentation retains no live ECS component references and cannot mutate movement state.

## Parallelism Policy

The movement pass is intentionally single-threaded with stable entity iteration. Existing component stores and mutable spatial indexing do not provide a simultaneous crowd-conflict ownership contract.

Parallelization first requires immutable or versioned movement inputs, read-only spatial queries, disjoint output buffers, deterministic conflict resolution and ordered publication at the phase boundary. Only benchmark evidence should justify that complexity.

## Validation

Existing tests cover constant-speed travel across tick rates, acceleration and turn limits, arrival, terrain following, slope rejection, chunk crossing, separation, obstacle detours, progress tracking and a 1,000-unit movement workload.

`GroundMovementPrecisionTests` adds small and zero-radius high-speed arrivals at 10/20/60 Hz, off-axis non-arrival, thin walls with avoidance disabled, blocked-target rejection, mobile collision with separation disabled, initial-overlap recovery and tangential contact travel.

The simulation BenchmarkDotNet host contains a resettable 1,000-unit movement workload. Navigation CPU measurements are not a substitute for profiling the added static collision work or crowded full-match movement.

## Current Limitations

The implementation does not introduce convoy lane discipline, road bonuses, amphibious travel, full rigid-body dynamics, suspension physics, predictive multi-agent velocity-obstacle solving, or a continuous swept heightfield solver. Existing tactical and supply constraints remain authoritative; the locomotion layer does not own the higher-level combat policy that produces them.

See [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for group routes, speed harmonization, slot following and choke-point fallback, and [Navigation and Movement Quality](NavigationMovementQuality.md) for the optimization boundaries and reproduction commands.
