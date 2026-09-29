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
    }

    public BuildingId BuildingId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs => _costs;

    public bool RequiresResourceDeposit { get; }

    public bool HasRequiredResources =>
        _costs.All(static cost => cost.IsAvailable);
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
    }

    public RecipeId RecipeId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Inputs => _inputs;

    public IReadOnlyList<PlayerActionResourceAmount> Outputs => _outputs;

    public bool HasInputs =>
        _inputs.All(static input => input.IsAvailable);
}

public readonly record struct PlayerProductionRequestReadModel(
    EntityId RequestEntity,
    RecipeId RecipeId,
    string DisplayName,
    ProductionPriority Priority,
    ProductionRequestMode Mode,
    bool Paused,
    bool IsActive);

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
        IReadOnlyList<PlayerProductionRequestReadModel> requests)
    {
        Entity = entity;
        ActiveRequest = activeRequest;
        ActiveRecipe = activeRecipe;
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

    public ProductionStatus Status { get; }

    public ProductionBlockReason BlockReason { get; }

    public double Progress { get; }

    public IReadOnlyList<PlayerProductionRecipeActionReadModel> Recipes =>
        _recipes;

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
        uint productionTicks)
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
    }

    public UnitId UnitId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs => _costs;

    public uint ProductionTicks { get; }

    public bool HasInputs =>
        _costs.All(static cost => cost.IsAvailable);
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
        IReadOnlyList<PlayerUnitProductionRequestReadModel> requests)
    {
        Entity = entity;
        ActiveRequest = activeRequest;
        ActiveUnit = activeUnit;
        Status = status;
        BlockReason = blockReason;
        Progress = progress;
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
}

public readonly record struct PlayerCargoStatusReadModel(
    EntityId Entity,
    CargoTransportLifecycleState Lifecycle,
    CargoTransportWaitReason WaitReason,
    CargoTransportFailureReason FailureReason,
    double CargoQuantity,
    double Capacity);

public sealed class PlayerLogisticsActionReadModel
{
    private readonly IReadOnlyList<PlayerStockPolicyActionReadModel> _policies;

    public PlayerLogisticsActionReadModel(
        EntityId entity,
        IReadOnlyList<PlayerStockPolicyActionReadModel> policies,
        PlayerCargoStatusReadModel? cargo)
    {
        Entity = entity;
        _policies =
            Array.AsReadOnly(
                policies?.ToArray() ??
                throw new ArgumentNullException(nameof(policies)));
        Cargo = cargo;
    }

    public EntityId Entity { get; }

    public IReadOnlyList<PlayerStockPolicyActionReadModel> Policies =>
        _policies;

    public PlayerCargoStatusReadModel? Cargo { get; }
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
    ResupplyProviderRejection ProviderRejections);

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
        IReadOnlyList<PlayerArtilleryActionReadModel> artillery)
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

        RequestedSelectionCount = requestedSelectionCount;
        CombatEligibleCount = combatEligibleCount;
        RejectedSelectionCount = rejectedSelectionCount;
        CriticalSupplyCount = criticalSupplyCount;
        ResupplyingCount = resupplyingCount;
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

    public bool HasCommonOrder { get; }

    public bool MixedOrderState { get; }

    public CombatOrderKind CurrentOrder { get; }

    public CombatOrderStatus CurrentStatus { get; }

    public IReadOnlyList<PlayerTacticalTargetReadModel> Targets =>
        _targets;

    public IReadOnlyList<PlayerArtilleryActionReadModel> Artillery =>
        _artillery;
}

public sealed class PlayerActionSnapshot
{
    private readonly IReadOnlyList<PlayerConstructionActionReadModel> _construction;

    public PlayerActionSnapshot(
        SimulationSessionId sessionId,
        SimulationTick tick,
        IReadOnlyList<PlayerConstructionActionReadModel> construction,
        int pendingCommandCount,
        PlayerProductionFacilityActionReadModel? production,
        PlayerUnitProductionFacilityActionReadModel? unitProduction,
        PlayerLogisticsActionReadModel? logistics = null,
        PlayerSupplyActionReadModel? supply = null,
        PlayerTacticalActionReadModel? tactical = null)
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
        UnitProduction = unitProduction;
        Logistics = logistics;
        Supply = supply;
        Tactical = tactical;
    }

    public SimulationSessionId SessionId { get; }

    public SimulationTick Tick { get; }

    public IReadOnlyList<PlayerConstructionActionReadModel> Construction =>
        _construction;

    public int PendingCommandCount { get; }

    public PlayerProductionFacilityActionReadModel? Production { get; }

    public PlayerUnitProductionFacilityActionReadModel? UnitProduction { get; }

    public PlayerLogisticsActionReadModel? Logistics { get; }

    public PlayerSupplyActionReadModel? Supply { get; }

    public PlayerTacticalActionReadModel? Tactical { get; }
}

internal static class PlayerActionSnapshotFactory
{
    public static PlayerActionSnapshot Capture(
        SimulationContext context,
        PresentationExtractionContext extraction,
        in PresentationInteractionRequestSnapshot interaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extraction);

        VerticalSliceScenario scenario =
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
                    selectedFacility)
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

        return new PlayerActionSnapshot(
            scenario.Simulation.SessionId,
            context.Tick,
            construction,
            extraction.Commands.PendingCount,
            production,
            unitProduction,
            logistics,
            supply,
            tactical);
    }

    private static PlayerConstructionActionReadModel[]
        CaptureConstructionActions(
            VerticalSliceScenario scenario,
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

        return actions.ToArray();
    }

    private static PlayerProductionFacilityActionReadModel
        CaptureProductionFacility(
            EntityRegistry entities,
            VerticalSliceScenario scenario,
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
                    requestEntity == facility.ActiveRequest));
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
            requests);
    }

    private static PlayerUnitProductionFacilityActionReadModel
        CaptureUnitProductionFacility(
            EntityRegistry entities,
            VerticalSliceScenario scenario,
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

            units.Add(
                new PlayerUnitProductionActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    costs,
                    definition.ProductionTicks));
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

        return new PlayerUnitProductionFacilityActionReadModel(
            entity,
            facility.ActiveRequest,
            facility.ActiveUnit,
            facility.Status,
            facility.BlockReason,
            progress,
            units,
            requests);
    }

    private static PlayerTacticalActionReadModel? CaptureTactical(
        EntityRegistry entities,
        VerticalSliceScenario scenario,
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
                    out ArtilleryWeaponDefinition? definition))
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
            artillery);
    }

    private static PlayerLogisticsActionReadModel? CaptureLogistics(
        EntityRegistry entities,
        VerticalSliceScenario scenario,
        EntityId entity)
    {
        bool hasInventory =
            TryResolveDistributionInventory(
                entities,
                entity,
                out InventoryId inventory);

        PlayerCargoStatusReadModel? cargo = null;
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
                    transport.Capacity);
        }

        if (!hasInventory)
        {
            return cargo.HasValue
                ? new PlayerLogisticsActionReadModel(
                    entity,
                    [],
                    cargo)
                : null;
        }

        var policies =
            new List<PlayerStockPolicyActionReadModel>();

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
            double target =
                hasPolicy
                    ? policy.DesiredTarget
                    : Math.Max(
                        10.0,
                        Math.Ceiling(currentQuantity));
            double minimum =
                hasPolicy
                    ? policy.DesiredMinimum
                    : Math.Min(
                        target,
                        target * 0.5);
            double maximum =
                hasPolicy
                    ? policy.DesiredMaximum
                    : Math.Max(
                        target,
                        target * 2.0);

            ResolveDistributionStatus(
                scenario,
                entities,
                policyEntity,
                out PlayerDistributionActionState distributionState,
                out LogisticsTransportRequestFailureReason failure,
                out LogisticsBottleneckReason bottleneck,
                out EntityId assignedTruck);

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
                    assignedTruck));
        }

        return new PlayerLogisticsActionReadModel(
            entity,
            policies,
            cargo);
    }

    private static PlayerSupplyActionReadModel? CaptureSupply(
        EntityRegistry entities,
        VerticalSliceScenario scenario,
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
            rejections);
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
        VerticalSliceScenario scenario,
        EntityRegistry entities,
        EntityId policyEntity,
        out PlayerDistributionActionState state,
        out LogisticsTransportRequestFailureReason failure,
        out LogisticsBottleneckReason bottleneck,
        out EntityId assignedTruck)
    {
        state = PlayerDistributionActionState.None;
        failure = LogisticsTransportRequestFailureReason.None;
        bottleneck = LogisticsBottleneckReason.None;
        assignedTruck = EntityId.Invalid;

        if (!policyEntity.IsValid)
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
        failure = selected.FailureReason;
        bottleneck = selected.BottleneckReason;
        assignedTruck = selected.AssignedTruck;

        if (selected.AssignedTruck.IsValid &&
            entities.TryGetComponent(
                selected.AssignedTruck,
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
        VerticalSliceScenario scenario,
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
        VerticalSliceScenario scenario,
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
                ? scenario.Inventories.GetQuantity(
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
