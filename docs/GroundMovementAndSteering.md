# Ground Movement and Local Steering

## Purpose

Ground movement is the authoritative fixed-tick locomotion layer for standard RTS ground units.

It converts accepted `MovementOrder` state into simulation-owned `WorldTransform` updates while preserving the engine boundaries between commands, simulation, world queries, spatial indexing, presentation, and rendering.

Strategic pathfinding is intentionally separate. This subsystem moves an entity toward the current local target or future route waypoint; it does not compute long-range routes.

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

`GroundMovement` is the data-driven locomotion profile. It contains:

- maximum speed
- acceleration
- deceleration
- turn rate in radians per second
- unit radius
- arrival / stop radius
- local separation radius
- static-obstacle look-ahead distance
- maximum traversable terrain slope
- terrain height offset for the entity origin

`GroundMovementState` contains the dynamic authoritative state:

- current velocity
- current yaw heading
- movement status
- accepted-order tick currently being observed
- previous target distance used for progress detection
- consecutive stalled ticks

Movement status is one of:

- `Idle`
- `Moving`
- `Arrived`
- `Stuck`

## Fixed-Tick Semantics

Movement uses only `SimulationContext.TickDuration`.

Render delta, frame rate, camera update rate, and presentation interpolation never influence simulation displacement.

Acceleration, deceleration, and heading changes are bounded per fixed tick. Braking speed is derived from the remaining target distance and configured deceleration so units slow down as they approach the stop radius.

An arrived unit:

- stops with zero authoritative velocity
- reports `Arrived` for the completion tick
- consumes its `MovementOrder`

On the following tick, with no active order, the unit returns to `Idle`.

## Heading and Turning

Ground movement uses yaw around +Y.

The horizontal forward vector is derived from the authoritative heading. Heading moves toward the desired steering heading by at most:

```text
TurnRateRadiansPerSecond × TickDuration
```

The transform rotation is written from that authoritative yaw value.

Ground units do not use rigid-body angular dynamics.

## Terrain Following

When an `ITerrainQuery` is supplied, every accepted movement candidate is validated against the authoritative terrain.

The system:

1. samples candidate terrain height
2. samples candidate terrain normal
3. derives slope from the normal
4. rejects movement when the configured maximum slope is exceeded
5. writes Y as sampled terrain height plus the unit height offset

A candidate outside available terrain is treated as blocked rather than allowing undefined movement beyond authoritative world data.

Terrain following remains headless and has no graphics dependency.

## Local Separation

Local separation uses `SpatialGridIndex` radius queries with stable entity ordering.

Mobile neighbors contribute short-range steering away from congestion. Candidate positions are additionally projected out of overlapping mobile radii to prevent units from occupying the same ground space under normal local movement.

This is intentionally lightweight local conflict resolution. It does not replace:

- strategic A*
- sector routing
- formation corridors
- formation slots
- traffic scheduling

Those systems can later provide route waypoints or corridors without changing the locomotion contract.

## Static Obstacle Steering

Static spatial entries are treated as short-range obstacles.

The movement system:

- queries static entries near the current movement direction
- evaluates a look-ahead probe
- adds deterministic lateral steering when an obstacle directly blocks the approach
- projects candidate positions outside configured obstacle clearance

This is deliberately simple steering for isolated local obstacles.

It is not a general path planner and is not expected to solve deep concave traps, maze routing, bridges, or long detours. Those cases belong to navigation.

## Progress and Stuck Detection

Progress is measured as reduction in horizontal distance to the active target.

The default progress epsilon is:

```text
0.025 m per tick
```

The default stuck threshold is:

```text
40 consecutive stalled ticks
```

At the initial 20 Hz simulation rate this corresponds to approximately two seconds, but the authoritative definition remains tick-based.

A new accepted order resets progress observation.

Stuck state is diagnostic and does not silently invent a strategic reroute. Future navigation can consume this state to request local or hierarchical replanning.

## Spatial Occupancy and Chunk Crossing

`SpatialIndexSystem` runs after ground movement in the same movement phase.

After each movement tick it upserts the new entity bounds into the chunk-aware spatial grid. Crossing world chunk or spatial-cell boundaries therefore requires no locomotion-specific migration path.

Lifecycle cleanup remains owned by `SpatialIndexCleanupSystem`.

## Diagnostics

`GroundMovementSystem.LastDiagnostics` exposes per-tick counters for:

- ground units
- ordered units
- moving units
- stuck units
- arrivals
- terrain-blocked units
- neighbor steering adjustments
- static-obstacle steering adjustments

These counters are simulation diagnostics only and do not affect movement behavior.

## Debug Visualization

Movement debug capture is opt-in through `GroundMovementSystem.DebugCaptureEnabled`.

When enabled, the system publishes an immutable `GroundMovementDebugSnapshot` containing position, velocity, target, radii, and movement status.

The presentation-side `GroundMovementDebugVisualization` draws:

- velocity vectors
- target lines and points
- local separation neighborhoods
- status-dependent movement colors

The Windows development client enables this capture together with the existing F2 world-debug mode.

No live ECS component references are retained by presentation.

## Parallelism Policy

The current movement pass is intentionally single-threaded and iterates ground units in stable entity order.

The existing ECS component stores and mutable spatial index do not currently expose a safe concurrent read/write ownership contract for a movement pass that both queries neighbors and commits transforms.

Parallel range processing must therefore not be introduced merely to satisfy a threading goal.

A future parallel version should first establish:

1. immutable or versioned movement input snapshots
2. read-only spatial-query ownership during calculation
3. disjoint output buffers
4. deterministic conflict resolution
5. ordered transform / spatial publication at the phase boundary

Only benchmark evidence should justify that additional complexity.

## Validation

Correctness coverage includes:

- equal constant-velocity travel over equal logical time at different fixed tick rates
- acceleration limits
- turn-rate limits
- target arrival and order consumption
- terrain-height following
- slope rejection
- world chunk crossing with spatial occupancy updates
- local non-overlap behavior
- static-obstacle detours
- stuck detection
- a 1,000-unit fixed-tick movement stress scenario

The simulation BenchmarkDotNet host also contains a resettable 1,000-unit movement workload.

The benchmark is measurement evidence, not a CI timing gate.

## Current Limitations

The current implementation intentionally does not include:

- long-range pathfinding
- hierarchical route generation
- formation slots or corridors
- combat movement policy
- road movement modifiers
- amphibious movement
- full rigid-body vehicle dynamics
- suspension or wheel simulation
- predictive multi-agent velocity-obstacle solvers

These are separate higher-level systems and must not be folded into base locomotion without a demonstrated gameplay requirement.
