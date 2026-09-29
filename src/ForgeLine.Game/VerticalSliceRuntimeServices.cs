using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class VerticalSliceRuntimeServices
{
    internal VerticalSliceRuntimeServices(
        BuildingDefinitionCatalog buildingDefinitions,
        UnitDefinitionCatalog unitDefinitions,
        SpatialGridIndex spatialIndex,
        BuildingPlacementService buildingPlacement,
        BuildingCommandProcessingSystem buildingCommands,
        GroundMovementSystem groundMovement,
        FormationMovementSystem formationMovement,
        HierarchicalNavigationSystem navigation,
        BattlefieldIntelligenceSystem battlefieldIntelligence,
        TargetAcquisitionSystem targetAcquisition,
        TacticalCombatSystem tacticalCombat,
        AutomaticResupplyDecisionSystem automaticResupply,
        CombatDebugSnapshotSystem combatDebugSnapshots,
        IReadOnlyList<Type> registeredSystemTypes)
    {
        BuildingDefinitions = buildingDefinitions;
        UnitDefinitions = unitDefinitions;
        SpatialIndex = spatialIndex;
        BuildingPlacement = buildingPlacement;
        BuildingCommands = buildingCommands;
        GroundMovement = groundMovement;
        FormationMovement = formationMovement;
        Navigation = navigation;
        BattlefieldIntelligence = battlefieldIntelligence;
        TargetAcquisition = targetAcquisition;
        TacticalCombat = tacticalCombat;
        AutomaticResupply = automaticResupply;
        CombatDebugSnapshots = combatDebugSnapshots;
        RegisteredSystemTypes = registeredSystemTypes;
    }

    public BuildingDefinitionCatalog BuildingDefinitions { get; }

    public UnitDefinitionCatalog UnitDefinitions { get; }

    public SpatialGridIndex SpatialIndex { get; }

    public BuildingPlacementService BuildingPlacement { get; }

    public BuildingCommandProcessingSystem BuildingCommands { get; }

    public GroundMovementSystem GroundMovement { get; }

    public FormationMovementSystem FormationMovement { get; }

    public HierarchicalNavigationSystem Navigation { get; }

    public BattlefieldIntelligenceSystem BattlefieldIntelligence { get; }

    public TargetAcquisitionSystem TargetAcquisition { get; }

    public TacticalCombatSystem TacticalCombat { get; }

    public AutomaticResupplyDecisionSystem AutomaticResupply { get; }

    public CombatDebugSnapshotSystem CombatDebugSnapshots { get; }

    public IReadOnlyList<Type> RegisteredSystemTypes { get; }
}
