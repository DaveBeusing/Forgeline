# Diagnostics and Performance

ForgeLine Engine treats correctness diagnostics and performance measurement as part of the engine foundation rather than late-stage tooling.

## Diagnostic Model

Core diagnostic terminology is shared through:

- `DiagnosticCategory`
- `DiagnosticSeverity`
- `EngineDiagnostic`
- `EngineInvariant`
- `EngineInvariantException`

Invariant failures use a stable category and code together with an explicit failure message. Current ECS invalid-entity mutations report `ECS_ENTITY_NOT_ALIVE`.

The invariant mechanism is intended for states that indicate engine correctness failures. It is not a replacement for normal input validation or recoverable gameplay errors.

## Simulation Diagnostics

`SimulationCoordinator` exposes opt-in diagnostics through `SimulationDiagnostics`.

Diagnostics are disabled by default. Disabled diagnostics do not collect tick timing or allocation deltas and retain the allocation-free empty-tick behavior verified by the simulation tests.

When enabled, snapshots expose:

- configured simulation tick rate
- completed tick count
- last tick duration
- average observed tick duration
- maximum observed tick duration
- allocated bytes observed during the diagnostic session
- Gen 0, Gen 1, and Gen 2 collection deltas
- live entity count
- entity capacity
- component type count
- total component instance count
- deterministic per-component counts
- job scheduler counters and aggregate timing when a scheduler is present
- current managed runtime allocation, heap, memory-load, and collection information

Tick timing is diagnostic observation only. It must never influence simulation decisions.

## Combat Runtime Metrics

`CombatRuntime.Metrics` exposes simulation-owned combat counters without introducing presentation authority:

- active physical projectiles
- shots fired and Ammunition consumed for the current tick
- projectiles spawned and impacts for the current tick
- successful damage applications and damage amount for the current tick
- combat destructions for the current tick
- cumulative shots, Ammunition consumption, projectiles, impacts, hits, damage, and destructions

`CombatRuntime.Events` contains the current tick's shot, projectile-spawn, impact, damage, and destruction outputs. These events are presentation/diagnostic outputs only; consumers must not feed visual timing back into combat resolution.

`CombatDebugSnapshotSystem` can capture weapon ranges/targets, projectile positions/velocities, Health values, impacts, and the runtime metrics during `SnapshotEvents`. The Windows client F2 world-debug path renders those copies without mutating simulation state.

Directional armor and target acquisition add simulation-owned diagnostics without changing that authority boundary.

`TargetAcquisitionSystem.Metrics` reports current-tick and cumulative scans, candidates, acquisitions, reacquisitions, and rejects, plus cumulative friendly, target-class, range, intelligence-availability, line-of-fire, and fire-policy rejection counts.

`CombatDamageResolutionSystem.Metrics` reports armored hits, Front/Side/Rear/Top hit counts, and cumulative mitigated damage.

When debug capture is enabled, target rejection positions/reasons and armor facing transforms are copied into the combat debug snapshot for F2 visualization.

## Battlefield Intelligence Metrics

`BattlefieldIntelligenceSystem.Metrics` reports active visual/radar sensors, scans, candidate counts, Detected/Identified contact counts, visible/explored cell counts, cumulative sensing work, and optional measured sensor-update duration.

Sensor timing is enabled only when requested by the development composition. Timing never feeds simulation decisions.

Faction-intelligence snapshots provide presentation-safe Fog-of-War cells and contact read models. Detected-only contacts contain opaque contact keys and last-known positions rather than current hidden entity transforms.

## Job Metrics

The job scheduler exposes:

- submitted jobs
- completed jobs
- faulted jobs
- canceled jobs
- pending jobs
- running jobs
- worker count
- peak concurrent running jobs
- aggregate wait duration
- aggregate execution time
- instrumentation failures

Per-job timing remains available through the scheduler timing observer.

## Headless Diagnostic Reports

The headless host can write an indented JSON report containing runtime metadata, requested scenario parameters, loop counters, and a simulation diagnostic snapshot.

Example:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --tick-rate 20 --entities 1000 --diagnostics-output artifacts/headless.json
```

The report records:

- .NET runtime description
- operating system description
- process architecture
- processor count
- seed and logical tick rate
- requested and executed tick counts
- requested entity count
- wall-clock elapsed time and throughput
- simulation loop counters
- simulation, ECS, job, allocation, and GC metrics

These values make results interpretable across different machines. They are measurements, not product guarantees.

For integrated readiness work, the headless host can execute the complete Central Divide vertical slice:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario vertical-slice --profile validation --ticks 80000 --seed 2026 --require-terminal --diagnostics-output artifacts/opponent-full-match.json
```

The vertical-slice report adds match outcome/pacing, entity and pending-command state, tick timing, observed allocation/GC activity, Cargo Transport completion/failure/route counters, Automated Distribution state, Battlefield Supply transfer totals, Artillery shot/impact totals, and per-side economy/power/industry/intelligence/readiness/force summaries.

`gameplay` and `validation` are intentionally different profiles. Gameplay uses normal starting stock, default opponent settings, and the interactive client's navigation resolution. Validation uses explicit accelerated resources/opponent pacing and coarser navigation for bounded deterministic coverage. Validation values must not silently become gameplay defaults.

### Bounded Skirmish Progression Reports

A vertical-slice run with `--diagnostics-output` additionally registers the read-only `SkirmishProgressionDiagnostics` observer. It writes a per-match sidecar by replacing the main file extension with `progression-N.json`; for example:

```text
artifacts/opponent-full-match.json
artifacts/opponent-full-match.progression-1.json
```

The sidecar contains profile, match index, seed, executed ticks, terminal status, observed decision count, dropped history count, retained history, first eligibility-loss pairs, and the latest decision for each side. No progression observer is attached when the headless run omits a diagnostics output path.

Observation occurs at the end of `AiDecisions`, after strategic and automatic resupply decisions, but before the current tick's movement, supply, and production. The selected branch and commands are current to that decision; readiness and industrial results normally reflect the previously completed tick. An end-of-decision snapshot is not the exact pre-command state.

Observations include eligible attacker counts and individual exclusions, unit readiness and orders, consuming-facility required/available/reserved/missing inputs, provider cargo versus propulsion Fuel, industrial production and power state, extractor outputs and remaining deposits, and transport cargo, reservations, delivery state, positions, movement intent, and navigation failure. Exclusion categories overlap and must not be summed as unique excluded units. `HasReadiness=false` distinguishes missing readiness from a measured full-readiness value. Missing inputs do not replace the facility's active block reason, such as NoPower.

History is bounded to 128 entries: the opening 32 and a rolling 96-entry tail. A changed strategic/eligibility/production/provider/industry signature or a 1,000-tick sampling interval adds an entry at a real decision boundary. The first transition from sufficient eligible attackers to fewer than the configured minimum is retained separately for each player, even when ordinary entries expire. The latest decision is independently retained. The post-run console includes at most 12 retained transport-history snapshots; it does not emit transport details every tick.

Detail caps per observation are 128 combat candidates, 32 unit-production facilities, 64 providers, 32 industrial facilities, 32 extractors, and 32 transports. Provider recipient lists are capped at 128. Transport motion includes the current local waypoint, pending destination, route progress and up to four upcoming waypoints, velocity, stalled-tick count, and observed order tick. Omission counts distinguish bounded output from a complete listing. Diagnostic snapshots do not issue commands or change resource quantities, movement, eligibility, or match results.

`ResupplyPlanningResult` retains one compact result per recipient for the latest planner attempt. It records the attempt tick, friendly and rejected provider counts, selected provider, and a bitmask of rejection reasons. These distinguish no friendly provider, self-supply, disabled provider/depot, missing inventory or position, unavailable Fuel/Ammunition cargo, unavailable recipient travel, nonmobile/refueling/busy providers, insufficient provider propulsion Fuel, and route retry deferral. Foreign provider counts and positions are not disclosed. A successful selection can still include rejection reasons for other candidates; the selected provider is authoritative for that attempt. An old attempt result is not a current availability guarantee.

`SupplyRescueAssignment` identifies provider movement ownership. `SupplyRescueRejection` records the last route admission rejection per provider, including the affected recipient, required/available Fuel and tick. The bounded supply snapshot retains at most 128 planner results and 64 route rejections, with omission counts. `RouteFuelInsufficient`, `RemainingRouteFuelInsufficient`, `FuelBudgetUnavailable`, and navigation failure are distinct from cargo shortages. A geometric path success is not proof of affordable travel or completed physical supply. See [Battlefield Supply](BattlefieldSupply.md).

### Investigating a Nonterminal Match

Start with the first recorded loss of minimum offensive eligibility. Trace extraction, processing, actual consuming inventories, stock reservations, current transport progress, provider assignment, and physical supply rather than inferring a root cause from the final army size. A threshold crossing can be correct behavior; failure to recover requires separate evidence.

A zero final resupply-order count does not establish healthy supply. A faction's total Fuel does not establish that provider cargo or factory inputs contain deliverable Fuel. `OutputBlocked` with a nonempty deposit is not deposit exhaustion. Successful cumulative cargo deliveries do not establish that currently assigned trucks are moving, and Cargo Transport route counters do not describe every battlefield navigation request. Compare repeated positions, waypoint/accepted ticks, and delivery-state ticks to distinguish travel, navigation churn, and physical obstruction.

For a stable logistics-node visit, the Cargo Transport approach target remains fixed while its matching movement intent is active. Steering around an obstacle must not continuously relocate the requested loading point and restart its route. An interrupted visit can select a fresh approach from the new location; network invalidation and normal navigation remain responsible for replacing invalid routes. `CargoTransportApproachTests` checks target stability and resumption without moving inventory remotely.

Historical [CI run 36309353158](https://github.com/DaveBeusing/Forgeline/actions/runs/36309353158), head `cc7b48acf90305b8e7e0a86c19832a28c8cbbdfd`, built cleanly and passed 411 tests on 2026-09-27 but remained Active after 80,000 validation ticks for seed 2026. East first lost minimum attacker eligibility at decision 15161 and West at 16681 through configured Fuel thresholds. At decision 79991, both sides had generation 400 against demand 357 and no offline consumers, but empty refinery inputs and OutputBlocked extractors with substantial remaining deposits. These observations rule out persistent final power shortage and deposit exhaustion for that run; they do not prove one universal failure mechanism.

That run measured 3.382 ms average tick, 223.188 ms maximum tick, and 34,976,826,808 allocated bytes. These single-run measurements are neither timing guarantees nor valid comparisons against a different workload or diagnostic configuration. Final-head CI and subsequent scenario reports remain the evidence for later changes.

Natural terminal acceptance is separate from forced-objective lifecycle tests. Preserve `--require-terminal` and the 80,000-tick bound, repeat the same successful seeded configuration, and inspect every requested fresh-session soak result, including failures. Do not weaken resource costs, eligibility thresholds, or the match gate to hide a failure.

## Headless Test Harness

Simulation tests use a reusable `SimulationTestHarness` that can:

- create a simulation with a known seed
- optionally create a job scheduler
- create entities
- submit commands to explicit target ticks
- run an exact number of ticks
- expose resulting ECS and simulation state

This keeps deterministic integration scenarios concise and ensures tests do not initialize graphics, audio, UI, or windowing.

Use the test runner selected by `global.json`. The canonical full-suite command for the current Microsoft.Testing.Platform configuration is:

```powershell
dotnet test --solution ForgeLine.sln --configuration Release
```

## Benchmarks

BenchmarkDotNet hosts are separate from correctness tests.

ECS baselines cover:

- entity creation
- entity destruction
- component add/remove
- component lookup
- dense ECS iteration
- multi-component queries
- 1,000 and 10,000 entity workloads

Rendering diagnostics additionally expose frame time, CPU render time, terrain visibility/submission counts, generic instance visibility/submission counts, and development overlay allocation/GC state. GPU timestamps remain deferred until the graphics abstraction owns a clean timestamp-query/readback lifecycle.

Navigation baselines cover long-distance path searches over a multi-chunk map, including sector routing, local refinement, cache reuse, expanded-node counts, and route length. Benchmark timing remains observational rather than a CI gate.

Simulation baselines cover:

- 10, 50, and 100-unit independent strategic routing versus one shared formation route
- empty fixed ticks
- light system ticks
- 1,000-entity iteration ticks
- representative multi-system ticks
- command scheduling and processing
- sequential range work
- parallel scheduler range work
- spatial radius queries at multiple query sizes
- spatial AABB queries
- filtered spatial queries with stable result ordering
- updates of 10,000 indexed entries

Run them with:

```powershell
dotnet run --project benchmarks/ForgeLine.Ecs.Benchmarks/ForgeLine.Ecs.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release
```

The rendering host includes terrain workloads plus 1,000 near-field simple instances and 5,000 total simple instances with far-field culling. BenchmarkDotNet output includes runtime and machine information. Keep benchmark results when comparing architecture or hot-path changes so the environment remains visible.

## Stress Scenarios

Bounded lightweight-entity and integrated vertical-slice scenarios are available directly from the host:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 64 --seed 42 --entities 10000 --diagnostics-output artifacts/stress-10000.json
```

The simulation test suite also verifies that 10,000 lightweight ECS entities can exist and execute headless ticks without stale-entity or lifecycle failure. Spatial correctness coverage separately indexes and repeatedly moves 10,000 entries, verifies queryability afterward, compares radius results against brute-force reference fixtures, and checks negative-coordinate and chunk-crossing semantics. A 1,000-entity scenario exercises a representative multi-component load.

Combat scale measurement is available through the simulation BenchmarkDotNet host with 100/1,000 simultaneously armed direct-fire entities and 100/1,000 moving physical projectiles. Logistics stress coverage remains separate so the measured workload is attributable to the subsystem under test.

Repeated full-match endurance runs use `build/Run-VerticalSliceSoak.ps1` or the manually dispatched `Vertical Slice Soak` workflow. These runs create a fresh simulation for every match and retain environment/context in the structured report. They are deliberately not hard PR timing gates.

## Interpreting Results

Use timing values comparatively rather than as universal thresholds.

Investigate regressions by checking:

1. whether the runtime, OS, architecture, and processor count are comparable;
2. whether entity count and workload parameters are identical;
3. whether allocation or GC behavior changed;
4. whether job counts, wait time, or execution time changed;
5. whether a code change altered the amount of simulated work;
6. whether the result reproduces across repeated benchmark runs.

Hardware-sensitive timing is intentionally not a hard CI gate.

## CI

CI:

- restores and builds the complete solution;
- validates project-reference boundaries;
- runs a 1,000-entity headless diagnostics smoke scenario;
- runs a bounded 10,000-lightweight-entity stress smoke scenario;
- runs one accelerated terminal Central Divide match through the real headless game stack;
- runs the complete correctness test suite;
- uploads the generated JSON diagnostics, including the vertical-slice report and its progression sidecar, as the `engine-diagnostics` workflow artifact.

The artifact exists to make failures and performance observations inspectable without turning volatile timing into pass/fail thresholds.

## Current Engineering Targets

The following remain non-binding engineering targets:

- stable 20 Hz simulation;
- 1,000+ active combat/logistics entities without architectural redesign once those gameplay systems exist;
- 10,000+ lightweight simulation entities as an early stress target.

They are engineering goals, not shipped product guarantees.

## Spatial Index Diagnostics

`SpatialGridIndex.CaptureDiagnostics()` exposes:

- indexed entity count
- occupied cell count
- maximum cell occupancy
- average cell occupancy
- query count
- total, average, and maximum query duration when query timing is enabled

Query timing is optional so timing instrumentation does not become mandatory hot-path overhead. The Windows client enables it for the development scenario.

`CaptureDebugSnapshot()` exposes a presentation-safe copy of occupied cell bounds and occupancy counts. F2 world debugging uses this snapshot to draw occupied cells and query regions without allowing rendering to mutate the simulation index.

See [Spatial Index and World Queries](SpatialIndexAndWorldQueries.md) for query semantics, lifecycle ownership, and performance limits.

## Presentation Diagnostics

The Windows client development overlay can be toggled with F1. World debug visualization can be toggled with F2. The presentation path, metric semantics, extraction ownership, and render baselines are documented in [Presentation Extraction and Debugging](PresentationExtractionAndDebugging.md).

## Navigation Diagnostics

`HierarchicalNavigationSystem.LastDiagnostics` exposes queued path requests; completed, failed, canceled, and stale-result counts; currently pending requests; active routes; expanded high-level and local nodes for the latest completed route; latest route length; and latest pathfinding latency.

`NavigationPath.Diagnostics` additionally records whether the high-level route cache was hit. These values are diagnostic observations only and never change simulation outcomes.

F2 world debugging can display local traversability, nearby sector boundaries, sector portals, the latest high-level route, and its refined waypoint path. Rendering receives navigation-derived read data only and never mutates navigation or simulation state.

See [Hierarchical Navigation](HierarchicalNavigation.md) for lifecycle and interpretation details.

## Formation Movement Diagnostics

`FormationMovementSystem.LastDiagnostics` exposes active group/member counts, largest group size, compressed groups, shared strategic path requests, completed/failed groups, slot reassignments, compression events, split-cohort events, and blocked-slot projections.

A healthy large-selection move should show one shared path request for the movement group while `HierarchicalNavigationSystem` reports no new per-member strategic requests for formation-local slot orders.

F2 world debugging additionally renders movement-group bounds and centroids, travel direction, active shared waypoint and route, slot targets, assignment lines, compression scale, and split cohort count. The presentation layer consumes copied debug snapshots only.

The Simulation BenchmarkDotNet host includes `FormationRoutingBenchmarks` for 10, 50, and 100 members. It compares independent strategic path searches with one centroid-based shared group route. Timing is observational; the primary invariant is the reduction from N strategic route requests to one group route where members can share navigation.

See [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for lifecycle, fallback behavior, and interpretation details.

## Artillery Diagnostics

`ArtilleryFireMissionSystem.Metrics` reports active missions, `NoAmmo` missions, shells in flight, shots, impacts, affected area-damage targets, Ammunition consumption, and queued area damage for the current tick and cumulatively.

The F2 artillery debug read model exposes mission min/max range, fixed target coordinate, lifecycle state, requested/fired rounds, shell position, complete parabolic trajectory, impact radius, and a compact metrics label. These diagnostics consume simulation-owned state without controlling targeting, dispersion, impact timing, or damage.

## Tactical Combat and Readiness Metrics

`TacticalCombatSystem.Metrics` reports ordered, engaging, pursuing, holding, retreating, and intelligence-waiting unit counts together with current and cumulative group target assignments.

`AutomaticResupplyDecisionSystem.Metrics` reports evaluated/low-supply units, active real resupply orders, newly issued provider orders, unavailable-provider decisions, and cumulative values.

`CombatReadinessSystem.Metrics` reports unit/group counts, ready/degraded/combat-ineffective unit counts, and average unit/group readiness. Readiness is calculated after authoritative work in `SnapshotEvents` and never substitutes for Health, Fuel, Ammunition, mobility, or weapon state.

The F2 tactical read model exposes current combat order/status, legitimate target position where available, pursuit leash, movement permission, resupply state, and per-unit Health/Fuel/Ammunition/readiness summaries.

The simulation benchmark host includes 100/1,000-unit tactical acquisition and deterministic group target-coordination workloads.


## Skirmish Strategic Assessment Allocation Measurement

Strategic-opponent allocation work is measured separately from aggregate full-match allocation. The historical cumulative allocation reported by a headless run describes the complete instrumented process and must not be attributed to one system without narrower evidence.

`SkirmishOpponentSystem.WorkMetrics` exposes cumulative operation counts for owned-state captures, intelligence captures, economy assessments, force assessments, decision evaluations, non-decision evaluations, and per-controller scratch lifetime. The vertical-slice headless JSON report records the same counters in each match's `OpponentWork` field next to aggregate allocation and GC observations. These counters are semantic diagnostics rather than timing gates. With opponent debug capture disabled, non-decision ticks still refresh owned state for critical logistics recovery but do not capture faction intelligence or recompute economy/force assessment. With debug capture enabled, those assessments remain current on non-decision ticks so the debug snapshot is not presented as fresh while containing stale values.

Owned-state collection storage is reused per live controller. Every capture clears logical contents before refill, collection iteration retains the existing stable entity ordering, unusually large retained collection capacity is trimmed to the bounded scratch limit, and controller removal releases the corresponding scratch state. A fresh vertical-slice runtime owns a fresh opponent system, so scratch state does not cross match/session lifetime.

The allocation benchmark is part of the existing simulation BenchmarkDotNet host:

```powershell
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release -- --filter *SkirmishOpponentBenchmarks*
```

The workload matrix covers one versus two strategic controllers, diagnostics disabled versus enabled, and an early versus later vertical-slice state. `EightNonDecisionTicks` measures the lightweight cadence path between strategic decisions. `DecisionCadenceWindow` advances exactly one configured reaction window and therefore includes one strategic decision evaluation per active controller, including the layered debug projection when diagnostics are enabled. BenchmarkDotNet `MemoryDiagnoser` reports managed allocation for the benchmark process, while the opponent work counters show which strategic assessments were actually performed. Worker-thread allocation is not represented by `GC.GetAllocatedBytesForCurrentThread`-style accounting; use BenchmarkDotNet/process diagnostics when whole-process attribution is required.

For before/after comparisons, retain the BenchmarkDotNet environment header, runtime version, OS, architecture, processor count, scenario age, controller count, diagnostics state, and seed. Compare normalized allocation distributions for identical parameters. Do not treat a reduced managed-allocation result as proof that every maximum-tick outlier, retained-heap issue, or GC pause has been resolved.


## Visual Asset Qualification

The integrated visual qualification path combines runtime asset validation, the real Windows Direct3D 12 Vertical Slice smoke, and representative tactical/strategic rendering benchmarks.

CI publishes `artifacts/asset-qualification.json` and `artifacts/visual-qualification.json`. The latter records frame/CPU-render timing, terrain submission, draw calls, instance counts, LOD distribution, VFX population, and adapter information from a completed render frame. GPU milliseconds remain unavailable until the graphics abstraction implements validated D3D12 timestamp queries.

See [Asset Performance and Visual Qualification](AssetPerformanceQualification.md) for commands, budgets, interpretation, regression policy, and outlier handling.
