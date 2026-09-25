using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Economy;

public enum ProductionPriority : byte
{
    High = 0,
    Normal = 1,
    Low = 2
}

public enum ProductionRequestMode : byte
{
    OneShot = 0,
    Repeat = 1,
    DesiredStock = 2
}

public enum ProductionStatus : byte
{
    Idle = 0,
    Running = 1,
    NoInput = 2,
    OutputFull = 3,
    NoPower = 4,
    Paused = 5
}

public enum ProductionBlockReason : byte
{
    None = 0,
    NoInput = 1,
    OutputFull = 2,
    NoPower = 3,
    Paused = 4,
    DesiredStockReached = 5,
    UnsupportedRecipe = 6,
    InvalidInventory = 7
}

public struct ProductionFacility
{
    public ProductionFacility(
        InventoryId inputInventory,
        InventoryId outputInventory,
        ProductionCapability capabilities,
        SimulationTick activatedAtTick)
    {
        if (!inputInventory.IsSpecified)
        {
            throw new ArgumentException(
                "Production facilities require a valid input inventory.",
                nameof(inputInventory));
        }

        if (!outputInventory.IsSpecified)
        {
            throw new ArgumentException(
                "Production facilities require a valid output inventory.",
                nameof(outputInventory));
        }

        if (inputInventory == outputInventory)
        {
            throw new ArgumentException(
                "Production input and output inventories must be distinct.",
                nameof(outputInventory));
        }

        uint capabilityValue = (uint)capabilities;
        const uint supportedCapabilities =
            (uint)(ProductionCapability.SteelProcessing |
                   ProductionCapability.FuelProcessing |
                   ProductionCapability.ElectronicsProcessing |
                   ProductionCapability.AmmunitionProcessing);

        if (capabilityValue == 0 ||
            (capabilityValue & supportedCapabilities) != capabilityValue)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        InputInventory = inputInventory;
        OutputInventory = outputInventory;
        Capabilities = capabilities;
        ActivatedAtTick = activatedAtTick;
        ActiveRequest = EntityId.Invalid;
        ActiveRecipe = RecipeId.None;
        ProgressTicks = 0;
        InputsReserved = false;
        Status = ProductionStatus.Idle;
        BlockReason = ProductionBlockReason.None;
        CompletedCycles = 0;
        TotalOutputQuantity = 0.0;
        LastCompletedTick = SimulationTick.Zero;
    }

    public InventoryId InputInventory { get; }

    public InventoryId OutputInventory { get; }

    public ProductionCapability Capabilities { get; }

    public SimulationTick ActivatedAtTick { get; }

    public EntityId ActiveRequest { get; internal set; }

    public RecipeId ActiveRecipe { get; internal set; }

    public uint ProgressTicks { get; internal set; }

    public bool InputsReserved { get; internal set; }

    public ProductionStatus Status { get; internal set; }

    public ProductionBlockReason BlockReason { get; internal set; }

    public ulong CompletedCycles { get; internal set; }

    public double TotalOutputQuantity { get; internal set; }

    public SimulationTick LastCompletedTick { get; internal set; }

    public bool Supports(ProductionCapability capability) =>
        capability != ProductionCapability.None &&
        (Capabilities & capability) == capability;

    internal void ActivateRequest(EntityId requestEntity, RecipeId recipeId)
    {
        ActiveRequest = requestEntity;
        ActiveRecipe = recipeId;
        ProgressTicks = 0;
        InputsReserved = false;
        Status = ProductionStatus.Idle;
        BlockReason = ProductionBlockReason.None;
    }

    internal void ClearActive(
        ProductionStatus status = ProductionStatus.Idle,
        ProductionBlockReason blockReason = ProductionBlockReason.None)
    {
        ActiveRequest = EntityId.Invalid;
        ActiveRecipe = RecipeId.None;
        ProgressTicks = 0;
        InputsReserved = false;
        Status = status;
        BlockReason = blockReason;
    }
}

public readonly record struct ProductionRequest
{
    public ProductionRequest(
        EntityId facility,
        RecipeId recipeId,
        ProductionPriority priority,
        ProductionRequestMode mode,
        SimulationTick submittedAtTick,
        ResourceId desiredStockResourceId = default,
        double desiredStockQuantity = 0.0,
        bool paused = false)
    {
        if (!facility.IsValid)
        {
            throw new ArgumentException(
                "Production requests require a valid facility entity.",
                nameof(facility));
        }

        if (!recipeId.IsSpecified)
        {
            throw new ArgumentException(
                "Production requests require a stable recipe ID.",
                nameof(recipeId));
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == ProductionRequestMode.DesiredStock)
        {
            if (!desiredStockResourceId.IsSpecified)
            {
                throw new ArgumentException(
                    "Desired-stock production requires a target resource.",
                    nameof(desiredStockResourceId));
            }

            if (!double.IsFinite(desiredStockQuantity) ||
                desiredStockQuantity <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(desiredStockQuantity));
            }
        }
        else if (desiredStockResourceId.IsSpecified ||
                 desiredStockQuantity != 0.0)
        {
            throw new ArgumentException(
                "Only desired-stock production may define a stock target.");
        }

        Facility = facility;
        RecipeId = recipeId;
        Priority = priority;
        Mode = mode;
        SubmittedAtTick = submittedAtTick;
        DesiredStockResourceId = desiredStockResourceId;
        DesiredStockQuantity = desiredStockQuantity;
        Paused = paused;
    }

    public EntityId Facility { get; }

    public RecipeId RecipeId { get; }

    public ProductionPriority Priority { get; }

    public ProductionRequestMode Mode { get; }

    public SimulationTick SubmittedAtTick { get; }

    public ResourceId DesiredStockResourceId { get; }

    public double DesiredStockQuantity { get; }

    public bool Paused { get; }

    public ProductionRequest WithPaused(bool paused) =>
        new(
            Facility,
            RecipeId,
            Priority,
            Mode,
            SubmittedAtTick,
            DesiredStockResourceId,
            DesiredStockQuantity,
            paused);
}

public readonly record struct ProductionCancellationRequest;

public sealed class QueueProductionCommand : ISimulationCommand
{
    public QueueProductionCommand(
        EntityId facility,
        RecipeId recipeId,
        SimulationTick submittedAtTick,
        ProductionPriority priority = ProductionPriority.Normal,
        ProductionRequestMode mode = ProductionRequestMode.OneShot,
        ResourceId desiredStockResourceId = default,
        double desiredStockQuantity = 0.0)
    {
        if (!facility.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(facility));
        }

        if (!recipeId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(recipeId));
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Facility = facility;
        RecipeId = recipeId;
        SubmittedAtTick = submittedAtTick;
        Priority = priority;
        Mode = mode;
        DesiredStockResourceId = desiredStockResourceId;
        DesiredStockQuantity = desiredStockQuantity;
    }

    public EntityId Facility { get; }

    public RecipeId RecipeId { get; }

    public SimulationTick SubmittedAtTick { get; }

    public ProductionPriority Priority { get; }

    public ProductionRequestMode Mode { get; }

    public ResourceId DesiredStockResourceId { get; }

    public double DesiredStockQuantity { get; }

    public EntityId RequestEntity { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.IsAlive(Facility) ||
            !context.Entities.HasComponent<ProductionFacility>(Facility))
        {
            Accepted = false;
            RequestEntity = EntityId.Invalid;
            return;
        }

        EntityId request = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new ProductionRequest(
                Facility,
                RecipeId,
                Priority,
                Mode,
                SubmittedAtTick,
                DesiredStockResourceId,
                DesiredStockQuantity));

        RequestEntity = request;
        Accepted = true;
    }
}

public sealed class SetProductionRequestPausedCommand : ISimulationCommand
{
    public SetProductionRequestPausedCommand(
        EntityId requestEntity,
        bool paused,
        SimulationTick submittedAtTick)
    {
        if (!requestEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(requestEntity));
        }

        RequestEntity = requestEntity;
        Paused = paused;
        SubmittedAtTick = submittedAtTick;
    }

    public EntityId RequestEntity { get; }

    public bool Paused { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.TryGetComponent(
                RequestEntity,
                out ProductionRequest request))
        {
            Accepted = false;
            return;
        }

        context.Entities.SetComponent(
            RequestEntity,
            request.WithPaused(Paused));
        Accepted = true;
    }
}

public sealed class CancelProductionRequestCommand : ISimulationCommand
{
    public CancelProductionRequestCommand(
        EntityId requestEntity,
        SimulationTick submittedAtTick)
    {
        if (!requestEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(requestEntity));
        }

        RequestEntity = requestEntity;
        SubmittedAtTick = submittedAtTick;
    }

    public EntityId RequestEntity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.IsAlive(RequestEntity) ||
            !context.Entities.HasComponent<ProductionRequest>(RequestEntity))
        {
            Accepted = false;
            return;
        }

        if (!context.Entities.HasComponent<ProductionCancellationRequest>(
                RequestEntity))
        {
            context.Entities.AddComponent(
                RequestEntity,
                new ProductionCancellationRequest());
        }

        Accepted = true;
    }
}

public readonly record struct ProductionMetrics(
    int FacilityCount,
    int RunningFacilityCount,
    int BlockedFacilityCount,
    long CompletedCycles,
    long CancelledRequests,
    long RejectedRequests,
    double TotalOutputQuantity);

public readonly record struct ProductionFacilityReadModel(
    EntityId Entity,
    InventoryId InputInventory,
    InventoryId OutputInventory,
    RecipeId ActiveRecipe,
    ProductionStatus Status,
    ProductionBlockReason BlockReason,
    double Progress,
    bool InputAvailable,
    bool OutputCapacityAvailable,
    PowerOperationalState PowerState,
    ulong CompletedCycles,
    double TotalOutputQuantity,
    double AverageOutputPerSecond);
