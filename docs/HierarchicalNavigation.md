# Hierarchical Navigation

## Purpose

ForgeLine uses hierarchical navigation so long-distance RTS movement scales with regional structure instead of running one full-resolution global A* search for every unit.

The implemented ground-navigation stack is:

```text
Sector graph
    ↓
High-level route
    ↓
Local traversability grid
    ↓
Sector corridor
    ↓
Refined local route
    ↓
Ground movement waypoints
```

Ground locomotion remains separate. Navigation decides where a ground unit should travel; `GroundMovementSystem` owns final fixed-tick movement, terrain following, separation, short-range obstacle steering, arrival, and transform mutation.

## Ownership and Dependencies

`ForgeLine.Navigation` owns derived navigation data and pathfinding algorithms. It depends only on lower-level Core, Jobs, and World projects.

`ForgeLine.Game` owns the ECS-facing path-request lifecycle through `HierarchicalNavigationSystem`. The simulation coordinator provides the canonical `NavigationRequests` phase before `Movement`.

Presentation may visualize navigation read data. It does not write navigation or simulation state.

## Movement Classes

The baseline ground movement classes are:

- `Infantry`
- `Wheeled`
- `Tracked`

Each resolves to `NavigationCapabilities`, currently defining maximum traversable slope and slope cost weighting. Capabilities are part of the high-level cache key so routes derived for different mobility constraints cannot be mixed.

Rail-specific movement, road bonuses, air/naval navigation, and faction-specific navigation rules are deferred.

## Traversability Grid

`NavigationGridBuilder` derives an immutable grid from:

- canonical terrain height and surface normals;
- configured navigation cell size;
- static obstacle bounds;
- obstacle clearance.

The default cell size is 8 m and must divide the world chunk size exactly. Each cell records sampled height, slope, terrain presence, and static blockage.

Traversability is evaluated per movement capability. Slope contributes a movement cost until the capability limit is exceeded.

## Sectors and Portals

The local grid is divided into fixed-size navigation sectors aligned to chunk structure. Sector size must divide the number of navigation cells per chunk.

Adjacent sectors are connected only where their shared boundary contains traversable cells for the selected movement class. Each contiguous traversable boundary run creates one representative portal at its midpoint.

The resulting high-level graph is compact relative to the local grid and contains no presentation dependency.

## High-Level Search

Long routes use A* over the sector graph.

A full high-resolution world search is not the primary architecture. The high-level result contains ordered sectors, transition portals, and the expanded high-level node count.

Sector-route results are cached by navigation version, complete movement capabilities, start sector, and destination sector. Units or future combat groups sharing a movement class and regional endpoints therefore reuse the same high-level route computation.

## Local Refinement

After high-level routing, local A* refines the route only inside the selected sector corridor.

The first pass permits only sectors in the high-level route. If that exact corridor cannot refine successfully, a bounded one-sector expansion is attempted. The local search never silently falls back to unrestricted full-map A*.

Eight-directional movement is supported with diagonal corner-cut prevention. The final cell path is simplified at direction changes and converted into world-space waypoints for ground locomotion.

## Request and Result Lifecycle

Player input still enters through `MoveEntitiesCommand`.

For entities with `NavigationAgent`, `HierarchicalNavigationSystem` performs the following in the `NavigationRequests` phase:

1. Capture the accepted movement order and remove it from direct locomotion.
2. Create a versioned `NavigationPathRequest`.
3. Schedule read-only pathfinding through the simulation job scheduler when available.
4. Complete work at the normal simulation job boundary.
5. Apply completed results only on a later `NavigationRequests` boundary.
6. Reject results whose requester, request ID, order, or navigation version is stale.
7. Publish one local waypoint at a time as a normal `MovementOrder`.
8. Allow `GroundMovementSystem` to consume and complete that local order.
9. Publish the next waypoint on a later navigation phase until the route is complete.

Path jobs never mutate transforms or ECS state.

If no scheduler is configured, the same result queue is used and results still enter through the navigation phase boundary.

## Cancellation and Stale Results

A newer movement order supersedes an older pending request. The old ECS pending marker is canceled immediately. A worker already computing that immutable request may finish, but its result is discarded by request ID and order checks.

Replacing the navigation world also invalidates old requests and routes through `NavigationVersion`.

This avoids unsafe worker-side ECS mutation while ensuring obsolete results cannot take ownership of live movement.

## Versioning and Invalidation

`NavigationVersionTracker` provides a monotonic invalidation hook for future world changes.

A `NavigationWorld` is immutable for one version. When traversability-relevant world state changes, build a replacement derived navigation world with a new version and pass it to `HierarchicalNavigationSystem.UpdateWorld`.

Updating the world clears high-level route cache entries, causes active routes to be re-requested, and rejects pending results from the old version.

Localized incremental rebuilds are deferred until measurements justify the added complexity.

## Failure Behavior

Path search returns explicit failure states:

- start outside navigation world;
- destination outside navigation world;
- start blocked;
- destination blocked;
- no high-level route;
- no local refined route;
- stale navigation version;
- canceled request.

Game integration records route failures in `NavigationFailureState` instead of inventing direct fallback movement through blocked terrain.

## Diagnostics

`NavigationPath.Diagnostics` records high-level expanded nodes, local expanded nodes, refined cell count, route length, and high-level cache hits.

`HierarchicalNavigationSystem.LastDiagnostics` additionally records cumulative queued, completed, failed, canceled, and stale requests, current pending and active routes, and latest latency.

F2 development visualization can show local traversable and blocked cells, nearby sector boundaries, sector portals, the latest high-level portal route, and the latest refined waypoint route.

## Validation

Correctness fixtures cover open fields, movement-class slope constraints, static obstacles, blocked boundaries, choke points, multi-sector routes, explicit unreachable destinations, high-level cache reuse, job-scheduled simulation handoff, stale-result rejection, and large-map hierarchy scaling.

The large-map test requires local expansion to remain below the complete high-resolution grid cell count for a distant route. This protects the architectural property without introducing a hardware-sensitive timing assertion.

## Benchmarks

`ForgeLine.Navigation.Benchmarks` contains a multi-chunk long-distance route workload.

Run:

```powershell
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release
```

Benchmark timing is evidence for optimization work and is not a CI pass/fail gate.

## Current Limitations

The current implementation intentionally excludes formation slot placement, formation-specific local avoidance, road and rail movement bonuses, air/naval navigation, tactical combat routing, advanced dynamic replanning, flow fields, and localized incremental navigation-grid rebuilds.

These should be added only when gameplay requirements and measurements justify the permanent complexity.
