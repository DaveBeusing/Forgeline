using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum BuildingDemolitionFailure : byte
{
    None = 0,
    InvalidTarget = 1,
    NotOwned = 2,
    ProtectedCommandCore = 3,
    MissingPaymentRecord = 4,
    CommandCoreUnavailable = 5,
    InventoryNotEmpty = 6,
    InsufficientRefundCapacity = 7,
    ConstructionAlreadyCancelling = 8,
    ProductionActive = 9
}

public readonly record struct BuildingDemolitionTargetResult(
    EntityId Target,
    BuildingDemolitionFailure Failure,
    BuildingResourceCost[] Refunded)
{
    public bool Accepted => Failure == BuildingDemolitionFailure.None;
}

public readonly record struct BuildingDemolitionBatchResult(
    PlayerId Issuer,
    PlayerCommandCorrelationId CorrelationId,
    SimulationTick ResolvedAtTick,
    BuildingDemolitionTargetResult[] Targets)
{
    public int AcceptedTargets => Targets.Count(target => target.Accepted);
    public int RejectedTargets => Targets.Length - AcceptedTargets;
}

internal readonly record struct BuildingDemolitionRequest(
    PlayerId Issuer,
    EntityId[] Targets,
    SimulationTick SubmittedAtTick,
    PlayerCommandCorrelationId CorrelationId);

public sealed class DemolishBuildingsCommand : ISimulationCommand
{
    public DemolishBuildingsCommand(
        PlayerId issuer,
        IReadOnlyList<EntityId> targets,
        SimulationTick submittedAtTick,
        PlayerCommandCorrelationId correlationId = default)
    {
        if (!issuer.IsSpecified)
            throw new ArgumentOutOfRangeException(nameof(issuer));
        ArgumentNullException.ThrowIfNull(targets);
        Issuer = issuer;
        Targets = targets.Distinct().Order().ToArray();
        SubmittedAtTick = submittedAtTick;
        CorrelationId = correlationId;
    }

    public PlayerId Issuer { get; }
    public EntityId[] Targets { get; }
    public SimulationTick SubmittedAtTick { get; }
    public PlayerCommandCorrelationId CorrelationId { get; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EntityId request = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new BuildingDemolitionRequest(Issuer, Targets, SubmittedAtTick, CorrelationId));
    }
}

/// <summary>
/// Processes ordered demolition requests after command submission, before
/// construction and production. No presentation state or wall-clock time is read.
/// </summary>
public sealed class BuildingDemolitionSystem : ISimulationSystem
{
    private readonly InventoryStore _inventories;
    private readonly SpatialGridIndex _spatialIndex;
    private readonly IReadOnlyDictionary<PlayerId, EntityId> _commandCores;
    private readonly List<EntityId> _requests = [];
    private readonly Dictionary<PlayerCommandCorrelationId, BuildingDemolitionBatchResult> _results = [];

    public BuildingDemolitionSystem(
        InventoryStore inventories,
        SpatialGridIndex spatialIndex,
        IReadOnlyDictionary<PlayerId, EntityId> commandCores)
    {
        _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
        _spatialIndex = spatialIndex ?? throw new ArgumentNullException(nameof(spatialIndex));
        _commandCores = commandCores ?? throw new ArgumentNullException(nameof(commandCores));
    }

    public SimulationPhase Phase => SimulationPhase.OrderProcessing;

    public bool TryTakeResult(
        PlayerCommandCorrelationId correlationId,
        out BuildingDemolitionBatchResult result) =>
        _results.Remove(correlationId, out result);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _requests.Clear();
        foreach (EntityId request in context.Entities.Query<BuildingDemolitionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
            _requests.Add(request);

        foreach (EntityId requestEntity in _requests)
        {
            if (!context.Entities.TryGetComponent(
                    requestEntity, out BuildingDemolitionRequest request))
                continue;

            BuildingDemolitionTargetResult[] outcomes =
                new BuildingDemolitionTargetResult[request.Targets.Length];
            for (int index = 0; index < outcomes.Length; index++)
                outcomes[index] = ProcessTarget(context, request.Issuer, request.Targets[index]);

            if (request.CorrelationId.IsSpecified)
                _results[request.CorrelationId] =
                    new BuildingDemolitionBatchResult(
                        request.Issuer, request.CorrelationId, context.Tick, outcomes);

            context.Entities.DestroyEntity(requestEntity);
        }
    }

    private BuildingDemolitionTargetResult ProcessTarget(
        SimulationContext context,
        PlayerId issuer,
        EntityId entity)
    {
        BuildingDemolitionTargetResult Fail(BuildingDemolitionFailure reason) =>
            new(entity, reason, []);

        if (!context.Entities.IsAlive(entity) ||
            !context.Entities.TryGetComponent(entity, out ControllableEntity controlled) ||
            controlled.Category != ControllableEntityCategory.Building)
            return Fail(BuildingDemolitionFailure.InvalidTarget);

        if (controlled.Owner != issuer)
            return Fail(BuildingDemolitionFailure.NotOwned);

        if (context.Entities.TryGetComponent(entity, out ConstructionSite site))
        {
            if (site.Owner != issuer)
                return Fail(BuildingDemolitionFailure.NotOwned);
            if (context.Entities.HasComponent<ConstructionCancellationRequest>(entity))
                return Fail(BuildingDemolitionFailure.ConstructionAlreadyCancelling);

            context.Entities.AddComponent(
                entity,
                new ConstructionCancellationRequest(issuer, context.Tick, context.Tick));
            return new BuildingDemolitionTargetResult(entity, BuildingDemolitionFailure.None, []);
        }

        if (!context.Entities.TryGetComponent(entity, out CompletedBuilding completed))
            return Fail(BuildingDemolitionFailure.InvalidTarget);
        if (completed.Owner != issuer)
            return Fail(BuildingDemolitionFailure.NotOwned);
        if (completed.BuildingId == BuildingIds.CommandCore)
            return Fail(BuildingDemolitionFailure.ProtectedCommandCore);

        if (!context.Entities.TryGetComponent(entity, out PaidBuildingMaterials paid))
        {
            // Initial map structures have no construction transaction. Older
            // post-start buildings without a receipt must not inherit today's prices.
            if (completed.CompletedAtTick != SimulationTick.Zero)
                return Fail(BuildingDemolitionFailure.MissingPaymentRecord);
            paid = new PaidBuildingMaterials([]);
        }

        if (!_commandCores.TryGetValue(issuer, out EntityId core) ||
            core == entity ||
            !context.Entities.IsAlive(core) ||
            !context.Entities.TryGetComponent(core, out CompletedBuilding coreBuilding) ||
            coreBuilding.Owner != issuer ||
            coreBuilding.BuildingId != BuildingIds.CommandCore ||
            !context.Entities.TryGetComponent(core, out InventoryStorage coreStorage) ||
            !_inventories.Contains(coreStorage.InventoryId))
            return Fail(BuildingDemolitionFailure.CommandCoreUnavailable);

        // Refuse removal while production is committed to this facility.
        foreach (EntityId request in context.Entities.Query<ProductionRequest>())
            if (context.Entities.GetComponent<ProductionRequest>(request).Facility == entity)
                return Fail(BuildingDemolitionFailure.ProductionActive);
        foreach (EntityId request in context.Entities.Query<UnitProductionRequest>())
            if (context.Entities.GetComponent<UnitProductionRequest>(request).Facility == entity)
                return Fail(BuildingDemolitionFailure.ProductionActive);

        // Inventory contents are not construction materials: never silently
        // delete stock or reservations when removing a storage/production site.
        HashSet<InventoryId> attachedInventories = [];
        if (context.Entities.TryGetComponent(entity, out InventoryStorage storage))
            attachedInventories.Add(storage.InventoryId);
        if (context.Entities.TryGetComponent(entity, out ProductionFacility production))
        {
            attachedInventories.Add(production.InputInventory);
            attachedInventories.Add(production.OutputInventory);
        }
        if (context.Entities.TryGetComponent(entity, out UnitProductionFacility unitProduction))
            attachedInventories.Add(unitProduction.InputInventory);

        foreach (InventoryId inventoryId in attachedInventories)
            if (!_inventories.Contains(inventoryId) ||
                _inventories.GetResourceQuantities(inventoryId).Length != 0)
                return Fail(BuildingDemolitionFailure.InventoryNotEmpty);

        Dictionary<ResourceId, double> totals = [];
        foreach (BuildingResourceCost cost in paid.Items)
        {
            if (totals.TryGetValue(cost.ResourceId, out double previous))
                totals[cost.ResourceId] = previous + cost.Quantity * 0.40;
            else
                totals.Add(cost.ResourceId, cost.Quantity * 0.40);
        }

        BuildingResourceCost[] refunded = totals
            .OrderBy(entry => entry.Key)
            .Select(entry => new BuildingResourceCost(entry.Key, entry.Value))
            .ToArray();
        double aggregate = refunded.Sum(item => item.Quantity);
        if (_inventories.GetRemainingCapacity(coreStorage.InventoryId) < aggregate ||
            refunded.Any(item => !_inventories.CanAdd(
                coreStorage.InventoryId, item.ResourceId, item.Quantity)))
            return Fail(BuildingDemolitionFailure.InsufficientRefundCapacity);

        foreach (BuildingResourceCost item in refunded)
        {
            InventoryOperationResult result = _inventories.Add(
                coreStorage.InventoryId, item.ResourceId, item.Quantity);
            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "DEMOLITION_REFUND_COMMIT_FAILED",
                "Validated demolition refund could not be committed.");
        }

        // Remove stock policies owned by this building; ordinary logistics
        // registration removes stale nodes on the same simulation tick.
        List<EntityId> policies = [];
        foreach (EntityId policy in context.Entities.Query<LogisticsStockPolicy>())
            if (context.Entities.GetComponent<LogisticsStockPolicy>(policy).TargetEntity == entity)
                policies.Add(policy);
        foreach (EntityId policy in policies)
            context.Entities.DestroyEntity(policy);

        _spatialIndex.Remove(entity);
        context.Entities.DestroyEntity(entity);
        foreach (InventoryId inventoryId in attachedInventories)
            _inventories.DestroyInventory(inventoryId);

        return new BuildingDemolitionTargetResult(entity, BuildingDemolitionFailure.None, refunded);
    }
}
