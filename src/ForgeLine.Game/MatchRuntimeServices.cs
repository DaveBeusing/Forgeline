using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class MatchRuntimeServices
{
    internal MatchRuntimeServices(
        ResourceCatalog resources,
        BuildingDefinitionCatalog buildingDefinitions,
        UnitDefinitionCatalog unitDefinitions,
        TechnologyDefinitionCatalog technologyDefinitions,
        ProductionRecipeCatalog productionRecipes,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons,
        CombatRuntime combatRuntime,
        SpatialGridIndex spatialIndex,
        BuildingPlacementService buildingPlacement,
        BuildingCommandProcessingSystem buildingCommands,
        BuildingConstructionSystem buildingConstruction,
        BuildingDemolitionSystem demolition,
        UnitProductionSystem unitProduction,
        TechnologyResearchSystem technologyResearch,
        GroundMovementSystem groundMovement,
        FormationMovementSystem formationMovement,
        HierarchicalNavigationSystem navigation,
        BattlefieldIntelligenceSystem battlefieldIntelligence,
        TargetAcquisitionSystem targetAcquisition,
        TacticalCombatSystem tacticalCombat,
        SuppressionSystem suppression,
        AutomaticResupplyDecisionSystem automaticResupply,
        RepairRecoverySystem repairRecovery,
        CombatDebugSnapshotSystem combatDebugSnapshots,
        IReadOnlyList<Type> registeredSystemTypes)
    {
        Resources = resources;
        BuildingDefinitions = buildingDefinitions;
        UnitDefinitions = unitDefinitions;
        TechnologyDefinitions =
            technologyDefinitions ??
            throw new ArgumentNullException(nameof(technologyDefinitions));
        ProductionRecipes = productionRecipes;
        Weapons = weapons;
        ArtilleryWeapons = artilleryWeapons;
        CombatRuntime =
            combatRuntime ??
            throw new ArgumentNullException(nameof(combatRuntime));
        SpatialIndex = spatialIndex;
        BuildingPlacement = buildingPlacement;
        BuildingCommands = buildingCommands;
        BuildingConstruction = buildingConstruction;
        Demolition = demolition;
        UnitProduction = unitProduction;
        TechnologyResearch =
            technologyResearch ??
            throw new ArgumentNullException(nameof(technologyResearch));
        GroundMovement = groundMovement;
        FormationMovement = formationMovement;
        Navigation = navigation;
        BattlefieldIntelligence = battlefieldIntelligence;
        TargetAcquisition = targetAcquisition;
        TacticalCombat = tacticalCombat;
        Suppression =
            suppression ??
            throw new ArgumentNullException(nameof(suppression));
        AutomaticResupply = automaticResupply;
        RepairRecovery =
            repairRecovery ??
            throw new ArgumentNullException(nameof(repairRecovery));
        CombatDebugSnapshots = combatDebugSnapshots;
        RegisteredSystemTypes = registeredSystemTypes;
    }

    public IDeveloperActionDispatcher DeveloperActions { get; internal set; } = MissingDeveloperProvider.Instance;
    public IDeveloperParameterProvider DeveloperParameters { get; internal set; } = MissingDeveloperProvider.Instance;
    public IDeveloperMenuCatalog DeveloperCatalog { get; internal set; } = MissingDeveloperProvider.Instance;

    public ResourceCatalog Resources { get; }

    public BuildingDefinitionCatalog BuildingDefinitions { get; }

    public UnitDefinitionCatalog UnitDefinitions { get; }

    public TechnologyDefinitionCatalog TechnologyDefinitions { get; }

    public ProductionRecipeCatalog ProductionRecipes { get; }

    public WeaponCatalog Weapons { get; }

    public ArtilleryWeaponCatalog ArtilleryWeapons { get; }

    public CombatRuntime CombatRuntime { get; }

    public SpatialGridIndex SpatialIndex { get; }

    public StrategicInfrastructureSystem StrategicInfrastructure { get; internal set; } = null!;

    public ConveyorSimulationSystem ConveyorFlow { get; internal set; } = null!;

    public ConveyorConstructionService Conveyors { get; internal set; } = null!;

    public BuildingPlacementService BuildingPlacement { get; }

    public BuildingCommandProcessingSystem BuildingCommands { get; }

    public BuildingConstructionSystem BuildingConstruction { get; }

    public BuildingDemolitionSystem Demolition { get; }

    public UnitProductionSystem UnitProduction { get; }

    public TechnologyResearchSystem TechnologyResearch { get; }

    public GroundMovementSystem GroundMovement { get; }

    public FormationMovementSystem FormationMovement { get; }

    public HierarchicalNavigationSystem Navigation { get; }

    public BattlefieldIntelligenceSystem BattlefieldIntelligence { get; }

    public TargetAcquisitionSystem TargetAcquisition { get; }

    public TacticalCombatSystem TacticalCombat { get; }

    public SuppressionSystem Suppression { get; }

    public AutomaticResupplyDecisionSystem AutomaticResupply { get; }

    public RepairRecoverySystem RepairRecovery { get; }

    public CombatDebugSnapshotSystem CombatDebugSnapshots { get; }

    public IReadOnlyList<Type> RegisteredSystemTypes { get; internal set; }
}
