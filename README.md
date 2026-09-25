# FORGELINE

**Build. Supply. Conquer.**

FORGELINE is a large-scale real-time strategy game where industrial production, logistics, intelligence, and direct battlefield command form one interconnected war machine.

The project is built on **ForgeLine Engine**, a custom C#/.NET RTS engine designed specifically for large-scale, deterministic-friendly simulation. It is not intended to become a general-purpose game engine.

## Technical Baseline

- C# 14
- .NET 10 LTS
- Windows x64 as the initial client platform
- Direct3D 12 as the initial graphics backend
- data-oriented, ECS-first architecture
- fixed-tick, headless-capable simulation
- custom job system
- chunk-based world model
- hierarchical RTS navigation
- graph-based logistics simulation
- strict simulation/presentation separation
- multiplayer-aware architecture with networking deferred

Implemented engine foundations currently include the repository architecture, stable entity/component storage, a command-driven fixed-tick simulation runtime, deterministic simulation-owned randomness, a persistent-worker job scheduler, opt-in engine diagnostics, repeatable headless test scenarios, performance baselines, a standalone headless host, the native Windows interactive client host, the Direct3D 12 graphics foundation, the production-oriented RTS camera/input stack, the first chunked heightfield world, a chunk-aware uniform spatial index for world queries, the simulation-to-presentation snapshot pipeline with interpolated generic render instances and debug drawing, plus RTS selection and movement-order interaction, hierarchical ground navigation with movement-class traversability, sector portals, high-level routing, corridor-bounded local refinement and job-scheduled path requests, shared-route multi-unit formation movement with stable line/column/wedge/compact slots and choke-point fallback, together with authoritative fixed-tick ground locomotion, terrain/slope following, local separation, simple static-obstacle steering, and movement diagnostics, plus finite world resource deposits for Ferrous Ore, Volatiles, and Silicates with deterministic-friendly fixed-tick extraction, depletion, ownership validation, metrics, debug read models, and headless coverage, together with capacity-limited aggregate inventories, resource filters, reservations, atomic transfers, storage-depot state, and extractor-output backpressure, plus authoritative logical power networks with generator/consumer state, Critical/Industrial/Optional priority allocation, deterministic brownout handling, diagnostics, data-driven power profiles, and power-aware extraction throughput, together with data-driven building definitions, terrain/footprint/resource placement validation, command-driven construction, inventory reservations, cancellation refunds, fixed-tick progress, completion-time capability activation, and player-facing placement diagnostics.

Role-aware combat formations, convoy specialization, artillery deployment, attack-move, retreat, road-lane discipline, road/rail navigation bonuses, air/naval navigation, advanced dynamic replanning, permanent combat groups, minimap commands, transport logistics, resource processing, combat, production terrain materials and streaming, editor functionality, asset conversion, and networking remain deferred to their owning implementation stages.

## Repository Layout

```text
src/          Engine, simulation, game, presentation, client, and headless projects
tools/        FORGELINE-specific editor and compiler hosts
tests/        Unit and simulation-oriented test projects
benchmarks/   Performance benchmark hosts
build/        Repository validation and future build-support scripts
docs/         Architecture and development documentation
.github/      Continuous integration workflows
```

## Prerequisites

Install the .NET SDK version selected by `global.json`.

The current foundation targets .NET SDK **10.0.401**.

## Restore

```powershell
dotnet restore ForgeLine.sln
```

## Validate Architecture

```powershell
pwsh ./build/Validate-ProjectReferences.ps1
```

This validates the project-reference graph, rejects circular references, and enforces key simulation and headless dependency boundaries.

## Build

```powershell
dotnet build ForgeLine.sln --configuration Release
```

## Test

```powershell
dotnet test ForgeLine.sln --configuration Release
```

## Windows Client

Launch the native Windows x64 client host:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

The current client creates a DPI-aware native Win32 window, initializes Direct3D 12, runs the fixed-tick simulation, extracts immutable presentation snapshots, interpolates moving test entities, and renders them together with the chunked terrain. Left click selects a visible local unit/logistics entity, Shift + left click toggles selection, left-drag performs box selection, and right click submits a movement order through the fixed-tick command queue. A single eligible ground unit uses hierarchical navigation directly; multi-unit selections create one shared strategic route and formation-relative local targets before authoritative locomotion moves each member. F1 toggles the development metrics overlay, F2 toggles broader world-debug visualization, and F3 cycles the development formation selection through Compact, Line, Column, and Wedge. F4–F8 select Command Core, Power Plant, Mine / Extractor, Storage Depot, and Smelter placement respectively; F9 rotates the selected footprint clockwise, left click submits a valid placement as a simulation command, and Escape exits placement mode. World debugging includes movement targets, velocity vectors, local steering neighborhoods, navigation cells, sectors, portals, formation centroids, slots, assignments, shared routes, resource-deposit bounds, construction footprints, and construction progress.

Run the bounded client smoke validation used by CI:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test
```

The smoke mode creates the same native window, initializes Direct3D 12 with hardware-adapter selection and WARP fallback, generates the development world, runs the fixed-tick simulation/presentation pipeline, renders terrain and test entities briefly, then requests a clean shutdown.

See [Windows Client](docs/WindowsClient.md) for the platform boundary, window lifecycle, supported modes, DPI behavior, and validation procedure. See [Graphics](docs/Graphics.md) for Direct3D 12 ownership, frame synchronization, resize behavior, shader compilation, diagnostics, and resource lifetime. See [RTS Camera and Input](docs/CameraAndInput.md) for controls, coordinate conventions, action mapping, focus-loss behavior, and screen/world APIs. See [Selection and Command Interaction](docs/SelectionAndCommandInteraction.md) for selection ownership, picking/filtering, movement-command flow, and stale-entity handling. See [World and Terrain](docs/WorldAndTerrain.md) for world units, chunk/region coordinates, heightfield semantics, mesh generation, culling, diagnostics, and headless terrain queries. See [Spatial Index and World Queries](docs/SpatialIndexAndWorldQueries.md) for spatial entry ownership, cell mapping, query semantics, movement synchronization, diagnostics, and performance constraints. See [Ground Movement and Local Steering](docs/GroundMovementAndSteering.md) for fixed-tick locomotion, terrain and slope semantics, arrival, local separation, obstacle steering, stuck detection, diagnostics, and current limitations. See [Hierarchical Navigation](docs/HierarchicalNavigation.md) for movement classes, traversability, sectors/portals, path refinement, request lifecycle, version invalidation, caching, diagnostics, and benchmarks. See [Formation Movement and Group Orders](docs/FormationMovementAndGroupOrders.md) for shared-route group movement, formation templates, stable slot assignment, speed cohesion, choke-point fallback, diagnostics, and stress coverage. See [Resource Deposits and Extraction](docs/ResourceDepositsAndExtraction.md) for stable resource IDs, finite deposit semantics, extractor eligibility, depletion, diagnostics, headless tests, and stress benchmarks. See [Inventory and Storage](docs/InventoryAndStorage.md) for capacity, filtering, reservation, transfer, storage-depot, and extractor-output semantics. See [Power Networks](docs/PowerNetworks.md) for logical network membership, generation/demand aggregation, priority shortage allocation, brownout semantics, diagnostics, and the future physical-grid boundary. See [Building Placement and Construction](docs/BuildingPlacementAndConstruction.md) for building definitions, footprints, preview versus authoritative validation, construction resource policy, cancellation, progress, activation, controls, and diagnostics.

## Headless Simulation

Run the simulation without graphics, audio, UI, or window creation:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --tick-rate 20
```

The headless host executes logical simulation ticks as quickly as the machine permits. The configured tick rate defines simulation time; it does not force wall-clock pacing.

Create a repeatable lightweight-entity stress scenario and write structured diagnostics:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 64 --seed 42 --entities 10000 --diagnostics-output artifacts/stress-10000.json
```

## Diagnostics

Opt-in diagnostics expose tick timing, entity/component counts, job metrics, managed allocation and GC observations, and runtime environment metadata without coupling simulation to presentation or platform code.

See [Diagnostics and Performance](docs/DiagnosticsAndPerformance.md) for invariant conventions, metric interpretation, headless reports, stress scenarios, and regression investigation.

## Benchmarks

BenchmarkDotNet hosts cover the implemented ECS, hierarchical navigation, shared-versus-independent formation routing, simulation, command-processing, job-scheduler, spatial-query, 1,000-unit ground-movement, 100/1,000/10,000-pair resource extraction, 100/1,000/10,000-operation inventory transfer batches, 100/1,000/10,000-consumer power-network allocation, and rendering foundations.

Examples:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release
```

Benchmark timing is measurement evidence rather than a hardware-sensitive CI pass/fail gate.

## Architecture

See [Architecture](docs/Architecture.md) for project responsibilities and dependency rules.

See [Presentation Extraction and Debugging](docs/PresentationExtractionAndDebugging.md) for snapshot ownership, interpolation, debug tooling, overlay metrics, and rendering baselines.

See [Selection and Command Interaction](docs/SelectionAndCommandInteraction.md) for player interaction state, picking, ownership/category filtering, and simulation-safe movement orders.

See [Ground Movement and Local Steering](docs/GroundMovementAndSteering.md) for authoritative locomotion and short-range steering semantics.

See [Hierarchical Navigation](docs/HierarchicalNavigation.md) for long-range ground routing and the path-request lifecycle.

See [Formation Movement and Group Orders](docs/FormationMovementAndGroupOrders.md) for multi-unit shared routing, formation slots, cohesion, fallback behavior, diagnostics, and scale validation.

See [Simulation Runtime](docs/SimulationRuntime.md) for fixed-tick semantics, phase ordering, commands, deterministic randomness, and headless execution.

See [Resource Deposits and Extraction](docs/ResourceDepositsAndExtraction.md) for the authoritative raw-resource simulation contract.

See [Inventory and Storage](docs/InventoryAndStorage.md) for aggregate inventory ownership, capacity/filter rules, reservation semantics, atomic transfers, diagnostics, and extraction integration.

See [Power Networks](docs/PowerNetworks.md) for continuous-capacity semantics, logical membership, priority allocation, brownout behavior, operational states, diagnostics, and future physical-grid refinement.

See [Building Placement and Construction](docs/BuildingPlacementAndConstruction.md) for the authoritative building construction lifecycle, player placement flow, resource reservations, cancellation/refunds, and capability activation.

See [Development](docs/Development.md) for the canonical development and validation workflow.
