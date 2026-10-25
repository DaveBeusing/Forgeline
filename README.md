# FORGELINE

<div align="center">

### BUILD. SUPPLY. CONQUER.

**Large-scale RTS · Industrial Production · Physical Logistics · Combined-Arms Warfare**

*Build the industrial machine that sustains the army that wins the war.*

[Gameplay](#gameplay) · [Pre-Alpha](#playable-pre-alpha) · [Getting Started](#getting-started) · [Engine](#forgeline-engine) · [Documentation](#documentation)

</div>

---

## Overview

**FORGELINE** is a real-time strategy game combining direct battlefield command with industry, supply chains, intelligence, and automation. Build factories, transport resources, sustain combat units, and destroy the infrastructure supporting your opponent.

> **Preparation matters more than click speed.** Logistics creates strategic opportunities, not repetitive chores.

## Gameplay

| Pillar | What you do |
| :--- | :--- |
| **Build** | Extract finite resources, refine materials, power factories, manufacture units, and automate production. |
| **Supply** | Establish transport routes, depots, fuel and ammunition distribution, and maintenance capacity. |
| **Conquer** | Reconnoiter, position combined-arms forces, exploit terrain, disrupt supply lines, and advance the frontline. |

**Core gameplay loop**

```text
Extract → Process → Manufacture → Supply → Fight → Expand → Automate → Scale
```

Industry is part of the battlefield. Destroying a bridge, cutting off fuel, or disabling production can decide the outcome of a battle.

## Playable Pre-Alpha

The current development slice centers on **Central Divide**, a **3.072 × 3.072 km** two-player map featuring contested resources, a North Bridge, an alternative South Ford, expansion sites, and destructible transport links.

| System | Present in the development slice |
| :--- | :--- |
| **Directorate** | 13 constructible structures and 7 producible units |
| **Industry** | Extraction, storage, power distribution, recipes, and production queues |
| **Logistics** | Capacity-based graph routing, physical cargo/supply loading queues, automated distribution, fuel and ammunition resupply |
| **Combat** | Infantry, vehicles, directional armor, suppression, repairs, artillery, and tactical orders |
| **Intelligence** | Fog of war, radar and visual sensors, detection/identification, information-limited targeting |
| **Skirmish** | Computer-controlled opponent operating under the same core economy, supply, and combat rules |
| **Match flow** | Setup, pause, victory/surrender, results, and restart |
| **Presentation** | Native Direct3D 12, terrain, Directorate assets, contextual HUD, minimap, strategic overlays, and effects |

Directorate buildings and units use compiled assets with physical LODs and damage-state geometry. Rifle Squad and Combat Engineer now have distinct four-member visual kits; their figures are presentation detail, while each squad remains one simulation entity. Scout Vehicle, artillery, cargo and supply trucks have dedicated component kits and damaged/critical/wreck meshes. Human art review and sustained native performance acceptance for those vehicle kits remain pending. See [infantry and battlefield production](docs/InfantryBattlefieldProduction.md) and [combat/support unit production](docs/CombatSupportUnitProduction.md).

**Development status:** This is a **pre-alpha vertical slice**. Rail gameplay, interactive map editing, other factions and multiplayer remain future work. The Windows client now plays bounded spatial battlefield sounds, alerts and ambience; see [spatial world audio](docs/SpatialWorldAudio.md). Measured qualification covers specific workloads and hardware; frame-rate and entity-scale targets remain engineering goals.

See [Central Divide](docs/CentralDivideScenario.md) and the [Windows client guide](docs/WindowsClient.md).

## Getting Started

### Prerequisites

- **Windows x64** for the interactive client.
- **.NET SDK 10.0.401**, pinned in [global.json](global.json), with C# 14.
- A **Direct3D 12-capable** graphics environment; designated development/smoke scenarios support WARP fallback.
- **PowerShell 7** (`pwsh`) for build and validation scripts.

From the repository root:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
```

### Build runtime assets and launch

```powershell
pwsh ./build/Start-DevelopmentClient.ps1
```

The development launcher builds the client, then compiles and qualifies runtime assets before starting it. Compilation failure prevents launch. Qualification and progress are recorded under `artifacts/`. See [runtime visual compatibility and recovery](docs/RuntimeVisualAssetCompatibility.md). Development mode supports incremental rebuilds; build mode prepares a complete runtime cache, and validate mode checks an existing cache without rebuilding. See [asset build modes and deployment integrity](docs/AssetPipeline.md#build-modes-and-deployment-integrity).

The client opens the Central Divide / Directorate skirmish flow and consumes compiled runtime assets; it never starts the compiler. Packaged builds consume adjacent runtime assets and require neither authoring sources nor the compiler. `FORGELINE_RUNTIME_ASSETS` selects an authoritative asset root; an invalid override is reported rather than silently replaced.

A branded native startup surface appears before settings and graphics initialization. Startup checkpoints are saved under `%LOCALAPPDATA%/FORGELINE/diagnostics`. See [startup timing](docs/StartupDiagnostics.md) and [asset compilation diagnostics](docs/AssetStartupDiagnostics.md).

### Essential controls

| Action | Default input |
| :--- | :--- |
| Select / add / box-select | Left click / Shift + left click / left drag |
| Move units | Right click |
| Pause | Space |
| Build / industrial production / unit production | B / P / U |
| Logistics / supply / combat / technology | L / Y / K / H |
| Strategic overlays | F10 |
| Controls help | F1 or F12 |
| Development metrics | Shift + F1 |
| Restart finished match / exit session | R / Escape |

Gameplay shortcuts can be changed in Settings through GAMEPLAY ACTION and GAMEPLAY KEY. Conflicts block APPLY with an explanation; saved camera mappings retain priority. Mouse selection, control-group digits, help and Escape remain fixed. See [input binding rules](docs/GameplayInputAccessibilityAndQualification.md).

Owned selections use the existing single/group action rings. Their read-only tooltip preserves selection details, covered group averages and exact command results. Group tools retain type filters, attention focus and formation; saved control groups remain available. The rectangular action bar and selection inspector are removed. Existing mode shortcuts also open available global actions without selection. See [HUD controls and parity](docs/HudSurfaceConsolidation.md).

The optional **OPERATIONS** tab inspects local production, stock, power and logistics bottlenecks. Resource/power-strip and populated alert controls open relevant views. Alerts marked **FOCUS** select and center an authorized target; **OPS** and **MORE** open operations filters. Strategic-map layers have full names and shape legends. See [operations](docs/ProductionAndLogisticsOperations.md) and [actionable alerts and strategic map](docs/ActionableAlertsAndStrategicMap.md).

Frontend and HUD use the shared [Command Interface Visual Language](docs/CommandInterfaceVisualLanguage.md). Further interaction details are in [Windows Client](docs/WindowsClient.md) and [Camera and Input](docs/CameraAndInput.md).

### Graphics settings

Settings → GRAPHICS QUALITY selects **Low, Medium, High or Ultra** for the next match; Medium is the default. Independent controls cover world resolution, spatial anti-aliasing, shadows, textures, object/surface detail, vegetation motion, atmosphere and particles while keeping HUD and selection indicators at full resolution. Display changes use a 15-second confirmation with automatic rollback, and effective hardware fallbacks are reported. See [graphics quality settings](docs/GraphicsQualitySettings.md).

The renderer includes a shared metallic-roughness lighting foundation. Optional fixed-state visual references and paired warm/renderer-cold performance runs record build/asset identity, resolution and timing. See [PBR qualification](docs/PbrFoundationQualification.md), [native visual evidence](docs/VisualReferenceEvidence.md) and [graphics reference performance](docs/GraphicsReferencePerformance.md). Structural checks and human art review remain separate gates.

## ForgeLine Engine

FORGELINE uses **ForgeLine Engine**, a purpose-built C#/.NET RTS engine.

| Area | Baseline |
| :--- | :--- |
| Language / runtime | **C# 14 · .NET 10** |
| Interactive target | **Windows x64** |
| Renderer | Custom **Direct3D 12** |
| Simulation | Fixed-tick, command-driven, headless-capable; **20 Hz target** |
| Entity architecture | Data-oriented custom ECS |
| Multithreading | Persistent-worker job scheduler |
| World / navigation | Chunk-based world, hierarchical paths, local steering, formation corridors |
| Logistics | Graph-based transport and capacity simulation |
| Content | Stable identifiers, editable source kits, compiled runtime assets |
| Recovery | Versioned save/load and command replay |
| Design | Strict simulation/presentation separation; future networking considered |

```text
Player Input → Validated Commands → Fixed-Tick Simulation
                                         │
                         ┌───────────────┴───────────────┐
                         │ ECS / World / Jobs / Gameplay │
                         └───────────────┬───────────────┘
                                         │
                               Presentation Snapshots
                                         │
                                  D3D12 Renderer
```

**Architectural invariant:** Simulation is authoritative. The UI submits commands; the renderer consumes extracted snapshots and cannot mutate gameplay state.

See [Architecture](docs/Architecture.md) and [Project Context](docs/ProjectContext.md).

## Development and Validation

### Versions and pull requests

`master` is the integration branch. Each PR commits exactly one PATCH increment relative to current master in [Directory.Build.props](Directory.Build.props). Additional commits and CI reruns do not increment it again. Prepare the PR version with:

```powershell
pwsh ./build/Increment-PrVersion.ps1 -BaseRef origin/master
```

CI validates the increment and runs with read-only repository permissions. It creates **no automatic tags or releases**. PR/run/attempt and full Git SHA identify validation builds; packages retain checksums and provenance. See [PR Versioning](docs/ReleaseVersioning.md).

### Tests and architecture

After the Release build and runtime asset preparation:

```powershell
pwsh ./build/Validate-ProjectReferences.ps1
dotnet test --solution ForgeLine.sln --configuration Release --no-build
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000
```

The repository selects **Microsoft.Testing.Platform** through `global.json`. The complete CI `build-test` job also covers map/runtime asset qualification, native checks, packaging and a bounded terminal match. Follow [Development](docs/Development.md) for the canonical sequence and opt-in hardware checks; a local subset does not represent the full CI gate.

### Headless simulation and recovery

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 1000 --seed 1 --tick-rate 20
```

A repeatable terminal-match scenario:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --require-terminal
```

Save and replay output:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 5000 --seed 2026 --save-output artifacts/match.save.json --replay-output artifacts/match.replay.json
```

The `validation` profile supports automated qualification; `gameplay` retains player-facing defaults. See [Save, Load, Replay, and Recovery](docs/SaveLoadReplayAndRecovery.md).

### Package a Windows build

```powershell
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible
```

Publishing prepares a complete runtime cache by default and produces a versioned Windows ZIP under `artifacts/prealpha`, with SHA-256 and provenance sidecars. An explicitly supplied `-RuntimeAssetRoot` must pass validation. The package is framework-dependent and requires the .NET 10 x64 runtime. The installed client consumes compiled assets without the source tree or compiler. Application icon and native-window identity follow [Windows application branding](docs/WindowsApplicationBranding.md).

## Repository Structure

```text
src/          Engine, simulation, game, Windows client, headless host
assets/       Editable source and compiled runtime assets
tools/        Editors, asset and map compilation
tests/        Unit, integration and headless tests
benchmarks/   Performance benchmarks
build/        Build, packaging and validation scripts
docs/         Technical documentation and qualification evidence
.github/      CI workflows
```

## Documentation

| Topic | References |
| :--- | :--- |
| **Development and governance** | [Development](docs/Development.md) · [Project Context](docs/ProjectContext.md) · [PR Versioning](docs/ReleaseVersioning.md) · [Architecture](docs/Architecture.md) |
| **Client and controls** | [Windows Client](docs/WindowsClient.md) · [Camera and Input](docs/CameraAndInput.md) · [Interaction Qualification](docs/InteractionQualification.md) |
| **Interface and operations** | [Command Interface Visual Language](docs/CommandInterfaceVisualLanguage.md) · [Group Orders](docs/FormationMovementAndGroupOrders.md) · [Operations](docs/ProductionAndLogisticsOperations.md) · [Alerts and Strategic Map](docs/ActionableAlertsAndStrategicMap.md) |
| **World and runtime** | [Central Divide Battlefield](docs/CentralDivideBattlefield.md) · [Simulation Runtime](docs/SimulationRuntime.md) · [Navigation](docs/HierarchicalNavigation.md) |
| **Economy and supply** | [Industrial Production](docs/IndustrialProduction.md) · [Logistics Setup](docs/LogisticsSetup.md) · [Logistics Routing](docs/LogisticsNetworkAndRouting.md) · [Battlefield Supply](docs/BattlefieldSupply.md) · [Vehicle Docking](docs/VehicleDocking.md) · [Vehicle Queues](docs/VehicleQueues.md) · [Docking Qualification](docs/VehicleDockingQualification.md) |
| **Warfare** | [Combat Execution](docs/CombatExecution.md) · [Battlefield Intelligence](docs/BattlefieldIntelligence.md) · [Skirmish Opponent](docs/SkirmishOpponent.md) |
| **Source assets** | [Asset Pipeline](docs/AssetPipeline.md) · [Directorate Buildings](docs/DirectorateBuildingAssets.md) · [Directorate Units](docs/DirectorateUnitAssets.md) · [Infantry and Battlefield Production](docs/InfantryBattlefieldProduction.md) · [Combat/Support Units](docs/CombatSupportUnitProduction.md) |
| **Graphics and qualification** | [Graphics Settings](docs/GraphicsQualitySettings.md) · [PBR Foundation](docs/PbrFoundationQualification.md) · [Visual Evidence](docs/VisualReferenceEvidence.md) · [Reference Performance](docs/GraphicsReferencePerformance.md) · [Scalability](docs/ScalabilityQualification.md) |
| **Packaging and diagnostics** | [Pre-Alpha Operations](docs/PreAlphaUxAndOperations.md) · [Application Branding](docs/WindowsApplicationBranding.md) · [Startup Timing](docs/StartupDiagnostics.md) · [Asset Diagnostics](docs/AssetStartupDiagnostics.md) |
| **Architecture decisions** | [Match Runtime Composition](docs/adr/MatchRuntimeAndScenarioComposition.md) · [Render Snapshot Ownership](docs/adr/RenderFrameScratchAndSnapshotOwnership.md) · [GPU Resource Safety](docs/adr/GpuResourceRetirementAndFaultShutdown.md) |

---

<div align="center">

**FORGELINE — Build. Supply. Conquer.**

</div>


### Battlefields

New Game offers Central Divide (3,072 m ridge map) and [Split Basin](docs/SplitBasinBattlefield.md) (2,048 m open basin with a longer outer route). Up/Down selects the battlefield. Client and headless accept `--scenario split-basin`; Central Divide remains the default. Both use normal Directorate content, exact save/replay identities and compiled data included in the package.
