# Simulation Runtime

## Purpose

ForgeLine Simulation provides the deterministic-friendly execution boundary shared by interactive and headless game composition.

The runtime owns logical tick progression, explicit simulation phases, command ingestion, simulation-owned random state, ECS access, and lightweight loop metrics. It has no dependency on rendering, audio, UI, windowing, or wall-clock pacing.

## Fixed-Tick Semantics

The default rate is 20 simulation ticks per second.

One logical tick therefore represents 50 ms of simulation time. The rate is configurable for development and tests, but changing the rate changes logical simulation semantics and is not a real-time pacing control.

`FixedTickClock` is purely logical:

- it starts at tick zero before simulation work has executed;
- each call to advance produces the next monotonically increasing positive tick identifier;
- simulation state changes are driven by tick execution rather than elapsed wall-clock time;
- headless execution may advance ticks as quickly as the CPU permits.

The interactive client may later pace tick submission against a platform clock, but wall-clock time must not become a simulation decision input.

## Canonical Phase Order

Every executed tick traverses the following phases in order:

```text
Input Commands
Order Processing
AI Decisions
Navigation Requests
Movement
Sensors
Combat
Damage Resolution
Supply
Logistics
Production
Economy
Entity Lifecycle
Snapshot / Events
```

Systems implement `ISimulationSystem` and register for exactly one phase.

Phase ordering is explicit and centralized. Registration order is preserved for multiple systems in the same phase. System registration is sealed once ticking starts so update order cannot change invisibly during a running simulation.

Not every phase contains domain logic yet. The phase model exists now so later gameplay systems can enter the correct execution boundary without creating hidden update-order dependencies.

## Command Boundary

External state changes enter through `ISimulationCommand` values submitted to `SimulationCoordinator`.

A submitted command receives a `SimulationCommandEnvelope` containing:

- the target simulation tick;
- a monotonic submission sequence;
- an optional source identity;
- the command payload/type.

Commands must target a tick that has not executed yet. Commands targeting the same tick execute in submission-sequence order.

The queue executes the current tick's commands during the Input Commands phase before later phases run.

UI, input, network, replay, and automation layers must not bypass this boundary by mutating simulation-owned ECS state directly.

## Simulation State and ECS

`SimulationContext` exposes the simulation-owned `EntityRegistry`, current tick, current phase, and deterministic random source to commands and systems.

The ECS remains responsible for entity/component lifetime and storage. The simulation runtime coordinates when systems and commands are allowed to operate on that state.

Later gameplay systems should prefer stable ECS query ordering wherever gameplay outcomes can depend on iteration order.

## Random Number Ownership

`SimulationRandom` is owned by `SimulationCoordinator`.

The initial primitive uses a stable SplitMix64 sequence with explicit seed and observable state. The same seed and the same sequence of random requests therefore produce the same values within this runtime implementation.

Simulation code must not use wall-clock-derived seeds or presentation-owned random state for gameplay outcomes.

Future savegame and replay formats should persist the relevant simulation RNG state or reconstruct equivalent streams explicitly.

## Metrics

`SimulationLoopMetrics` currently exposes:

- completed ticks;
- processed commands;
- system invocations;
- pending commands;
- peak pending commands.

These counters are simulation-safe and do not use wall-clock time.

The headless executable may separately measure elapsed host time for diagnostics and benchmark-style throughput reporting. Those measurements do not feed back into simulation decisions.

## Headless Execution

`ForgeLine.Headless` references `ForgeLine.Simulation` directly and starts no graphics, audio, UI, presentation, client, or windowing subsystem.

Example:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 100000 --seed 12345 --tick-rate 20
```

Options:

- `--ticks <count>`: exact maximum number of ticks to run;
- `--seed <value>`: deterministic simulation seed;
- `--tick-rate <hz>`: logical tick rate;
- `--help`: usage information.

Ctrl+C requests graceful cancellation. A normal finite run exits after exactly the requested number of ticks.

## Deterministic-Friendly Contract

The current runtime guarantees deterministic-friendly structure, not universal cross-machine bit-identical simulation.

Required practices are:

- use explicit tick and phase ordering;
- submit external actions through commands;
- preserve stable ordering where outcomes depend on order;
- use simulation-owned seeded randomness;
- avoid wall-clock-dependent game-state mutation;
- avoid rendering-dependent game-state mutation;
- keep headless and interactive simulation execution on the same runtime contracts.

Parallel job execution, rollback, replay persistence, savegame persistence, networking, and domain gameplay commands remain outside this runtime foundation and must preserve these contracts when introduced.
