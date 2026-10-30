# Documentation

**Start with the guide for your task.** This hub points to current workflows; the [complete catalog](DocumentationIndex.md) lists every Markdown document, including architecture decisions, source instructions, implementation plans and dated evidence.

[Repository overview](../README.md) · [Complete catalog](DocumentationIndex.md) · [Writing conventions](DocumentationGuide.md) · [Master documentation audit](DocumentationStatus.md)

## Choose a path

| I want to… | Read first | Continue with |
| :--- | :--- | :--- |
| **Play or configure a match** | [Windows Client](WindowsClient.md) | [Camera and input](CameraAndInput.md), [graphics settings](GraphicsQualitySettings.md), [match flow](MatchFlowAndPlayerExperience.md) |
| **Build, test or contribute** | [Development](Development.md) | [PR Versioning](ReleaseVersioning.md), [Project Context](ProjectContext.md), [Architecture](Architecture.md) |
| **Choose or generate a battlefield** | [Seeded skirmish selection](SeededSkirmishSelection.md) | [Generated packages](GeneratedBattlefieldMaps.md), [Central Divide](CentralDivideBattlefield.md), [Split Basin](SplitBasinBattlefield.md) |
| **Understand simulation and ownership** | [Simulation Runtime](SimulationRuntime.md) | [ECS](Ecs.md), [Job System](JobSystem.md), [Presentation](PresentationExtractionAndDebugging.md), [save/load/replay](SaveLoadReplayAndRecovery.md) |
| **Understand the economy and supply** | [Industrial Production](IndustrialProduction.md) | [Logistics setup](LogisticsSetup.md), [routing](LogisticsNetworkAndRouting.md), [battlefield supply](BattlefieldSupply.md), [operations](ProductionAndLogisticsOperations.md) |
| **Work on rendering** | [Graphics](Graphics.md) | [Renderer inventory](RendererFeatureInventory.md), [frame composition](RenderPassComposition.md), [reference performance](GraphicsReferencePerformance.md) |
| **Author content** | [Asset Pipeline](AssetPipeline.md) | [Blender round-trip](BlenderAssetRoundTrip.md), [buildings](DirectorateBuildingAssets.md), [units](DirectorateUnitAssets.md), [world assets](WorldAssetPresentation.md) |
| **Capture or review evidence** | [UI review](UiVisualReview.md) | [Windows renderer runbook](WindowsRendererQualification.md), [visual evidence](VisualReferenceEvidence.md), [integrated rendering report](rendering/performance-integration/Qualification.md) |

## Gameplay systems

| Area | Guides |
| :--- | :--- |
| **Battlefield and movement** | [World and terrain](WorldAndTerrain.md), [hierarchical navigation](HierarchicalNavigation.md), [steering](GroundMovementAndSteering.md), [formations](FormationMovementAndGroupOrders.md) |
| **Industry and transport** | [Construction](BuildingPlacementAndConstruction.md), [production](IndustrialProduction.md), [storage](InventoryAndStorage.md), [power](PowerNetworks.md), [conveyors](BaseConveyorLogistics.md), [cargo](CargoTransportOperations.md) |
| **Warfare and intelligence** | [Combat execution](CombatExecution.md), [armor](ArmorAndTargetAcquisition.md), [artillery](ArtilleryAndIndirectFire.md), [intelligence](BattlefieldIntelligence.md), [cover](CoverAndVisualOcclusion.md), [opponent](SkirmishOpponent.md), [difficulty](FairOpponentDifficulty.md) |
| **Commands and interface** | [Action menus](WorldAnchoredActionMenus.md), [HUD consolidation](HudSurfaceConsolidation.md), [interaction/accessibility](GameplayInputAccessibilityAndQualification.md), [Operations](ProductionAndLogisticsOperations.md), [alerts/map](ActionableAlertsAndStrategicMap.md), [strategic capacity](StrategicLogisticsCapacity.md) |
| **Recovery and learning** | [Save/load/replay](SaveLoadReplayAndRecovery.md), [replay cancellation](DeterministicCommandReplay.md), [guided practice](GuidedStrategyPractice.md), [debrief](AfterActionStrategicDebrief.md) |

## Procedural world pipeline

| Stage | Owning guide |
| :--- | :--- |
| **Versioned recipe, integer coordinates and random derivation** | [Deterministic world-generation contracts](DeterministicWorldGeneration.md) |
| **Terrain, climate and strategic geography** | [Procedural terrain](ProceduralTerrain.md), [biome/ecology fields](BiomeAndEcologyFields.md), [strategic geography](ProceduralStrategicGeography.md) |
| **Presentation from copied fields** | [Biome terrain materials](BiomeTerrainMaterials.md), [chunk decoration](ChunkDecorationRendering.md) |
| **Verified package and host identity** | [Generated battlefield maps](GeneratedBattlefieldMaps.md) |
| **Frontend validation, cache, save and restart** | [Seeded skirmish selection](SeededSkirmishSelection.md) |
| **Executed checks and limits** | [Procedural qualification](ProceduralMapQualification.md) |

The contract layer, a generated package and a verified playable selection are distinct scopes. A valid seed/recipe can still fail strategic qualification. Authored maps retain their existing registry and package contracts.

## Rendering: implementation and acceptance

**Merged code, passing structure checks and full performance acceptance are different facts.** Read the exact source, payload, hardware and outstanding gates in each measurement record.

| Read this for… | Document |
| :--- | :--- |
| **Current backend and resource ownership** | [Graphics](Graphics.md) |
| **Copied snapshots and correspondence** | [Presentation extraction](PresentationExtractionAndDebugging.md) |
| **Reproducing native reference timing** | [Graphics reference performance](GraphicsReferencePerformance.md) |
| **The integrated wave-02 measurements** | [Serial integration qualification](rendering/performance-integration/Qualification.md) |
| **Broader live-client/memory/soak acceptance requirements** | [Integration requirements](rendering/qualification/IntegrationRequirements.md) |

The integrated report retains 80 cases with unchanged images and a repeated 10k High Sweep CPU-p95 regression. Its named-source evidence remains useful after merge; it does not automatically qualify a later source or close human art, thermal, complete-memory or live-client soak requirements.

The newer [render-owner telemetry foundation](rendering/render-engine-upgrade/Telemetry.md) provides opt-in live-owner intervals, raw completion records and logical resource bytes. It keeps ordinary live smoke data separate from native reference fixtures; source matching, full physical allocation, calibrated hardware and lifecycle/art acceptance remain independent gates.

## How to read document status

| Kind | Interpretation |
| :--- | :--- |
| **Guide / contract** | Explains the repository workflow or implementation boundary. Dated results embedded in a guide retain their original scope. |
| **Runbook** | Describes how to execute validation; instructions alone do not establish success. |
| **Qualification / evidence** | Records a named source, environment, commands and observed result. Preserve hashes, failures, skips and open gates. |
| **Architecture decision** | Captures a decision and its context; consult current implementation and related guides for subsequent changes. |
| **Implementation plan** | Defines intended work; presence in the repository does not prove implementation or approval. |
| **Historical proposal** | Reference material. [PR Versioning](ReleaseVersioning.md) owns the active FORGELINE policy. |

Use [Project Context](ProjectContext.md#source-precedence) when sources disagree. Product Owner approval and effective branch protection require their own evidence.

## Full documentation catalog

Browse [every Markdown document by topic](DocumentationIndex.md), or use GitHub file search for an exact type, system or command. The catalog includes source/tool READMEs and historical material without moving or renaming existing documents. For maintenance rules, see [Documentation conventions](DocumentationGuide.md).
