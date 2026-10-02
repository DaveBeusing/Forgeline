using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class VerticalSliceRuntimeServices
{
    internal VerticalSliceRuntimeServices(
        ResourceCatalog resources,
        BuildingDefinitionCatalog buildingDefinitions,
        UnitDefinitionCatalog unitDefinitions,
        ProductionRecipeCatalog productionRecipes,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog artilleryWeapons,
        CombatRuntime combatRuntime,
        SpatialGridIndex spatialIndex,
        BuildingPlacementService buildingPlacement,
        BuildingCommandProcessingSystem buildingCommands,
        BuildingConstructionSystem buildingConstruction,
        UnitProductionSystem unitProduction,
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
        UnitProduction = unitProduction;
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

    public ResourceCatalog Resources { get; }

    public BuildingDefinitionCatalog BuildingDefinitions { get; }

    public UnitDefinitionCatalog UnitDefinitions { get; }

    public ProductionRecipeCatalog ProductionRecipes { get; }

    public WeaponCatalog Weapons { get; }

    public ArtilleryWeaponCatalog ArtilleryWeapons { get; }

    public CombatRuntime CombatRuntime { get; }

    public SpatialGridIndex SpatialIndex { get; }

    public BuildingPlacementService BuildingPlacement { get; }

    public BuildingCommandProcessingSystem BuildingCommands { get; }

    public BuildingConstructionSystem BuildingConstruction { get; }

    public UnitProductionSystem UnitProduction { get; }

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

    public IReadOnlyList<Type> RegisteredSystemTypes { get; }
}
