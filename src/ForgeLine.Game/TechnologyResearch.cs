using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum TechnologyResearchStatus : byte
{
    Blocked = 0,
    Researching = 1
}

public enum TechnologyResearchBlockReason : byte
{
    None = 0,
    InvalidTechnology = 1,
    AlreadyCompleted = 2,
    UnmetPrerequisite = 3,
    MissingFacility = 4,
    MissingInventory = 5,
    MissingMaterials = 6,
    InsufficientPower = 7
}

public readonly record struct TechnologyResearchRequest
{
    public TechnologyResearchRequest(
        PlayerId player,
        TechnologyId technologyId,
        EntityId facility,
        InventoryId sourceInventory,
        SimulationTick submittedAtTick,
        uint progressTicks = 0,
        bool materialsConsumed = false,
        TechnologyResearchStatus status =
            TechnologyResearchStatus.Blocked,
        TechnologyResearchBlockReason blockReason =
            TechnologyResearchBlockReason.None)
    {
        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(player));
        }

        if (!technologyId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(technologyId));
        }

        if (!facility.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(facility));
        }

        if (!sourceInventory.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceInventory));
        }

        if (!Enum.IsDefined(status) ||
            !Enum.IsDefined(blockReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status));
        }

        Player = player;
        TechnologyId = technologyId;
        Facility = facility;
        SourceInventory = sourceInventory;
        SubmittedAtTick = submittedAtTick;
        ProgressTicks = progressTicks;
        MaterialsConsumed = materialsConsumed;
        Status = status;
        BlockReason = blockReason;
    }

    public PlayerId Player { get; }

    public TechnologyId TechnologyId { get; }

    public EntityId Facility { get; }

    public InventoryId SourceInventory { get; }

    public SimulationTick SubmittedAtTick { get; }

    public uint ProgressTicks { get; }

    public bool MaterialsConsumed { get; }

    public TechnologyResearchStatus Status { get; }

    public TechnologyResearchBlockReason BlockReason { get; }

    public TechnologyResearchRequest WithState(
        uint progressTicks,
        bool materialsConsumed,
        TechnologyResearchStatus status,
        TechnologyResearchBlockReason blockReason) =>
        new(
            Player,
            TechnologyId,
            Facility,
            SourceInventory,
            SubmittedAtTick,
            progressTicks,
            materialsConsumed,
            status,
            blockReason);
}

public readonly record struct TechnologyResearchCancellationRequest;

public readonly record struct CompletedTechnology(
    PlayerId Player,
    TechnologyId TechnologyId,
    SimulationTick CompletedAtTick);

public readonly record struct TechnologyCapabilityUnlock(
    PlayerId Player,
    TechnologyCapabilityId CapabilityId,
    TechnologyId SourceTechnology,
    SimulationTick UnlockedAtTick);

public readonly record struct TechnologyResearchMetrics(
    int ActiveRequests,
    int BlockedRequests,
    ulong CompletedTechnologies,
    ulong CancelledRequests);

public static class TechnologyStateQueries
{
    public static bool IsCompleted(
        EntityRegistry entities,
        PlayerId player,
        TechnologyId technologyId)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (EntityId entity in
                 entities.Query<CompletedTechnology>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedTechnology completed =
                entities.GetComponent<CompletedTechnology>(
                    entity);

            if (completed.Player == player &&
                completed.TechnologyId == technologyId)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCapabilityUnlocked(
        EntityRegistry entities,
        PlayerId player,
        TechnologyCapabilityId capabilityId)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!capabilityId.IsSpecified)
        {
            return true;
        }

        foreach (EntityId entity in
                 entities.Query<TechnologyCapabilityUnlock>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            TechnologyCapabilityUnlock unlock =
                entities.GetComponent<TechnologyCapabilityUnlock>(
                    entity);

            if (unlock.Player == player &&
                unlock.CapabilityId == capabilityId)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetActiveResearch(
        EntityRegistry entities,
        PlayerId player,
        out EntityId requestEntity,
        out TechnologyResearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (EntityId entity in
                 entities.Query<TechnologyResearchRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            TechnologyResearchRequest candidate =
                entities.GetComponent<TechnologyResearchRequest>(
                    entity);

            if (candidate.Player == player)
            {
                requestEntity = entity;
                request = candidate;
                return true;
            }
        }

        requestEntity = EntityId.Invalid;
        request = default;
        return false;
    }
}

public static class TechnologyResearchPolicy
{
    private const double QuantityTolerance = 1e-9;

    public static EntityId FindRequiredFacility(
        EntityRegistry entities,
        PlayerId player,
        BuildingId buildingId)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (EntityId entity in
                 entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building =
                entities.GetComponent<CompletedBuilding>(
                    entity);

            if (building.Owner == player &&
                building.BuildingId == buildingId)
            {
                return entity;
            }
        }

        return EntityId.Invalid;
    }

    public static TechnologyResearchBlockReason Evaluate(
        EntityRegistry entities,
        InventoryStore inventories,
        TechnologyDefinitionCatalog catalog,
        PlayerId player,
        TechnologyDefinition definition,
        EntityId facility,
        InventoryId sourceInventory,
        bool materialsConsumed)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definition);

        if (TechnologyStateQueries.IsCompleted(
                entities,
                player,
                definition.Id))
        {
            return TechnologyResearchBlockReason.AlreadyCompleted;
        }

        for (int index = 0;
             index < definition.Prerequisites.Count;
             index++)
        {
            if (!TechnologyStateQueries.IsCompleted(
                    entities,
                    player,
                    definition.Prerequisites[index]))
            {
                return TechnologyResearchBlockReason.UnmetPrerequisite;
            }
        }

        if (!facility.IsValid ||
            !entities.TryGetComponent(
                facility,
                out CompletedBuilding building) ||
            building.Owner != player ||
            building.BuildingId !=
                definition.RequiredFacility)
        {
            return TechnologyResearchBlockReason.MissingFacility;
        }

        if (definition.RequiredPowerFraction > 0.0)
        {
            if (!entities.TryGetComponent(
                    facility,
                    out PowerConsumer power) ||
                !power.Enabled ||
                power.SupplyFraction +
                    QuantityTolerance <
                definition.RequiredPowerFraction)
            {
                return TechnologyResearchBlockReason.InsufficientPower;
            }
        }

        if (!inventories.Contains(
                sourceInventory))
        {
            return TechnologyResearchBlockReason.MissingInventory;
        }

        if (!materialsConsumed)
        {
            for (int index = 0;
                 index < definition.Costs.Count;
                 index++)
            {
                TechnologyResourceCost cost =
                    definition.Costs[index];

                if (inventories.GetAvailableQuantity(
                        sourceInventory,
                        cost.ResourceId) +
                    QuantityTolerance <
                    cost.Quantity)
                {
                    return TechnologyResearchBlockReason.MissingMaterials;
                }
            }
        }

        return TechnologyResearchBlockReason.None;
    }
}

public sealed class TechnologyResearchSystem : ISimulationSystem
{
    private readonly TechnologyDefinitionCatalog _catalog;
    private readonly InventoryStore _inventories;
    private readonly List<EntityId> _requests = [];
    private ulong _completedTechnologies;
    private ulong _cancelledRequests;

    public TechnologyResearchSystem(
        TechnologyDefinitionCatalog catalog,
        InventoryStore inventories)
    {
        _catalog =
            catalog ??
            throw new ArgumentNullException(nameof(catalog));
        _inventories =
            inventories ??
            throw new ArgumentNullException(nameof(inventories));
    }

    public SimulationPhase Phase =>
        SimulationPhase.Production;

    public TechnologyResearchMetrics Metrics { get; private set; }

    public void Execute(
        SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        CollectRequests(
            context.Entities);
        ProcessCancellations(
            context.Entities);
        CollectRequests(
            context.Entities);

        int blocked = 0;

        for (int index = 0;
             index < _requests.Count;
             index++)
        {
            EntityId requestEntity =
                _requests[index];

            if (!context.Entities.IsAlive(
                    requestEntity) ||
                !context.Entities.TryGetComponent(
                    requestEntity,
                    out TechnologyResearchRequest request))
            {
                continue;
            }

            if (!_catalog.TryGet(
                    request.TechnologyId,
                    out TechnologyDefinition? definition))
            {
                context.Entities.SetComponent(
                    requestEntity,
                    request.WithState(
                        request.ProgressTicks,
                        request.MaterialsConsumed,
                        TechnologyResearchStatus.Blocked,
                        TechnologyResearchBlockReason.InvalidTechnology));
                blocked++;
                continue;
            }

            TechnologyResearchBlockReason block =
                TechnologyResearchPolicy.Evaluate(
                    context.Entities,
                    _inventories,
                    _catalog,
                    request.Player,
                    definition,
                    request.Facility,
                    request.SourceInventory,
                    request.MaterialsConsumed);

            if (block ==
                TechnologyResearchBlockReason.AlreadyCompleted)
            {
                context.Entities.DestroyEntity(
                    requestEntity);
                continue;
            }

            if (block !=
                TechnologyResearchBlockReason.None)
            {
                context.Entities.SetComponent(
                    requestEntity,
                    request.WithState(
                        request.ProgressTicks,
                        request.MaterialsConsumed,
                        TechnologyResearchStatus.Blocked,
                        block));
                blocked++;
                continue;
            }

            bool materialsConsumed =
                request.MaterialsConsumed;

            if (!materialsConsumed)
            {
                ConsumeMaterials(
                    request.SourceInventory,
                    definition);
                materialsConsumed = true;
            }

            uint progress =
                checked(
                    request.ProgressTicks +
                    1);

            if (progress >=
                definition.ResearchTicks)
            {
                Complete(
                    context,
                    requestEntity,
                    request.Player,
                    definition);
                continue;
            }

            context.Entities.SetComponent(
                requestEntity,
                request.WithState(
                    progress,
                    materialsConsumed,
                    TechnologyResearchStatus.Researching,
                    TechnologyResearchBlockReason.None));
        }

        Metrics =
            new TechnologyResearchMetrics(
                CountActiveRequests(
                    context.Entities),
                blocked,
                _completedTechnologies,
                _cancelledRequests);
    }

    private void CollectRequests(
        EntityRegistry entities)
    {
        _requests.Clear();

        foreach (EntityId entity in
                 entities.Query<TechnologyResearchRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _requests.Add(
                entity);
        }
    }

    private void ProcessCancellations(
        EntityRegistry entities)
    {
        for (int index = 0;
             index < _requests.Count;
             index++)
        {
            EntityId requestEntity =
                _requests[index];

            if (!entities.IsAlive(
                    requestEntity) ||
                !entities.HasComponent<
                    TechnologyResearchCancellationRequest>(
                        requestEntity))
            {
                continue;
            }

            entities.DestroyEntity(
                requestEntity);
            _cancelledRequests++;
        }
    }

    private void ConsumeMaterials(
        InventoryId inventory,
        TechnologyDefinition definition)
    {
        for (int index = 0;
             index < definition.Costs.Count;
             index++)
        {
            TechnologyResourceCost cost =
                definition.Costs[index];
            InventoryOperationResult result =
                _inventories.Remove(
                    inventory,
                    cost.ResourceId,
                    cost.Quantity);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Technology material consumption failed after requirement validation: {result.Failure}.");
            }
        }
    }

    private void Complete(
        SimulationContext context,
        EntityId requestEntity,
        PlayerId player,
        TechnologyDefinition definition)
    {
        EntityId completed =
            context.Entities.CreateEntity();
        context.Entities.AddComponent(
            completed,
            new CompletedTechnology(
                player,
                definition.Id,
                context.Tick));

        for (int index = 0;
             index < definition.Unlocks.Count;
             index++)
        {
            EntityId unlock =
                context.Entities.CreateEntity();
            context.Entities.AddComponent(
                unlock,
                new TechnologyCapabilityUnlock(
                    player,
                    definition.Unlocks[index],
                    definition.Id,
                    context.Tick));
        }

        if (context.Entities.IsAlive(
                requestEntity))
        {
            context.Entities.DestroyEntity(
                requestEntity);
        }

        _completedTechnologies++;
    }

    private static int CountActiveRequests(
        EntityRegistry entities)
    {
        int count = 0;

        foreach (EntityId _ in
                 entities.Query<TechnologyResearchRequest>())
        {
            count++;
        }

        return count;
    }
}
