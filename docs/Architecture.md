# ForgeLine Architecture

## Purpose

ForgeLine Engine is the custom RTS engine for FORGELINE. Its architecture is optimized for large-scale deterministic-friendly simulation, high entity counts, predictable simulation cost, efficient multithreading, large worlds, hierarchical navigation, industrial/logistics simulation, headless execution, testability, replayability, and future multiplayer support.

It is deliberately not a general-purpose game engine.

## Dependency Direction

The intended high-level direction is:

```text
Core / low-level infrastructure
        ↓
ECS / Jobs / World / Navigation
        ↓
Simulation domains
        ↓
Game rules
        ↓
Presentation / UI / Client
```

Platform, graphics, audio, input, and asset infrastructure remain isolated from simulation rules. Project references must remain acyclic.

## Fundamental Rules

- `ForgeLine.Core` depends on no higher-level project.
- Simulation code must not depend on Direct3D, windowing, UI, audio, editor code, or client composition.
- `ForgeLine.Graphics` must not depend on game rules.
- Rendering must never mutate simulation state directly.
- `ForgeLine.Headless` must operate without graphics, audio, UI, presentation, client, or Windows-windowing dependencies.
- Platform-specific APIs remain behind platform boundaries.
- External actions enter simulation through commands rather than direct state mutation.
- Selection and hover remain presentation/player-interaction state rather than authoritative simulation ownership.
- Presentation picking returns stable simulation entity IDs copied through immutable snapshots.
- Simulation and presentation remain separate so complete matches can run without a window.
- Circular project references are prohibited.

The repository validates several of these invariants with `build/Validate-ProjectReferences.ps1`, which also checks the complete project graph for cycles.

## Project Responsibilities

### Low-Level Infrastructure

- `ForgeLine.Core`: identifiers, math, collections, memory helpers, diagnostics, timing, and serialization primitives.
- `ForgeLine.Platform.Windows`: Windows x64 platform integration, native window lifecycle, high-resolution host timing, DPI handling, and Win32 message processing behind platform-facing contracts.
- `ForgeLine.Graphics`: Direct3D 12 adapter/device ownership, command submission, flip-model swap chain, render-target descriptors, frame synchronization, resize handling, GPU resource foundations, DXC shader compilation, and graphics diagnostics behind engine-facing contracts.
- `ForgeLine.Audio`: audio infrastructure.
- `ForgeLine.Input`: raw input state, configurable RTS action mapping, and input-frame contracts above the platform event boundary.
- `ForgeLine.Assets`: stable runtime asset identifiers, manifest contracts, validated runtime-path lookup, and versioned `.flasset` loading. Source import and compilation remain outside runtime code; see `docs/AssetPipeline.md`.

### Simulation Foundation

- `ForgeLine.Ecs`: custom data-oriented entity/component storage and queries. The implemented low-level contracts and invariants are documented in `docs/Ecs.md`.
- `ForgeLine.Jobs`: persistent-worker job scheduling, dependency handles, range execution, fences, failure propagation, and timing instrumentation. The implemented contracts and safe usage rules are documented in `docs/JobSystem.md`.
- `ForgeLine.World`: canonical world/region/chunk coordinates, chunk-based terrain ownership, headless heightfield sampling, terrain bounds, deterministic development terrain, CPU terrain mesh generation, and the derived chunk-aware uniform spatial index used by simulation-facing world queries.
- `ForgeLine.Navigation`: immutable movement-class traversability data, chunk-aligned sector graphs and portals, versioned high-level route caching, bounded local path refinement, navigation requests/results, and pathfinding diagnostics. See `docs/HierarchicalNavigation.md`.
- `ForgeLine.Simulation`: command-driven fixed-tick coordination, explicit phase ordering, simulation-owned randomness, and common simulation infrastructure.

The implemented simulation lifecycle and command boundary are documented in `docs/SimulationRuntime.md`.

### Simulation Domains

- `ForgeLine.Economy`: economy and production simulation.
- `ForgeLine.Logistics`: graph-based logistics topology, transport metadata, reachability, deterministic route planning, versioned invalidation/caching, and logistics diagnostics. See `docs/LogisticsNetworkAndRouting.md`.
- `ForgeLine.Combat`: combat simulation.
- `ForgeLine.Intelligence`: visibility, sensors, and intelligence simulation.
- `ForgeLine.AI`: strategic, operational, tactical, and unit-behavior orchestration.

Simulation domains remain independently layered and are implemented progressively. A domain project may therefore contain production simulation while later capabilities in the same domain remain deferred.

### Game and Presentation

- `ForgeLine.Game`: FORGELINE rules and composition of simulation domains. Interaction-facing game contracts include player ownership, controllable entity categories, simulation-owned movement-order state, movement-group identity/lifecycle, shared-route formation state, formation slot assignment, and the validated movement command. Physical Cargo Truck execution also lives here because it composes economy inventories, logistics routes, hierarchical navigation, and ground movement while leaving each lower-level domain authoritative for its own state.
- `ForgeLine.Presentation`: post-tick extraction into immutable snapshots, buffered simulation-to-render handoff, render-world interpolation, generic instance rendering, debug visualization, development metrics, RTS camera state/projection, visible-entity picking, and player selection state.
- `ForgeLine.UI`: RTS-specific user-interface boundary.
- `ForgeLine.Client`: composition root for the interactive Windows application. It owns platform/graphics lifecycle and translates presentation movement requests into simulation commands without exposing live ECS mutation to input or rendering code.
- `ForgeLine.Headless`: non-visual composition root for simulation tests, AI matches, balancing, performance work, replay validation, and future server experiments.

### Tools

- `ForgeLine.Editor`: FORGELINE-specific editor host.
- `ForgeLine.AssetCompiler`: production source-to-runtime asset compiler for static glTF/GLB meshes, PNG/TGA textures, material definitions, stable IDs, dependency validation, incremental rebuilds, and runtime manifest generation. See `docs/AssetPipeline.md`.
- `ForgeLine.MapCompiler`: map compilation host.

The Editor and Map Compiler remain foundation hosts. The Asset Compiler is an implemented tooling boundary and must not leak source-format parsing into runtime or simulation projects.

## Simulation Baseline

The simulation runtime is fixed-step with a default engineering target of 20 simulation ticks per second, equivalent to 50 ms of logical simulation time per tick.

Each tick traverses an explicit canonical phase sequence. Commands scheduled for a tick are executed at the Input Commands boundary before later phases run. Systems register for a specific phase and execute in stable registration order within that phase.

The movement interaction preserves this boundary: the client schedules `MoveEntitiesCommand` for a future tick. Individual eligible units receive validated strategic `MovementOrder` state. Multi-unit ground selections create a simulation-owned movement group instead. `FormationMovementSystem` consumes those groups first in `NavigationRequests`, schedules one shared hierarchical path, derives stable formation-relative slot targets, and publishes only `FormationLocal` movement orders plus a group speed constraint. `HierarchicalNavigationSystem` continues to route individual strategic orders and explicitly ignores formation-local slot orders. `GroundMovementSystem` remains the sole owner of final locomotion and transform changes in `Movement`.

Cargo transport preserves the same separation. `ForgeLine.Logistics` selects versioned strategic routes between logistics nodes, `CargoTransportSystem` in `ForgeLine.Game` advances the load/move/unload lifecycle, `HierarchicalNavigationSystem` resolves physical ground paths to each route node, and `GroundMovementSystem` remains the only owner of vehicle transform mutation. Inventory changes occur only through shared atomic `InventoryStore` operations after physical arrival. See `docs/CargoTransportOperations.md`.

Automated regional distribution is composed above those existing authorities. `AutomatedDistributionSystem` in `ForgeLine.Game` evaluates stock policies, coalesces deficits into transport requests, selects authoritative logistics routes and surplus sources, reserves source inventory, and assigns idle physical Cargo Trucks. It never moves resources remotely: `CargoTransportSystem` remains responsible for physical loading, navigation, unloading, and transport failure state. Presentation receives only distribution read models and debug snapshots. See `docs/AutomatedDistributionAndLogisticsHubs.md`.

Battlefield supply is the downstream operational layer. `BattlefieldSupplySystem` executes in the canonical `Supply` phase, consumes Fuel from authoritative unit movement, loads dedicated Supply Trucks only when physically near an operational Supply Depot, and transfers Fuel/Ammunition only between real inventories within provider range. Supply Depots are normal logistics-network destinations with automated Fuel/Ammunition stock policies; regional replenishment therefore remains owned by the existing distribution and Cargo Truck systems. Unit status and debug data are read models only, while zero-Fuel mobility constraints are consumed by `GroundMovementSystem`. See `docs/BattlefieldSupply.md`.

Logistics capacity and disruption extend the same graph without creating a parallel transport authority. `LogisticsCapacityTracker` accounts scheduled edge/node load over a simulation-tick window, `LogisticsNetwork.FindCapacityAwareRoute` applies current capacity and lightweight congestion cost during dispatch planning, and `AutomatedDistributionSystem` admits work only when real throughput remains. Dynamic load does not change the structural logistics-network version; node/edge availability changes do, so active Cargo Truck routes continue to invalidate and reroute through the existing physical transport lifecycle. Explicit node disruptions persist as simulation-owned availability overrides that building registration must honor. Presentation consumes capacity and bottleneck snapshots only. See `docs/LogisticsCapacityAndDisruption.md`.

Combat execution follows the canonical `Combat -> DamageResolution -> EntityLifecycle -> SnapshotEvents` boundaries. `ForgeLine.Combat` owns stable weapon/projectile/health/event contracts and the existing inventory-backed `AmmunitionState`; `ForgeLine.Game` owns tick execution against simulation transforms and world queries. `CombatExecutionSystem` validates cadence, range, faction and Ammunition before resolving hitscan fire or moving physical projectiles. Damage is buffered into `CombatDamageResolutionSystem`, while zero-health entities and spent projectiles are destroyed only by `CombatEntityLifecycleSystem`. `CombatDebugSnapshotSystem` and presentation visualization consume copied outputs only and never determine outcomes. See `docs/CombatExecution.md`.

Directional armor and target acquisition extend the same authority chain. `TargetAcquisitionSystem` runs in `Sensors` against authoritative transforms and the world spatial index, then `CombatExecutionSystem` performs final target/fire validation before Ammunition use. `CombatDamageResolutionSystem` classifies incoming direction against target facing and resolves logical penetration through data-driven weapon and armor profiles. Intelligence availability and line-of-fire are policy boundaries rather than presentation decisions. See `docs/ArmorAndTargetAcquisition.md`.

Battlefield intelligence owns faction-specific knowledge before target acquisition. `BattlefieldIntelligenceSystem` runs in `Sensors` after movement/spatial synchronization, updates persistent exploration, current visual coverage, and Detected/Identified contacts, then the existing targeting system consumes `IntelligenceTargetAvailabilityPolicy`. Presentation receives a faction-bounded intelligence snapshot and filters hidden enemy render instances; it never determines visibility. See `docs/BattlefieldIntelligence.md`.

Simulation code is written in a deterministic-friendly style:

- explicit tick ordering
- stable command ordering
- stable iteration where required
- seeded simulation-owned randomness
- no wall-clock simulation decisions
- no rendering-dependent game state
- controlled parallel reductions across job execution

Perfect cross-machine bit-level determinism is not a first-prototype requirement.

## Shared Vertical-Slice Runtime Composition

`VerticalSliceScenario` is the authoritative construction path for the canonical vertical-slice gameplay runtime. The Windows client, headless vertical-slice execution, and scenario-level tests delegate world, entity, inventory, logistics, intelligence, navigation, combat, supply, production, objective, and opponent-system construction to this game-layer composition instead of recreating parallel stacks.

`VerticalSliceRuntimeSettings` makes host choices explicit:

- scenario profile and deterministic seed;
- participant/start assignments and computer-control flags;
- diagnostics and spatial-query timing;
- initial entity capacity;
- optional job-scheduler provision and ownership.

Participant control is independent from balance profile. Human participants keep their normal simulation entities but do not receive a strategic opponent controller; computer-controlled participants receive the configured opponent policy. Equivalent gameplay-profile configurations therefore share the same gameplay systems and settings even when their participant-control assignments differ.

The `Gameplay` profile retains product-facing navigation and distribution behavior. The `Validation` profile retains its accelerated starting stock, opponent cadence/readiness settings, coarser navigation, and distribution retry/fairness policy. Validation tuning is never promoted into normal gameplay implicitly.

Navigation is constructed once inside the shared runtime. Starting static gameplay entities are incorporated into the initial navigation obstacle set, then the same `HierarchicalNavigationSystem` instance is supplied to `CargoTransportSystem` and `StrategicInfrastructureSystem`. Cargo approach projection and later infrastructure topology invalidation consequently observe the same navigation world.

Scheduler ownership is explicit. A host-provided `JobScheduler` remains host-owned and is not disposed by `VerticalSliceScenario`; a runtime-owned scheduler is disposed with the scenario and also cleaned up if scenario creation fails. Headless execution may deliberately run without a scheduler. Disposing a scenario never owns or tears down an externally supplied scheduler.

A restart constructs a new `VerticalSliceScenario`. Authoritative ECS state, inventories, routes, controller state, navigation requests, orders, and match state are therefore recreated rather than retained from the prior match.

## Headless Runtime

`ForgeLine.Headless` delegates vertical-slice gameplay construction to `VerticalSliceScenario` and adds only headless execution, cancellation, diagnostics/reporting, and command-line configuration. It has no graphics, audio, UI, presentation, client, or Windows-windowing dependency.

Headless execution supports a configurable tick count, deterministic seed, and logical tick-rate override. It intentionally runs faster than real time when work permits; wall-clock timing is used only for host diagnostics and never to mutate simulation state.

CI includes short headless smoke executions after the Release build. The Windows runner also performs a bounded native client smoke launch that creates and closes the primary Win32 window.

## Rendering and Interaction Boundary

The first graphics backend is Direct3D 12. `ForgeLine.Graphics` owns the D3D12/DXGI objects and exposes a narrow engine-facing device contract. The Windows client passes only the platform-native window target across the platform/graphics boundary.

Graphics frame ownership is independent of simulation. Swap-chain resize, command allocators, command lists, fences, render targets, and shader compilation remain graphics concerns.

Rendering and simulation operate independently:

```text
Simulation-owned completed tick
    ↓
PresentationExtractor
    ↓
PresentationSnapshot(session, tick)
    ↓
RenderWorld / HUD / Selection / Debug

Player input
    ↓
PlayerCommandGateway(correlation, source, target tick, sequence)
    ↓
Simulation command queue
    ↓
Bounded copied command-result buffer
    ↓
HUD / interaction feedback
```

The renderer consumes extracted presentation/world data and does not determine simulation outcomes. `PresentationExtractor` observes the completed post-tick boundary and publishes one coherent envelope containing copied render instances, faction-filtered intelligence, local HUD/selection inspection, placement preview results, construction state, copied diagnostics, and optional copied debug state. `RenderWorld` retains previous/current snapshots for visual transform interpolation only within one `SimulationSessionId`.

Selection picking operates against extracted instances. It filters hidden/off-screen, foreign-owned, and disallowed-category entities before returning the same full-generation `EntityId` used by simulation. Player commands cross back through `PlayerCommandGateway`, which records correlation/source/tick/sequence metadata and publishes resolved outcomes as copied bounded results instead of exposing mutable command objects to rendering.

Placement preview is advisory and asynchronous. Presentation publishes the latest request facts; completed-tick extraction evaluates those facts through the existing game-side placement service and publishes a request-ID/tick-tagged result. The actual `BuildCommand` still revalidates authoritative placement, source ownership, and resources during command processing.

See `docs/PresentationExtractionAndDebugging.md` for extraction timing, snapshot ownership, buffering, interpolation, debug tooling, metrics, and render stress baselines. See `docs/SelectionAndCommandInteraction.md` for interaction ownership, picking/filtering, command flow, and stale-ID handling. See `docs/Graphics.md` for the implemented graphics lifecycle and ownership rules. See `docs/CameraAndInput.md` for the raw-input boundary, RTS action mapping, camera coordinate convention, controls, and screen/world APIs. See `docs/WorldAndTerrain.md` for the canonical spatial model, heightfield semantics, terrain query boundary, mesh generation, culling, and render ownership. See `docs/SpatialIndexAndWorldQueries.md` for spatial cell mapping, derived-index lifecycle, query semantics, filtering, ordering, diagnostics, and benchmark coverage.

### Presentation Lifetime Decision

**Context.** Player-facing rendering previously combined an immutable render-instance snapshot with live ECS, inventory, placement-service, command-object, match-state, and debug-system reads. The client is serial today, so this was not a reproduced concurrent race, but moving simulation execution independently would make those mixed lifetimes unsafe.

**Decision.** Dynamic player-facing state is copied at the simulation-owned completed-tick boundary into a `PresentationSnapshot` identified by `SimulationSessionId` and tick. Stateful visual/HUD data uses the latest-value snapshot handoff. Essential command outcomes use a separate bounded consumptive result buffer so snapshot replacement cannot lose them. Presentation-to-simulation placement intent uses copied request facts and explicit request freshness; authoritative build validation remains simulation-owned.

**Alternatives considered.** Keeping direct reads was rejected because it preserves hidden mutable lifetime coupling. Locking the entire ECS around rendering was rejected because it couples render cadence to simulation and would undermine independent execution. Copying the entire world every render frame was rejected as unnecessary and unbounded. A generic event bus was rejected because the current needs are explicit state snapshots plus a bounded result channel.

**Trade-offs.** Player-facing state can be one completed tick behind input/render time, and toggling heavy debug capture can require a tick before every system reflects the new request. Snapshot replacement may skip intermediate state by design. Essential command results cannot be skipped; under sustained consumer backpressure the command boundary rejects new submissions explicitly instead of allocating without bound.

**Ownership and migration.** Immutable catalogs, terrain queries, camera/input state, renderer diagnostics, and host tick control remain safely outside the dynamic snapshot. Runtime ECS/inventory/system reads required to build player-facing models occur only inside completed-tick extraction. Client-only smoke/render-stress setup may mutate simulation as explicit host test/setup behavior and is not a presentation read path.

**Re-evaluate when.** Revisit the boundary when independent simulation execution is introduced, when networking/replay requires a different command transport, when snapshot copy cost is measured as material, or when a new player control needs data not represented by the bounded contracts. New controls extend these contracts rather than restoring live dynamic reads.

## Performance Direction

Performance-sensitive decisions are benchmark-driven. The architecture targets a path toward:

- stable 20 Hz simulation
- 1,000+ active combat/logistics entities without architectural redesign
- 10,000+ lightweight simulation entities as an early stress target
- 60+ FPS rendering on target hardware

The simulation benchmark host includes empty and light fixed-tick workloads, representative sequential-versus-parallel scheduler range workloads, and spatial-query/update workloads over 10,000 indexed entries. Benchmark timing remains observational rather than a CI timing gate.

These are engineering targets, not product promises.


## Hierarchical Navigation

Ground navigation follows a strict hierarchy:

```text
Sector graph
    ↓
High-level sector route
    ↓
Local traversability grid
    ↓
Corridor-bounded local refinement
    ↓
Ground movement waypoint
```

The high-resolution grid is never searched across the complete world as the primary long-range strategy. Sector connectivity is derived from traversable boundary runs, high-level A* selects the regional route, and local A* is constrained to that sector corridor with one-sector expansion only as a bounded fallback.

Navigation data is immutable for a specific `NavigationVersion`. Replacing the derived navigation world is the invalidation boundary: high-level cache entries are cleared and stale asynchronous results are rejected before they can enter simulation state. Path jobs read navigation snapshots only; they never mutate ECS transforms or live world state.

See `docs/HierarchicalNavigation.md` for movement classes, request/result ownership, failures, diagnostics, debug rendering, and benchmark coverage.

Multi-unit movement groups sit above this hierarchy. A group owns one strategic route/corridor and projects line, column, wedge, or compact formation slots around the active route direction. Stable per-member slot assignment, conservative speed harmonization, blocked-slot projection, lateral compression, and longitudinal cohort fallback remain game simulation concerns; local avoidance and final transform mutation remain in `GroundMovementSystem`. See `docs/FormationMovementAndGroupOrders.md` for the complete lifecycle and scale behavior.


## Artillery Authority Boundary

Indirect fire extends the combat/intelligence boundary without reintroducing hidden entity access. `FireMissionCommand` creates a request; `ArtilleryFireMissionSystem` resolves that request after the Sensors phase to either a currently visible coordinate or a stored intelligence contact position. The resulting `FireMissionState` contains a fixed coordinate, not a target entity. Ballistic shell travel, terrain impact, area damage, Ammunition consumption, and Health/destruction remain authoritative fixed-tick simulation. Battlefield Supply may replenish the same Ammunition inventory after Combat, allowing a `NoAmmo` mission to resume on a later tick. See `docs/ArtilleryAndIndirectFire.md`.


## Tactical Combat and Readiness Boundary

Phase-5 tactical behavior preserves the command/simulation split. Combat commands create `CombatOrderState` intent; `TacticalOrderPreparationSystem` maps that intent into targeting and movement policy; existing navigation, formation, and ground movement execute locomotion; battlefield intelligence and target acquisition run before `TacticalCombatSystem` resolves engagement transitions. Direct target transforms are read only after current faction identification is confirmed.

`TacticalMovementConstraint` pauses combat movement independently from `SupplyMovementConstraint`, keeping tactical stop/engage state separate from physical Fuel limitations.

`AutomaticResupplyDecisionSystem` may issue a real `ResupplyOrder` through the shared `BattlefieldResupplyPlanner`, but only `BattlefieldSupplySystem` transfers Fuel or Ammunition.

`CombatReadinessSystem` runs in `SnapshotEvents` and derives unit/group summaries from authoritative Health, inventory quantities, mobility, weapon state, supply state, and surviving members. Readiness is observation, not a substitute source of gameplay truth.

See `docs/CombatOrdersAndReadiness.md`. Directorate faction data, stable unit definitions, cross-catalog content validation, deterministic unit-production queues, and generic unit entity composition live here because they coordinate existing economy, logistics, movement, combat, intelligence, and supply domains without moving ownership out of those domains.


## Prototype Battlefield and Strategic Infrastructure Boundary

The canonical `Central Divide` battlefield is game content, not a presentation script. `PrototypeBattlefieldDefinition` owns stable map metadata, starts, finite resource locations, build/expansion sites, road topology, crossings, and Command Core objective positions. `PrototypeBattlefieldTerrainFactory` produces the deterministic chunked `TerrainWorld`, and `PrototypeBattlefieldRuntime` materializes finite deposits and the road corridor through the existing economy and logistics types.

Strategic crossings use the generic `StrategicInfrastructure` / `StrategicInfrastructureState` lifecycle. `StrategicInfrastructureSystem` is simulation-authoritative: disabling a crossing disables its real logistics edge and publishes a new `NavigationWorld` with that crossing blocked. The new navigation version invalidates cached/stale paths through the existing navigation boundary. Restoration remains unavailable while its fixed-tick progress advances and only re-enables logistics and navigation on completion.

`MatchObjectiveSystem` owns match completion state. Command Core objective registration adds the normal combat-target components required by the existing targeting, damage, and entity-lifecycle systems rather than introducing objective-specific damage. Presentation may visualize map landmarks and infrastructure state, but it never decides reachability, restoration, or victory.

See `docs/PrototypeBattlefield.md`.


## Skirmish Opponent Authority Boundary

The vertical-slice skirmish opponent is a strategic orchestration layer in ForgeLine.Game. It observes faction-owned state, public static battlefield content, and the faction's Battlefield Intelligence snapshot, then expresses intent through the same simulation commands and automation policies available to player-controlled forces.

It does not own economy quantities, construction completion, logistics transfers, transforms, visibility, combat damage, or match outcomes. Those remain authoritative in their existing domain systems. Building and unit plans consume real inventories; stock targets flow through automated distribution and physical Cargo Trucks; movement flows through combat/movement groups, hierarchical navigation, formations, and ground locomotion; Fuel and Ammunition remain inventory-backed; and damage/objective completion continue through normal combat and MatchObjectiveSystem execution.

Direct entity targeting has an explicit intelligence boundary. A strategic decision may create Attack intent only after the faction can resolve a currently Identified contact to that entity. Detected contacts remain coordinate-level knowledge for legitimate movement or artillery workflows. When no current hostile intelligence is available, offensive movement may use only allowed static map knowledge such as public expansion/FOB locations and the battlefield center; exact hidden enemy objective coordinates are not a fallback target.

SkirmishOpponentConfiguration changes decision cadence, thresholds, group-size limits, and engagement policy only. It must never modify income, costs, production speed, sensing, unit statistics, weapon performance, Fuel, Ammunition, or logistics capacity.

The existing combat-command layer remains the group authority. Attack and AttackMove create CombatGroupIntent, while movement-oriented commands also create normal formation/movement groups. CombatReadinessSystem, TacticalTestOpponentSystem, navigation, movement, supply, and combat then operate on those normal entities and components. The skirmish layer must not introduce a parallel tactical simulation.

Headless integration uses SkirmishScenarioHarness to compose the same authoritative systems for two symmetric opponents. Deterministic scenarios verify knowledge boundaries and bounded Build–Supply–Conquer progression in regular CI. Terminal full-match soak execution uses the same composition outside the regular CI duration budget until long-horizon logistics endurance is reliable enough for a deterministic merge gate.

See docs/SkirmishOpponent.md.


## Interactive Execution Ownership

The Windows interactive host now has explicit platform, simulation, and render owners instead of a serial all-in-one loop.

- The platform owner creates the Windows platform/window, pumps native events, drains input, and reads mutable window state.
- The simulation owner is the only long-lived owner allowed to advance `SimulationCoordinator`, invoke `PlayerCommandGateway`, or mutate authoritative game state after startup handoff.
- The render owner creates, uses, resizes, idles, and disposes D3D12 objects and presentation renderer resources.
- The existing persistent `JobScheduler` remains below simulation as bounded worker execution and does not own window or graphics APIs.
- Immutable `PresentationSnapshot` instances flow simulation → latest-value buffer → independent platform/render consumers. Essential command results use the separate bounded result queue.
- Platform-authored gameplay requests cross a bounded host queue and are converted to normal future-tick simulation commands only on the simulation owner.

Minimize/pause, terminal freeze/acknowledgement, restart, shutdown, and owner failure are explicit lifecycle transitions. See [Client Execution Ownership](adr/ClientExecutionOwnership.md).
