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
| **Supply** | Establish roads, transport routes, depots, fuel and ammunition distribution, and maintenance capacity. |
| **Conquer** | Reconnoiter, position combined-arms forces, exploit terrain, disrupt supply lines, and advance the frontline. |

**Core gameplay loop**

```text
Extract → Process → Manufacture → Supply → Fight → Expand → Automate → Scale
```

Unlike a conventional RTS economy, **industry is part of the battlefield**. Destroying a bridge, cutting off fuel, or disabling production can decide the outcome of a battle.

## Playable Pre-Alpha

The current development slice centers on **Central Divide**, a **3.072 × 3.072 km** two-player map featuring contested resources, a North Bridge, an alternative South Ford, expansion sites, and destructible transport links.

| System | Present in the development slice |
| :--- | :--- |
| **Directorate** | 13 constructible structures and 7 producible units |
| **Industry** | Extraction, storage, power distribution, recipes, and production queues |
| **Logistics** | Capacity-based graph routing, cargo/supply trucks, automated distribution, fuel and ammunition resupply |
| **Combat** | Infantry, vehicles, directional armor, suppression, repairs, artillery, and tactical orders |
| **Intelligence** | Fog of war, radar and visual sensors, detection/identification, information-limited targeting |
| **Skirmish** | Computer-controlled opponent operating under the same core economy, supply, and combat rules |
| **Match flow** | Setup, pause, victory/surrender, results, and restart |
| **Presentation** | Native Direct3D 12, terrain, Directorate assets, HUD, minimap, strategic overlays, and effects |

The Windows client provides resource and power information, selection-derived contextual commands, and opt-in FPS/simulation-rate readings through Shift + F1. Directorate unit and building families use compiled assets with LOD and gameplay-relevant visual states. The Combat Engineer currently shares the Rifle Squad visual family.

**Development status:** This is a **pre-alpha vertical slice**, not a finished game. Rail gameplay, more advanced tactics, interactive map editing, other factions, and multiplayer remain future work.

See [Central Divide](docs/CentralDivideScenario.md) and the [Windows client guide](docs/WindowsClient.md).

## Getting Started

### Prerequisites

- **Windows x64**
- **.NET SDK 10.0.401**, pinned in [`global.json`](global.json)
- A **Direct3D 12-capable** graphics environment; WARP fallback supports designated development/smoke scenarios
- **PowerShell** for validation scripts

From the repository root:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release
dotnet test --solution ForgeLine.sln --configuration Release
```

The repository selects **Microsoft.Testing.Platform** through `global.json`.

### Launch the client

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

The client starts the Central Divide / Directorate skirmish flow. Source checkouts refresh compiled assets before loading. Packaged builds locate assets beside the executable; `FORGELINE_RUNTIME_ASSETS` overrides asset discovery.

### Essential controls

| Action | Default input |
| :--- | :--- |
| Select / add / box-select | Left click / Shift + left click / left drag |
| Move units | Right click |
| Pause | Space |
| Build / industrial production / unit production | B / P / U |
| Logistics / supply / combat commands | L / Y / K |
| Strategic overlays | F10 |
| Controls help | F1 or F12 |
| Development metrics | Shift + F1 |
| Restart finished match / exit session | R / Escape |

Additional behavior is covered by [Windows Client](docs/WindowsClient.md) and [Camera and Input](docs/CameraAndInput.md).

## ForgeLine Engine

FORGELINE is built on **ForgeLine Engine**, a purpose-built C#/.NET RTS engine—not Unity, Unreal, or a general-purpose engine.

| Area | Baseline |
| :--- | :--- |
| Language / runtime | **C# 14 · .NET 10 LTS** |
| Target | **Windows x64** |
| Renderer | Custom **Direct3D 12** |
| Simulation | Fixed-tick, command-driven, headless-capable; **20 Hz target** |
| Entity architecture | Data-oriented custom ECS |
| Multithreading | Persistent-worker job scheduler |
| World / navigation | Chunk-based world, hierarchical paths, local steering, formation corridors |
| Logistics | Graph-based transport and capacity simulation |
| Content | Stable data identifiers, compiled source-to-runtime assets |
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

### Headless simulation

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --tick-rate 20
```

A repeatable, bounded terminal-match scenario:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --require-terminal
```

The `validation` profile supports automated qualification; `gameplay` retains player-facing defaults.

### Architecture and renderer checks

```powershell
pwsh ./build/Validate-ProjectReferences.ps1
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test --render-stress 1000
```

### Save and replay

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 5000 --seed 2026 --save-output artifacts/match.save.json --replay-output artifacts/match.replay.json
```

See [Save, Load, Replay, and Recovery](docs/SaveLoadReplayAndRecovery.md).

**Note:** Frame-rate, simulation, and entity-scale targets are engineering goals, not performance guarantees.

## Repository Structure

```text
src/          Engine, simulation, game, Windows client, headless host
assets/       Editable source and compiled runtime assets
tools/        Editors, asset and map compilation
tests/        Unit, integration and headless tests
benchmarks/   Performance benchmarks
build/        Validation scripts
docs/         Technical and operational documentation
.github/      CI workflows
```

## Documentation

| Topic | References |
| :--- | :--- |
| **Development** | [Development workflow](docs/Development.md) · [Architecture](docs/Architecture.md) |
| **Client and controls** | [Windows Client](docs/WindowsClient.md) · [Camera and Input](docs/CameraAndInput.md) · [Interaction Qualification](docs/InteractionQualification.md) |
| **World and runtime** | [Central Divide Battlefield](docs/CentralDivideBattlefield.md) · [Simulation Runtime](docs/SimulationRuntime.md) · [Navigation](docs/HierarchicalNavigation.md) |
| **Economy and supply** | [Industrial Production](docs/IndustrialProduction.md) · [Logistics Routing](docs/LogisticsNetworkAndRouting.md) · [Battlefield Supply](docs/BattlefieldSupply.md) |
| **Warfare** | [Combat Execution](docs/CombatExecution.md) · [Battlefield Intelligence](docs/BattlefieldIntelligence.md) · [Skirmish Opponent](docs/SkirmishOpponent.md) |
| **Production and quality** | [Asset Pipeline](docs/AssetPipeline.md) · [Scalability Qualification](docs/ScalabilityQualification.md) · [Pre-Alpha Operations](docs/PreAlphaUxAndOperations.md) |
| **Architecture decisions** | [Match Runtime Composition](docs/adr/MatchRuntimeAndScenarioComposition.md) · [Render Snapshot Ownership](docs/adr/RenderFrameScratchAndSnapshotOwnership.md) · [GPU Resource Safety](docs/adr/GpuResourceRetirementAndFaultShutdown.md) |

---

<div align="center">

**FORGELINE — Build. Supply. Conquer.**

</div>
