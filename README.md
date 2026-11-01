# FORGELINE

<div align="center">

<a id="build-supply-conquer"></a>

**BUILD. SUPPLY. CONQUER.**

**Large-scale RTS · Industrial production · Physical logistics · Combined-arms warfare**

*Build the industrial machine that sustains the army that wins the war.*

[Gameplay](#gameplay) · [Battlefields](#battlefields) · [Quick start](#quick-start) · [Engine](#forgeline-engine) · [Documentation](docs/README.md)

</div>

---

<a id="overview"></a>

**FORGELINE** combines direct battlefield command with industry, supply chains, intelligence and automation. Build factories, move real resources, sustain combat units and disrupt the infrastructure supporting your opponent.

> Preparation matters more than click speed. Logistics creates strategic opportunities, not repetitive chores.

## Gameplay

| Pillar | What you do |
| :--- | :--- |
| **Build** | Extract finite resources, refine materials, power factories and manufacture units. |
| **Supply** | Establish transport routes, storage, loading queues, fuel and ammunition distribution. |
| **Conquer** | Reconnoiter, command combined-arms forces, exploit terrain and cut enemy supply lines. |

```text
Extract → Process → Manufacture → Supply → Fight → Expand → Automate → Scale
```

### Playable pre-alpha

| System | Implemented development slice |
| :--- | :--- |
| **Directorate** | 13 constructible structures and 7 producible units. |
| **Industry and logistics** | Production queues, power, capacity-based routing, physical cargo, automated distribution and resupply. Refineries temporarily accept Volatiles and export processed Fuel through compatible elevated local conveyors; regional trucks remain supported and pipelines are not yet implemented. |
| **Combat and intelligence** | Infantry, vehicles, directional armor, suppression, repairs, artillery, fog of war and faction-scoped sensors. |
| **Skirmish and recovery** | A computer opponent using the normal economy/supply/combat rules; setup, pause, victory, surrender, save/load/replay and restart. |
| **Presentation** | Native Direct3D 12, compiled assets and physical LODs, terrain, contextual HUD, minimap, overlays, effects and bounded spatial audio. |

> [!NOTE]
> This is a **pre-alpha vertical slice**. Rail gameplay, an interactive map editor, additional factions and multiplayer remain future work. Measured workloads and hardware results do not establish a general frame-rate or entity-scale guarantee.

Directorate infantry and vehicles have dedicated visual kits and damage states. A four-member squad visual remains one simulation entity. Human art review and sustained performance acceptance remain separate from implementation and structural checks. See [infantry production](docs/InfantryBattlefieldProduction.md), [combat/support production](docs/CombatSupportUnitProduction.md) and [spatial audio](docs/SpatialWorldAudio.md).

## Battlefields

| Selection | Size / scope | Guide |
| :--- | :--- | :--- |
| **Central Divide** — default authored map | 3.072 × 3.072 km; ridge, North Bridge, South Ford and contested expansion routes. | [Scenario](docs/CentralDivideScenario.md) |
| **Split Basin** — alternate authored map | 2.048 × 2.048 km; open basin and a longer outer route. | [Battlefield](docs/SplitBasinBattlefield.md) |
| **Generated Map** — explicit New Game choice | Two players; 2.048, 3.072 or 4.096 km square; a full unsigned 64-bit seed and supported temperate presets. | [Player flow](docs/SeededSkirmishSelection.md) · [Package contracts](docs/GeneratedBattlefieldMaps.md) |

Generated maps must pass **Validate Map** before a separate **Start** action. A rejected recipe stays editable; the client does not silently choose a replacement seed. Generated saves require their exact verified package in the generated-map cache. Authored maps continue using the registered compiled-map allowlist.

<a id="getting-started"></a>

## Quick start

### Requirements

- **Windows x64** for the interactive client.
- **.NET SDK 10.0.402**, selected exactly by [global.json](global.json); **C# 14**.
- **PowerShell 7** (`pwsh`) for repository scripts.
- A **Direct3D 12** graphics environment. WARP is available for designated functional checks; it does not qualify physical-GPU performance.

Blender is needed when authoring or re-exporting Blender sources, not for an ordinary build using the checked-in source assets.

### Build and launch

Run from the repository root:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./build/Start-DevelopmentClient.ps1
```

The launcher builds the client, compiles and qualifies runtime assets, and stops on failure. Reports go under `artifacts/`. The running client consumes compiled assets and never starts the compiler. Packaged builds use adjacent runtime assets without requiring authoring sources.

`FORGELINE_RUNTIME_ASSETS` selects an authoritative runtime root; an invalid override is reported. See [asset build modes](docs/AssetPipeline.md#build-modes-and-deployment-integrity), [asset recovery](docs/RuntimeVisualAssetCompatibility.md) and the [Windows client guide](docs/WindowsClient.md).

### Essential controls

| Action | Default input |
| :--- | :--- |
| Select / add / box-select | Left click / Shift + left click / left drag |
| Move units | Right click |
| Pause | Space |
| Build / industrial production / unit production | B / P / U |
| Logistics / supply / combat / technology | L / Y / K / H |
| Strategic overlays | F10 |
| Controls help | Escape → Controls |
| Statistics overlay selector (nonmodal) | F1 |
| Full developer tools — Developer build only | Escape → Developer Tools |
| Restart a finished match / exit session | R / Escape |

Settings exposes rebindable gameplay actions with conflict checks. Owned selections use single/group action rings; Operations and the strategic map provide authorized diagnosis and focus actions. See [controls](docs/CameraAndInput.md), [HUD consolidation](docs/HudSurfaceConsolidation.md), [input accessibility](docs/GameplayInputAccessibilityAndQualification.md) and [operations](docs/ProductionAndLogisticsOperations.md).

### Graphics and diagnostics

**Medium** is the default graphics profile. Low, Medium, High and Ultra apply at the next match boundary; independent overrides preserve full-resolution HUD and selection indicators. Display changes use a 15-second confirmation and rollback. See [graphics settings](docs/GraphicsQualitySettings.md).

Startup checkpoints are written under `%LOCALAPPDATA%/FORGELINE/diagnostics`. See [startup diagnostics](docs/StartupDiagnostics.md) and [asset diagnostics](docs/AssetStartupDiagnostics.md).

## ForgeLine Engine

FORGELINE uses a purpose-built **C#/.NET RTS engine**.

| Area | Current architecture |
| :--- | :--- |
| **Runtime** | C# 14 / .NET 10; Windows x64 interactive host. |
| **Simulation** | Fixed tick, validated commands, custom data-oriented ECS and a persistent-worker job scheduler; 20 Hz target. |
| **World and logistics** | Chunk-based world, hierarchical navigation, formations and graph-based capacity simulation. |
| **Rendering** | Custom Direct3D 12, instanced submission, initialized static geometry and copied presentation snapshots. |
| **Content and recovery** | Stable identifiers, editable sources, compiled runtime assets, versioned save/load and replay. |

```mermaid
flowchart LR
    Input[Player input] --> Commands[Validated commands]
    Commands --> Simulation[Fixed-tick simulation]
    Simulation --> Snapshots[Copied presentation snapshots]
    Snapshots --> Renderer[D3D12 renderer]
```

**Simulation is authoritative.** UI submits commands; rendering consumes extracted snapshots. Headless simulation remains independent of the interactive graphics/platform stack. Read [Architecture](docs/Architecture.md), [Presentation](docs/PresentationExtractionAndDebugging.md) and [Project Context](docs/ProjectContext.md).

The current [render-owner telemetry foundation](docs/rendering/render-engine-upgrade/Telemetry.md) adds an explicit bounded live stream, raw completed GPU timestamps and disjoint logical resource-byte observations. It is diagnostic instrumentation: matched fixture/live acceptance, complete physical allocations, controlled hardware and human review remain open.

### Rendering qualification status

Instancing, default-memory geometry, prepared correspondence and native benchmarking are existing capabilities. The [integrated qualification report](docs/rendering/performance-integration/Qualification.md) retains 80 source-bound cases, 40,960 CPU frames, 40,742 completed-GPU observations and unchanged images. **Performance acceptance remains open**, including the repeated 10k High Sweep CPU-p95 regression. This is evidence for the named sources and workload, not approval of every later master build.

## Development and validation

Use [Development](docs/Development.md) for the complete canonical sequence. A useful local entry point after the Release build and runtime preparation is:

```powershell
pwsh ./build/Validate-ProjectReferences.ps1
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
```

CI’s required `build-test` also includes map/runtime qualification, native and headless checks, reproducible packaging and the natural terminal-match gate. A local subset does not replace the complete job.

<details>
<summary><strong>Headless, recovery and packaging commands</strong></summary>

```powershell
# Lightweight headless run
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --ticks 1000 --seed 1 --tick-rate 20

# Natural terminal match — validation profile, not gameplay balance
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --require-terminal

# Reproducible local Windows package
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible
```

Packages are framework-dependent Windows ZIPs with checksums and provenance; they require the .NET 10 x64 runtime. See [save/load/replay](docs/SaveLoadReplayAndRecovery.md), [generated headless maps](docs/GeneratedBattlefieldMaps.md#compiler-and-headless-invocation) and [packaging](docs/PreAlphaUxAndOperations.md).

</details>

### Pull requests and versions

Start from current `master`, use a short-lived branch, and commit **one PATCH increment per PR**:

```powershell
pwsh ./build/Increment-PrVersion.ps1 -BaseRef origin/master
```

Additional commits and CI reruns do not increment again. CI has read-only repository permissions and creates no automatic tags or releases. [PR Versioning](docs/ReleaseVersioning.md) defines the current policy; the generic [Unified Versioning Standard](docs/UnifiedVersioningStandard.md) is historical.

## Repository structure

```text
src/          Engine, game, Windows client and headless host
assets/       Editable source and compiled runtime assets
tools/        Authoring, asset and map compilation tools
tests/        Unit, integration, native and headless tests
benchmarks/   ECS, navigation, simulation, logistics and rendering
build/        Build, packaging and qualification scripts
docs/         Guides, architecture decisions and source-bound evidence
.github/      CI workflows
```

## Documentation

| I want to… | Start here |
| :--- | :--- |
| **Play or configure the slice** | [Windows Client](docs/WindowsClient.md) · [Battlefields](#battlefields) · [Graphics settings](docs/GraphicsQualitySettings.md) |
| **Build, test or contribute** | [Development](docs/Development.md) · [PR Versioning](docs/ReleaseVersioning.md) |
| **Understand the engine** | [Architecture](docs/Architecture.md) · [Simulation](docs/SimulationRuntime.md) · [Presentation](docs/PresentationExtractionAndDebugging.md) |
| **Author assets or maps** | [Asset Pipeline](docs/AssetPipeline.md) · [Generated maps](docs/GeneratedBattlefieldMaps.md) · [Blender round-trip](docs/BlenderAssetRoundTrip.md) |
| **Inspect measurements and acceptance limits** | [Graphics reference performance](docs/GraphicsReferencePerformance.md) · [Integrated rendering qualification](docs/rendering/performance-integration/Qualification.md) |
| **Find any document** | [Documentation hub](docs/README.md) · [Complete catalog](docs/DocumentationIndex.md) · [Documentation conventions](docs/DocumentationGuide.md) |

---

**FORGELINE — Build. Supply. Conquer.**
