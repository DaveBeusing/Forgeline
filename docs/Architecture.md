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
- `ForgeLine.Assets`: runtime asset infrastructure.

### Simulation Foundation

- `ForgeLine.Ecs`: custom data-oriented entity/component storage and queries. The implemented low-level contracts and invariants are documented in `docs/Ecs.md`.
- `ForgeLine.Jobs`: persistent-worker job scheduling, dependency handles, range execution, fences, failure propagation, and timing instrumentation. The implemented contracts and safe usage rules are documented in `docs/JobSystem.md`.
- `ForgeLine.World`: canonical world/region/chunk coordinates, chunk-based terrain ownership, headless heightfield sampling, terrain bounds, deterministic development terrain, and CPU terrain mesh generation.
- `ForgeLine.Navigation`: hierarchical RTS navigation boundaries.
- `ForgeLine.Simulation`: command-driven fixed-tick coordination, explicit phase ordering, simulation-owned randomness, and common simulation infrastructure.

The implemented simulation lifecycle and command boundary are documented in `docs/SimulationRuntime.md`.

### Simulation Domains

- `ForgeLine.Economy`: economy and production simulation.
- `ForgeLine.Logistics`: graph-based logistics and supply simulation.
- `ForgeLine.Combat`: combat simulation.
- `ForgeLine.Intelligence`: visibility, sensors, and intelligence simulation.
- `ForgeLine.AI`: strategic, operational, tactical, and unit-behavior orchestration.

These domain projects establish dependency boundaries only at this stage; their gameplay implementations are intentionally deferred.

### Game and Presentation

- `ForgeLine.Game`: FORGELINE rules and composition of simulation domains.
- `ForgeLine.Presentation`: conversion of game/read-model state into player-visible presentation state plus presentation-only RTS camera state, projection, and picking math.
- `ForgeLine.UI`: RTS-specific user-interface boundary.
- `ForgeLine.Client`: composition root for the interactive Windows application. It owns the platform host lifecycle and will later compose graphics, input, presentation, and game services without moving platform details into simulation.
- `ForgeLine.Headless`: non-visual composition root for simulation tests, AI matches, balancing, performance work, replay validation, and future server experiments.

### Tools

- `ForgeLine.Editor`: FORGELINE-specific editor host.
- `ForgeLine.AssetCompiler`: source-to-runtime asset compiler host.
- `ForgeLine.MapCompiler`: map compilation host.

Tool functionality is not part of the repository foundation.

## Simulation Baseline

The simulation runtime is fixed-step with a default engineering target of 20 simulation ticks per second, equivalent to 50 ms of logical simulation time per tick.

Each tick traverses an explicit canonical phase sequence. Commands scheduled for a tick are executed at the Input Commands boundary before later phases run. Systems register for a specific phase and execute in stable registration order within that phase.

Simulation code is written in a deterministic-friendly style:

- explicit tick ordering
- stable command ordering
- stable iteration where required
- seeded simulation-owned randomness
- no wall-clock simulation decisions
- no rendering-dependent game state
- controlled parallel reductions across job execution

Perfect cross-machine bit-level determinism is not a first-prototype requirement.

## Headless Runtime

`ForgeLine.Headless` composes the simulation runtime directly and has no graphics, audio, UI, presentation, client, or Windows-windowing dependency.

Headless execution supports a configurable tick count, deterministic seed, and logical tick-rate override. It intentionally runs faster than real time when work permits; wall-clock timing is used only for host diagnostics and never to mutate simulation state.

CI includes short headless smoke executions after the Release build. The Windows runner also performs a bounded native client smoke launch that creates and closes the primary Win32 window.

## Rendering Boundary

The first graphics backend is Direct3D 12. `ForgeLine.Graphics` owns the D3D12/DXGI objects and exposes a narrow engine-facing device contract. The Windows client passes only the platform-native window target across the platform/graphics boundary.

Graphics frame ownership is independent of simulation. Swap-chain resize, command allocators, command lists, fences, render targets, and shader compilation remain graphics concerns.

Rendering and simulation operate independently:

```text
Simulation
    ↓
Presentation Snapshot
    ↓
Render World
    ↓
Renderer
```

The renderer consumes extracted presentation/world data and does not determine simulation outcomes. The current client derives persistent terrain render resources from `ForgeLine.World`, performs presentation-owned chunk frustum culling, and submits indexed depth-tested terrain through `ForgeLine.Graphics`. Units, fog of war, and simulation presentation extraction remain later rendering stages.

See `docs/Graphics.md` for the implemented graphics lifecycle and ownership rules. See `docs/CameraAndInput.md` for the raw-input boundary, RTS action mapping, camera coordinate convention, controls, and screen/world APIs. See `docs/WorldAndTerrain.md` for the canonical spatial model, heightfield semantics, terrain query boundary, mesh generation, culling, and render ownership.

## Performance Direction

Performance-sensitive decisions are benchmark-driven. The architecture targets a path toward:

- stable 20 Hz simulation
- 1,000+ active combat/logistics entities without architectural redesign
- 10,000+ lightweight simulation entities as an early stress target
- 60+ FPS rendering on target hardware

The simulation benchmark host includes empty and light fixed-tick workloads plus representative sequential-versus-parallel scheduler range workloads. Benchmark timing remains observational rather than a CI timing gate.

These are engineering targets, not product promises.
