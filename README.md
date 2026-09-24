# FORGELINE

**Build. Supply. Conquer.**

FORGELINE is a large-scale real-time strategy game where industrial production, logistics, intelligence, and direct battlefield command form one interconnected war machine.

The project is built on **ForgeLine Engine**, a custom C#/.NET RTS engine designed specifically for large-scale, deterministic-friendly simulation. It is not intended to become a general-purpose game engine.

## Technical Baseline

- C# 14
- .NET 10 LTS
- Windows x64 as the initial client platform
- Direct3D 12 as the planned initial graphics backend
- data-oriented, ECS-first architecture
- fixed-tick, headless-capable simulation
- custom job system
- chunk-based world model
- hierarchical RTS navigation
- graph-based logistics simulation
- strict simulation/presentation separation
- multiplayer-aware architecture with networking deferred

Implemented engine foundations currently include the repository architecture, stable entity/component storage, a command-driven fixed-tick simulation runtime, deterministic simulation-owned randomness, a persistent-worker job scheduler, opt-in engine diagnostics, repeatable headless test scenarios, performance baselines, a standalone headless host, and the first native Windows interactive client host.

Gameplay systems, Direct3D 12 rendering, navigation algorithms, logistics simulation, combat, editor functionality, asset conversion, and networking remain deferred to their owning implementation stages.

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

The current client creates a DPI-aware native Win32 window and runs the platform message loop without initializing graphics or simulation. Close the window normally to exercise orderly shutdown.

Run the bounded client smoke validation used by CI:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test
```

The smoke mode creates the same native window, pumps messages briefly, then requests a clean shutdown.

See [Windows Client](docs/WindowsClient.md) for the platform boundary, window lifecycle, supported modes, DPI behavior, and validation procedure.

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

BenchmarkDotNet hosts cover the implemented ECS, simulation, command-processing, and job-scheduler foundations.

Examples:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
```

Benchmark timing is measurement evidence rather than a hardware-sensitive CI pass/fail gate.

## Architecture

See [Architecture](docs/Architecture.md) for project responsibilities and dependency rules.

See [Simulation Runtime](docs/SimulationRuntime.md) for fixed-tick semantics, phase ordering, commands, deterministic randomness, and headless execution.

See [Development](docs/Development.md) for the canonical development and validation workflow.
