# Development

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

This is the canonical build and validation entry point. Run commands from the repository root using the exact SDK and Microsoft.Testing.Platform selected by [global.json](../global.json).

<a id="source-of-truth"></a>

<details>
<summary>On this page</summary>

- [Start here](#start-here)
- [SDK and build configuration](#sdk-and-build-configuration)
- [Semantic versioning](#semantic-versioning)
- [Canonical validation path](#canonical-validation-path)
- [Pull requests and required check](#pull-requests-and-required-check)
- [Test projects](#test-projects)
- [Windows client host](#windows-client-host)
  - [Developer builds](#developer-builds)
- [Headless runtime and recovery](#headless-runtime-and-recovery)
- [Feature-specific validation](#feature-specific-validation)
- [Diagnostics and benchmarks](#diagnostics-and-benchmarks)
  - [Opt-in render-owner telemetry](#opt-in-render-owner-telemetry)
- [Documentation maintenance](#documentation-maintenance)

</details>

## Start here

| Task | Source of truth |
| :--- | :--- |
| Build and validate a PR | [Canonical validation](#canonical-validation-path) and [CI workflow](../.github/workflows/ci.yml) |
| Prepare the version | [PR Versioning](ReleaseVersioning.md) and [Directory.Build.props](../Directory.Build.props) |
| Understand dependencies | [Architecture](Architecture.md) and `build/Validate-ProjectReferences.ps1` |
| Understand governance and approvals | [Project Context](ProjectContext.md) |
| Compile or repair runtime content | [Asset Pipeline](AssetPipeline.md) and [Runtime recovery](RuntimeVisualAssetCompatibility.md) |
| Find a feature or historical result | [Documentation hub](README.md) and [complete catalog](DocumentationIndex.md) |

Current `master` is the technical source of truth for implemented code, configuration and build behavior. Start technical work from its current revision, reuse relevant existing work deliberately, and integrate through short-lived PRs. Placeholder approval metadata does not establish product approval.

<a id="sdk"></a>

## SDK and build configuration

| Setting | Repository value / owner |
| :--- | :--- |
| **SDK** | .NET SDK **10.0.402**, exact selection with roll-forward disabled in `global.json`. |
| **Language** | C# 14. |
| **Test runner** | Microsoft.Testing.Platform, selected in `global.json`. |
| **Compiler and analysis** | Central settings in `Directory.Build.props`; nullable analysis, deterministic builds and warnings as errors. |
| **NuGet packages** | Central versions in `Directory.Packages.props`. |
| **Interactive host** | Windows x64 / Direct3D 12. Simulation and headless code keep their platform separation. |

```powershell
dotnet --version
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
```

Blender is required for Blender authoring/export tasks, not an ordinary build from existing source assets. Use the family-specific toolchain instructions when reauthoring.

## Semantic versioning

Each PR commits **one PATCH increment relative to current master**, including documentation-only PRs. Repeating preparation against the same base is idempotent; extra commits and CI attempts do not consume versions.

```powershell
pwsh ./build/Increment-PrVersion.ps1 -BaseRef origin/master
pwsh ./build/Validate-SemVer.ps1 -BaseRef origin/master
```

Rebase concurrent PRs onto current master and refresh their version before integration. [PR Versioning](ReleaseVersioning.md) owns the policy, artifact metadata and validation contracts. CI performs no automatic tag allocation or release publication. The generic [Unified Versioning Standard](UnifiedVersioningStandard.md) remains a historical proposal rather than the current repository increment policy.

## Canonical validation path

> [!IMPORTANT]
> This is the complete local reproduction sequence for a PR. The required remote `build-test` result belongs to the exact source under review. A successful build, selected tests, smoke run or earlier green revision does not substitute for that complete CI job.

```powershell
# 1. Version, restore, project graph and Release build
pwsh ./build/Validate-SemVer.ps1 -BaseRef origin/master -HeadRef HEAD
dotnet restore ForgeLine.sln
pwsh ./build/Validate-ProjectReferences.ps1
dotnet build ForgeLine.sln --configuration Release --no-restore

# 2. Compiled maps and a complete runtime asset build
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/map-qualification.json
pwsh ./build/Test-CompiledScenarioLoading.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode build -ForceRebuild -NoBuild
pwsh ./build/Test-RuntimeAssetPipeline.ps1

# 3. Native and headless smoke/stress
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 64 --seed 12345 --tick-rate 20 --entities 1000 --diagnostics-output artifacts/headless-smoke.json
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 16 --seed 67890 --tick-rate 20 --entities 10000 --diagnostics-output artifacts/headless-stress-10000.json

# 4. Complete solution, reproducible local package and natural terminal match
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --require-terminal --diagnostics-output artifacts/match.json
```

Run solution test modules **one at a time**, as CI does. Native desktop consumers and bounded simulation/client waits share machine resources. This preserves the full suite, assertions and deadlines; it does not filter tests or weaken the natural terminal-match requirement.

The CI workflow runs for PRs targeting `master` and pushes to `master`. Native client smoke is Windows-specific. Diagnostic JSON is uploaded as the `engine-diagnostics` artifact. The workflow includes additional focused feature gates before the complete suite; consult the workflow for their exact current selections.

The terminal gate uses seed **2026**, an **80,000-tick ceiling** and **`--require-terminal`**. Preserve the natural result and configured stock. A forced winner, free resources or shortened replacement workload cannot satisfy it.

<a id="commit-discipline"></a>

## Pull requests and required check

| Requirement | Meaning |
| :--- | :--- |
| **Required check** | GitHub Actions `build-test`, covering the complete job. |
| **Branch** | Short-lived feature/fix/technical work based on current `master`. |
| **Version** | Exactly one PATCH increment per PR. |
| **Commits** | Small, logically complete English commits; preserve technical history. |
| **Integration** | Ordinary merge and rebase remain permitted; the existing no-squash policy remains in force. |
| **Protection** | Required checks, up-to-date enforcement and effective repository settings need actual GitHub readback. |

See [Project Context](ProjectContext.md#intended-master-protection) for intended protection and dated enforcement records. Documentation does not itself enforce repository settings or grant product approval.

Early feature gates provide independent diagnostics. Their success does not remove map/runtime, full-solution, native/WARP, package, recovery or terminal-match requirements. Packaging also validates registered maps in the exported client folders.

## Test projects

The solution has **14 test projects**. Keep functional tests near their owning systems, including controlled failure, cancellation, ownership and recovery behavior.

| Group | Projects / focus |
| :--- | :--- |
| **Engine primitives** | Core, Ecs, Jobs, Input: identity, data/storage, scheduling, dependency/fence behavior and input contracts. |
| **World and execution** | World, Simulation, Navigation, Logistics: generation/terrain, fixed-tick phases, movement/pathing and transport capacity. |
| **Game composition** | Game: shared `MatchRuntime`, economy, combat, intelligence, supply, persistence and scenario progression. |
| **Content** | Assets: source/catalog/compiler/runtime integrity and authoring contracts. |
| **Interactive presentation** | Platform.Windows, Graphics, Presentation, Client: native lifecycle, rendering, copied read models, UI/host behavior and session recovery. |

Simulation/Game headless tests must not acquire interactive graphics or Windows dependencies. Use `MatchRuntime` for canonical composition and `SkirmishScenarioHarness` for test-facing scenario access. Focused deterministic tests remain part of the normal suite; repeated full-match cohorts remain opt-in.

## Windows client host

The normal client composes the skirmish through `MatchRuntime` with the `gameplay` profile. Player 1 is human-controlled and Player 2 computer-controlled. The client owns its shared `JobScheduler`; scenario/session disposal does not dispose that host-owned scheduler. Restart creates a fresh scenario and does not retain ECS, route, inventory, controller or order state.

```powershell
# Preferred development launcher: build, compile/qualify content, then launch
pwsh ./build/Start-DevelopmentClient.ps1

# Launch existing prepared outputs
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build

# Bounded native smoke
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test
```

The client never starts asset compilation. For explicit content preparation, use `Build-RuntimeAssets.ps1 -Mode development`; publish/build modes and invalid override handling are documented in [Asset Pipeline](AssetPipeline.md#build-modes-and-deployment-integrity). Native bootstrap checkpoints are described in [Startup diagnostics](StartupDiagnostics.md).

Synthetic `--render-stress` instances are opt-in presentation load added after authoritative gameplay/navigation construction. They do not change gameplay navigation topology. Debug/world overlays expose copied diagnostics; they do not own simulation truth.

Use [Windows Client](WindowsClient.md), [Camera and Input](CameraAndInput.md) and [Graphics Quality Settings](GraphicsQualitySettings.md) for lifecycle, DPI, controls, next-session graphics changes and display rollback. [Manual renderer qualification](WindowsRendererQualification.md) covers physical/WARP capture and external package installation. Its default Plan stage executes no captures and grants no hardware acceptance.

### Developer builds

The default Public build excludes developer implementations. Internal Developer builds use isolated `bin/Developer` and `obj/Developer` roots; apply the property consistently to restore and dependent builds:

```powershell
dotnet restore ForgeLine.sln -p:ForgeLineEnableDeveloperTools=true
dotnet build ForgeLine.sln --configuration Release --no-restore -p:ForgeLineEnableDeveloperTools=true
```

The public publisher forces developer tools off and verifies assembly variant metadata. See [Developer tools architecture](DeveloperToolsArchitecture.md), [menu](DeveloperToolsMenu.md), [session controls](DeveloperSessionControls.md), [simulation actions](DeveloperSimulationActions.md) and [presets](DeveloperPresets.md).

<a id="headless-runtime"></a>

## Headless runtime and recovery

Headless execution uses logical simulation time without wall-clock pacing. Use `--help` for the actual supported CLI:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --help
```

| Mode / option | Contract |
| :--- | :--- |
| **`--scenario`** | `lightweight`, `central-divide`, `split-basin` or a registered composition key. `vertical-slice` remains a Central Divide alias. |
| **`--profile`** | `gameplay` retains player-facing tuning; `validation` uses accelerated stock/opponent and validation-specific navigation/distribution settings. |
| **`--generated-map`** | Explicit verified generated package; requires the host asset identity. It conflicts with authored scenario/preset selection. |
| **`--ticks`, `--seed`, `--entities`, `--tick-rate`** | Bounded run configuration; a generated map recipe seed remains separate from the simulation seed. |
| **`--matches`, `--require-terminal`** | Repeated fresh sessions and explicit terminal-result validation. |
| **Save / replay / diagnostics** | See [Save, Load, Replay and Recovery](SaveLoadReplayAndRecovery.md) and [Diagnostics](DiagnosticsAndPerformance.md) for complete arguments and identity rules. |

| Profile | Navigation cells / sectors | Distribution retry / attempts / aging |
| :--- | :--- | :--- |
| **gameplay** | 16 m cells / 8-cell sectors | 20 ticks / 4 attempts / 200 ticks |
| **validation** | 32 m cells / 4-cell sectors | 10 ticks / 8 attempts / 100 ticks |

Tests can assign human/computer participants explicitly regardless of profile. Match creation forwards cancellation; each fresh scenario is disposed after its run. Validation tuning is not a product-balance claim.

```powershell
pwsh ./build/Run-MatchSoak.ps1 -Profile validation -Matches 5 -TicksPerMatch 80000 -Seed 2026
```

After a Release build, add `-NoBuild` to the soak runner to avoid rebuilding. `Run-VerticalSliceSoak.ps1` remains a compatibility forwarder with its historical report defaults. The manual `Match Soak` workflow uploads the same runner’s report; repeated five-match soak is separate from the ordinary PR timing gate.

<a id="skirmish-opponent-validation"></a>

<a id="world-environment-integration"></a>

<a id="building-demolition"></a>

<a id="tactical-supply-and-convoy-reliability"></a>

<a id="operations-chain-validation"></a>

<a id="alternate-battlefield"></a>

<a id="developer-tools-foundation"></a>

<a id="fair-opponent-qualification"></a>

<a id="strategic-logistics-capacity"></a>

<a id="command-replay-qualification"></a>

<a id="strategic-map-audit"></a>

## Feature-specific validation

Keep feature details and reproduction with their owning guides rather than duplicating the full CI sequence.

| Area | Guides and dedicated checks |
| :--- | :--- |
| **Authored maps and audits** | [Central Divide](CentralDivideBattlefield.md), [Split Basin](SplitBasinBattlefield.md), [strategic geography](StrategicMapAndOperationalGeography.md), [strategic audit](StrategicMapBalanceAudit.md); `Test-CompiledScenarioLoading.ps1` and `Test-StrategicMapAudit.ps1`. |
| **Generated maps** | [Deterministic contracts](DeterministicWorldGeneration.md), [generated packages](GeneratedBattlefieldMaps.md), [seeded selection](SeededSkirmishSelection.md), [procedural qualification](ProceduralMapQualification.md). |
| **Terrain and decoration** | [Biome fields](BiomeAndEcologyFields.md), [terrain materials](BiomeTerrainMaterials.md), [chunk decoration](ChunkDecorationRendering.md), [world environment](WorldEnvironmentRendering.md). |
| **Navigation, supply and convoy** | [Hierarchical navigation](HierarchicalNavigation.md), [navigation resilience](LogisticsNavigationResilience.md), [supply](BattlefieldSupply.md), [convoys](CargoConvoyOperations.md); qualify reachability separately from timing. |
| **Opponent policies** | [Skirmish Opponent](SkirmishOpponent.md), [Fair opponent difficulty](FairOpponentDifficulty.md); optional normal-stock cohorts distinguish terminal results from censored runs. |
| **Operations and strategic logistics** | [Rules](OperationsAutomation.md), [diagnosis](OperationsDiagnosis.md), [capacity](StrategicLogisticsCapacity.md); copied visibility, real paid recovery and bounded observations remain required. |
| **Commands and recovery** | [Replay](DeterministicCommandReplay.md), [save/load/recovery](SaveLoadReplayAndRecovery.md), [building demolition](BuildingDemolition.md); retain cancellation, identity and authority contracts. |
| **UI and native interaction** | [UI review](UiVisualReview.md), [HUD consolidation](HudSurfaceConsolidation.md), [input qualification](GameplayInputAccessibilityAndQualification.md), [interface qualification](GraphicalInterfaceQualification.md). |
| **Rendering and effects** | [Graphics](Graphics.md), [scalable rendering](ScalableRendering.md), [combat/industry rendering](CombatIndustrialRendering.md), [reference performance](GraphicsReferencePerformance.md), [serial integration evidence](rendering/performance-integration/Qualification.md). |

The navigation refinement workload is reproduced with:

```powershell
dotnet run --project benchmarks/ForgeLine.Navigation.Benchmarks/ForgeLine.Navigation.Benchmarks.csproj --configuration Release -- --filter *HierarchicalPathfindingBenchmarks* --job Short --inProcess --artifacts artifacts/navigation-fallback --exporters BriefJSON
```

The old counterexample can return an incorrect `NoLocalRoute` and therefore perform less work. Use the source-pinned navigation measurements; incomplete work is not a valid speedup baseline.

<a id="diagnostics"></a>

<a id="benchmark-projects"></a>

## Diagnostics and benchmarks

Engine diagnostics are opt-in and independent of rendering, UI, audio and platform-specific code.

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 1000 --seed 1 --entities 1000 --diagnostics-output artifacts/headless.json
```

The repository has five benchmark hosts: **Ecs, Navigation, Simulation, Logistics and Rendering**. Choose measured workloads in the owning host; use the corresponding `benchmarks/ForgeLine.<Area>.Benchmarks/ForgeLine.<Area>.Benchmarks.csproj` project. [Diagnostics and Performance](DiagnosticsAndPerformance.md) explains metrics, bounds, report fields and investigation.

Correctness tests run separately from timing. Finish builds, asset compilation and other GPU tests before native timing. Record the exact source, payload, settings, hardware, warmups, sample counts and limitations. Do not add overlapping pass durations or infer a speedup from API choice/draw reduction. Timing thresholds require explicit gates; a merged implementation or green structural capture does not grant performance or human art acceptance.

<a id="documentation"></a>

### Opt-in render-owner telemetry

The [render-owner telemetry](rendering/render-engine-upgrade/Telemetry.md) adds `FORGELINE_RENDER_TELEMETRY_OUTPUT` with predeclared warmup/sample counts (defaults 64/512), bounded startup/in-flight headroom, raw completed GPU joins and logical resource-byte accounting. Follow that runbook for the exact enable/restore commands and strict validation. The stream covers the active render-owner iteration, excluding deliberate idle-mailbox wait and independent platform/simulation owners. It is not an approved matched native fixture or a complete physical-allocation budget pass. Preserve source/payload identity, startup states, incomplete windows, overflows and failed attempts.

## Documentation maintenance

Update the relevant guide whenever implementation, dependency boundaries, commands, baselines, diagnostics or qualification workflows change. Use the [documentation conventions](DocumentationGuide.md), keep [the catalog](DocumentationIndex.md) complete, and preserve source-pinned results and raw evidence. Current instructions belong in current guides; historical results keep their original SDK, source SHA, status and measurement scope.
