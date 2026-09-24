# Development

## Source of Truth

The current GitHub repository is the technical source of truth.

The integration branch is `master`. Feature and fix work should begin from the current `master`, reuse relevant existing work where appropriate, use short-lived technical branches, and integrate through pull requests.

## SDK

The repository is pinned through `global.json`:

```text
.NET SDK 10.0.401
C# 14
```

Common compiler, analyzer, nullability, deterministic-build, and formatting settings are centralized in the repository root.

NuGet versions are managed centrally through `Directory.Packages.props`.

## Canonical Validation Path

From the repository root:

```powershell
dotnet restore ForgeLine.sln
pwsh ./build/Validate-ProjectReferences.ps1
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 64 --seed 12345 --tick-rate 20
dotnet test ForgeLine.sln --configuration Release --no-build
```

The GitHub Actions CI workflow executes the same essential sequence on pull requests targeting `master` and on pushes to `master`.

## Test Projects

The repository contains test projects for:

- Core
- ECS
- Jobs
- Simulation
- Navigation
- Logistics
- Game

Functional tests belong with the systems they validate and should cover controlled failure behavior as well as successful behavior.

Job tests verify range coverage, dependency ordering, fences, exception propagation, one-worker execution, cancellation-aware shutdown, bounded stress execution, and instrumentation. Simulation tests verify fixed tick counts, explicit phase order, command scheduling and stable ordering, deterministic seeded behavior, job-boundary integration, fast headless-style execution, and allocation behavior. Simulation tests must remain runnable without starting the interactive client.

## Benchmark Projects

The repository contains BenchmarkDotNet hosts for:

- ECS
- Navigation
- Simulation
- Rendering

Benchmark code should be introduced together with meaningful measured workloads. The simulation benchmark host includes scheduler range benchmarks that compare representative sequential and parallel execution. Performance-sensitive architectural changes require measurement rather than assumption.

Examples:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
```

Correctness tests remain separate from benchmark timing. Benchmark timing thresholds are not CI gates unless explicitly introduced later.

## Headless Runtime

The development host supports:

```text
--ticks <count>
--seed <value>
--tick-rate <hz>
--help
```

The logical tick rate describes simulation time. Headless execution does not sleep to match real time and may run substantially faster than the configured logical rate.

## Commit Discipline

Prefer several small, logically complete commits over broad aggregate commits.

Keep unrelated formatting, refactoring, functional changes, tests, documentation, and CI adjustments separate when practical. Avoid knowingly broken intermediate commits.

## Documentation

Documentation changes with implementation. When project responsibilities, dependency boundaries, build commands, validation requirements, or supported technical baselines change, update the relevant documents in the same work.
