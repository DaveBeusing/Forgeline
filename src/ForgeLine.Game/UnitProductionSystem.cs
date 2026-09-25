using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class UnitProductionSystem : ISimulationSystem
{
    private const double QuantityTolerance = 1e-9;

    private readonly UnitDefinitionCatalog _units;
    private readonly InventoryStore _inventories;
    private readonly UnitFactory _unitFactory;
    private readonly List<EntityId> _requestEntities = new();
    private readonly List<EntityId> _facilityEntities = new();
    private readonly List<UnitProductionFacilityReadModel> _readModels =
        new();

    private long _completedUnits;
    private long _cancelledRequests;
    private long _rejectedRequests;

    public UnitProductionSystem(
        UnitDefinitionCatalog units,
        InventoryStore inventories,
        UnitFactory unitFactory)
    {
        _units = units ??
            throw new ArgumentNullException(nameof(units));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _unitFactory = unitFactory ??
            throw new ArgumentNullException(nameof(unitFactory));
    }

    public SimulationPhase Phase => SimulationPhase.Production;

    public IReadOnlyList<UnitProductionFacilityReadModel> Facilities =>
        _readModels;

    public UnitProductionMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        CollectRequests(context.Entities);
        ProcessCancellations(context.Entities);
        CollectRequests(context.Entities);
        RejectInvalidRequests(context.Entities);
        CollectRequests(context.Entities);
        CollectFacilities(context.Entities);

        _readModels.Clear();

        int running = 0;
        int blocked = 0;

        for (int index = 0; index < _facilityEntities.Count; index++)
        {
            EntityId facilityEntity = _facilityEntities[index];
            if (!context.Entities.IsAlive(facilityEntity) ||
                !context.Entities.TryGetComponent(
                    facilityEntity,
                    out UnitProductionFacility facility))
            {
                continue;
            }

            ProcessFacility(
                context,
                facilityEntity,
                ref facility);
            context.Entities.SetComponent(
                facilityEntity,
                facility);

            if (facility.Status == UnitProductionStatus.Running)
            {
                running++;
            }
            else if (facility.Status is
                     UnitProductionStatus.NoInput or
                     UnitProductionStatus.NoPower or
                     UnitProductionStatus.Paused)
            {
                blocked++;
            }

            _readModels.Add(
                CreateReadModel(
                    context,
                    facilityEntity,
                    facility));
        }

        Metrics =
            new UnitProductionMetrics(
                _facilityEntities.Count,
                running,
                blocked,
                _completedUnits,
                _cancelledRequests,
                _rejectedRequests);
    }

    private void CollectRequests(EntityRegistry entities)
    {
        _requestEntities.Clear();

        foreach (EntityId entity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _requestEntities.Add(entity);
        }
    }

    private void CollectFacilities(EntityRegistry entities)
    {
        _facilityEntities.Clear();

        foreach (EntityId entity in
                 entities.Query<UnitProductionFacility>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _facilityEntities.Add(entity);
        }
    }

    private void ProcessCancellations(EntityRegistry entities)
    {
        for (int index = 0; index < _requestEntities.Count; index++)
        {
            EntityId requestEntity = _requestEntities[index];

            if (!entities.IsAlive(requestEntity) ||
                !entities.HasComponent<UnitProductionCancellationRequest>(
                    requestEntity) ||
                !entities.TryGetComponent(
                    requestEntity,
                    out UnitProductionRequest request))
            {
                continue;
            }

            if (entities.TryGetComponent(
                    request.Facility,
                    out UnitProductionFacility facility) &&
                facility.ActiveRequest == requestEntity)
            {
                if (facility.InputsReserved &&
                    _units.TryGet(
                        facility.ActiveUnit,
                        out UnitDefinition? activeUnit))
                {
                    ReleaseReservations(
                        facility.InputInventory,
                        activeUnit);
                }

                facility.ClearActive();
                entities.SetComponent(
                    request.Facility,
                    facility);
            }

            entities.DestroyEntity(requestEntity);
            _cancelledRequests++;
        }
    }

    private void RejectInvalidRequests(EntityRegistry entities)
    {
        for (int index = 0; index < _requestEntities.Count; index++)
        {
            EntityId requestEntity = _requestEntities[index];

            if (!entities.IsAlive(requestEntity) ||
                !entities.TryGetComponent(
                    requestEntity,
                    out UnitProductionRequest request))
            {
                continue;
            }

            if (!entities.TryGetComponent(
                    request.Facility,
                    out UnitProductionFacility facility) ||
                !_units.TryGet(
                    request.UnitId,
                    out UnitDefinition? definition) ||
                !facility.Supports(
                    definition.RequiredProductionCapability))
            {
                entities.DestroyEntity(requestEntity);
                _rejectedRequests++;
            }
        }
    }

    private void ProcessFacility(
        SimulationContext context,
        EntityId facilityEntity,
        ref UnitProductionFacility facility)
    {
        if (facility.ActiveRequest.IsValid)
        {
            if (!context.Entities.IsAlive(facility.ActiveRequest) ||
                !context.Entities.TryGetComponent(
                    facility.ActiveRequest,
                    out UnitProductionRequest activeRequest))
            {
                if (facility.InputsReserved &&
                    _units.TryGet(
                        facility.ActiveUnit,
                        out UnitDefinition? interruptedUnit))
                {
                    ReleaseReservations(
                        facility.InputInventory,
                        interruptedUnit);
                }

                facility.ClearActive();
            }
            else if (activeRequest.Paused)
            {
                facility.Status = UnitProductionStatus.Paused;
                facility.BlockReason =
                    UnitProductionBlockReason.Paused;
                return;
            }
            else
            {
                UnitDefinition activeUnit =
                    _units[activeRequest.UnitId];

                ProcessActiveProduction(
                    context,
                    facilityEntity,
                    ref facility,
                    facility.ActiveRequest,
                    activeUnit);
                return;
            }
        }

        if (!TrySelectRequest(
                context.Entities,
                facilityEntity,
                out EntityId requestEntity,
                out UnitProductionRequest request))
        {
            facility.ClearActive();
            return;
        }

        facility.ActivateRequest(
            requestEntity,
            request.UnitId);

        ProcessActiveProduction(
            context,
            facilityEntity,
            ref facility,
            requestEntity,
            _units[request.UnitId]);
    }

    private bool TrySelectRequest(
        EntityRegistry entities,
        EntityId facilityEntity,
        out EntityId selectedEntity,
        out UnitProductionRequest selected)
    {
        selectedEntity = EntityId.Invalid;
        selected = default;
        bool found = false;

        for (int index = 0; index < _requestEntities.Count; index++)
        {
            EntityId requestEntity = _requestEntities[index];

            if (!entities.IsAlive(requestEntity) ||
                !entities.TryGetComponent(
                    requestEntity,
                    out UnitProductionRequest candidate) ||
                candidate.Facility != facilityEntity)
            {
                continue;
            }

            if (!found ||
                IsHigherPriority(
                    requestEntity,
                    candidate,
                    selectedEntity,
                    selected))
            {
                selectedEntity = requestEntity;
                selected = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool IsHigherPriority(
        EntityId candidateEntity,
        in UnitProductionRequest candidate,
        EntityId selectedEntity,
        in UnitProductionRequest selected)
    {
        int priority =
            candidate.Priority.CompareTo(selected.Priority);
        if (priority != 0)
        {
            return priority < 0;
        }

        int tick =
            candidate.SubmittedAtTick.CompareTo(
                selected.SubmittedAtTick);
        if (tick != 0)
        {
            return tick < 0;
        }

        return candidateEntity < selectedEntity;
    }

    private void ProcessActiveProduction(
        SimulationContext context,
        EntityId facilityEntity,
        ref UnitProductionFacility facility,
        EntityId requestEntity,
        UnitDefinition definition)
    {
        if (!HasRequiredPower(
                context.Entities,
                facilityEntity,
                out _))
        {
            facility.Status = UnitProductionStatus.NoPower;
            facility.BlockReason =
                UnitProductionBlockReason.NoPower;
            return;
        }

        if (!facility.InputsReserved)
        {
            if (!InputsAvailable(
                    facility.InputInventory,
                    definition))
            {
                facility.Status = UnitProductionStatus.NoInput;
                facility.BlockReason =
                    UnitProductionBlockReason.NoInput;
                return;
            }

            ReserveInputs(
                facility.InputInventory,
                definition);
            facility.InputsReserved = true;
        }

        facility.Status = UnitProductionStatus.Running;
        facility.BlockReason = UnitProductionBlockReason.None;

        if (facility.ProgressTicks < definition.ProductionTicks)
        {
            facility.ProgressTicks++;
        }

        if (facility.ProgressTicks < definition.ProductionTicks)
        {
            return;
        }

        CommitProduction(
            context,
            facilityEntity,
            ref facility,
            requestEntity,
            definition);
    }

    private void CommitProduction(
        SimulationContext context,
        EntityId facilityEntity,
        ref UnitProductionFacility facility,
        EntityId requestEntity,
        UnitDefinition definition)
    {
        ValidateReservations(
            facility.InputInventory,
            definition);

        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];
            InventoryOperationResult result =
                _inventories.ConsumeReserved(
                    facility.InputInventory,
                    cost.ResourceId,
                    cost.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "UNIT_PRODUCTION_INPUT_COMMIT_FAILED",
                $"Unit production input commit failed for unit {definition.Id}.");
        }

        if (!context.Entities.TryGetComponent(
                facilityEntity,
                out WorldTransform facilityTransform))
        {
            EngineInvariant.Fail(
                DiagnosticCategory.Simulation,
                "UNIT_PRODUCTION_FACILITY_TRANSFORM_MISSING",
                $"Unit production facility {facilityEntity} has no world transform.");
        }

        Vector3 worldOffset =
            Vector3.Transform(
                facility.SpawnOffset,
                facilityTransform.Rotation);
        Vector3 spawnPosition =
            facilityTransform.Position + worldOffset;

        _unitFactory.Create(
            definition,
            spawnPosition,
            facility.Owner);

        facility.CompletedUnits++;
        facility.LastCompletedTick = context.Tick;
        facility.ClearActive();

        if (context.Entities.IsAlive(requestEntity))
        {
            context.Entities.DestroyEntity(requestEntity);
        }

        _completedUnits++;
    }

    private bool InputsAvailable(
        InventoryId inventory,
        UnitDefinition definition)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];

            if (_inventories.GetAvailableQuantity(
                    inventory,
                    cost.ResourceId) + QuantityTolerance <
                cost.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private void ReserveInputs(
        InventoryId inventory,
        UnitDefinition definition)
    {
        int reservedCount = 0;

        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];
            InventoryOperationResult result =
                _inventories.Reserve(
                    inventory,
                    cost.ResourceId,
                    cost.Quantity);

            if (result.Succeeded)
            {
                reservedCount++;
                continue;
            }

            for (int rollbackIndex = reservedCount - 1;
                 rollbackIndex >= 0;
                 rollbackIndex--)
            {
                UnitResourceCost reserved =
                    definition.Costs[rollbackIndex];
                InventoryOperationResult rollback =
                    _inventories.ReleaseReservation(
                        inventory,
                        reserved.ResourceId,
                        reserved.Quantity);

                EngineInvariant.Require(
                    rollback.Succeeded,
                    DiagnosticCategory.Simulation,
                    "UNIT_PRODUCTION_RESERVATION_ROLLBACK_FAILED",
                    $"Failed to roll back unit production reservation for unit {definition.Id}.");
            }

            EngineInvariant.Fail(
                DiagnosticCategory.Simulation,
                "UNIT_PRODUCTION_RESERVATION_FAILED",
                $"Input reservation changed after validation for unit {definition.Id}.");
        }
    }

    private void ReleaseReservations(
        InventoryId inventory,
        UnitDefinition definition)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];

            InventoryOperationResult result =
                _inventories.ReleaseReservation(
                    inventory,
                    cost.ResourceId,
                    cost.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "UNIT_PRODUCTION_RESERVATION_RELEASE_FAILED",
                $"Failed to release unit production reservation for unit {definition.Id}.");
        }
    }

    private void ValidateReservations(
        InventoryId inventory,
        UnitDefinition definition)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];

            EngineInvariant.Require(
                _inventories.GetReservedQuantity(
                    inventory,
                    cost.ResourceId) + QuantityTolerance >=
                cost.Quantity,
                DiagnosticCategory.Simulation,
                "UNIT_PRODUCTION_INPUT_RESERVATION_MISSING",
                $"Unit production input reservation for unit {definition.Id} is incomplete.");
        }
    }

    private static bool HasRequiredPower(
        EntityRegistry entities,
        EntityId facilityEntity,
        out PowerOperationalState state)
    {
        if (!entities.TryGetComponent(
                facilityEntity,
                out PowerConsumer consumer))
        {
            state = PowerOperationalState.Offline;
            return false;
        }

        state = consumer.State;

        return consumer.Enabled &&
            consumer.SupplyFraction + QuantityTolerance >= 1.0;
    }

    private UnitProductionFacilityReadModel CreateReadModel(
        SimulationContext context,
        EntityId facilityEntity,
        UnitProductionFacility facility)
    {
        double progress = 0.0;

        if (facility.ActiveUnit.IsSpecified &&
            _units.TryGet(
                facility.ActiveUnit,
                out UnitDefinition? definition))
        {
            progress =
                Math.Clamp(
                    (double)facility.ProgressTicks /
                    definition.ProductionTicks,
                    0.0,
                    1.0);
        }

        PowerOperationalState powerState =
            context.Entities.TryGetComponent(
                facilityEntity,
                out PowerConsumer consumer)
                ? consumer.State
                : PowerOperationalState.Offline;

        return new UnitProductionFacilityReadModel(
            facilityEntity,
            facility.InputInventory,
            facility.ActiveUnit,
            facility.Status,
            facility.BlockReason,
            progress,
            powerState,
            facility.CompletedUnits);
    }
}
