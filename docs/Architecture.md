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
- `ForgeLine.Platform.Windows`: Windows platform integration behind platform boundaries.
- `ForgeLine.Graphics`: graphics device, rendering resources, synchronization, and future Direct3D 12 implementation.
- `ForgeLine.Audio`: audio infrastructure.
- `ForgeLine.Input`: input infrastructure.
- `ForgeLine.Assets`: runtime asset infrastructure.

### Simulation Foundation

- `ForgeLine.Ecs`: custom data-oriented entity/component storage and queries. The implemented low-level contracts and invariants are documented in `docs/Ecs.md`.
- `ForgeLine.Jobs`: persistent-worker job scheduling and synchronization.
- `ForgeLine.World`: chunk-based world ownership and spatial foundations.
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
- `ForgeLine.Presentation`: conversion of game/read-model state into player-visible presentation state.
- `ForgeLine.UI`: RTS-specific user-interface boundary.
- `ForgeLine.Client`: composition root for the interactive application.
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
- controlled parallel reductions when job execution is introduced

Perfect cross-machine bit-level determinism is not a first-prototype requirement.

## Headless Runtime

`ForgeLine.Headless` composes the simulation runtime directly and has no graphics, audio, UI, presentation, client, or Windows-windowing dependency.

Headless execution supports a configurable tick count, deterministic seed, and logical tick-rate override. It intentionally runs faster than real time when work permits; wall-clock timing is used only for host diagnostics and never to mutate simulation state.

CI includes a short headless smoke execution after the Release build.

## Rendering Boundary

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

The renderer consumes extracted presentation state and does not determine simulation outcomes.

## Performance Direction

Performance-sensitive decisions are benchmark-driven. The architecture targets a path toward:

- stable 20 Hz simulation
- 1,000+ active combat/logistics entities without architectural redesign
- 10,000+ lightweight simulation entities as an early stress target
- 60+ FPS rendering on target hardware

The simulation benchmark host includes empty and light fixed-tick workloads to track baseline runtime overhead.

These are engineering targets, not product promises.
