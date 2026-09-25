using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct BattlefieldSupplyMetrics(
    int SuppliedUnitCount,
    int LowSupplyUnitCount,
    int CriticalUnitCount,
    int UnsuppliedUnitCount,
    int ProviderCount,
    int SupplyDepotCount,
    int SupplyTruckCount,
    double FuelTransferredThisTick,
    double AmmunitionTransferredThisTick,
    double TotalFuelTransferred,
    double TotalAmmunitionTransferred);

public readonly record struct BattlefieldSupplyUnitReadModel(
    EntityId Entity,
    BattlefieldSupplyStatus Status,
    BattlefieldSupplyPriority Priority,
    double FuelFraction,
    double AmmunitionFraction,
    Vector3 WorldPosition,
    EntityId ResupplyProvider);

public readonly record struct BattlefieldSupplyProviderReadModel(
    EntityId Entity,
    bool IsDepot,
    bool IsTruck,
    double FuelQuantity,
    double AmmunitionQuantity,
    float ResupplyRangeMeters,
    Vector3 WorldPosition);

public sealed class BattlefieldSupplyDebugSnapshot
{
    private readonly BattlefieldSupplyUnitReadModel[] _units;
    private readonly BattlefieldSupplyProviderReadModel[] _providers;

    internal BattlefieldSupplyDebugSnapshot(
        BattlefieldSupplyMetrics metrics,
        BattlefieldSupplyUnitReadModel[] units,
        BattlefieldSupplyProviderReadModel[] providers)
    {
        Metrics = metrics;
        _units = units;
        _providers = providers;
    }

    public static BattlefieldSupplyDebugSnapshot Empty { get; } =
        new(default, [], []);

    public BattlefieldSupplyMetrics Metrics { get; }

    public IReadOnlyList<BattlefieldSupplyUnitReadModel> Units => _units;

    public IReadOnlyList<BattlefieldSupplyProviderReadModel> Providers =>
        _providers;
}

public sealed class BattlefieldSupplySystem : ISimulationSystem
{
    private const double QuantityEpsilon = 0.000000001;

    private readonly InventoryStore _inventories;
    private readonly List<RecipientCandidate> _recipients = new();
    private readonly List<ProviderCandidate> _providers = new();
    private double _totalFuelTransferred;
    private double _totalAmmunitionTransferred;

    public BattlefieldSupplySystem(InventoryStore inventories)
    {
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
    }

    public SimulationPhase Phase => SimulationPhase.Supply;

    public BattlefieldSupplyMetrics Metrics { get; private set; }

    public BattlefieldSupplyDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = BattlefieldSupplyDebugSnapshot.Empty;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ConsumeMovementFuel(context);
        double loadedFuel = 0.0;
        double loadedAmmunition = 0.0;
        LoadSupplyTrucks(
            context,
            ref loadedFuel,
            ref loadedAmmunition);

        CaptureProviders(context);
        CaptureRecipients(context);

        double transferredFuel = loadedFuel;
        double transferredAmmunition = loadedAmmunition;
        ResupplyRecipients(
            context,
            ref transferredFuel,
            ref transferredAmmunition);

        _totalFuelTransferred += transferredFuel;
        _totalAmmunitionTransferred += transferredAmmunition;

        UpdateOperationalStatesAndDiagnostics(
            context,
            transferredFuel,
            transferredAmmunition);
    }

    private void ConsumeMovementFuel(SimulationContext context)
    {
        var units = new List<EntityId>();

        foreach (EntityId entity in
                 context.Entities.Query<UnitFuelState>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (context.Entities.HasComponent<WorldTransform>(entity))
            {
                units.Add(entity);
            }
        }

        for (int index = 0; index < units.Count; index++)
        {
            EntityId entity = units[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out UnitFuelState fuel) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform) ||
                !_inventories.Contains(fuel.InventoryId))
            {
                continue;
            }

            if (fuel.HasObservedPosition)
            {
                double distance =
                    HorizontalDistance(
                        fuel.ObservedPosition,
                        transform.Position);
                double requested =
                    distance * fuel.ConsumptionPerMeter;
                double available =
                    _inventories.GetAvailableQuantity(
                        fuel.InventoryId,
                        ResourceIds.Fuel);
                double consumed =
                    Math.Min(
                        requested,
                        available);

                if (consumed > QuantityEpsilon)
                {
                    InventoryOperationResult result =
                        _inventories.Remove(
                            fuel.InventoryId,
                            ResourceIds.Fuel,
                            consumed);
                    EngineInvariant.Require(
                        result.Succeeded,
                        DiagnosticCategory.Simulation,
                        "BATTLEFIELD_FUEL_CONSUMPTION_FAILED",
                        $"Validated fuel consumption failed for entity {entity}.");
                }
            }

            context.Entities.SetComponent(
                entity,
                fuel.WithObservedPosition(
                    transform.Position));
        }
    }

    private void LoadSupplyTrucks(
        SimulationContext context,
        ref double fuelTransferred,
        ref double ammunitionTransferred)
    {
        var trucks = new List<EntityId>();

        foreach (EntityId entity in
                 context.Entities.Query<SupplyTruck>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            trucks.Add(entity);
        }

        for (int index = 0; index < trucks.Count; index++)
        {
            EntityId truckEntity = trucks[index];

            if (!context.Entities.TryGetComponent(
                    truckEntity,
                    out SupplyTruck truck) ||
                !context.Entities.TryGetComponent(
                    truckEntity,
                    out WorldTransform truckTransform) ||
                !_inventories.Contains(truck.InventoryId))
            {
                continue;
            }

            if (!TryFindLoadingDepot(
                    context,
                    truck,
                    truckTransform.Position,
                    out SupplyDepot depot))
            {
                continue;
            }

            fuelTransferred +=
                TransferTowardTarget(
                    depot.InventoryId,
                    truck.InventoryId,
                    ResourceIds.Fuel,
                    truck.FuelTarget);

            ammunitionTransferred +=
                TransferTowardTarget(
                    depot.InventoryId,
                    truck.InventoryId,
                    ResourceIds.Ammunition,
                    truck.AmmunitionTarget);
        }
    }

    private bool TryFindLoadingDepot(
        SimulationContext context,
        in SupplyTruck truck,
        Vector3 truckPosition,
        out SupplyDepot selectedDepot)
    {
        selectedDepot = default;
        EntityId selectedEntity = EntityId.Invalid;
        float bestDistanceSquared = float.PositiveInfinity;
        float maximumDistanceSquared =
            truck.LoadRangeMeters * truck.LoadRangeMeters;

        foreach (EntityId entity in
                 context.Entities.Query<SupplyDepot>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            SupplyDepot depot =
                context.Entities.GetComponent<SupplyDepot>(entity);

            if (depot.State != SupplyDepotState.Operational ||
                depot.Owner != truck.Owner ||
                !_inventories.Contains(depot.InventoryId) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                continue;
            }

            float distanceSquared =
                HorizontalDistanceSquared(
                    truckPosition,
                    transform.Position);

            if (distanceSquared > maximumDistanceSquared)
            {
                continue;
            }

            if (!selectedEntity.IsValid ||
                distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared &&
                 entity < selectedEntity))
            {
                selectedEntity = entity;
                selectedDepot = depot;
                bestDistanceSquared = distanceSquared;
            }
        }

        return selectedEntity.IsValid;
    }

    private double TransferTowardTarget(
        InventoryId source,
        InventoryId destination,
        ResourceId resource,
        double targetQuantity)
    {
        if (!_inventories.Contains(source) ||
            !_inventories.Contains(destination))
        {
            return 0.0;
        }

        double current =
            _inventories.GetQuantity(
                destination,
                resource);
        double deficit =
            Math.Max(
                0.0,
                targetQuantity - current);

        return TransferAvailable(
            source,
            destination,
            resource,
            deficit);
    }

    private void CaptureProviders(SimulationContext context)
    {
        _providers.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<SupplyProvider>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            SupplyProvider provider =
                context.Entities.GetComponent<SupplyProvider>(entity);

            if (!provider.Enabled ||
                !_inventories.Contains(provider.InventoryId) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                continue;
            }

            bool isDepot =
                context.Entities.TryGetComponent(
                    entity,
                    out SupplyDepot depot);
            if (isDepot &&
                depot.State != SupplyDepotState.Operational)
            {
                continue;
            }

            _providers.Add(
                new ProviderCandidate(
                    entity,
                    provider,
                    transform.Position,
                    isDepot,
                    context.Entities.HasComponent<SupplyTruck>(entity)));
        }
    }

    private void CaptureRecipients(SimulationContext context)
    {
        _recipients.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<ControllableEntity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                context.Entities.GetComponent<ControllableEntity>(entity);

            if (!controllable.IsControllable ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                continue;
            }

            bool hasFuel =
                context.Entities.HasComponent<UnitFuelState>(entity);
            bool hasAmmunition =
                context.Entities.HasComponent<AmmunitionState>(entity);

            if (!hasFuel && !hasAmmunition)
            {
                continue;
            }

            BattlefieldSupplyPriority priority =
                context.Entities.TryGetComponent(
                    entity,
                    out UnitSupplyPriority configuredPriority)
                    ? configuredPriority.Priority
                    : BattlefieldSupplyPriority.Normal;

            _recipients.Add(
                new RecipientCandidate(
                    entity,
                    controllable.Owner,
                    transform.Position,
                    priority,
                    hasFuel,
                    hasAmmunition));
        }

        _recipients.Sort(
            static (left, right) =>
            {
                int priority =
                    left.Priority.CompareTo(right.Priority);
                return priority != 0
                    ? priority
                    : left.Entity.CompareTo(right.Entity);
            });
    }

    private void ResupplyRecipients(
        SimulationContext context,
        ref double fuelTransferred,
        ref double ammunitionTransferred)
    {
        for (int index = 0; index < _recipients.Count; index++)
        {
            RecipientCandidate recipient =
                _recipients[index];

            EntityId preferredProvider =
                context.Entities.TryGetComponent(
                    recipient.Entity,
                    out ResupplyOrder order)
                    ? order.Provider
                    : EntityId.Invalid;

            if (recipient.HasFuel &&
                context.Entities.TryGetComponent(
                    recipient.Entity,
                    out UnitFuelState fuel) &&
                _inventories.Contains(fuel.InventoryId))
            {
                double deficit =
                    Math.Max(
                        0.0,
                        fuel.Capacity -
                        _inventories.GetQuantity(
                            fuel.InventoryId,
                            ResourceIds.Fuel));

                if (deficit > QuantityEpsilon &&
                    TrySelectProvider(
                        recipient,
                        ResourceIds.Fuel,
                        preferredProvider,
                        out ProviderCandidate provider))
                {
                    fuelTransferred +=
                        TransferAvailable(
                            provider.Provider.InventoryId,
                            fuel.InventoryId,
                            ResourceIds.Fuel,
                            deficit);
                }
            }

            if (recipient.HasAmmunition &&
                context.Entities.TryGetComponent(
                    recipient.Entity,
                    out AmmunitionState ammunition) &&
                _inventories.Contains(ammunition.InventoryId))
            {
                double deficit =
                    Math.Max(
                        0.0,
                        ammunition.Capacity -
                        _inventories.GetQuantity(
                            ammunition.InventoryId,
                            ResourceIds.Ammunition));

                if (deficit > QuantityEpsilon &&
                    TrySelectProvider(
                        recipient,
                        ResourceIds.Ammunition,
                        preferredProvider,
                        out ProviderCandidate provider))
                {
                    ammunitionTransferred +=
                        TransferAvailable(
                            provider.Provider.InventoryId,
                            ammunition.InventoryId,
                            ResourceIds.Ammunition,
                            deficit);
                }
            }
        }
    }

    private bool TrySelectProvider(
        in RecipientCandidate recipient,
        ResourceId resource,
        EntityId preferredProvider,
        out ProviderCandidate selected)
    {
        selected = default;
        float bestDistanceSquared = float.PositiveInfinity;

        for (int index = 0; index < _providers.Count; index++)
        {
            ProviderCandidate candidate =
                _providers[index];

            if (candidate.Provider.Owner != recipient.Owner ||
                _inventories.GetAvailableQuantity(
                    candidate.Provider.InventoryId,
                    resource) <= QuantityEpsilon)
            {
                continue;
            }

            float distanceSquared =
                HorizontalDistanceSquared(
                    recipient.Position,
                    candidate.Position);
            float maximumDistanceSquared =
                candidate.Provider.ResupplyRangeMeters *
                candidate.Provider.ResupplyRangeMeters;

            if (distanceSquared > maximumDistanceSquared)
            {
                continue;
            }

            if (preferredProvider.IsValid)
            {
                if (candidate.Entity == preferredProvider)
                {
                    selected = candidate;
                    return true;
                }

                continue;
            }

            if (!selected.Entity.IsValid ||
                distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared &&
                 candidate.Entity < selected.Entity))
            {
                selected = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        return selected.Entity.IsValid;
    }

    private double TransferAvailable(
        InventoryId source,
        InventoryId destination,
        ResourceId resource,
        double requestedQuantity)
    {
        if (requestedQuantity <= QuantityEpsilon ||
            !_inventories.Contains(source) ||
            !_inventories.Contains(destination))
        {
            return 0.0;
        }

        double available =
            _inventories.GetAvailableQuantity(
                source,
                resource);
        double addable =
            _inventories.GetAddableQuantity(
                destination,
                resource,
                requestedQuantity);
        double quantity =
            Math.Min(
                requestedQuantity,
                Math.Min(
                    available,
                    addable));

        if (quantity <= QuantityEpsilon)
        {
            return 0.0;
        }

        InventoryOperationResult result =
            _inventories.Transfer(
                source,
                destination,
                resource,
                quantity);

        EngineInvariant.Require(
            result.Succeeded,
            DiagnosticCategory.Simulation,
            "BATTLEFIELD_SUPPLY_TRANSFER_FAILED",
            $"Validated battlefield supply transfer failed for resource {resource}.");

        return quantity;
    }

    private void UpdateOperationalStatesAndDiagnostics(
        SimulationContext context,
        double fuelTransferred,
        double ammunitionTransferred)
    {
        int supplied = 0;
        int low = 0;
        int critical = 0;
        int unsupplied = 0;

        var unitModels =
            new BattlefieldSupplyUnitReadModel[_recipients.Count];

        for (int index = 0; index < _recipients.Count; index++)
        {
            RecipientCandidate recipient = _recipients[index];

            double fuelFraction = 1.0;
            if (recipient.HasFuel &&
                context.Entities.TryGetComponent(
                    recipient.Entity,
                    out UnitFuelState fuel))
            {
                fuelFraction =
                    GetResourceFraction(
                        fuel.InventoryId,
                        ResourceIds.Fuel,
                        fuel.Capacity);

                SupplyMovementConstraint constraint =
                    BattlefieldSupplyFactory.CreateMovementConstraint(
                        fuelFraction);

                if (context.Entities.HasComponent<
                        SupplyMovementConstraint>(recipient.Entity))
                {
                    context.Entities.SetComponent(
                        recipient.Entity,
                        constraint);
                }
                else
                {
                    context.Entities.AddComponent(
                        recipient.Entity,
                        constraint);
                }
            }

            double ammunitionFraction = 1.0;
            if (recipient.HasAmmunition &&
                context.Entities.TryGetComponent(
                    recipient.Entity,
                    out AmmunitionState ammunition))
            {
                ammunitionFraction =
                    GetResourceFraction(
                        ammunition.InventoryId,
                        ResourceIds.Ammunition,
                        ammunition.Capacity);
            }

            BattlefieldSupplyStatus status =
                BattlefieldSupplyFactory.ResolveStatus(
                    fuelFraction,
                    ammunitionFraction);

            var state =
                new UnitSupplyState(
                    fuelFraction,
                    ammunitionFraction,
                    status,
                    context.Tick);

            if (context.Entities.HasComponent<UnitSupplyState>(
                    recipient.Entity))
            {
                context.Entities.SetComponent(
                    recipient.Entity,
                    state);
            }
            else
            {
                context.Entities.AddComponent(
                    recipient.Entity,
                    state);
            }

            switch (status)
            {
                case BattlefieldSupplyStatus.Supplied:
                    supplied++;
                    CompleteResupplyOrderIfPresent(
                        context,
                        recipient.Entity);
                    break;
                case BattlefieldSupplyStatus.LowSupply:
                    low++;
                    break;
                case BattlefieldSupplyStatus.Critical:
                    critical++;
                    break;
                case BattlefieldSupplyStatus.Unsupplied:
                    unsupplied++;
                    break;
            }

            EntityId provider =
                context.Entities.TryGetComponent(
                    recipient.Entity,
                    out ResupplyOrder resupplyOrder)
                    ? resupplyOrder.Provider
                    : EntityId.Invalid;

            unitModels[index] =
                new BattlefieldSupplyUnitReadModel(
                    recipient.Entity,
                    status,
                    recipient.Priority,
                    fuelFraction,
                    ammunitionFraction,
                    recipient.Position,
                    provider);
        }

        var providerModels =
            new BattlefieldSupplyProviderReadModel[_providers.Count];
        int depotCount = 0;
        int truckCount = 0;

        for (int index = 0; index < _providers.Count; index++)
        {
            ProviderCandidate provider =
                _providers[index];

            if (provider.IsDepot)
            {
                depotCount++;
            }

            if (provider.IsTruck)
            {
                truckCount++;
            }

            providerModels[index] =
                new BattlefieldSupplyProviderReadModel(
                    provider.Entity,
                    provider.IsDepot,
                    provider.IsTruck,
                    _inventories.GetQuantity(
                        provider.Provider.InventoryId,
                        ResourceIds.Fuel),
                    _inventories.GetQuantity(
                        provider.Provider.InventoryId,
                        ResourceIds.Ammunition),
                    provider.Provider.ResupplyRangeMeters,
                    provider.Position);
        }

        Metrics =
            new BattlefieldSupplyMetrics(
                supplied,
                low,
                critical,
                unsupplied,
                _providers.Count,
                depotCount,
                truckCount,
                fuelTransferred,
                ammunitionTransferred,
                _totalFuelTransferred,
                _totalAmmunitionTransferred);

        LastDebugSnapshot =
            new BattlefieldSupplyDebugSnapshot(
                Metrics,
                unitModels,
                providerModels);
    }

    private double GetResourceFraction(
        InventoryId inventory,
        ResourceId resource,
        double capacity)
    {
        if (!_inventories.Contains(inventory) ||
            capacity <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp(
            _inventories.GetQuantity(
                inventory,
                resource) / capacity,
            0.0,
            1.0);
    }

    private static void CompleteResupplyOrderIfPresent(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.HasComponent<ResupplyOrder>(entity))
        {
            return;
        }

        context.Entities.RemoveComponent<ResupplyOrder>(entity);

        if (context.Entities.HasComponent<MovementOrder>(entity))
        {
            context.Entities.RemoveComponent<MovementOrder>(entity);
        }

        if (context.Entities.HasComponent<NavigationPendingPath>(entity))
        {
            context.Entities.RemoveComponent<NavigationPendingPath>(entity);
        }

        if (context.Entities.HasComponent<NavigationRouteState>(entity))
        {
            context.Entities.RemoveComponent<NavigationRouteState>(entity);
        }

        if (context.Entities.HasComponent<NavigationFailureState>(entity))
        {
            context.Entities.RemoveComponent<NavigationFailureState>(entity);
        }
    }

    private static double HorizontalDistance(
        Vector3 left,
        Vector3 right) =>
        Math.Sqrt(
            HorizontalDistanceSquared(
                left,
                right));

    private static float HorizontalDistanceSquared(
        Vector3 left,
        Vector3 right)
    {
        float x = left.X - right.X;
        float z = left.Z - right.Z;
        return x * x + z * z;
    }

    private readonly record struct RecipientCandidate(
        EntityId Entity,
        PlayerId Owner,
        Vector3 Position,
        BattlefieldSupplyPriority Priority,
        bool HasFuel,
        bool HasAmmunition);

    private readonly record struct ProviderCandidate(
        EntityId Entity,
        SupplyProvider Provider,
        Vector3 Position,
        bool IsDepot,
        bool IsTruck);
}
