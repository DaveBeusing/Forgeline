# FORGELINE

> **Build. Supply. Conquer.**

**FORGELINE** is a large-scale real-time strategy game combining classic battlefield command with industrial production, physical logistics, automation, and intelligence.

**Build the industry. Supply the army. Control the battlefield.**

Unlike conventional RTS resource economies, factories, power networks, cargo transport, ammunition, and fuel are part of the war itself. Disrupting a bridge or supply route can matter as much as winning a direct engagement.

## The Game

The core loop is **Extract â†’ Process â†’ Manufacture â†’ Supply â†’ Fight â†’ Expand**.

- **Build:** Extract finite resources, process Steel/Fuel/Electronics, construct infrastructure, and automate production.
- **Supply:** Move cargo through capacity-constrained networks; keep combat forces fueled, armed, and operational.
- **Conquer:** Use reconnaissance, formations, terrain, artillery, and combined arms to defeat an opponent.

### Current playable slice

The current pre-alpha slice centers on **Central Divide**, a **3.072 Ã— 3.072 km** two-player battlefield with contested resources, a North Bridge, an alternate South Ford, expansion areas, and destructible transport links.

It includes:

| Area | Implemented foundation |
| --- | --- |
| Faction | **Directorate** â€” 13 constructible structures and 7 producible units |
| Economy | Resource extraction, inventory/storage, power allocation, recipes, and production queues |
| Logistics | Graph routing, Cargo/Supply Trucks, automatic distribution, Fuel/Ammunition resupply, and disruption |
| Warfare | Infantry and vehicles, directional armor, suppression, repairs, artillery, and tactical commands |
| Intelligence | Fog of war, radar/visual sensors, detection/identification, and intelligence-limited targeting |
| Opponent | Computer-controlled skirmish opponent using the same construction, economy, supply, and combat rules |
| Match flow | Setup, pause, victory/surrender, results, and fresh-match restart |
| Presentation | Native D3D12 rendering, faction assets, terrain, minimap, strategic overlays, and combat/logistics effects |

The gameplay HUD includes authoritative resources/power, selection details and contextual commands. Its small top-right FPS/SIM readout measures actual presented frames and completed ticks per second independently. See [Windows Client](docs/WindowsClient.md#match-hud) for availability and pause behavior.

The six main unit families and all thirteen industrial building families use compiled Directorate assets, including LODs and gameplay-relevant visual states. Combat Engineer currently shares the Rifle Squad visual family.

**Status:** This is a developing *pre-alpha vertical slice*, not a completed or production-ready game. Advanced tactics, rail gameplay, interactive map editing, other factions, and multiplayer remain future work.

## ForgeLine Engine

FORGELINE runs on **ForgeLine Engine**, a purpose-built C#/.NET RTS engine rather than Unity, Unreal, or a general-purpose engine.

| Technology | Baseline |
| --- | --- |
| Language | **C# 14** |
| Runtime | **.NET 10 LTS**; SDK pinned in `global.json` (currently **10.0.401**) |
| Platform | Windows x64 |
| Graphics | Custom **Direct3D 12** renderer |
| Simulation | Fixed-tick, command-driven, headless-capable; **20 Hz** target |
| Data model | Data-oriented, custom **Entity Component System (ECS)** |
| Concurrency | Persistent-worker job scheduler |
| World | Chunk-based terrain and spatial queries |
| Navigation | Hierarchical ground routing, local steering, shared formation routes |
| Logistics | Graph-based capacity and transport simulation |
| Data/assets | Stable IDs, data-driven definitions, source-to-runtime compilation |
| Persistence | Authoritative save/load and command-based replay |
| Architecture | Strict simulation/presentation separation; future networking considered but deferred |

**Architectural rule:** Simulation state is authoritative. Input enters through validated commands; presentation consumes snapshots and cannot change game state directly. This supports repeatable headless tests, replays, and eventual multiplayer work.

The codebase is organized into engine, simulation, game, presentation, Windows client, headless host, tooling, and tests. See [Architecture](docs/Architecture.md) and [Project Governance](docs/ProjectContext.md).

## Getting Started

**Requirements:** Windows x64, the .NET SDK specified by `global.json`, and a Direct3D 12-capable graphics environment. The Windows client can fall back to WARP for supported development/smoke scenarios.

From the repository root:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release
dotnet test --solution ForgeLine.sln --configuration Release
```

### Play the development client

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

The client opens the Central Divide / Directorate skirmish flow. In a source checkout, it also refreshes compiled runtime assets before loading them. Packaged builds resolve assets beside the executable; `FORGELINE_RUNTIME_ASSETS` overrides asset discovery.

**Main controls**

| Action | Input |
| --- | --- |
| Select / multi-select / box-select | Left click / Shift + left click / left drag |
| Issue movement order | Right click |
| Pause | Space |
| Controls reference | F1 or F12 |
| Build / industrial production / unit production | B / P / U |
| Logistics / supply / combat commands | L / Y / K |
| Strategic overlays | F10 |
| Development metrics | Shift + F1 |
| Restart completed match / end session | R / Escape |

Selected units and buildings use terrain-aligned circular rings. Hover uses a broken ring; foreign ownership adds radial ticks. Left-drag shows a faint filled marquee with a contrasting outline. Brief move/attack/invalid markers acknowledge targeting intent; command results still arrive through the existing HUD.

Additional interaction and diagnostics shortcuts are documented in [Windows Client](docs/WindowsClient.md), [Camera and Input](docs/CameraAndInput.md), and [RTS Information Layer](docs/RTSInformationLayer.md).

## Development and Validation

### Headless simulation

Run simulation without a window, graphics, or audio:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --ticks 1000 --seed 1 --tick-rate 20
```

Run an accelerated, repeatable full-match validation:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --require-terminal
```

The `validation` profile is intended for bounded testing, not gameplay balancing; the `gameplay` profile retains player-facing defaults.

### Architecture and client smoke checks

```powershell
pwsh ./build/Validate-ProjectReferences.ps1
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test --render-stress 1000
```

CI checks architecture, builds, tests, Windows client startup, map artifacts, and bounded terminal skirmish behavior. BenchmarkDotNet hosts cover ECS, navigation, simulation, logistics, combat, and rendering. Performance targets are engineering goals, not guaranteed frame rates or entity counts.

### Save, load, and replay

The headless host supports versioned checkpoints and command-based replay:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 5000 --seed 2026 --save-output artifacts/match.save.json --replay-output artifacts/match.replay.json
```

See [Save, Load, Replay, and Recovery](docs/SaveLoadReplayAndRecovery.md) for validation and recovery behavior.

## Repository

```text
src/          Engine, simulation, game, client, and headless host
assets/       Source assets and compiled runtime assets
tools/        Specialized editors and asset/map compilation
tests/        Unit, integration, and headless tests
benchmarks/   Performance benchmarks
build/        Validation and build-support scripts
docs/         Technical documentation
.github/      CI workflows
```

## Documentation

- [Match runtime and scenario composition](docs/adr/MatchRuntimeAndScenarioComposition.md)

Start with these detailed references rather than using the README as an exhaustive subsystem specification:

- [Development workflow](docs/Development.md) and [Architecture](docs/Architecture.md)
- [Render scratch and snapshot ownership](docs/adr/RenderFrameScratchAndSnapshotOwnership.md)
- [Scalability workloads, budgets and qualification](docs/ScalabilityQualification.md)
- [GPU resource retirement and fault shutdown](docs/adr/GpuResourceRetirementAndFaultShutdown.md)
- [Central Divide scenario](docs/CentralDivideScenario.md) and [Central Divide battlefield](docs/CentralDivideBattlefield.md)
- [Simulation runtime](docs/SimulationRuntime.md) and [Hierarchical navigation](docs/HierarchicalNavigation.md)
- [Industrial production](docs/IndustrialProduction.md), [Logistics routing](docs/LogisticsNetworkAndRouting.md), and [Battlefield supply](docs/BattlefieldSupply.md)
- [Combat execution](docs/CombatExecution.md), [Battlefield intelligence](docs/BattlefieldIntelligence.md), and [Skirmish opponent](docs/SkirmishOpponent.md)
- [Asset pipeline](docs/AssetPipeline.md), [Match flow](docs/MatchFlowAndPlayerExperience.md), and [Pre-alpha operations](docs/PreAlphaUxAndOperations.md)

---

**FORGELINE â€” Build. Supply. Conquer.**
