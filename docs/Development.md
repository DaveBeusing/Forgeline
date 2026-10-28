# Development

The [rendering performance work packages](rendering/performance-work-packages/00_Implementation_Order.md) define parallel implementation ownership, dependencies and qualification for instancing, static geometry, interpolation and GPU benchmarks.

The [UI review handbook](UiVisualReview.md) describes bounded native capture and
resume. [Graphical interface qualification](GraphicalInterfaceQualification.md)
records executed coverage, package identity and required gates still outstanding.

## Source of Truth

The current GitHub repository is the technical source of truth for implemented code, configuration, tests, and current build behavior.

The integration branch is `master`. Feature, fix, and technical work should begin from the current `master`, reuse relevant existing work where appropriate, use short-lived branches, and integrate through pull requests.

Project-specific governance, source precedence, protection requirements, and unresolved Product Owner decisions are recorded in [Project Context and Repository Governance](ProjectContext.md). Placeholder or template approval metadata is not treated as project approval evidence.

## SDK

The repository is pinned through `global.json`:

```text
.NET SDK 10.0.402
C# 14
```

Common compiler, analyzer, nullability, deterministic-build, and formatting settings are centralized in the repository root.

NuGet versions are managed centrally through `Directory.Packages.props`.

## Semantic Versioning

Each PR increments PATCH exactly once relative to current master.
Run `pwsh ./build/Increment-PrVersion.ps1 -BaseRef origin/master` and commit
Directory.Build.props with the PR. Repeating preparation against the same base
does not increment again. CI validates the PR as a whole; commits and reruns
do not consume versions. Concurrent PRs must rebase and refresh their version.
See [PR Versioning](ReleaseVersioning.md) for metadata and artifact identity.
CI does not create tags or publish releases.

Windows icon resources and package assertions are documented in [Windows application branding](WindowsApplicationBranding.md).

## Canonical Validation Path

From the repository root:

```powershell
pwsh ./build/Validate-SemVer.ps1 -BaseRef <base-commit> -HeadRef HEAD
dotnet restore ForgeLine.sln
pwsh ./build/Validate-ProjectReferences.ps1
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/map-qualification.json
pwsh ./build/Test-CompiledScenarioLoading.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode build -ForceRebuild -NoBuild
pwsh ./build/Test-RuntimeAssetPipeline.ps1

dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 64 --seed 12345 --tick-rate 20 --entities 1000 --diagnostics-output artifacts/headless-smoke.json
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 16 --seed 67890 --tick-rate 20 --entities 10000 --diagnostics-output artifacts/headless-stress-10000.json
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --require-terminal --diagnostics-output artifacts/match.json
```

The GitHub Actions CI workflow executes this sequence on pull requests targeting `master` and on pushes to `master`. Windows client smoke validation is guarded to Windows runners, while headless diagnostic JSON files are uploaded as the `engine-diagnostics` workflow artifact.

## Pull Requests and Required Check

After the Release build, `pwsh ./build/Run-MatchSoak.ps1 -NoBuild` runs the canonical preset soak without rebuilding. Omit `-NoBuild` to retain the normal build-and-run behavior. The legacy `Run-VerticalSliceSoak.ps1` forwards to the new script and preserves its historical default report paths. The canonical CLI is `--scenario central-divide`; `--scenario vertical-slice` remains an alias.

`master` is the integration branch. Integration is intended to occur through pull requests from short-lived technical branches.

The CI workflow emits the required check context `build-test` from GitHub Actions. That check represents the complete job, including the natural vertical-slice terminal validation; a successful build or unit-test subset is not equivalent to the full required check.

The early technology gate checks paid artillery/radar effects, research and fleet priorities, normal-stock opponent progression, serialized Save/Replay and matching Technology focus. It preserves all complete-suite, asset, native, package and terminal gates.

The early Operations rule-controls gate runs the existing rule and command-boundary tests, real selected-facility UI/production/save-replay cases and client submission/native-state contracts. It preserves all later complete-solution, asset, Windows/WARP, package and terminal-match gates. See [Operations rules](OperationsAutomation.md) for scope and player controls.

The early logistics contract step also runs `PlayerActionDockHudTests` so
construction, manual quantities, controller behavior and displayed availability
are qualified together. After the existing runtime-asset compilation, the
production catalog and render-usage step runs the source/manifest membership,
building/unit/world/UI/VFX authoring and material/proxy regression tests. These
focused checks provide independent diagnostics before later interaction gates;
they do not replace the complete solution test, WARP, package, recovery or
natural terminal-match checks.

Repository settings must require that check before merge once `master` protection is applied. Integration preserves small validated technical commits. Ordinary merge and rebase integration remain permitted; the existing no-squash policy remains in force. Documentation does not itself enforce branch protection, so the effective GitHub settings must be verified by readback. See [Project Context and Repository Governance](ProjectContext.md).

The compiled scenario loading and recovery CI gate verifies complete terrain parity, loaded map state, identity rejection, owned new/load/restart and disabled setup entries. Test-CompiledScenarioLoading exercises the actual CLI and missing-content failure. Publish-PreAlpha additionally qualifies the map and runs Validate-ScenarioPackage from each exported client folder. These checks preserve all existing full-solution, renderer, asset, package and natural terminal gates. See [compiled scenario contracts](StrategicMapAndOperationalGeography.md).

## Test Projects

The repository contains focused test projects for Core, ECS, Jobs, World, Simulation, Navigation, Logistics, Game, Platform.Windows, Graphics, Input, and Presentation.

Functional tests belong with the systems they validate and should cover controlled failure behavior as well as successful behavior. Directorate content validation and bounded vertical-slice scenarios run in `ForgeLine.Game.Tests` and remain fully headless. Central Divide definition validation, compiled-map roundtrip, explicit build-zone policy, complete strategic/resource reachability, disruption/rerouting/restoration, finite-resource loading, and Command Core objective tests also run in `ForgeLine.Game.Tests` without a presentation dependency. Skirmish-opponent validation reuses `MatchRuntime` for shortage recovery, intelligence-constrained direct targeting, same-seed strategic progression, integrated Build–Supply–Conquer coverage, and fresh-session cleanup. CI additionally executes one bounded terminal match through the real headless host. Repeated multi-match soak remains an on-demand workflow rather than a per-PR timing gate.

Job tests verify range coverage, dependency ordering, fences, exception propagation, one-worker execution, cancellation-aware shutdown, bounded stress execution, and instrumentation. Simulation tests verify fixed tick counts, explicit phase order, command scheduling and stable ordering, deterministic seeded behavior, job-boundary integration, fast headless-style execution, allocation behavior, diagnostics, reusable test scenarios, and bounded entity stress. Simulation tests must remain runnable without starting the interactive client. Navigation tests cover movement-class traversability, obstacle blocking, sector decomposition, portals, high-level routing, choke points, bounded local refinement, cache reuse, explicit route failure, and large-map hierarchy scaling. Logistics tests cover connected and disconnected graphs, disabled infrastructure, alternate routes, deterministic equal-cost ties, node removal, versioned cache invalidation, transport-mode filtering, and minimum-capacity filtering. Game tests additionally cover job-scheduled navigation handoff, stale-result rejection, fixed-tick ground locomotion, arrival, acceleration and turn limits, terrain/slope handling, spatial chunk crossing, local separation, static obstacle steering, stuck detection, one shared strategic route for 10/50/100-unit selections, stable formation slots, unit removal, replacement orders, choke-point fallback, concurrent groups, a 1,000-unit movement stress scenario, economic-building logistics registration, physical cargo transport, load/unload conservation, route invalidation/rerouting, destination-capacity recovery, vehicle-loss semantics, bounded multi-transport stress, throughput-window enforcement, congestion-aware alternate routing, persistent node/edge disruption and restoration, saturation backlog/recovery, route-churn reservation cleanup, hitscan cadence and reload timing, authoritative Ammunition depletion, range rejection, physical projectile travel/collision, exactly-once impact, stale source/target handling, zero-health lifecycle destruction, repeatable headless combat outcomes, Front/Side/Rear/Top armor classification, penetration mitigation, target-class filtering, deterministic target priority, reacquisition, fire-policy behavior, and target-availability/line-of-fire hooks, persistent exploration, visual-visibility loss, radar-only contacts, Detected/Identified transitions, faction isolation, hidden-target exclusion, last-known moving contacts, faction-safe presentation filtering, hidden artillery-coordinate rejection, radar-contact fire missions, indirect min/max range, projectile travel, exactly-once area damage, Ammunition exhaustion, Battlefield-Supply-driven mission recovery, Attack/pursuit-leash behavior, AttackMove engagement/resume, Hold/Stop semantics, group target spreading, real automatic resupply, Retreat, derived unit/group readiness, entity-loss strength degradation, and intelligence-constrained tactical test-opponent behavior.

## Diagnostics

Engine diagnostics are opt-in and must remain independent of rendering, UI, audio, and platform-specific code.

The headless host can emit structured JSON diagnostics:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --entities 1000 --diagnostics-output artifacts/headless.json
```

See [Diagnostics and Performance](DiagnosticsAndPerformance.md) for available metrics, invariants, report fields, stress scenarios, and regression-investigation guidance.

## Benchmark Projects

The repository contains BenchmarkDotNet hosts for ECS, Navigation, Simulation, and Rendering. Benchmark code should be introduced together with meaningful measured workloads. The navigation benchmark host covers both synthetic long-distance hierarchy scaling and complete Central Divide cross-map, expansion, and bridge-loss alternate-route searches; the simulation benchmark host covers fixed-tick, scheduler, spatial-query, 1,000-unit ground-movement workloads, a 10/50/100-unit comparison between independent strategic path searches and one shared formation route, cold logistics routing/reachability on 1,000-node and 10,000-node road graphs, 100/500-vehicle physical cargo transport batches, and 100/1,000-request capacity-aware routing plus repeated topology-change workloads, 100/1,000-weapon authoritative direct-fire ticks, 100/1,000-projectile movement ticks, and 100/1,000-candidate dense-field target acquisition and 100/1,000-unit mixed visual/radar sensor workloads and 10/100 simultaneous artillery fire missions and 100/1,000-unit tactical acquisition/coordination workloads; the rendering benchmark host covers terrain mesh generation, visible-chunk submission preparation, 1,000 near-field instances, and 5,000 total instances with far-field culling. Performance-sensitive architectural changes require measurement rather than assumption.

Examples:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release
```

Correctness tests remain separate from benchmark timing. Benchmark timing thresholds are not CI gates unless explicitly introduced later.

## Windows Client Host

For source-pinned manual build, native/WARP capture matrices, live overlay checks
and an external Windows package install, follow [Windows renderer qualification](WindowsRendererQualification.md).
Its default Plan stage executes no tests or captures and grants no hardware acceptance.

The frontend exposes four graphics presets and supported independent feature overrides. Schema 3 persists them per user and migrates earlier settings. Graphics apply at the next session boundary; display changes require a successfully presented 15-second trial before saving. Fixture requests resolve the same policy, including optional reduced world resolution and supplied texture mips. See [graphics settings and benchmark reproduction](GraphicsQualitySettings.md) for resource costs, effective fallback diagnostics and native qualification commands.

Compile runtime assets explicitly with `pwsh ./build/Build-RuntimeAssets.ps1 -Mode development` before launching a source checkout. Publishing without `-RuntimeAssetRoot` builds its own runtime cache; the CI path validates and packages its already built/tested root. See [Asset Pipeline](AssetPipeline.md#build-modes-and-deployment-integrity) for force-rebuild, validation and migration. The interactive client never starts source compilation. A native bootstrap window appears before settings and graphics initialization; changed startup events are checkpointed under the local user diagnostics directory. See [Startup timing](StartupDiagnostics.md) and [Early Native Bootstrap](adr/EarlyNativeBootstrap.md).

The interactive client owns the native Windows host, Direct3D 12 graphics backend, RTS input/camera stack, immutable presentation extraction, frame pacing, and development visualization. Canonical gameplay construction is delegated to `MatchRuntime` using the `Gameplay` profile. Player 1 is explicitly human-controlled and Player 2 computer-controlled; the local slot therefore does not receive a strategic opponent controller.

The client supplies a host-owned `JobScheduler` to the shared runtime. Scenario disposal does not dispose that scheduler; the client host owns its single disposal. Match restart creates a fresh shared scenario so ECS state, inventories, navigation state, routes, controllers, and orders do not leak between sessions.

Normal launches contain only the skirmish world. Synthetic instance load is opt-in through `--render-stress` and is added after authoritative gameplay/navigation construction, so rendering stress entities do not alter the gameplay navigation topology. F2 includes the battlefield's starts, resources, strategic sites, logistics corridor, crossings, infrastructure state, Command Core objectives, and skirmish strategic state alongside the existing system diagnostics.

Launch it with:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

For bounded validation that exits automatically:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test
```

The client is Windows x64 specific. Native Win32 calls remain confined to `ForgeLine.Platform.Windows`; headless and simulation projects must not reference the client or Windows platform project.

See [Windows Client](WindowsClient.md) for lifecycle and DPI details, [RTS Camera and Input](CameraAndInput.md) for camera controls and coordinate conventions, [World and Terrain](WorldAndTerrain.md) for terrain queries, mesh generation, culling, diagnostics, and benchmark coverage, [Ground Movement and Local Steering](GroundMovementAndSteering.md) for authoritative locomotion semantics, [Hierarchical Navigation](HierarchicalNavigation.md) for long-range ground routing, and [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for multi-unit shared routing and formation behavior.

## Headless Runtime

The development host supports lightweight engine stress and complete vertical-slice execution:

```text
--scenario <lightweight|vertical-slice>
--profile <gameplay|validation>
--ticks <count>
--seed <value>
--tick-rate <hz>
--entities <count>
--matches <count>
--require-terminal
--diagnostics-output <path>
--help
```

The logical tick rate describes simulation time. Headless execution does not sleep to match real time and may run substantially faster than the configured logical rate. The vertical-slice runtime remains fixed at the canonical 20 Hz simulation rate and uses the same `MatchRuntime` construction path as the Windows client.

`gameplay` preserves product-facing starting stock, opponent behavior, 16 m navigation cells with 8-cell sectors, and the normal distribution retry/attempt/fairness policy (20 ticks / 4 attempts / 200 aging ticks). `validation` deliberately retains accelerated starting stock and opponent behavior, 32 m navigation cells with 4-cell sectors, and its faster distribution policy (10 / 8 / 100) for deterministic CI/soak coverage. Validation tuning must not be treated as product balance.

Headless participant assignments are explicit runtime settings rather than hidden host behavior. Tests may choose human or computer slots independently of the profile when validating composition parity. Headless match creation forwards cancellation and disposes each scenario after the fresh-session run; the shared runtime introduces no wall-clock pacing.

Run repeated fresh sessions with:

```powershell
pwsh ./build/Run-MatchSoak.ps1 -Profile validation -Matches 5 -TicksPerMatch 80000 -Seed 2026
```

The `Match Soak` GitHub Actions workflow exposes the same runner through manual dispatch and uploads the JSON report.

## Commit Discipline

Prefer several small, logically complete commits over broad aggregate commits.

Prepare one PATCH increment per PR against current master. Keep unrelated
changes separate, preserve history and validate the PR version.
The existing no-squash integration convention remains in force.
## Documentation

Documentation changes with implementation. When project responsibilities, dependency boundaries, build commands, validation requirements, supported technical baselines, diagnostic conventions, or performance workflows change, update the relevant documents in the same work.


## Skirmish Opponent Validation

The skirmish opponent is a game-composition layer, not an alternate simulation authority. New behavior must preserve the command boundary, faction-scoped intelligence, real resource costs, physical Fuel/Ammunition, and normal logistics/movement/combat execution. Difficulty/configuration changes may adjust decision cadence and thresholds but must not alter simulation advantages.

Use `MatchRuntime` as the canonical reusable game composition and `SkirmishScenarioHarness` as its test-facing wrapper. Keep focused deterministic scenarios in the normal test suite; CI also executes one accelerated terminal validation match, while repeated multi-match soak remains on demand. See [Skirmish Opponent](SkirmishOpponent.md) for behavior, allowed knowledge, configuration, diagnostics, and current limitations.

## World environment integration

World appearance overrides and multi-chunk Traverse qualification are described in [World environment rendering](WorldEnvironmentRendering.md). Capture clean committed binaries sequentially; timed traversal includes terrain admissions.


Combat/industrial validation adds bounded extraction, industrial production parity, decal lifecycle/visibility, allocation churn, fallback saturation and production perspective-depth regressions to existing mechanical and particle tests. EffectsBarrage and IndustrialEffects are additional native reference scenes. Reproduction and portable measurements: [combat and industrial rendering](CombatIndustrialRendering.md).


Scalable rendering adds RenderSnapshotIndexTests, reference/optimized upload parity and native DepthReconstructionTests to CI. Frame hot-path reports now measure snapshot-index rebuild separately. Full reproduction, capability probes and CPU/GPU qualification limits: [scalable rendering](ScalableRendering.md).

## Building demolition

[Building demolition](BuildingDemolition.md) describes Delete input ownership, historical material refunds, authoritative validation and replay compatibility.

## Tactical supply and convoy reliability

The early Tactical supply, convoy and bounded navigation contracts step runs TacticalCombatSystemTests, CargoTransportSystemTests, MoveEntitiesCommandTests, HierarchicalNavigationSystemTests, MatchPersistenceTests and the full Navigation test module after the Release build. It preserves the independent conveyor gate and every later full-solution, Windows/WARP, asset/map/package/recovery and natural-terminal check.

The paired refinement workload is reproduced with `dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release -- --filter *HierarchicalPathfindingBenchmarks* --job Short --inProcess --artifacts artifacts/navigation-fallback --exporters BriefJSON`. Qualify reachability separately from timing: the old counterexample returns an incorrect NoLocalRoute and therefore performs less work. See HierarchicalNavigation.md and NavigationRefinementMeasurements.json for recorded sources, allocation, raw measurements and limits.

## Operations chain validation

The early Operations chain diagnosis and safe focus CI step checks transient conveyor causes, real paid production-chain recovery, typed bounded links, source-stock semantics, privacy, held navigation, dense copied rendering and extraction/composition bounds. It preserves the tactical, recovery, conveyor, asset/map, full-solution, native, package and natural terminal gates. See [Operations diagnosis](OperationsDiagnosis.md) and its source-pinned measurement report. Native human readability and reference-hardware performance remain separate acceptance classes.

## Alternate battlefield

Central Divide remains the default; Split Basin is selected in New Game or via `--scenario split-basin` in client/headless. Both use the same compiled registry, save/replay and deployed package contracts. See [Split Basin](SplitBasinBattlefield.md) for actual-data geography, normal-stock opening, route measurements and reproducible checks. The early Split Basin gate adds focused geography/runtime tests and map compilation without removing any existing gates.

## Developer tools foundation

The default public build excludes developer implementations. Internal Release builds
opt in with -p:ForgeLineEnableDeveloperTools=true and use isolated bin/Developer
and obj/Developer roots. Use the same property for restore and dependent builds.
The public publisher forces false and verifies assembly variant metadata.
See [Developer tools architecture](DeveloperToolsArchitecture.md) for capability,
request/result, parameter, preset and provider contracts and ownership handoffs.
No menu or mutating tool is implied by these contracts.

## Fair opponent qualification

The early CI fair-opponent step runs named-policy, legal-intelligence, raid-counterplay and blocked-factory recovery cases. Opt-in normal-stock seeded cohorts retain fixed tick bounds and explicitly distinguish completed matches from censored results. See [Fair opponent difficulty](FairOpponentDifficulty.md) for selection and commands; the canonical terminal gate and shared pacing runner are unchanged.

## Strategic logistics capacity

The optional LOG layer separates conveyors, regional Cargo roads and field service orders. Repeat LOG to filter; click an authorized marker for the existing Operations diagnosis and controls. Current output, buffers and scheduled load retain distinct meanings; missing rates, queues and downtime remain N/A. See [controls, visibility and bounded sampling](StrategicLogisticsCapacity.md) and [qualification](StrategicLogisticsQualification.md).

## Command replay qualification

The early CI command-replay step preserves the existing journal, integrity and save-cancellation checks and adds cooperative replay cancellation plus optional intermediate diagnostic comparisons. See [Deterministic command replay](DeterministicCommandReplay.md) for CLI stop behavior, schema/visibility limits and API reproduction, and [executed qualification](CommandReplayQualification.md) for measured results.

## Strategic map audit

After the Release build, run `./build/Test-StrategicMapAudit.ps1` for every registered compiled map, repeatable JSON/Markdown, explicit warning exceptions and controlled failure cases. The same check and focused regression suite run early in CI. See [criteria and reproduction](StrategicMapBalanceAudit.md).
