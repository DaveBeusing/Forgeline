using System.Collections.ObjectModel;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct PlayerActionResourceAmount(
    ResourceId ResourceId,
    string DisplayName,
    double RequiredQuantity,
    double AvailableQuantity)
{
    public bool IsAvailable =>
        AvailableQuantity >= RequiredQuantity;
}

public sealed class PlayerConstructionActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _costs;

    public PlayerConstructionActionReadModel(
        BuildingId buildingId,
        string displayName,
        IReadOnlyList<PlayerActionResourceAmount> costs,
        bool requiresResourceDeposit)
    {
        BuildingId = buildingId;
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        _costs =
            Array.AsReadOnly(
                costs?.ToArray() ??
                throw new ArgumentNullException(nameof(costs)));
        RequiresResourceDeposit = requiresResourceDeposit;
        // Placement tools must stay available for demolition and repair even
        // when a player cannot currently afford a new route.
        HasRequiredResources = buildingId.Value>=ConveyorGeometry.MenuBase || _costs.All(static cost => cost.IsAvailable);
    }

    public BuildingId BuildingId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs => _costs;

    public bool RequiresResourceDeposit { get; }

    public bool HasRequiredResources { get; }
}

public sealed class PlayerProductionRecipeActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _inputs;
    private readonly IReadOnlyList<PlayerActionResourceAmount> _outputs;

    public PlayerProductionRecipeActionReadModel(
        RecipeId recipeId,
        string displayName,
        IReadOnlyList<PlayerActionResourceAmount> inputs,
        IReadOnlyList<PlayerActionResourceAmount> outputs)
    {
        RecipeId = recipeId;
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        _inputs =
            Array.AsReadOnly(
                inputs?.ToArray() ??
                throw new ArgumentNullException(nameof(inputs)));
        _outputs =
            Array.AsReadOnly(
                outputs?.ToArray() ??
                throw new ArgumentNullException(nameof(outputs)));
        HasInputs = _inputs.All(static input => input.IsAvailable);
    }

    public RecipeId RecipeId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Inputs => _inputs;

    public IReadOnlyList<PlayerActionResourceAmount> Outputs => _outputs;

    public bool HasInputs { get; }
}

public readonly record struct PlayerProductionRequestReadModel(
    EntityId RequestEntity,
    RecipeId RecipeId,
    string DisplayName,
    ProductionPriority Priority,
    ProductionRequestMode Mode,
    bool Paused,
    bool IsActive)
{
    public ResourceId DesiredStockResource { get; init; }
    public double DesiredStockQuantity { get; init; }
}

public sealed class PlayerProductionFacilityActionReadModel
{
    private readonly IReadOnlyList<PlayerProductionRecipeActionReadModel> _recipes;
    private readonly IReadOnlyList<PlayerProductionRequestReadModel> _requests;

    public PlayerProductionFacilityActionReadModel(
        EntityId entity,
        EntityId activeRequest,
        RecipeId activeRecipe,
        ProductionStatus status,
        ProductionBlockReason blockReason,
        double progress,
        IReadOnlyList<PlayerProductionRecipeActionReadModel> recipes,
        IReadOnlyList<PlayerProductionRequestReadModel> requests,
        bool isAutomatic = false)
    {
        Entity = entity;
        ActiveRequest = activeRequest;
        ActiveRecipe = activeRecipe;
        IsAutomatic = isAutomatic;
        Status = status;
        BlockReason = blockReason;
        Progress = progress;
        _recipes =
            Array.AsReadOnly(
                recipes?.ToArray() ??
                throw new ArgumentNullException(nameof(recipes)));
        _requests =
            Array.AsReadOnly(
                requests?.ToArray() ??
                throw new ArgumentNullException(nameof(requests)));
    }

    public EntityId Entity { get; }

    public EntityId ActiveRequest { get; }

    public RecipeId ActiveRecipe { get; }

    public bool IsAutomatic { get; }

    public ProductionStatus Status { get; }

    public ProductionBlockReason BlockReason { get; }

    public double Progress { get; }

    public IReadOnlyList<PlayerProductionRecipeActionReadModel> Recipes =>
        _recipes;

    public IReadOnlyList<PlayerProductionRecipeActionReadModel> QueueRecipes =>
        IsAutomatic ? Array.Empty<PlayerProductionRecipeActionReadModel>() : _recipes;

    public IReadOnlyList<PlayerProductionRequestReadModel> Requests =>
        _requests;
}

public sealed class PlayerUnitProductionActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _costs;

    public PlayerUnitProductionActionReadModel(
        UnitId unitId,
        string displayName,
        IReadOnlyList<PlayerActionResourceAmount> costs,
        uint productionTicks,
        bool technologyUnlocked = true,
        TechnologyCapabilityId requiredTechnologyCapability = default)
    {
        UnitId = unitId;
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        _costs =
            Array.AsReadOnly(
                costs?.ToArray() ??
                throw new ArgumentNullException(nameof(costs)));
        ProductionTicks = productionTicks;
        TechnologyUnlocked = technologyUnlocked;
        RequiredTechnologyCapability =
            requiredTechnologyCapability;
        HasInputs = _costs.All(static cost => cost.IsAvailable);
    }

    public UnitId UnitId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs => _costs;

    public uint ProductionTicks { get; }

    public bool TechnologyUnlocked { get; }

    public TechnologyCapabilityId RequiredTechnologyCapability { get; }

    public bool HasInputs { get; }
}

public readonly record struct PlayerUnitProductionRequestReadModel(
    EntityId RequestEntity,
    UnitId UnitId,
    string DisplayName,
    ProductionPriority Priority,
    bool Paused,
    bool IsActive);

public sealed class PlayerUnitProductionFacilityActionReadModel
{
    private readonly IReadOnlyList<PlayerUnitProductionActionReadModel> _units;
    private readonly IReadOnlyList<PlayerUnitProductionRequestReadModel> _requests;

    public PlayerUnitProductionFacilityActionReadModel(
        EntityId entity,
        EntityId activeRequest,
        UnitId activeUnit,
        UnitProductionStatus status,
        UnitProductionBlockReason blockReason,
        double progress,
        IReadOnlyList<PlayerUnitProductionActionReadModel> units,
        IReadOnlyList<PlayerUnitProductionRequestReadModel> requests,
        System.Numerics.Vector3? rallyPoint = null)
    {
        Entity = entity;
        ActiveRequest = activeRequest;
        ActiveUnit = activeUnit;
        Status = status;
        BlockReason = blockReason;
        Progress = progress;
        RallyPoint = rallyPoint;
        _units =
            Array.AsReadOnly(
                units?.ToArray() ??
                throw new ArgumentNullException(nameof(units)));
        _requests =
            Array.AsReadOnly(
                requests?.ToArray() ??
                throw new ArgumentNullException(nameof(requests)));
    }

    public EntityId Entity { get; }

    public EntityId ActiveRequest { get; }

    public UnitId ActiveUnit { get; }

    public UnitProductionStatus Status { get; }

    public UnitProductionBlockReason BlockReason { get; }

    public double Progress { get; }

    public System.Numerics.Vector3? RallyPoint { get; }

    public IReadOnlyList<PlayerUnitProductionActionReadModel> Units =>
        _units;

    public IReadOnlyList<PlayerUnitProductionRequestReadModel> Requests =>
        _requests;
}

public enum PlayerDistributionActionState : byte
{
    None = 0,
    Pending = 1,
    Assigned = 2,
    Loading = 3,
    Traveling = 4,
    Unloading = 5,
    Waiting = 6,
    Failed = 7
}

public readonly record struct PlayerStockPolicyActionReadModel(
    EntityId PolicyEntity,
    ResourceId ResourceId,
    string DisplayName,
    double CurrentQuantity,
    double DesiredMinimum,
    double DesiredTarget,
    double DesiredMaximum,
    LogisticsStockPriority Priority,
    bool Enabled,
    PlayerDistributionActionState DistributionState,
    LogisticsTransportRequestFailureReason FailureReason,
    LogisticsBottleneckReason BottleneckReason,
    EntityId AssignedTruck)
{
    public bool HasPolicy => PolicyEntity.IsValid;
    public LogisticsStockPriority DispatchPriority { get; init; } = Priority;
    public double ReservedQuantity { get; init; }
    public double AvailableQuantity => Math.Max(0, CurrentQuantity - ReservedQuantity);
    public double AnnouncedQuantity { get; init; }
    public double RequestedQuantity { get; init; }
    public EntityId Source { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public PlayerWorkSummary Service { get; init; }

}

public readonly record struct PlayerCargoStatusReadModel(
    EntityId Entity,
    CargoTransportLifecycleState Lifecycle,
    CargoTransportWaitReason WaitReason,
    CargoTransportFailureReason FailureReason,
    double CargoQuantity,
    double Capacity)
{
    public EntityId Source { get; init; }
    public EntityId Destination { get; init; }
    public ResourceId Resource { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string DestinationName { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public double RequestedQuantity { get; init; }
}

public readonly record struct PlayerCargoFleet(int Total, int Working, int Bound, int EmptyIdle, int NoDriveFuel);

/// <summary>Authorized copied distribution observation, never a dispatch command.</summary>
public readonly record struct PlayerLogisticsOrderReadModel(
    LogisticsTransportRequestId RequestId,
    ResourceId ResourceId,
    string ResourceName,
    LogisticsTransportRequestState State,
    LogisticsTransportRequestFailureReason FailureReason,
    LogisticsBottleneckReason BottleneckReason,
    double RequestedQuantity,
    double LoadedQuantity,
    EntityId AssignedTruck,
    EntityId Origin,
    EntityId Destination,
    string OriginName,
    string DestinationName);

public sealed class PlayerLogisticsActionReadModel
{
    public const int MaximumVisibleOrders = 128;
    private readonly IReadOnlyList<PlayerLogisticsOrderReadModel> _orders;
    private readonly IReadOnlyList<PlayerStockPolicyActionReadModel> _policies;

    public PlayerLogisticsActionReadModel(
        EntityId entity,
        IReadOnlyList<PlayerStockPolicyActionReadModel> policies,
        PlayerCargoStatusReadModel? cargo,
        double inventoryCapacity = 0,
        double inventoryQuantity = 0,
        PlayerWorkSummary service = default,
        IReadOnlyList<PlayerProductionRequirement>? requirements = null,
        IReadOnlyList<PlayerLogisticsOrderReadModel>? orders = null)
    {
        Entity = entity;
        _policies =
            Array.AsReadOnly(
                policies?.ToArray() ??
                throw new ArgumentNullException(nameof(policies)));
        Cargo = cargo;
        InventoryCapacity = inventoryCapacity;
        InventoryQuantity = inventoryQuantity;
        Service = service;
        Requirements = Array.AsReadOnly(requirements?.ToArray() ?? []);
        _orders = Array.AsReadOnly(orders?.Take(MaximumVisibleOrders).ToArray() ?? []);
    }

    public EntityId Entity { get; }

    public IReadOnlyList<PlayerStockPolicyActionReadModel> Policies =>
        _policies;

    public PlayerCargoStatusReadModel? Cargo { get; }
    public double InventoryCapacity { get; }
    public double InventoryQuantity { get; }
    public PlayerWorkSummary Service { get; }
    public IReadOnlyList<PlayerProductionRequirement> Requirements { get; }
    public IReadOnlyList<PlayerLogisticsOrderReadModel> Orders => _orders;
    public int OrderStartIndex => Policies.Count + Requirements.Count;
    public int ItemCount => OrderStartIndex + Orders.Count;
    public int TotalOrderCount { get; init; }
    public bool IsCommandCore { get; init; }
    public bool HasEnabledGraphNode { get; init; }
    public PlayerCargoFleet CargoFleet { get; init; }
    public CargoConvoyReadModel? SelectedConvoy { get; init; }
}

public enum PlayerSupplyProviderState : byte
{
    None = 0,
    Available = 1,
    Assigned = 2,
    Traveling = 3,
    Transferring = 4,
    Empty = 5,
    Blocked = 6
}

public readonly record struct PlayerSupplyActionReadModel(
    EntityId Entity,
    BattlefieldSupplyStatus Status,
    double FuelFraction,
    double AmmunitionFraction,
    bool AutomaticEnabled,
    double AutomaticFuelThreshold,
    double AutomaticAmmunitionThreshold,
    EntityId Provider,
    PlayerSupplyProviderState ProviderState,
    double ProviderFuel,
    double ProviderAmmunition,
    ResupplyProviderRejection ProviderRejections,
    BattlefieldSupplyPriority Priority = BattlefieldSupplyPriority.Normal)
{
    public PlayerWorkSummary Service { get; init; }
    public bool IsSupplyTruck { get; init; }
    public bool IsStaticProvider { get; init; }
    public bool HasLocalProviderInventory { get; init; }
    public double LocalProviderFuel { get; init; }
    public double LocalProviderAmmunition { get; init; }
    public double CargoFuel { get; init; }
    public double CargoAmmunition { get; init; }
    public double DriveFuelQuantity { get; init; }
    public double DriveFuelCapacity { get; init; }
}

public readonly record struct PlayerTacticalTargetReadModel(
    EntityId Entity,
    IntelligenceContactKey ContactKey,
    System.Numerics.Vector3 LastKnownPosition,
    IntelligenceState State,
    SimulationTick LastSeenTick,
    int CompatibleUnitCount);

public readonly record struct PlayerArtilleryActionReadModel(
    EntityId Entity,
    double AmmunitionQuantity,
    double AmmunitionCapacity,
    float MinimumRangeMeters,
    float MaximumRangeMeters,
    FireMissionStatus MissionStatus,
    int RoundsFired,
    int RequestedRounds)
{
    public bool HasActiveMission =>
        MissionStatus is not
            FireMissionStatus.Complete and not
            FireMissionStatus.Cancelled;
}

public sealed class PlayerTacticalActionReadModel
{
    private readonly IReadOnlyList<EntityId> _selectedEntities;
    private readonly IReadOnlyList<PlayerTacticalTargetReadModel> _targets;
    private readonly IReadOnlyList<PlayerArtilleryActionReadModel> _artillery;

    public PlayerTacticalActionReadModel(
        IReadOnlyList<EntityId> selectedEntities,
        int requestedSelectionCount,
        int combatEligibleCount,
        int rejectedSelectionCount,
        int criticalSupplyCount,
        int resupplyingCount,
        bool hasCommonOrder,
        bool mixedOrderState,
        CombatOrderKind currentOrder,
        CombatOrderStatus currentStatus,
        IReadOnlyList<PlayerTacticalTargetReadModel> targets,
        IReadOnlyList<PlayerArtilleryActionReadModel> artillery,
        int suppressedCount = 0,
        int pinnedCount = 0,
        int repairingCount = 0,
        RetreatRecoveryReason retreatReason = RetreatRecoveryReason.None,
        EntityId retreatProvider = default)
    {
        _selectedEntities =
            Array.AsReadOnly(
                selectedEntities?.ToArray() ??
                throw new ArgumentNullException(nameof(selectedEntities)));
        _targets =
            Array.AsReadOnly(
                targets?.ToArray() ??
                throw new ArgumentNullException(nameof(targets)));
        _artillery =
            Array.AsReadOnly(
                artillery?.ToArray() ??
                throw new ArgumentNullException(nameof(artillery)));

        ArgumentOutOfRangeException.ThrowIfNegative(
            requestedSelectionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            combatEligibleCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            rejectedSelectionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            criticalSupplyCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            resupplyingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            suppressedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            pinnedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            repairingCount);

        RequestedSelectionCount = requestedSelectionCount;
        CombatEligibleCount = combatEligibleCount;
        RejectedSelectionCount = rejectedSelectionCount;
        CriticalSupplyCount = criticalSupplyCount;
        ResupplyingCount = resupplyingCount;
        SuppressedCount = suppressedCount;
        PinnedCount = pinnedCount;
        RepairingCount = repairingCount;
        RetreatReason = retreatReason;
        RetreatProvider = retreatProvider;
        HasCommonOrder = hasCommonOrder;
        MixedOrderState = mixedOrderState;
        CurrentOrder = currentOrder;
        CurrentStatus = currentStatus;
    }

    public IReadOnlyList<EntityId> SelectedEntities =>
        _selectedEntities;

    public int RequestedSelectionCount { get; }

    public int CombatEligibleCount { get; }

    public int RejectedSelectionCount { get; }

    public int CriticalSupplyCount { get; }

    public int ResupplyingCount { get; }

    public int SuppressedCount { get; }

    public int PinnedCount { get; }

    public int RepairingCount { get; }

    public RetreatRecoveryReason RetreatReason { get; }

    public EntityId RetreatProvider { get; }

    public bool HasCommonOrder { get; }

    public bool MixedOrderState { get; }

    public CombatOrderKind CurrentOrder { get; }

    public CombatOrderStatus CurrentStatus { get; }

    public IReadOnlyList<PlayerTacticalTargetReadModel> Targets =>
        _targets;

    public IReadOnlyList<PlayerArtilleryActionReadModel> Artillery =>
        _artillery;
}

public readonly record struct ConveyorRoutingActionReadModel(EntityId Entity,
    ConveyorRoutingPreference Preference, uint Revision, EntityId OutputA, EntityId OutputB,
    ConveyorRoutingFailure BalancedFailure, ConveyorRoutingFailure OutputAFailure, ConveyorRoutingFailure OutputBFailure)
{
    public ConveyorRoutingFailure Failure(int index) => index switch
    {
        0 => BalancedFailure, 1 => OutputAFailure, 2 => OutputBFailure,
        _ => ConveyorRoutingFailure.InvalidPreference
    };
    public static string Title(int index) => index switch
    { 0 => "BALANCED", 1 => "OUTPUT A 3:1", 2 => "OUTPUT B 3:1", _ => "" };
}

public sealed class PlayerActionSnapshot
{
    private readonly IReadOnlyList<PlayerConstructionActionReadModel> _construction;
    private readonly IReadOnlyList<PlayerTechnologyActionReadModel> _technology;

    public PlayerActionSnapshot(
        SimulationSessionId sessionId,
        SimulationTick tick,
        IReadOnlyList<PlayerConstructionActionReadModel> construction,
        int pendingCommandCount,
        PlayerProductionFacilityActionReadModel? production,
        PlayerUnitProductionFacilityActionReadModel? unitProduction,
        PlayerLogisticsActionReadModel? logistics = null,
        PlayerSupplyActionReadModel? supply = null,
        PlayerTacticalActionReadModel? tactical = null,
        IReadOnlyList<PlayerTechnologyActionReadModel>? technology = null,
        OperationsRuleSelectionSnapshot? operationsRules = null)
    {
        SessionId = sessionId;
        Tick = tick;
        _construction =
            Array.AsReadOnly(
                construction?.ToArray() ??
                throw new ArgumentNullException(nameof(construction)));
        ArgumentOutOfRangeException.ThrowIfNegative(
            pendingCommandCount);
        PendingCommandCount = pendingCommandCount;
        Production = production;
        OperationsRules = operationsRules;
        UnitProduction = unitProduction;
        Logistics = logistics;
        Supply = supply;
        Tactical = tactical;
        _technology =
            Array.AsReadOnly(
                technology?.ToArray() ??
                []);
    }

    public SimulationSessionId SessionId { get; }

    public SimulationTick Tick { get; }

    public IReadOnlyList<PlayerConstructionActionReadModel> Construction =>
        _construction;

    public int PendingCommandCount { get; }

    public PlayerProductionFacilityActionReadModel? Production { get; }

    public ProductionOutputStock? ProductionOutput { get; init; }
    public ProductionInputInformation? ProductionInput { get; init; }
    public OperationsRuleSelectionSnapshot? OperationsRules { get; }
    public PolicyTemplateSelectionSnapshot? PolicyTemplates { get; init; }
    public IReadOnlyList<BridgeRepairActionReadModel> BridgeRepairs { get; init; } = [];
    public ConveyorRoutingActionReadModel? ConveyorRouting { get; init; }

    public PlayerUnitProductionFacilityActionReadModel? UnitProduction { get; }

    public PlayerLogisticsActionReadModel? Logistics { get; }

    public PlayerSupplyActionReadModel? Supply { get; }

    public PlayerTacticalActionReadModel? Tactical { get; }

    public IReadOnlyList<PlayerTechnologyActionReadModel> Technology =>
        _technology;
}

internal static partial class PlayerActionSnapshotFactory
{
    public static PlayerActionSnapshot Capture(
        SimulationContext context,
        PresentationExtractionContext extraction,
        in PresentationInteractionRequestSnapshot interaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extraction);

        MatchRuntime scenario =
            extraction.Scenario;

        PlayerConstructionActionReadModel[] construction =
            CaptureConstructionActions(
                scenario,
                extraction.Side.StartingInventory);

        EntityId selectedFacility =
            ResolveSingleOwnedSelection(
                context.Entities,
                extraction.Player,
                interaction.SelectedEntities);

        PlayerProductionFacilityActionReadModel? production =
            selectedFacility.IsValid &&
            context.Entities.TryGetComponent(
                selectedFacility,
                out ProductionFacility productionFacility)
                ? CaptureProductionFacility(
                    context.Entities,
                    scenario,
                    selectedFacility,
                    productionFacility)
                : null;

        PlayerUnitProductionFacilityActionReadModel? unitProduction =
            selectedFacility.IsValid &&
            context.Entities.TryGetComponent(
                selectedFacility,
                out UnitProductionFacility unitFacility) &&
            unitFacility.Owner == extraction.Player
                ? CaptureUnitProductionFacility(
                    context.Entities,
                    scenario,
                    selectedFacility,
                    unitFacility)
                : null;

        PlayerLogisticsActionReadModel? logistics =
            selectedFacility.IsValid
                ? CaptureLogistics(
                    context.Entities,
                    scenario,
                    selectedFacility, production, unitProduction, context.Tick)
                : null;

        PlayerSupplyActionReadModel? supply =
            selectedFacility.IsValid
                ? CaptureSupply(
                    context.Entities,
                    scenario,
                    selectedFacility)
                : null;

        PlayerTacticalActionReadModel? tactical =
            CaptureTactical(
                context.Entities,
                scenario,
                extraction.Player,
                interaction.SelectedEntities);
        PlayerTechnologyActionReadModel[] technology =
            TechnologyActionSnapshotFactory.Capture(
                context.Entities,
                extraction);

        return new PlayerActionSnapshot(
            scenario.Simulation.SessionId,
            context.Tick,
            construction,
            extraction.Commands.PendingCount,
            production,
            unitProduction,
            logistics,
            supply,
            tactical,
            technology,
            interaction.OperationsOpen ? CaptureRules(context, extraction, production) : null)
        { ProductionOutput = ProductionOutputStockCapture.Capture(context, extraction, selectedFacility),
            ProductionInput = ProductionInputInformationCapture.Capture(context, extraction, selectedFacility),
            BridgeRepairs = CaptureBridgeRepairs(context, extraction, selectedFacility),
            ConveyorRouting = CaptureRouting(context, extraction.Player, selectedFacility),
            PolicyTemplates = interaction.OperationsOpen
                ? PolicyTemplateSelectionCapture.Capture(context, extraction, interaction.SelectedEntities) : null };
    }

    private static ConveyorRoutingActionReadModel? CaptureRouting(SimulationContext context, PlayerId player, EntityId entity)
    {
        if (!context.Entities.TryGetComponent(entity, out ConveyorPiece piece) || piece.Owner != player ||
            piece.Placement.Module != ConveyorModule.Splitter) return null;
        var state = context.Entities.TryGetComponent(entity, out ConveyorRoutingState current) ? current : default;
        var flow = context.Entities.TryGetComponent(entity, out ConveyorFlowState observed) ? observed : default;
        return new(entity, state.Preference, state.Revision, flow.OutputA, flow.OutputB,
            ConveyorRoutingPolicy.Evaluate(context.Entities, player, entity, ConveyorRoutingPreference.Balanced, state.Revision),
            ConveyorRoutingPolicy.Evaluate(context.Entities, player, entity, ConveyorRoutingPreference.OutputA, state.Revision),
            ConveyorRoutingPolicy.Evaluate(context.Entities, player, entity, ConveyorRoutingPreference.OutputB, state.Revision));
    }

    private static OperationsRuleSelectionSnapshot? CaptureRules(SimulationContext context,
        PresentationExtractionContext extraction, PlayerProductionFacilityActionReadModel? production)
    {
        if (production is null || !context.Entities.TryGetComponent(production.Entity, out ControllableEntity owner) ||
            !owner.IsControllable || context.Entities.TryGetComponent(production.Entity, out HealthState health) && health.IsDepleted ||
            !context.Entities.TryGetComponent(production.Entity, out ProductionFacility facility) ||
            !extraction.Scenario.Inventories.Contains(facility.InputInventory)) return null;
        RecipeId recipeId = production.ActiveRecipe.IsSpecified ? production.ActiveRecipe :
            production.Requests.Count > 0 ? production.Requests[0].RecipeId : RecipeId.None;
        var inputs = production.Recipes.FirstOrDefault(recipe => recipe.RecipeId == recipeId)?.Inputs ?? [];
        return new(extraction.Player, production.Entity,
            extraction.Scenario.Inventories.GetTotalCapacity(facility.InputInventory), context.TickDuration,
            recipeId, inputs, extraction.Scenario.OperationsRules.Capture(extraction.Player), production.Requests);
    }

    private static PlayerConstructionActionReadModel[]
        CaptureConstructionActions(
            MatchRuntime scenario,
            InventoryId constructionInventory)
    {
        var actions =
            new List<PlayerConstructionActionReadModel>();

        foreach (BuildingDefinition definition in
                 scenario.Services.BuildingDefinitions.Definitions)
        {
            PlayerActionResourceAmount[] costs =
                definition.Costs
                    .Select(
                        cost =>
                            CreateAmount(
                                scenario,
                                constructionInventory,
                                cost.ResourceId,
                                cost.Quantity))
                    .ToArray();

            actions.Add(
                new PlayerConstructionActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    costs,
                    definition.RequiresResourceDeposit));
        }

        foreach (ConveyorModule module in Enum.GetValues<ConveyorModule>())
        {
            double minimum=module==ConveyorModule.Route?8:ConveyorGeometry.Cost(module);
            actions.Add(new(new BuildingId(ConveyorGeometry.MenuBase+(uint)module),
                "Conveyor / "+ConveyorGeometry.Name(module), new[]{CreateAmount(scenario,constructionInventory,ResourceIds.Steel,minimum)},false));
        }
        for (uint template = 1; template <= 2; template++)
            actions.Add(new(new BuildingId(PaidConstructionTemplates.MenuBase + template),
                "Template / " + PaidConstructionTemplates.Name((int)template), Array.Empty<PlayerActionResourceAmount>(), false));
        return actions.ToArray();
    }

    private static PlayerProductionFacilityActionReadModel
        CaptureProductionFacility(
            EntityRegistry entities,
            MatchRuntime scenario,
            EntityId entity,
            ProductionFacility facility)
    {
        var recipes =
            new List<PlayerProductionRecipeActionReadModel>();

        foreach (ProductionRecipeDefinition definition in
                 scenario.Services.ProductionRecipes.Definitions)
        {
            if (!facility.Supports(
                    definition.RequiredCapability))
            {
                continue;
            }

            PlayerActionResourceAmount[] inputs =
                definition.Inputs
                    .Select(
                        ingredient =>
                            CreateAmount(
                                scenario,
                                facility.InputInventory,
                                ingredient.ResourceId,
                                ingredient.Quantity))
                    .ToArray();
            PlayerActionResourceAmount[] outputs =
                definition.Outputs
                    .Select(
                        ingredient =>
                            CreateAmount(
                                scenario,
                                facility.OutputInventory,
                                ingredient.ResourceId,
                                ingredient.Quantity))
                    .ToArray();

            recipes.Add(
                new PlayerProductionRecipeActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    inputs,
                    outputs));
        }

        var requests =
            new List<PlayerProductionRequestReadModel>();

        foreach (EntityId requestEntity in
                 entities.Query<ProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProductionRequest request =
                entities.GetComponent<ProductionRequest>(
                    requestEntity);

            if (request.Facility != entity)
            {
                continue;
            }

            string displayName =
                scenario.Services.ProductionRecipes.TryGet(
                    request.RecipeId,
                    out ProductionRecipeDefinition? definition)
                    ? definition.DisplayName
                    : request.RecipeId.ToString();

            requests.Add(
                new PlayerProductionRequestReadModel(
                    requestEntity,
                    request.RecipeId,
                    displayName,
                    request.Priority,
                    request.Mode,
                    request.Paused,
                    requestEntity == facility.ActiveRequest)
                { DesiredStockResource = request.DesiredStockResourceId, DesiredStockQuantity = request.DesiredStockQuantity });
        }

        double progress = 0.0;
        if (facility.ActiveRecipe.IsSpecified &&
            scenario.Services.ProductionRecipes.TryGet(
                facility.ActiveRecipe,
                out ProductionRecipeDefinition? activeDefinition))
        {
            progress =
                Math.Clamp(
                    facility.ProgressTicks /
                    (double)activeDefinition.DurationTicks,
                    0.0,
                    1.0);
        }

        return new PlayerProductionFacilityActionReadModel(
            entity,
            facility.ActiveRequest,
            facility.ActiveRecipe,
            facility.Status,
            facility.BlockReason,
            progress,
            recipes,
            requests,
            isAutomatic: AutomaticProcessing.RecipeFor(facility.Capabilities).IsSpecified);
    }

    private static PlayerUnitProductionFacilityActionReadModel
        CaptureUnitProductionFacility(
            EntityRegistry entities,
            MatchRuntime scenario,
            EntityId entity,
            UnitProductionFacility facility)
    {
        var units =
            new List<PlayerUnitProductionActionReadModel>();

        foreach (UnitDefinition definition in
                 scenario.Services.UnitDefinitions.Definitions)
        {
            if (!facility.Supports(
                    definition.RequiredProductionCapability))
            {
                continue;
            }

            PlayerActionResourceAmount[] costs =
                definition.Costs
                    .Select(
                        cost =>
                            CreateAmount(
                                scenario,
                                facility.InputInventory,
                                cost.ResourceId,
                                cost.Quantity))
                    .ToArray();

            bool technologyUnlocked =
                TechnologyStateQueries.IsCapabilityUnlocked(
                    entities,
                    facility.Owner,
                    definition.RequiredTechnologyCapability);

            units.Add(
                new PlayerUnitProductionActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    costs,
                    definition.ProductionTicks,
                    technologyUnlocked,
                    definition.RequiredTechnologyCapability));
        }

        var requests =
            new List<PlayerUnitProductionRequestReadModel>();

        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility != entity)
            {
                continue;
            }

            string displayName =
                scenario.Services.UnitDefinitions.TryGet(
                    request.UnitId,
                    out UnitDefinition? definition)
                    ? definition.DisplayName
                    : request.UnitId.ToString();

            requests.Add(
                new PlayerUnitProductionRequestReadModel(
                    requestEntity,
                    request.UnitId,
                    displayName,
                    request.Priority,
                    request.Paused,
                    requestEntity == facility.ActiveRequest));
        }

        double progress = 0.0;
        if (facility.ActiveUnit.IsSpecified &&
            scenario.Services.UnitDefinitions.TryGet(
                facility.ActiveUnit,
                out UnitDefinition? activeDefinition))
        {
            progress =
                Math.Clamp(
                    facility.ProgressTicks /
                    (double)activeDefinition.ProductionTicks,
                    0.0,
                    1.0);
        }

        System.Numerics.Vector3? rallyPoint =
            entities.TryGetComponent(
                entity,
                out UnitProductionRallyPoint configuredRallyPoint)
                ? configuredRallyPoint.WorldPosition
                : null;

        return new PlayerUnitProductionFacilityActionReadModel(
            entity,
            facility.ActiveRequest,
            facility.ActiveUnit,
            facility.Status,
            facility.BlockReason,
            progress,
            units,
            requests,
            rallyPoint);
    }

    private static PlayerTacticalActionReadModel? CaptureTactical(
        EntityRegistry entities,
        MatchRuntime scenario,
        PlayerId player,
        IReadOnlyList<EntityId> selectedEntities)
    {
        if (selectedEntities.Count == 0 ||
            player.Value == 0 ||
            player.Value > uint.MaxValue)
        {
            return null;
        }

        var owned =
            new List<EntityId>(selectedEntities.Count);
        var combatEligible =
            new List<EntityId>(selectedEntities.Count);
        var artillery =
            new List<PlayerArtilleryActionReadModel>();
        int criticalSupply = 0;
        int resupplying = 0;
        int suppressed = 0;
        int pinned = 0;
        int repairing = 0;
        bool observedRetreatRecovery = false;
        bool mixedRetreatRecovery = false;
        RetreatRecoveryReason retreatReason =
            RetreatRecoveryReason.None;
        EntityId retreatProvider =
            EntityId.Invalid;

        bool observedCombatState = false;
        bool hasOrder = false;
        bool mixedOrder = false;
        CombatOrderKind commonOrder = default;
        CombatOrderStatus commonStatus = default;

        for (int index = 0;
             index < selectedEntities.Count;
             index++)
        {
            EntityId entity =
                selectedEntities[index];

            if (!entities.IsAlive(entity) ||
                !entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                controllable.Owner != player)
            {
                continue;
            }

            owned.Add(entity);

            bool isCombatEligible =
                entities.HasComponent<WorldTransform>(entity) &&
                entities.HasComponent<Combatant>(entity);

            if (isCombatEligible)
            {
                combatEligible.Add(entity);

                if (entities.TryGetComponent(
                        entity,
                        out UnitSupplyState supply) &&
                    supply.Status is
                        BattlefieldSupplyStatus.Critical or
                        BattlefieldSupplyStatus.Unsupplied)
                {
                    criticalSupply++;
                }

                if (entities.TryGetComponent(
                        entity,
                        out SuppressionState suppression))
                {
                    if (suppression.Level ==
                        SuppressionLevel.Suppressed)
                    {
                        suppressed++;
                    }
                    else if (suppression.Level ==
                             SuppressionLevel.Pinned)
                    {
                        pinned++;
                    }
                }

                if (entities.TryGetComponent(
                        entity,
                        out RepairRecoveryState recovery) &&
                    recovery.Status ==
                        RepairRecoveryStatus.Repairing)
                {
                    repairing++;
                }

                if (entities.TryGetComponent(
                        entity,
                        out RetreatRecoveryState retreatRecovery))
                {
                    if (!observedRetreatRecovery)
                    {
                        observedRetreatRecovery = true;
                        retreatReason =
                            retreatRecovery.Reason;
                        retreatProvider =
                            retreatRecovery.Provider;
                    }
                    else if (retreatReason !=
                                 retreatRecovery.Reason ||
                             retreatProvider !=
                                 retreatRecovery.Provider)
                    {
                        mixedRetreatRecovery = true;
                    }
                }

                CombatOrderKind orderKind = default;
                CombatOrderStatus status = default;
                bool entityHasOrder =
                    entities.TryGetComponent(
                        entity,
                        out CombatOrderState order);
                bool entityHasStatus =
                    entities.TryGetComponent(
                        entity,
                        out TacticalCombatState tactical);

                if (entityHasOrder)
                {
                    orderKind = order.Kind;
                }

                if (entityHasStatus)
                {
                    status = tactical.Status;

                    if (status ==
                        CombatOrderStatus.Resupplying)
                    {
                        resupplying++;
                    }
                }

                if (!observedCombatState)
                {
                    observedCombatState = true;
                    hasOrder = entityHasOrder;
                    commonOrder = orderKind;
                    commonStatus = status;
                }
                else if (entityHasOrder != hasOrder ||
                         (entityHasOrder &&
                          orderKind != commonOrder) ||
                         status != commonStatus)
                {
                    mixedOrder = true;
                }
            }

            if (entities.TryGetComponent(
                    entity,
                    out ArtilleryCapability capability) &&
                scenario.Services.ArtilleryWeapons.TryGet(
                    capability.WeaponId,
                    out ArtilleryWeaponDefinition? definition) &&
                definition is not null)
            {
                double ammunitionQuantity = 0.0;
                double ammunitionCapacity = 0.0;

                if (entities.TryGetComponent(
                        entity,
                        out AmmunitionState ammunition))
                {
                    ammunitionCapacity =
                        ammunition.Capacity;

                    if (scenario.Inventories.Contains(
                            ammunition.InventoryId))
                    {
                        ammunitionQuantity =
                            scenario.Inventories.GetQuantity(
                                ammunition.InventoryId,
                                ResourceIds.Ammunition);
                    }
                }

                FireMissionState mission =
                    entities.TryGetComponent(
                        entity,
                        out FireMissionState currentMission)
                        ? currentMission
                        : new FireMissionState(
                            System.Numerics.Vector3.Zero,
                            IntelligenceContactKey.None,
                            0,
                            0,
                            FireMissionStatus.Complete,
                            SimulationTick.Zero,
                            SimulationTick.Zero,
                            SimulationTick.Zero,
                            SimulationTick.Zero);

                artillery.Add(
                    new PlayerArtilleryActionReadModel(
                        entity,
                        ammunitionQuantity,
                        ammunitionCapacity,
                        definition.MinimumRangeMeters,
                        definition.MaximumRangeMeters,
                        mission.Status,
                        mission.RoundsFired,
                        mission.RequestedRounds));
            }
        }

        if (owned.Count == 0)
        {
            return null;
        }

        FactionId faction =
            new((uint)player.Value);
        FactionIntelligenceSnapshot intelligence =
            scenario.Intelligence.Capture(
                faction);
        var targets =
            new List<PlayerTacticalTargetReadModel>();

        for (int index = 0;
             index < intelligence.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                intelligence.Contacts[index];

            if (!contact.IsCurrent ||
                !contact.IsIdentified ||
                !scenario.Intelligence.TryResolveCurrentlyIdentifiedEntity(
                    faction,
                    contact.ContactKey,
                    out EntityId target) ||
                !entities.IsAlive(target) ||
                !entities.TryGetComponent(
                    target,
                    out Combatant targetCombatant) ||
                targetCombatant.Faction == faction ||
                !entities.TryGetComponent(
                    target,
                    out Targetable targetable) ||
                !entities.TryGetComponent(
                    target,
                    out HealthState health) ||
                health.IsDepleted)
            {
                continue;
            }

            int compatible = 0;

            for (int unitIndex = 0;
                 unitIndex < combatEligible.Count;
                 unitIndex++)
            {
                EntityId unit =
                    combatEligible[unitIndex];

                if (!entities.TryGetComponent(
                        unit,
                        out WeaponState weaponState) ||
                    !scenario.Services.Weapons.TryGet(
                        weaponState.WeaponId,
                        out WeaponDefinition? weapon) ||
                    weapon is null ||
                    !weapon.Effectiveness.CanEngage(
                        targetable.Class))
                {
                    continue;
                }

                compatible++;
            }

            targets.Add(
                new PlayerTacticalTargetReadModel(
                    target,
                    contact.ContactKey,
                    contact.LastKnownPosition,
                    contact.State,
                    contact.LastSeenTick,
                    compatible));
        }

        return new PlayerTacticalActionReadModel(
            owned,
            selectedEntities.Count,
            combatEligible.Count,
            selectedEntities.Count -
                combatEligible.Count,
            criticalSupply,
            resupplying,
            hasOrder && !mixedOrder,
            mixedOrder,
            commonOrder,
            commonStatus,
            targets,
            artillery,
            suppressed,
            pinned,
            repairing,
            observedRetreatRecovery &&
            !mixedRetreatRecovery
                ? retreatReason
                : RetreatRecoveryReason.None,
            observedRetreatRecovery &&
            !mixedRetreatRecovery
                ? retreatProvider
                : EntityId.Invalid);
    }

    private static PlayerLogisticsActionReadModel? CaptureLogistics(
        EntityRegistry entities,
        MatchRuntime scenario,
        EntityId entity,
        PlayerProductionFacilityActionReadModel? production,
        PlayerUnitProductionFacilityActionReadModel? units,
        SimulationTick tick)
    {
        bool hasInventory =
            TryResolveDistributionInventory(
                entities,
                entity,
                out InventoryId inventory) && scenario.Inventories.Contains(inventory);

        PlayerCargoStatusReadModel? cargo = null;
        // The debug snapshot is a simulation-owned copy, not a live mutable queue.
        var distribution = scenario.AutomatedDistribution.LastDebugSnapshot;
        var orderRows = new List<PlayerLogisticsOrderReadModel>();
        int orderTotal = 0;
        if (distribution.Tick == tick)
        {
            foreach (var request in distribution.Requests)
            {
                if (request.State == LogisticsTransportRequestState.Completed ||
                    !entities.TryGetComponent(request.PolicyEntity, out LogisticsStockPolicy rule) ||
                    !Owned(rule.TargetEntity))
                    continue;

                bool forFacility = rule.TargetEntity == entity;
                bool forTruck = request.AssignedTruck == entity && entities.IsAlive(entity);
                if (!forFacility && !forTruck) continue;
                orderTotal++;
                if (orderRows.Count >= PlayerLogisticsActionReadModel.MaximumVisibleOrders) continue;

                EntityId origin = OwnedNode(request.Origin);
                EntityId destination = OwnedNode(request.Destination);
                EntityId truck = Owned(request.AssignedTruck) ? request.AssignedTruck : EntityId.Invalid;
                // A recycled truck cannot lend the new request stale cargo progress.
                double loaded = truck.IsValid &&
                    request.State is (LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit) &&
                    entities.TryGetComponent(truck, out CargoTransportOrder liveOrder) &&
                    liveOrder.Origin == request.Origin &&
                    liveOrder.Destination == request.Destination &&
                    liveOrder.ResourceId == request.ResourceId &&
                    entities.TryGetComponent(truck, out CargoTransportRuntimeState state)
                    ? Math.Max(0, state.LoadedQuantity - state.DeliveredQuantity)
                    : 0;
                string resourceName = scenario.Services.Resources.TryGet(request.ResourceId, out ResourceDefinition? resource)
                    ? resource.DisplayName : request.ResourceId.ToString();
                orderRows.Add(new PlayerLogisticsOrderReadModel(
                    request.RequestId, request.ResourceId, resourceName, request.State,
                    request.FailureReason, request.BottleneckReason,
                    request.RequestedQuantity, loaded, truck, origin, destination,
                    origin.IsValid ? Name(origin) : "SOURCE NOT ASSIGNED",
                    destination.IsValid ? Name(destination) : "DESTINATION NOT AVAILABLE"));
            }
        }
        if (entities.TryGetComponent(
                entity,
                out CargoTransport transport))
        {
            CargoTransportRuntimeState state =
                entities.TryGetComponent(
                    entity,
                    out CargoTransportRuntimeState current)
                    ? current
                    : CargoTransportRuntimeState.Idle;
            double quantity =
                scenario.Inventories.Contains(
                    transport.CargoInventory)
                    ? scenario.Inventories.GetTotalQuantity(
                        transport.CargoInventory)
                    : 0.0;
            cargo =
                new PlayerCargoStatusReadModel(
                    entity,
                    state.Lifecycle,
                    state.WaitReason,
                    state.FailureReason,
                    quantity,
                    transport.Capacity)
                {
                    Source = entities.TryGetComponent(entity, out CargoTransportOrder sourceOrder) ? OwnedNode(sourceOrder.Origin) : default,
                    SourceName = entities.TryGetComponent(entity, out CargoTransportOrder sourceNameOrder) ? Name(OwnedNode(sourceNameOrder.Origin)) : "NO SOURCE ASSIGNED",
                    DestinationName = entities.TryGetComponent(entity, out CargoTransportOrder destinationNameOrder) ? Name(OwnedNode(destinationNameOrder.Destination)) : "NO DESTINATION ASSIGNED",
                    Destination = entities.TryGetComponent(entity, out CargoTransportOrder destinationOrder) ? OwnedNode(destinationOrder.Destination) : default,
                    Resource = entities.TryGetComponent(entity, out CargoTransportOrder resourceOrder) ? resourceOrder.ResourceId : default,
                    ResourceName = entities.TryGetComponent(entity, out CargoTransportOrder namedOrder) &&
                        scenario.Services.Resources.TryGet(namedOrder.ResourceId, out var resourceDefinition) ? resourceDefinition.DisplayName : string.Empty,
                    RequestedQuantity = entities.TryGetComponent(entity, out CargoTransportOrder quantityOrder) ? quantityOrder.RequestedQuantity : 0
                };
        }

        CargoConvoyReadModel? selectedConvoy = null;
        if (cargo.HasValue &&
            entities.TryGetComponent(entity, out CargoConvoyMember convoyMember) &&
            entities.TryGetComponent(entity, out ControllableEntity convoyOwner))
        {
            foreach (CargoConvoyReadModel convoy in scenario.CargoConvoys.CaptureOwned(
                         entities, convoyOwner.Owner, tick))
            {
                if (convoy.Group == convoyMember.Group)
                {
                    selectedConvoy = convoy;
                    break;
                }
            }
        }

        if (!hasInventory)
        {
            return cargo.HasValue
                ? new PlayerLogisticsActionReadModel(
                    entity,
                    [],
                    cargo, service: CaptureService(entity), orders: orderRows)
                    { CargoFleet = CaptureFleet(), TotalOrderCount = orderTotal,
                        SelectedConvoy = selectedConvoy }
                : null;
        }

        PlayerWorkSummary CaptureService(EntityId target) => PlayerExperienceSnapshotFactory.CaptureWork(
            entities, target, scenario.Production, scenario.UnitProduction,
            scenario.Services.UnitDefinitions, scenario.Services.BuildingDefinitions);
        var policies = new List<PlayerStockPolicyActionReadModel>();

        foreach (ResourceDefinition resource in
                 scenario.Services.Resources.Definitions)
        {
            EntityId policyEntity =
                EntityId.Invalid;
            LogisticsStockPolicy policy =
                default;
            bool hasPolicy = false;

            foreach (EntityId candidate in
                     entities.Query<LogisticsStockPolicy>(
                         QueryIterationOrder.StableByEntityIndex))
            {
                LogisticsStockPolicy current =
                    entities.GetComponent<LogisticsStockPolicy>(
                        candidate);

                if (current.TargetEntity == entity &&
                    current.ResourceId == resource.Id)
                {
                    policyEntity = candidate;
                    policy = current;
                    hasPolicy = true;
                    break;
                }
            }

            double currentQuantity =
                scenario.Inventories.Contains(inventory)
                    ? scenario.Inventories.GetQuantity(
                        inventory,
                        resource.Id)
                    : 0.0;
            // A new manual policy is a draft; its defaults are not inventory demand.
            var defaults = LogisticsQuantityEditor.NewManualDefaults(
                scenario.Inventories.GetTotalCapacity(inventory));
            double minimum = hasPolicy ? policy.DesiredMinimum : defaults.Minimum;
            double target = hasPolicy ? policy.DesiredTarget : defaults.Target;
            double maximum = hasPolicy ? policy.DesiredMaximum : defaults.Maximum;

            ResolveDistributionStatus(
                scenario,
                entities,
                tick,
                policyEntity,
                hasPolicy ? policy.Priority : LogisticsStockPriority.Normal,
                out PlayerDistributionActionState distributionState,
                out LogisticsStockPriority dispatchPriority,
                out LogisticsTransportRequestFailureReason failure,
                out LogisticsBottleneckReason bottleneck,
                out EntityId assignedTruck);

            var sourceEntity = Source(policyEntity);
            policies.Add(
                new PlayerStockPolicyActionReadModel(
                    policyEntity,
                    resource.Id,
                    resource.DisplayName,
                    currentQuantity,
                    minimum,
                    target,
                    maximum,
                    hasPolicy
                        ? policy.Priority
                        : LogisticsStockPriority.Normal,
                    !hasPolicy || policy.Enabled,
                    distributionState,
                    failure,
                    bottleneck,
                    Owned(assignedTruck) ? assignedTruck : EntityId.Invalid)
                {
                    DispatchPriority = dispatchPriority,
                    ReservedQuantity = scenario.Inventories.GetReservedQuantity(inventory, resource.Id),
                    RequestedQuantity = Outstanding(policyEntity, false),
                    AnnouncedQuantity = Outstanding(policyEntity, true),
                    Source = sourceEntity,
                    SourceName = Name(sourceEntity),
                    Service = Owned(assignedTruck) ? CaptureService(assignedTruck) : default
                });
        }

        return new PlayerLogisticsActionReadModel(
            entity,
            policies,
            cargo,
            scenario.Inventories.GetTotalCapacity(inventory),
            scenario.Inventories.GetTotalQuantity(inventory),
            CaptureService(entity),
            (production?.Recipes.Select(r => new PlayerProductionRequirement(1, r.RecipeId.Value, r.DisplayName, r.Inputs)) ?? [])
                .Concat(units?.Units.Select(u => new PlayerProductionRequirement(2, u.UnitId.Value, u.DisplayName, u.Costs)) ?? [])
                .ToArray(),
            orderRows)
        {
            IsCommandCore = entities.TryGetComponent(entity, out CompletedBuilding coreIdentity) && coreIdentity.BuildingId == BuildingIds.CommandCore,
            HasEnabledGraphNode = scenario.Logistics.TryGetNodeForEntity(entity, out var localNode) &&
                scenario.Logistics.TryGetNode(localNode, out var graphNode) && graphNode.Enabled,
            CargoFleet = CaptureFleet(),
            TotalOrderCount = orderTotal,
            SelectedConvoy = selectedConvoy
        };

        string Name(EntityId target)
        {
            if (!Owned(target)) return "NO SOURCE ASSIGNED";
            if (entities.TryGetComponent(target, out CompletedBuilding building) &&
                scenario.Services.BuildingDefinitions.TryGet(building.BuildingId, out var definition)) return definition.DisplayName;
            return "OWNED LOGISTICS SOURCE";
        }
        EntityId OwnedNode(ForgeLine.Logistics.LogisticsNodeId node) => scenario.Logistics.TryGetNode(node, out var value) && Owned(value.Entity) ? value.Entity : default;
        PlayerCargoFleet CaptureFleet()
        {
            int total = 0, working = 0, bound = 0, idle = 0, noFuel = 0;
            foreach (var truck in entities.Query<CargoTransport>())
            {
                if (!Owned(truck) || entities.HasComponent<SupplyTruck>(truck) ||
                    entities.TryGetComponent(truck, out HealthState health) && health.IsDepleted) continue;
                total++;
                if (entities.HasComponent<CargoTransportOrder>(truck)) working++;
                if (entities.HasComponent<CargoTransportReservation>(truck) || entities.HasComponent<ResupplyOrder>(truck)) bound++;
                if (entities.TryGetComponent(truck, out UnitFuelState fuel) &&
                    (!scenario.Inventories.Contains(fuel.InventoryId) || scenario.Inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) <= 1e-9)) noFuel++;
                var transport = entities.GetComponent<CargoTransport>(truck);
                if (entities.TryGetComponent(truck, out CargoTransportRuntimeState state) && state.Lifecycle == CargoTransportLifecycleState.Idle &&
                    !entities.HasComponent<CargoTransportOrder>(truck) && !entities.HasComponent<CargoTransportReservation>(truck) &&
                    !entities.HasComponent<ResupplyOrder>(truck) && scenario.Inventories.Contains(transport.CargoInventory) &&
                    scenario.Inventories.GetTotalQuantity(transport.CargoInventory) <= 1e-9) idle++;
            }
            return new(total, working, bound, idle, noFuel);
        }
        bool Owned(EntityId target) => entities.IsAlive(target) &&
            entities.TryGetComponent(entity, out ControllableEntity local) &&
            entities.TryGetComponent(target, out ControllableEntity owner) && owner.Owner == local.Owner;
        double Outstanding(EntityId policyId, bool assignedOnly)
        {
            if (!policyId.IsValid ||
                scenario.AutomatedDistribution.LastDebugSnapshot.Tick != tick) return 0;
            double total = 0;
            foreach (var request in scenario.AutomatedDistribution.LastDebugSnapshot.Requests)
                if (request.PolicyEntity == policyId && request.State is not (LogisticsTransportRequestState.Completed or LogisticsTransportRequestState.Failed))
                {
                    if (!assignedOnly)
                    {
                        double delivered = request.State is (LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit) &&
                            Owned(request.AssignedTruck) && entities.TryGetComponent(request.AssignedTruck, out CargoTransportRuntimeState progress)
                            ? progress.DeliveredQuantity : 0;
                        total += Math.Max(0, request.RequestedQuantity - delivered); continue;
                    }
                    if (request.State is (LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit) &&
                        Owned(request.AssignedTruck) && entities.TryGetComponent(request.AssignedTruck, out CargoTransportOrder order) &&
                        entities.TryGetComponent(request.AssignedTruck, out CargoTransportRuntimeState state))
                    {
                        double reserved = entities.TryGetComponent(request.AssignedTruck, out CargoTransportReservation reservation) &&
                            reservation.RequestId == request.RequestId ? reservation.Quantity : 0;
                        total += reserved + Math.Max(0, state.LoadedQuantity - state.DeliveredQuantity);
                    }
                }
            return total;
        }
        EntityId Source(EntityId policyId)
        {
            if (!policyId.IsValid ||
                scenario.AutomatedDistribution.LastDebugSnapshot.Tick != tick) return EntityId.Invalid;
            foreach (var request in scenario.AutomatedDistribution.LastDebugSnapshot.Requests.Reverse())
                if (request.PolicyEntity == policyId && request.State is (LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit) &&
                    scenario.Logistics.TryGetNode(request.Origin, out var source) && Owned(source.Entity)) return source.Entity;
            return EntityId.Invalid;
        }
    }

    private static PlayerSupplyActionReadModel? CaptureSupply(
        EntityRegistry entities,
        MatchRuntime scenario,
        EntityId entity)
    {
        bool recipient =
            entities.HasComponent<UnitFuelState>(entity) ||
            entities.HasComponent<AmmunitionState>(entity);
        bool provider =
            entities.HasComponent<SupplyProvider>(entity) ||
            entities.HasComponent<SupplyTruck>(entity);

        if (!recipient && !provider)
        {
            return null;
        }

        UnitSupplyState supplyState =
            entities.TryGetComponent(
                entity,
                out UnitSupplyState currentSupply)
                ? currentSupply
                : new UnitSupplyState(
                    1.0,
                    1.0,
                    BattlefieldSupplyStatus.Supplied,
                    scenario.Simulation.CurrentTick);

        AutomaticResupplyPolicy automatic =
            entities.TryGetComponent(
                entity,
                out AutomaticResupplyPolicy currentAutomatic)
                ? currentAutomatic
                : new AutomaticResupplyPolicy();

        EntityId selectedProvider =
            EntityId.Invalid;
        if (entities.TryGetComponent(
                entity,
                out ResupplyOrder order))
        {
            selectedProvider =
                order.Provider;
        }
        else if (entities.TryGetComponent(
                     entity,
                     out ResupplyPlanningResult planning) &&
                 planning.SelectedProvider.IsValid)
        {
            selectedProvider =
                planning.SelectedProvider;
        }

        if (selectedProvider.IsValid &&
            (!entities.TryGetComponent(entity, out ControllableEntity recipientOwner) ||
             !entities.TryGetComponent(selectedProvider, out ControllableEntity providerOwner) || providerOwner.Owner != recipientOwner.Owner))
            selectedProvider = EntityId.Invalid;

        ResupplyProviderRejection rejections =
            entities.TryGetComponent(
                entity,
                out ResupplyPlanningResult result)
                ? result.Rejections
                : ResupplyProviderRejection.None;

        ResolveProviderState(
            entities,
            scenario,
            entity,
            selectedProvider,
            out PlayerSupplyProviderState providerState,
            out double providerFuel,
            out double providerAmmunition);

        BattlefieldSupplyPriority priority =
            entities.TryGetComponent(
                entity,
                out UnitSupplyPriority configuredPriority)
                ? configuredPriority.Priority
                : BattlefieldSupplyPriority.Normal;

        bool hasLocalProvider = entities.TryGetComponent(entity, out SupplyProvider localProvider);
        bool hasLocalInventory = hasLocalProvider && scenario.Inventories.Contains(localProvider.InventoryId);
        return new PlayerSupplyActionReadModel(
            entity,
            supplyState.Status,
            supplyState.FuelFraction,
            supplyState.AmmunitionFraction,
            automatic.Enabled,
            automatic.FuelThreshold,
            automatic.AmmunitionThreshold,
            selectedProvider,
            providerState,
            providerFuel,
            providerAmmunition,
            rejections,
            priority)
        {
            IsSupplyTruck = entities.HasComponent<SupplyTruck>(entity),
            IsStaticProvider = hasLocalProvider && !entities.HasComponent<SupplyTruck>(entity),
            HasLocalProviderInventory = hasLocalInventory,
            LocalProviderFuel = hasLocalInventory ? scenario.Inventories.GetQuantity(localProvider.InventoryId,ResourceIds.Fuel) : 0,
            LocalProviderAmmunition = hasLocalInventory ? scenario.Inventories.GetQuantity(localProvider.InventoryId,ResourceIds.Ammunition) : 0,
            CargoFuel = entities.TryGetComponent(entity, out SupplyTruck cargoFuel) && scenario.Inventories.Contains(cargoFuel.InventoryId)
                ? scenario.Inventories.GetQuantity(cargoFuel.InventoryId, ResourceIds.Fuel) : 0,
            CargoAmmunition = entities.TryGetComponent(entity, out SupplyTruck cargoAmmo) && scenario.Inventories.Contains(cargoAmmo.InventoryId)
                ? scenario.Inventories.GetQuantity(cargoAmmo.InventoryId, ResourceIds.Ammunition) : 0,
            DriveFuelQuantity = entities.TryGetComponent(entity, out UnitFuelState driveFuel) && scenario.Inventories.Contains(driveFuel.InventoryId)
                ? scenario.Inventories.GetQuantity(driveFuel.InventoryId, ResourceIds.Fuel) : 0,
            DriveFuelCapacity = entities.TryGetComponent(entity, out UnitFuelState driveCapacity) ? driveCapacity.Capacity : 0,
            Service = PlayerExperienceSnapshotFactory.CaptureWork(entities, entity, scenario.Production, scenario.UnitProduction,
                scenario.Services.UnitDefinitions, scenario.Services.BuildingDefinitions)
        };
    }

    private static bool TryResolveDistributionInventory(
        EntityRegistry entities,
        EntityId entity,
        out InventoryId inventory)
    {
        if (entities.TryGetComponent(
                entity,
                out ProductionFacility production))
        {
            inventory = production.InputInventory;
            return inventory.IsSpecified;
        }

        if (entities.TryGetComponent(
                entity,
                out UnitProductionFacility unitProduction))
        {
            inventory = unitProduction.InputInventory;
            return inventory.IsSpecified;
        }

        if (entities.TryGetComponent(
                entity,
                out InventoryStorage storage))
        {
            inventory = storage.InventoryId;
            return inventory.IsSpecified;
        }

        if (entities.TryGetComponent(
                entity,
                out LogisticsHub hub))
        {
            inventory = hub.InventoryId;
            return inventory.IsSpecified;
        }

        if (entities.TryGetComponent(
                entity,
                out SupplyDepot depot))
        {
            inventory = depot.InventoryId;
            return inventory.IsSpecified;
        }

        inventory = InventoryId.None;
        return false;
    }

    private static void ResolveDistributionStatus(
        MatchRuntime scenario,
        EntityRegistry entities,
        SimulationTick tick,
        EntityId policyEntity,
        LogisticsStockPriority configuredPriority,
        out PlayerDistributionActionState state,
        out LogisticsStockPriority dispatchPriority,
        out LogisticsTransportRequestFailureReason failure,
        out LogisticsBottleneckReason bottleneck,
        out EntityId assignedTruck)
    {
        state = PlayerDistributionActionState.None;
        dispatchPriority = configuredPriority;
        failure = LogisticsTransportRequestFailureReason.None;
        bottleneck = LogisticsBottleneckReason.None;
        assignedTruck = EntityId.Invalid;

        if (!policyEntity.IsValid ||
            scenario.AutomatedDistribution.LastDebugSnapshot.Tick != tick)
        {
            return;
        }

        LogisticsTransportRequestReadModel? best = null;
        foreach (LogisticsTransportRequestReadModel request in
                 scenario.AutomatedDistribution.LastDebugSnapshot.Requests)
        {
            if (request.PolicyEntity != policyEntity ||
                (best.HasValue &&
                 request.RequestId <= best.Value.RequestId))
            {
                continue;
            }

            best = request;
        }

        if (!best.HasValue)
        {
            return;
        }

        LogisticsTransportRequestReadModel selected =
            best.Value;
        if (selected.State == LogisticsTransportRequestState.Completed) return;
        dispatchPriority = selected.EffectivePriority;
        failure = selected.FailureReason;
        bottleneck = selected.BottleneckReason;
        assignedTruck = selected.State is LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit
            ? selected.AssignedTruck : EntityId.Invalid;

        if (assignedTruck.IsValid &&
            (!entities.TryGetComponent(policyEntity, out LogisticsStockPolicy policy) ||
             !entities.TryGetComponent(policy.TargetEntity, out ControllableEntity targetOwner) ||
             !entities.TryGetComponent(assignedTruck, out ControllableEntity truckOwner) || truckOwner.Owner != targetOwner.Owner))
            assignedTruck = EntityId.Invalid;

        if (assignedTruck.IsValid &&
            entities.TryGetComponent(
                assignedTruck,
                out CargoTransportRuntimeState transport))
        {
            state =
                transport.Lifecycle switch
                {
                    CargoTransportLifecycleState.Loading =>
                        PlayerDistributionActionState.Loading,
                    CargoTransportLifecycleState.ToOrigin or
                    CargoTransportLifecycleState.ToDestination =>
                        PlayerDistributionActionState.Traveling,
                    CargoTransportLifecycleState.Unloading =>
                        PlayerDistributionActionState.Unloading,
                    CargoTransportLifecycleState.Waiting =>
                        PlayerDistributionActionState.Waiting,
                    CargoTransportLifecycleState.Failed =>
                        PlayerDistributionActionState.Failed,
                    _ =>
                        PlayerDistributionActionState.Assigned
                };
            return;
        }

        state =
            selected.State switch
            {
                LogisticsTransportRequestState.Pending =>
                    PlayerDistributionActionState.Pending,
                LogisticsTransportRequestState.Assigned or
                LogisticsTransportRequestState.InTransit =>
                    PlayerDistributionActionState.Assigned,
                LogisticsTransportRequestState.RetryPending =>
                    PlayerDistributionActionState.Waiting,
                LogisticsTransportRequestState.Failed =>
                    PlayerDistributionActionState.Failed,
                _ =>
                    PlayerDistributionActionState.None
            };
    }

    private static void ResolveProviderState(
        EntityRegistry entities,
        MatchRuntime scenario,
        EntityId recipient,
        EntityId provider,
        out PlayerSupplyProviderState state,
        out double fuel,
        out double ammunition)
    {
        state = PlayerSupplyProviderState.None;
        fuel = 0.0;
        ammunition = 0.0;

        if (!provider.IsValid ||
            !entities.IsAlive(provider))
        {
            if (provider.IsValid)
            {
                state = PlayerSupplyProviderState.Blocked;
            }

            return;
        }

        InventoryId inventory =
            InventoryId.None;
        float range = 0.0f;

        if (entities.TryGetComponent(
                provider,
                out SupplyProvider staticProvider))
        {
            inventory = staticProvider.InventoryId;
            range = staticProvider.ResupplyRangeMeters;
        }
        else if (entities.TryGetComponent(
                     provider,
                     out SupplyTruck truck))
        {
            inventory = truck.InventoryId;
            range = truck.ResupplyRangeMeters;
        }

        if (scenario.Inventories.Contains(inventory))
        {
            fuel =
                scenario.Inventories.GetQuantity(
                    inventory,
                    ResourceIds.Fuel);
            ammunition =
                scenario.Inventories.GetQuantity(
                    inventory,
                    ResourceIds.Ammunition);
        }

        if (fuel <= 0.0 &&
            ammunition <= 0.0)
        {
            state = PlayerSupplyProviderState.Empty;
            return;
        }

        if (entities.TryGetComponent(
                recipient,
                out WorldTransform recipientTransform) &&
            entities.TryGetComponent(
                provider,
                out WorldTransform providerTransform) &&
            range > 0.0f)
        {
            float distanceSquared =
                System.Numerics.Vector3.DistanceSquared(
                    recipientTransform.Position,
                    providerTransform.Position);

            if (distanceSquared <=
                range * range)
            {
                state =
                    PlayerSupplyProviderState.Transferring;
                return;
            }

            state =
                PlayerSupplyProviderState.Traveling;
            return;
        }

        state = PlayerSupplyProviderState.Assigned;
    }

    private static EntityId ResolveSingleOwnedSelection(
        EntityRegistry entities,
        PlayerId player,
        IReadOnlyList<EntityId> selectedEntities)
    {
        if (selectedEntities.Count != 1)
        {
            return EntityId.Invalid;
        }

        EntityId entity =
            selectedEntities[0];

        return entities.IsAlive(entity) &&
               entities.TryGetComponent(
                   entity,
                   out ControllableEntity controllable) &&
               controllable.Owner == player
            ? entity
            : EntityId.Invalid;
    }

    private static PlayerActionResourceAmount CreateAmount(
        MatchRuntime scenario,
        InventoryId inventory,
        ResourceId resourceId,
        double requiredQuantity)
    {
        string displayName =
            scenario.Services.Resources.TryGet(
                resourceId,
                out ResourceDefinition? definition)
                ? definition.DisplayName
                : resourceId.ToString();

        double available =
            scenario.Inventories.Contains(inventory)
                ? scenario.Inventories.GetAvailableQuantity(
                    inventory,
                    resourceId)
                : 0.0;

        return new PlayerActionResourceAmount(
            resourceId,
            displayName,
            requiredQuantity,
            available);
    }
}
