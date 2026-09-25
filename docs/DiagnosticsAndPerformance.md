# Diagnostics and Performance

ForgeLine Engine treats correctness diagnostics and performance measurement as part of the engine foundation rather than late-stage tooling.

## Diagnostic Model

Core diagnostic terminology is shared through:

- `DiagnosticCategory`
- `DiagnosticSeverity`
- `EngineDiagnostic`
- `EngineInvariant`
- `EngineInvariantException`

Invariant failures use a stable category and code together with an explicit failure message. Current ECS invalid-entity mutations report `ECS_ENTITY_NOT_ALIVE`.

The invariant mechanism is intended for states that indicate engine correctness failures. It is not a replacement for normal input validation or recoverable gameplay errors.

## Simulation Diagnostics

`SimulationCoordinator` exposes opt-in diagnostics through `SimulationDiagnostics`.

Diagnostics are disabled by default. Disabled diagnostics do not collect tick timing or allocation deltas and retain the allocation-free empty-tick behavior verified by the simulation tests.

When enabled, snapshots expose:

- configured simulation tick rate
- completed tick count
- last tick duration
- average observed tick duration
- maximum observed tick duration
- allocated bytes observed during the diagnostic session
- Gen 0, Gen 1, and Gen 2 collection deltas
- live entity count
- entity capacity
- component type count
- total component instance count
- deterministic per-component counts
- job scheduler counters and aggregate timing when a scheduler is present
- current managed runtime allocation, heap, memory-load, and collection information

Tick timing is diagnostic observation only. It must never influence simulation decisions.

## Combat Runtime Metrics

`CombatRuntime.Metrics` exposes simulation-owned combat counters without introducing presentation authority:

- active physical projectiles
- shots fired and Ammunition consumed for the current tick
- projectiles spawned and impacts for the current tick
- successful damage applications and damage amount for the current tick
- combat destructions for the current tick
- cumulative shots, Ammunition consumption, projectiles, impacts, hits, damage, and destructions

`CombatRuntime.Events` contains the current tick's shot, projectile-spawn, impact, damage, and destruction outputs. These events are presentation/diagnostic outputs only; consumers must not feed visual timing back into combat resolution.

`CombatDebugSnapshotSystem` can capture weapon ranges/targets, projectile positions/velocities, Health values, impacts, and the runtime metrics during `SnapshotEvents`. The Windows client F2 world-debug path renders those copies without mutating simulation state.

Directional armor and target acquisition add simulation-owned diagnostics without changing that authority boundary.

`TargetAcquisitionSystem.Metrics` reports current-tick and cumulative scans, candidates, acquisitions, reacquisitions, and rejects, plus cumulative friendly, target-class, range, intelligence-availability, line-of-fire, and fire-policy rejection counts.

`CombatDamageResolutionSystem.Metrics` reports armored hits, Front/Side/Rear/Top hit counts, and cumulative mitigated damage.

When debug capture is enabled, target rejection positions/reasons and armor facing transforms are copied into the combat debug snapshot for F2 visualization.

## Battlefield Intelligence Metrics

`BattlefieldIntelligenceSystem.Metrics` reports active visual/radar sensors, scans, candidate counts, Detected/Identified contact counts, visible/explored cell counts, cumulative sensing work, and optional measured sensor-update duration.

Sensor timing is enabled only when requested by the development composition. Timing never feeds simulation decisions.

Faction-intelligence snapshots provide presentation-safe Fog-of-War cells and contact read models. Detected-only contacts contain opaque contact keys and last-known positions rather than current hidden entity transforms.

## Job Metrics

The job scheduler exposes:

- submitted jobs
- completed jobs
- faulted jobs
- canceled jobs
- pending jobs
- running jobs
- worker count
- peak concurrent running jobs
- aggregate wait duration
- aggregate execution duration
- instrumentation failures

Per-job timing remains available through the scheduler timing observer.

## Headless Diagnostic Reports

The headless host can write an indented JSON report containing runtime metadata, requested scenario parameters, loop counters, and a simulation diagnostic snapshot.

Example:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --tick-rate 20 --entities 1000 --diagnostics-output artifacts/headless.json
```

The report records:

- .NET runtime description
- operating system description
- process architecture
- processor count
- seed and logical tick rate
- requested and executed tick counts
- requested entity count
- wall-clock elapsed time and throughput
- simulation loop counters
- simulation, ECS, job, allocation, and GC metrics

These values make results interpretable across different machines. They are measurements, not product guarantees.

## Headless Test Harness

Simulation tests use a reusable `SimulationTestHarness` that can:

- create a simulation with a known seed
- optionally create a job scheduler
- create entities
- submit commands to explicit target ticks
- run an exact number of ticks
- expose resulting ECS and simulation state

This keeps deterministic integration scenarios concise and ensures tests do not initialize graphics, audio, UI, or windowing.

Run focused simulation tests with:

```powershell
dotnet test tests/ForgeLine.Simulation.Tests/ForgeLine.Simulation.Tests.csproj --configuration Release
```

Run the complete suite with:

```powershell
dotnet test ForgeLine.sln --configuration Release
```

## Benchmarks

BenchmarkDotNet hosts are separate from correctness tests.

ECS baselines cover:

- entity creation
- entity destruction
- component add/remove
- component lookup
- dense ECS iteration
- multi-component queries
- 1,000 and 10,000 entity workloads

Rendering diagnostics additionally expose frame time, CPU render time, terrain visibility/submission counts, generic instance visibility/submission counts, and development overlay allocation/GC state. GPU timestamps remain deferred until the graphics abstraction owns a clean timestamp-query/readback lifecycle.

Navigation baselines cover long-distance path searches over a multi-chunk world, including sector routing, local refinement, cache reuse, expanded-node counts, and route length. Benchmark timing remains observational rather than a CI gate.

Simulation baselines cover:

- 10, 50, and 100-unit independent strategic routing versus one shared formation route
- empty fixed ticks
- light system ticks
- 1,000-entity iteration ticks
- representative multi-system ticks
- command scheduling and processing
- sequential range work
- parallel scheduler range work
- spatial radius queries at multiple query sizes
- spatial AABB queries
- filtered spatial queries with stable result ordering
- updates of 10,000 indexed entries

Run them with:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release
```

The rendering host includes terrain workloads plus 1,000 near-field simple instances and 5,000 total simple instances with far-field culling. BenchmarkDotNet output includes runtime and machine information. Keep benchmark results when comparing architecture or hot-path changes so the environment remains visible.

## Stress Scenarios

A bounded headless lightweight-entity scenario is available directly from the host:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 64 --seed 42 --entities 10000 --diagnostics-output artifacts/stress-10000.json
```

The simulation test suite also verifies that 10,000 lightweight ECS entities can exist and execute headless ticks without stale-entity or lifecycle failure. Spatial correctness coverage separately indexes and repeatedly moves 10,000 entries, verifies queryability afterward, compares radius results against brute-force reference fixtures, and checks negative-coordinate and chunk-crossing semantics. A 1,000-entity scenario exercises a representative multi-component load.

Combat scale measurement is available through the simulation BenchmarkDotNet host with 100/1,000 simultaneously armed direct-fire entities and 100/1,000 moving physical projectiles. Logistics stress coverage remains separate so the measured workload is attributable to the subsystem under test.

## Interpreting Results

Use timing values comparatively rather than as universal thresholds.

Investigate regressions by checking:

1. whether the runtime, OS, architecture, and processor count are comparable;
2. whether entity count and workload parameters are identical;
3. whether allocation or GC behavior changed;
4. whether job counts, wait time, or execution time changed;
5. whether a code change altered the amount of simulated work;
6. whether the result reproduces across repeated benchmark runs.

Hardware-sensitive timing is intentionally not a hard CI gate.

## CI

CI:

- restores and builds the complete solution;
- validates project-reference boundaries;
- runs a 1,000-entity headless diagnostics smoke scenario;
- runs a bounded 10,000-lightweight-entity stress smoke scenario;
- runs the complete correctness test suite;
- uploads the generated JSON diagnostics as the `engine-diagnostics` workflow artifact.

The artifact exists to make failures and performance observations inspectable without turning volatile timing into pass/fail thresholds.

## Current Engineering Targets

The following remain non-binding engineering targets:

- stable 20 Hz simulation;
- 1,000+ active combat/logistics entities without architectural redesign once those gameplay systems exist;
- 10,000+ lightweight simulation entities as an early stress target.

They are engineering goals, not shipped product guarantees.


## Spatial Index Diagnostics

`SpatialGridIndex.CaptureDiagnostics()` exposes:

- indexed entity count
- occupied cell count
- maximum cell occupancy
- average cell occupancy
- query count
- total, average, and maximum query duration when query timing is enabled

Query timing is optional so timing instrumentation does not become mandatory hot-path overhead. The Windows client enables it for the development scenario.

`CaptureDebugSnapshot()` exposes a presentation-safe copy of occupied cell bounds and occupancy counts. F2 world debugging uses this snapshot to draw occupied cells and query regions without allowing rendering to mutate the simulation index.

See [Spatial Index and World Queries](SpatialIndexAndWorldQueries.md) for query semantics, lifecycle ownership, and performance limits.

## Presentation Diagnostics

The Windows client development overlay can be toggled with F1. World debug visualization can be toggled with F2. The presentation path, metric semantics, extraction ownership, and render baselines are documented in [Presentation Extraction and Debugging](PresentationExtractionAndDebugging.md).


## Navigation Diagnostics

`HierarchicalNavigationSystem.LastDiagnostics` exposes queued path requests; completed, failed, canceled, and stale-result counts; currently pending requests; active routes; expanded high-level and local nodes for the latest completed route; latest route length; and latest pathfinding latency.

`NavigationPath.Diagnostics` additionally records whether the high-level route cache was hit. These values are diagnostic observations only and never change simulation outcomes.

F2 world debugging can display local traversability, nearby sector boundaries, sector portals, the latest high-level route, and its refined waypoint path. Rendering receives navigation-derived read data only and never mutates navigation or simulation state.

See [Hierarchical Navigation](HierarchicalNavigation.md) for lifecycle and interpretation details.


## Formation Movement Diagnostics

`FormationMovementSystem.LastDiagnostics` exposes active group/member counts, largest group size, compressed groups, shared strategic path requests, completed/failed groups, slot reassignments, compression events, split-cohort events, and blocked-slot projections.

A healthy large-selection move should show one shared path request for the movement group while `HierarchicalNavigationSystem` reports no new per-member strategic requests for formation-local slot orders.

F2 world debugging additionally renders movement-group bounds and centroids, travel direction, active shared waypoint and route, slot targets, assignment lines, compression scale, and split cohort count. The presentation layer consumes copied debug snapshots only.

The Simulation BenchmarkDotNet host includes `FormationRoutingBenchmarks` for 10, 50, and 100 members. It compares independent strategic path searches with one centroid-based shared group route. Timing is observational; the primary invariant is the reduction from N strategic route requests to one group route where members can share navigation.

See [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for lifecycle, fallback behavior, and interpretation details.


## Artillery Diagnostics

`ArtilleryFireMissionSystem.Metrics` reports active missions, `NoAmmo` missions, shells in flight, shots, impacts, affected area-damage targets, Ammunition consumption, and queued area damage for the current tick and cumulatively.

The F2 artillery debug read model exposes mission min/max range, fixed target coordinate, lifecycle state, requested/fired rounds, shell position, complete parabolic trajectory, impact radius, and a compact metrics label. These diagnostics consume simulation-owned state without controlling targeting, dispersion, impact timing, or damage.
