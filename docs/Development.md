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
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 64 --seed 12345 --tick-rate 20 --entities 1000 --diagnostics-output artifacts/headless-smoke.json
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 16 --seed 67890 --tick-rate 20 --entities 10000 --diagnostics-output artifacts/headless-stress-10000.json
dotnet test ForgeLine.sln --configuration Release --no-build
```

The GitHub Actions CI workflow executes the same essential sequence on pull requests targeting `master` and on pushes to `master`. Windows client smoke validation is guarded to Windows runners, while headless diagnostic JSON files are uploaded as the `engine-diagnostics` workflow artifact.

## Test Projects

The repository contains focused test projects for Core, ECS, Jobs, World, Simulation, Navigation, Logistics, Game, Platform.Windows, Graphics, Input, and Presentation.

Functional tests belong with the systems they validate and should cover controlled failure behavior as well as successful behavior.

Job tests verify range coverage, dependency ordering, fences, exception propagation, one-worker execution, cancellation-aware shutdown, bounded stress execution, and instrumentation. Simulation tests verify fixed tick counts, explicit phase order, command scheduling and stable ordering, deterministic seeded behavior, job-boundary integration, fast headless-style execution, allocation behavior, diagnostics, reusable test scenarios, and bounded entity stress. Simulation tests must remain runnable without starting the interactive client. Navigation tests cover movement-class traversability, obstacle blocking, sector decomposition, portals, high-level routing, choke points, bounded local refinement, cache reuse, explicit route failure, and large-map hierarchy scaling. Game tests additionally cover job-scheduled navigation handoff, stale-result rejection, fixed-tick ground locomotion, arrival, acceleration and turn limits, terrain/slope handling, spatial chunk crossing, local separation, static obstacle steering, stuck detection, and a 1,000-unit movement stress scenario.

## Diagnostics

Engine diagnostics are opt-in and must remain independent of rendering, UI, audio, and platform-specific code.

The headless host can emit structured JSON diagnostics:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --entities 1000 --diagnostics-output artifacts/headless.json
```

See [Diagnostics and Performance](DiagnosticsAndPerformance.md) for available metrics, invariants, report fields, stress scenarios, and regression-investigation guidance.

## Benchmark Projects

The repository contains BenchmarkDotNet hosts for ECS, Navigation, Simulation, and Rendering. Benchmark code should be introduced together with meaningful measured workloads. The navigation benchmark host covers long-distance hierarchical path searches on a multi-chunk map; the simulation benchmark host covers fixed-tick, scheduler, spatial-query, and 1,000-unit ground-movement workloads; the rendering benchmark host covers terrain mesh generation, visible-chunk submission preparation, 1,000 near-field instances, and 5,000 total instances with far-field culling. Performance-sensitive architectural changes require measurement rather than assumption.

Examples:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release
```

Correctness tests remain separate from benchmark timing. Benchmark timing thresholds are not CI gates unless explicitly introduced later.

## Windows Client Host

The interactive client composes the native Windows host, Direct3D 12 graphics backend, RTS input/camera stack, fixed-tick simulation, immutable presentation extraction, generic interpolated instances, development debug visualization, and the representative chunked terrain world.

Launch it with:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

For bounded validation that exits automatically:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test
```

The client is Windows x64 specific. Native Win32 calls remain confined to `ForgeLine.Platform.Windows`; headless and simulation projects must not reference the client or Windows platform project.

See [Windows Client](WindowsClient.md) for lifecycle and DPI details, [RTS Camera and Input](CameraAndInput.md) for camera controls and coordinate conventions, [World and Terrain](WorldAndTerrain.md) for terrain queries, mesh generation, culling, diagnostics, and benchmark coverage, and [Ground Movement and Local Steering](GroundMovementAndSteering.md) for authoritative locomotion semantics, plus [Hierarchical Navigation](HierarchicalNavigation.md) for long-range ground routing.

## Headless Runtime

The development host supports:

```text
--ticks <count>
--seed <value>
--tick-rate <hz>
--entities <count>
--diagnostics-output <path>
--help
```

The logical tick rate describes simulation time. Headless execution does not sleep to match real time and may run substantially faster than the configured logical rate.

## Commit Discipline

Prefer several small, logically complete commits over broad aggregate commits.

Keep unrelated formatting, refactoring, functional changes, tests, documentation, and CI adjustments separate when practical. Avoid knowingly broken intermediate commits.

## Documentation

Documentation changes with implementation. When project responsibilities, dependency boundaries, build commands, validation requirements, supported technical baselines, diagnostic conventions, or performance workflows change, update the relevant documents in the same work.
