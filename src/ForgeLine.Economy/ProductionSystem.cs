using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Economy;

public sealed class ProductionSystem : ISimulationSystem
{
    private const double QuantityTolerance = 1e-9;

    private readonly ProductionRecipeCatalog _recipes;
    private readonly InventoryStore _inventories;
    private readonly List<EntityId> _requestEntities = new();
    private readonly List<EntityId> _facilityEntities = new();
    private readonly List<ProductionFacilityReadModel> _readModels = new();
    private long _completedCycles;
    private long _cancelledRequests;
    private long _rejectedRequests;
    private double _totalOutputQuantity;

    public ProductionSystem(
        ProductionRecipeCatalog recipes,
        InventoryStore inventories)
    {
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
    }

    public SimulationPhase Phase => SimulationPhase.Production;

    public IReadOnlyList<ProductionFacilityReadModel> Facilities => _readModels;

    public ProductionMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        CollectRequests(context.Entities);
        ProcessCancellations(context.Entities);

        CollectRequests(context.Entities);
        PruneInvalidRequests(context.Entities);

        CollectRequests(context.Entities);
        CollectFacilities(context.Entities);

        _readModels.Clear();

        int runningFacilityCount = 0;
        int blockedFacilityCount = 0;

        for (int index = 0; index < _facilityEntities.Count; index++)
        {
            EntityId facilityEntity = _facilityEntities[index];
            if (!context.Entities.IsAlive(facilityEntity) ||
                !context.Entities.TryGetComponent(
                    facilityEntity,
                    out ProductionFacility facility))
            {
                continue;
            }

            ProcessFacility(context, facilityEntity, ref facility);
            context.Entities.SetComponent(facilityEntity, facility);

            if (facility.Status == ProductionStatus.Running)
            {
                runningFacilityCount++;
            }
            else if (facility.Status is ProductionStatus.NoInput
                     or ProductionStatus.OutputFull
                     or ProductionStatus.NoPower
                     or ProductionStatus.Paused)
            {
                blockedFacilityCount++;
            }

            _readModels.Add(
                CreateReadModel(context, facilityEntity, in facility));
        }

        Metrics = new ProductionMetrics(
            _facilityEntities.Count,
            runningFacilityCount,
            blockedFacilityCount,
            _completedCycles,
            _cancelledRequests,
            _rejectedRequests,
            _totalOutputQuantity);
    }

    private void CollectRequests(EntityRegistry entities)
    {
        _requestEntities.Clear();

        foreach (EntityId entity in
                 entities.Query<ProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _requestEntities.Add(entity);
        }
    }

    private void CollectFacilities(EntityRegistry entities)
    {
        _facilityEntities.Clear();

        foreach (EntityId entity in
                 entities.Query<ProductionFacility>(
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
                !entities.HasComponent<ProductionCancellationRequest>(
                    requestEntity) ||
                !entities.TryGetComponent(
                    requestEntity,
                    out ProductionRequest request))
            {
                continue;
            }

            if (entities.TryGetComponent(
                    request.Facility,
                    out ProductionFacility facility) &&
                facility.ActiveRequest == requestEntity)
            {
                ReleaseActiveReservations(in facility);

                facility.ClearActive();
                entities.SetComponent(request.Facility, facility);
            }

            entities.DestroyEntity(requestEntity);
            _cancelledRequests++;
        }
    }

    private void PruneInvalidRequests(EntityRegistry entities)
    {
        for (int index = 0; index < _requestEntities.Count; index++)
        {
            EntityId requestEntity = _requestEntities[index];
            if (!entities.IsAlive(requestEntity) ||
                !entities.TryGetComponent(
                    requestEntity,
                    out ProductionRequest request))
            {
                continue;
            }

            if (!entities.TryGetComponent(
                    request.Facility,
                    out ProductionFacility facility) ||
                !_recipes.TryGet(
                    request.RecipeId,
                    out ProductionRecipeDefinition? recipe) ||
                !facility.Supports(recipe.RequiredCapability) ||
                !DesiredStockTargetMatchesRecipe(in request, recipe))
            {
                if (entities.TryGetComponent(
                        request.Facility,
                        out ProductionFacility activeFacility) &&
                    activeFacility.ActiveRequest == requestEntity)
                {
                    ReleaseActiveReservations(in activeFacility);
                    activeFacility.ClearActive(
                        ProductionStatus.Idle,
                        ProductionBlockReason.UnsupportedRecipe);
                    entities.SetComponent(
                        request.Facility,
                        activeFacility);
                }

                entities.DestroyEntity(requestEntity);
                _rejectedRequests++;
            }
        }
    }

    private void ProcessFacility(
        SimulationContext context,
        EntityId facilityEntity,
        ref ProductionFacility facility)
    {
        if (!_inventories.Contains(facility.InputInventory) ||
            !_inventories.Contains(facility.OutputInventory))
        {
            facility.Status = ProductionStatus.Idle;
            facility.BlockReason = ProductionBlockReason.InvalidInventory;
            return;
        }

        if (facility.ActiveRequest.IsValid)
        {
            if (!context.Entities.TryGetComponent(
                    facility.ActiveRequest,
                    out ProductionRequest activeRequest))
            {
                ReleaseActiveReservations(in facility);
                facility.ClearActive();
            }
            else if (activeRequest.Paused)
            {
                facility.Status = ProductionStatus.Paused;
                facility.BlockReason = ProductionBlockReason.Paused;
                return;
            }
            else
            {
                ProductionRecipeDefinition activeRecipe =
                    _recipes[activeRequest.RecipeId];

                if (ShouldStopForDesiredStock(
                        in activeRequest,
                        in facility,
                        activeRecipe) &&
                    !facility.InputsReserved &&
                    facility.ProgressTicks == 0)
                {
                    facility.ClearActive(
                        ProductionStatus.Idle,
                        ProductionBlockReason.DesiredStockReached);
                }
                else
                {
                    ProcessActiveCycle(
                        context,
                        facilityEntity,
                        ref facility,
                        facility.ActiveRequest,
                        in activeRequest,
                        activeRecipe);
                    return;
                }
            }
        }

        if (!TrySelectRequest(
                context.Entities,
                facilityEntity,
                in facility,
                out EntityId requestEntity,
                out ProductionRequest request,
                out bool hasPausedRequest,
                out bool hasSatisfiedDesiredStock))
        {
            facility.Status = hasPausedRequest
                ? ProductionStatus.Paused
                : ProductionStatus.Idle;
            facility.BlockReason = hasPausedRequest
                ? ProductionBlockReason.Paused
                : hasSatisfiedDesiredStock
                    ? ProductionBlockReason.DesiredStockReached
                    : ProductionBlockReason.None;
            return;
        }

        facility.ActivateRequest(requestEntity, request.RecipeId);

        ProductionRecipeDefinition recipe = _recipes[request.RecipeId];
        ProcessActiveCycle(
            context,
            facilityEntity,
            ref facility,
            requestEntity,
            in request,
            recipe);
    }

    private bool TrySelectRequest(
        EntityRegistry entities,
        EntityId facilityEntity,
        in ProductionFacility facility,
        out EntityId requestEntity,
        out ProductionRequest request,
        out bool hasPausedRequest,
        out bool hasSatisfiedDesiredStock)
    {
        requestEntity = EntityId.Invalid;
        request = default;
        hasPausedRequest = false;
        hasSatisfiedDesiredStock = false;
        bool found = false;

        for (int index = 0; index < _requestEntities.Count; index++)
        {
            EntityId candidateEntity = _requestEntities[index];
            if (!entities.TryGetComponent(
                    candidateEntity,
                    out ProductionRequest candidate) ||
                candidate.Facility != facilityEntity)
            {
                continue;
            }

            if (candidate.Paused)
            {
                hasPausedRequest = true;
                continue;
            }

            ProductionRecipeDefinition recipe = _recipes[candidate.RecipeId];
            if (ShouldStopForDesiredStock(
                    in candidate,
                    in facility,
                    recipe))
            {
                hasSatisfiedDesiredStock = true;
                continue;
            }

            if (!found ||
                IsHigherPriority(
                    in candidate,
                    candidateEntity,
                    in request,
                    requestEntity))
            {
                requestEntity = candidateEntity;
                request = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool IsHigherPriority(
        in ProductionRequest candidate,
        EntityId candidateEntity,
        in ProductionRequest current,
        EntityId currentEntity)
    {
        int priorityComparison =
            ((byte)candidate.Priority).CompareTo((byte)current.Priority);
        if (priorityComparison != 0)
        {
            return priorityComparison < 0;
        }

        int tickComparison =
            candidate.SubmittedAtTick.CompareTo(current.SubmittedAtTick);
        if (tickComparison != 0)
        {
            return tickComparison < 0;
        }

        return candidateEntity < currentEntity;
    }

    private void ProcessActiveCycle(
        SimulationContext context,
        EntityId facilityEntity,
        ref ProductionFacility facility,
        EntityId requestEntity,
        in ProductionRequest request,
        ProductionRecipeDefinition recipe)
    {
        if (!HasRequiredPower(
                context.Entities,
                facilityEntity,
                recipe,
                out _))
        {
            facility.Status = ProductionStatus.NoPower;
            facility.BlockReason = ProductionBlockReason.NoPower;
            return;
        }

        if (!facility.InputsReserved)
        {
            if (!InputsAvailable(in facility, recipe))
            {
                facility.Status = ProductionStatus.NoInput;
                facility.BlockReason = ProductionBlockReason.NoInput;
                return;
            }

            ReserveInputs(in facility, recipe);
            facility.InputsReserved = true;
        }

        facility.Status = ProductionStatus.Running;
        facility.BlockReason = ProductionBlockReason.None;

        if (facility.ProgressTicks < recipe.DurationTicks)
        {
            facility.ProgressTicks++;
        }

        if (facility.ProgressTicks < recipe.DurationTicks)
        {
            return;
        }

        if (!HasOutputCapacity(in facility, recipe))
        {
            facility.Status = ProductionStatus.OutputFull;
            facility.BlockReason = ProductionBlockReason.OutputFull;
            return;
        }

        CommitCycle(
            context,
            ref facility,
            requestEntity,
            in request,
            recipe);
    }

    private void CommitCycle(
        SimulationContext context,
        ref ProductionFacility facility,
        EntityId requestEntity,
        in ProductionRequest request,
        ProductionRecipeDefinition recipe)
    {
        for (int index = 0; index < recipe.Inputs.Count; index++)
        {
            ProductionIngredient input = recipe.Inputs[index];
            EngineInvariant.Require(
                _inventories.GetReservedQuantity(
                    facility.InputInventory,
                    input.ResourceId) + QuantityTolerance >= input.Quantity,
                DiagnosticCategory.Simulation,
                "PRODUCTION_INPUT_RESERVATION_MISSING",
                $"Production input reservation for recipe {recipe.Id} is incomplete.");
        }

        for (int index = 0; index < recipe.Inputs.Count; index++)
        {
            ProductionIngredient input = recipe.Inputs[index];
            InventoryOperationResult result =
                _inventories.ConsumeReserved(
                    facility.InputInventory,
                    input.ResourceId,
                    input.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "PRODUCTION_INPUT_COMMIT_FAILED",
                $"Production input commit failed for recipe {recipe.Id}.");
        }

        double producedQuantity = 0.0;

        for (int index = 0; index < recipe.Outputs.Count; index++)
        {
            ProductionIngredient output = recipe.Outputs[index];
            InventoryOperationResult result =
                _inventories.Add(
                    facility.OutputInventory,
                    output.ResourceId,
                    output.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "PRODUCTION_OUTPUT_COMMIT_FAILED",
                $"Production output commit failed for recipe {recipe.Id}.");

            producedQuantity += output.Quantity;
        }

        double totalOutputQuantity =
            facility.TotalOutputQuantity + producedQuantity;
        EngineInvariant.Require(
            double.IsFinite(totalOutputQuantity),
            DiagnosticCategory.Simulation,
            "PRODUCTION_OUTPUT_TOTAL_OVERFLOW",
            "Production output quantity must remain finite.");

        facility.CompletedCycles = checked(facility.CompletedCycles + 1);
        facility.TotalOutputQuantity = totalOutputQuantity;
        facility.LastCompletedTick = context.Tick;
        facility.ClearActive();

        _completedCycles = checked(_completedCycles + 1);
        _totalOutputQuantity += producedQuantity;

        EngineInvariant.Require(
            double.IsFinite(_totalOutputQuantity),
            DiagnosticCategory.Simulation,
            "PRODUCTION_METRIC_OUTPUT_OVERFLOW",
            "Production output metrics must remain finite.");

        if (request.Mode == ProductionRequestMode.OneShot &&
            context.Entities.IsAlive(requestEntity))
        {
            context.Entities.DestroyEntity(requestEntity);
        }
    }

    private bool InputsAvailable(
        in ProductionFacility facility,
        ProductionRecipeDefinition recipe)
    {
        for (int index = 0; index < recipe.Inputs.Count; index++)
        {
            ProductionIngredient input = recipe.Inputs[index];
            if (_inventories.GetAvailableQuantity(
                    facility.InputInventory,
                    input.ResourceId) + QuantityTolerance < input.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private void ReserveInputs(
        in ProductionFacility facility,
        ProductionRecipeDefinition recipe)
    {
        int reservedCount = 0;

        for (int index = 0; index < recipe.Inputs.Count; index++)
        {
            ProductionIngredient input = recipe.Inputs[index];
            InventoryOperationResult result =
                _inventories.Reserve(
                    facility.InputInventory,
                    input.ResourceId,
                    input.Quantity);

            if (result.Succeeded)
            {
                reservedCount++;
                continue;
            }

            for (int rollbackIndex = reservedCount - 1;
                 rollbackIndex >= 0;
                 rollbackIndex--)
            {
                ProductionIngredient reserved =
                    recipe.Inputs[rollbackIndex];
                InventoryOperationResult rollback =
                    _inventories.ReleaseReservation(
                        facility.InputInventory,
                        reserved.ResourceId,
                        reserved.Quantity);

                EngineInvariant.Require(
                    rollback.Succeeded,
                    DiagnosticCategory.Simulation,
                    "PRODUCTION_RESERVATION_ROLLBACK_FAILED",
                    $"Failed to roll back input reservation for recipe {recipe.Id}.");
            }

            EngineInvariant.Fail(
                DiagnosticCategory.Simulation,
                "PRODUCTION_RESERVATION_COMMIT_FAILED",
                $"Input reservation changed after validation for recipe {recipe.Id}.");
        }
    }

    private void ReleaseActiveReservations(in ProductionFacility facility)
    {
        if (!facility.InputsReserved || !facility.ActiveRecipe.IsSpecified)
        {
            return;
        }

        ProductionRecipeDefinition recipe = _recipes[facility.ActiveRecipe];

        for (int index = 0; index < recipe.Inputs.Count; index++)
        {
            ProductionIngredient input = recipe.Inputs[index];
            InventoryOperationResult result =
                _inventories.ReleaseReservation(
                    facility.InputInventory,
                    input.ResourceId,
                    input.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "PRODUCTION_RESERVATION_RELEASE_FAILED",
                $"Failed to release production reservation for recipe {recipe.Id}.");
        }
    }

    private bool HasOutputCapacity(
        in ProductionFacility facility,
        ProductionRecipeDefinition recipe)
    {
        double requiredTotalCapacity = 0.0;

        for (int index = 0; index < recipe.Outputs.Count; index++)
        {
            requiredTotalCapacity += recipe.Outputs[index].Quantity;
        }

        if (!double.IsFinite(requiredTotalCapacity) ||
            _inventories.GetRemainingCapacity(facility.OutputInventory) +
                QuantityTolerance < requiredTotalCapacity)
        {
            return false;
        }

        for (int index = 0; index < recipe.Outputs.Count; index++)
        {
            ProductionIngredient output = recipe.Outputs[index];
            if (_inventories.GetAddableQuantity(
                    facility.OutputInventory,
                    output.ResourceId,
                    output.Quantity) + QuantityTolerance < output.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasRequiredPower(
        EntityRegistry entities,
        EntityId facilityEntity,
        ProductionRecipeDefinition recipe,
        out PowerOperationalState powerState)
    {
        if (recipe.MinimumPowerFraction <= 0.0)
        {
            powerState = PowerOperationalState.Powered;
            return true;
        }

        if (!entities.TryGetComponent(
                facilityEntity,
                out PowerConsumer consumer))
        {
            powerState = PowerOperationalState.Offline;
            return false;
        }

        powerState = consumer.State;
        return consumer.Enabled &&
            consumer.SupplyFraction + QuantityTolerance >=
                recipe.MinimumPowerFraction;
    }

    private bool ShouldStopForDesiredStock(
        in ProductionRequest request,
        in ProductionFacility facility,
        ProductionRecipeDefinition recipe)
    {
        if (request.Mode != ProductionRequestMode.DesiredStock)
        {
            return false;
        }

        if (!DesiredStockTargetMatchesRecipe(in request, recipe))
        {
            return false;
        }

        double quantity = _inventories.GetQuantity(
            facility.OutputInventory,
            request.DesiredStockResourceId);
        return quantity + QuantityTolerance >= request.DesiredStockQuantity;
    }

    private static bool DesiredStockTargetMatchesRecipe(
        in ProductionRequest request,
        ProductionRecipeDefinition recipe)
    {
        if (request.Mode != ProductionRequestMode.DesiredStock)
        {
            return true;
        }

        for (int index = 0; index < recipe.Outputs.Count; index++)
        {
            if (recipe.Outputs[index].ResourceId ==
                request.DesiredStockResourceId)
            {
                return true;
            }
        }

        return false;
    }

    private ProductionFacilityReadModel CreateReadModel(
        SimulationContext context,
        EntityId facilityEntity,
        in ProductionFacility facility)
    {
        double progress = 0.0;
        bool inputAvailable = false;
        bool outputCapacityAvailable = false;
        PowerOperationalState powerState =
            context.Entities.TryGetComponent(
                facilityEntity,
                out PowerConsumer consumer)
                ? consumer.State
                : PowerOperationalState.Offline;

        if (facility.ActiveRecipe.IsSpecified &&
            _recipes.TryGet(
                facility.ActiveRecipe,
                out ProductionRecipeDefinition? recipe))
        {
            progress = Math.Clamp(
                (double)facility.ProgressTicks / recipe.DurationTicks,
                0.0,
                1.0);
            inputAvailable =
                facility.InputsReserved ||
                InputsAvailable(in facility, recipe);
            outputCapacityAvailable =
                HasOutputCapacity(in facility, recipe);
        }

        ulong elapsedTicks =
            context.Tick.Value >= facility.ActivatedAtTick.Value
                ? context.Tick.Value - facility.ActivatedAtTick.Value + 1
                : 1;
        double elapsedSeconds =
            elapsedTicks * context.TickDuration.TotalSeconds;
        double averageOutputPerSecond =
            elapsedSeconds > 0.0
                ? facility.TotalOutputQuantity / elapsedSeconds
                : 0.0;

        return new ProductionFacilityReadModel(
            facilityEntity,
            facility.InputInventory,
            facility.OutputInventory,
            facility.ActiveRecipe,
            facility.Status,
            facility.BlockReason,
            progress,
            inputAvailable,
            outputCapacityAvailable,
            powerState,
            facility.CompletedCycles,
            facility.TotalOutputQuantity,
            averageOutputPerSecond);
    }
}
