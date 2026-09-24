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
dotnet test ForgeLine.sln --configuration Release --no-build
```

The GitHub Actions CI workflow executes the same essential sequence on pull requests targeting `master` and on pushes to `master`.

## Test Projects

The foundation contains test projects for:

- Core
- ECS
- Simulation
- Navigation
- Logistics
- Game

The initial smoke tests intentionally verify test discovery/execution only. Functional tests belong with the systems they validate and should cover controlled failure behavior as well as successful behavior.

Simulation tests must remain runnable without starting the interactive client.

## Benchmark Projects

The foundation contains BenchmarkDotNet hosts for:

- ECS
- Navigation
- Simulation
- Rendering

Benchmark code should be introduced together with meaningful measured workloads. Performance-sensitive architectural changes require measurement rather than assumption.

Example benchmark hosts:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
```

## Commit Discipline

Prefer several small, logically complete commits over broad aggregate commits.

Keep unrelated formatting, refactoring, functional changes, tests, documentation, and CI adjustments separate when practical. Avoid knowingly broken intermediate commits.

## Documentation

Documentation changes with implementation. When project responsibilities, dependency boundaries, build commands, validation requirements, or supported technical baselines change, update the relevant documents in the same work.
