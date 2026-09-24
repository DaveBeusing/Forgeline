# Spatial Index and World Queries

## Purpose

`ForgeLine.World` provides the canonical spatial indexing foundation for simulation systems that need nearby-entity queries without scanning the complete ECS population.

The index is derived state. Canonical entity position and bounds remain simulation-owned world state; the index mirrors that state for efficient lookup and never becomes the source of truth for transforms.

The current implementation is intentionally a chunk-aware uniform grid. It is not a universal replacement for every future spatial workload.

## Coordinate and Cell Model

Spatial indexing reuses the existing canonical world conventions:

- world distances are meters
- +Y is up
- the query plane is X/Z
- world-to-chunk conversion uses mathematical floor semantics
- signed coordinates and negative chunks are supported
- the default world chunk size remains 256 meters

`SpatialGridSettings` adds a configurable spatial cell size. The default is 16 meters.

A configured cell size must divide the owning world chunk size into an integral number of cells. This keeps cell addressing unambiguous across chunk boundaries.

`SpatialCellAddress` contains:

- the canonical `ChunkCoordinate`
- local cell X
- local cell Z

Global cell coordinates exist only as an addressing helper. They do not replace canonical world or chunk coordinates.

## Spatial Entries

A `SpatialEntry` contains:

- stable `EntityId`
- world-space axis-aligned bounds
- faction value
- category bit mask
- mobility classification: static or mobile

The index stores no game-object references.

Bounds may overlap multiple cells. Such an entity is inserted into every overlapping cell but query buffers deduplicate the entity before returning results.

## Lifecycle

The basic lifecycle is:

```text
simulation/world state changes
        ↓
SpatialPresence + WorldTransform
        ↓
SpatialIndexSystem
        ↓
SpatialGridIndex
```

`SpatialIndexSystem` executes in the Movement phase after movement systems registered before it. This makes updated occupancy available to later Sensors and Combat phases in the same fixed tick.

`SpatialIndexCleanupSystem` executes at the Snapshot Events phase. It removes tracked entries for entities that were destroyed or lost the required spatial/transform components during later lifecycle processing.

Insert, update, upsert, and remove operations are also available directly for future systems that own specialized static or non-ECS spatial data.

## Query Semantics

The current index supports:

- exact occupied-cell candidates
- point queries
- axis-aligned bounding-box queries
- horizontal radius queries
- nearest-candidate lookup within a maximum radius

Point and AABB queries use complete 3D entry bounds.

Radius and nearest queries are intentionally horizontal X/Z operations. They measure distance from the query point to the closest X/Z point on each entry bound. This matches the initial RTS ground-world workload and avoids treating terrain elevation as radial separation.

Nearest-candidate ties are resolved by stable `EntityId`.

## Filtering

`SpatialQueryFilter` can restrict candidates by:

- exact faction value
- any matching category bit
- excluded category bits
- mobility classification

The filter is applied before candidates are written into the result buffer.

Higher-level systems should translate their domain-specific faction/category meaning into these generic values rather than adding combat, sensor, or logistics rules to `ForgeLine.World`.

## Result Ordering

The default query path does not sort results.

Callers that require deterministic iteration can request `SpatialQueryOrder.StableEntityId`. This sorts the deduplicated result set by stable entity identity.

Stable ordering is deliberately opt-in because sorting every query would add cost to workloads that do not depend on result order.

## Allocation Behavior

Hot query paths use a caller-owned `SpatialQueryBuffer`.

The buffer retains its list and deduplication set between queries. After capacity is established, ordinary repeated queries can reuse the same storage instead of allocating a new result collection each tick.

The grid query loops do not use LINQ or capturing delegates. Cell-size/chunk-size validation is performed during index construction rather than repeated for every visited cell in the internal range traversal.

Large queries can still cause the reusable collections to grow. Callers with known high candidate counts should construct the buffer with an appropriate initial capacity.

## Diagnostics

`SpatialIndexDiagnosticsSnapshot` reports:

- indexed entities
- occupied cells
- maximum cell occupancy
- average cell occupancy
- query count
- total query duration
- average query duration
- maximum query duration

Query timing is optional through `SpatialGridSettings.EnableQueryTiming`.

Timing metrics are observational only and must never affect simulation decisions.

## Debug Visualization

`CaptureDebugSnapshot()` produces a presentation-safe copy of occupied cell bounds and occupancy counts.

`SpatialIndexDebugVisualization` can draw:

- occupied cells
- occupancy labels
- AABB query regions
- radius query regions

The Windows development client uses the existing F2 world-debug toggle to display the active spatial grid. Presentation consumes snapshots and does not mutate the index.

## Movement and Static Entries

`SpatialPresence` describes the bounds and generic metadata required to derive a spatial entry from `WorldTransform`.

Mobile and static entries share the same current grid implementation.

Mobility is metadata rather than a promise that static entries are permanently immutable. Future measured workloads may justify separate storage or specialized acceleration structures, but callers must not create a competing world index merely to distinguish static from mobile entities.

## Correctness Validation

Spatial tests cover:

- positive and negative cell addressing
- exact chunk and cell crossings
- insert, update, and removal
- stale-cell removal after movement
- lifecycle cleanup after entity destruction
- multi-cell entry deduplication
- faction/category filtering
- stable ordering
- nearest-candidate tie behavior
- query diagnostics and debug snapshots
- brute-force comparison of radius queries
- 10,000 indexed entries repeatedly crossing cells

The brute-force fixtures are the correctness reference for indexed query results.

## Benchmarks

`ForgeLine.Simulation.Benchmarks` includes spatial workloads for:

- 64-meter radius queries
- 160-meter radius queries
- 256-meter AABB queries
- filtered radius queries with stable result ordering
- updates of 10,000 indexed entries

Run the benchmark host with:

```powershell
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release -- --filter *SpatialIndexBenchmarks*
```

Benchmark timing depends on hardware, runtime, density, entry bounds, and query size. Timing is measurement evidence and is not a hard CI threshold.

## Current Performance Characteristics

The implementation is optimized for the expected first RTS workloads:

- O(1)-style hash lookup for occupied cells
- query work scales primarily with visited cells and their occupancy rather than total entity count
- movement updates rewrite occupancy only when an entry crosses a cell-range boundary
- metadata-only or within-cell movement updates preserve cell membership
- result sorting is paid only when explicitly requested
- query buffers are reusable
- query timing instrumentation is optional

The 10,000-entry correctness stress scenario is an architectural guard, not a shipped performance guarantee.

## Current Limits

The current grid is deliberately simple.

Known limits include:

- one cell size per index instance
- single-threaded mutation ownership
- large bounds occupy every overlapped cell
- dense hotspots can create large cell buckets
- no BVH, octree, sweep structure, or specialized static geometry accelerator
- no built-in line-of-sight, terrain occlusion, combat targeting, fog-of-war, logistics routing, or pathfinding logic
- no automatic ECS hook; owning systems must synchronize derived occupancy explicitly

More specialized structures should be introduced only for measured query classes that the uniform grid does not serve efficiently.

## Architecture Rule

New steering, sensor, combat, logistics, selection, and navigation functionality should reuse this canonical nearby-entity query layer where its query semantics fit.

A specialized future index is valid when a measured workload requires different semantics or performance characteristics. It must complement the canonical world model rather than silently duplicate entity position ownership.
