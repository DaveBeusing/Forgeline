using System.Diagnostics.CodeAnalysis;
using ForgeLine.Core;

namespace ForgeLine.Economy;

public sealed class InventoryStore
{
    private readonly Dictionary<InventoryId, InventoryState> _inventories = new();
    private uint _nextInventoryId;
    private long _addAttempts;
    private long _addFailures;
    private long _removeAttempts;
    private long _removeFailures;
    private long _transferAttempts;
    private long _transferFailures;
    private long _reservationAttempts;
    private long _reservationFailures;

    public InventoryStoreMetrics Metrics =>
        new(
            _inventories.Count,
            _addAttempts,
            _addFailures,
            _removeAttempts,
            _removeFailures,
            _transferAttempts,
            _transferFailures,
            _reservationAttempts,
            _reservationFailures);

    public InventoryId CreateInventory(InventorySpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        uint value = checked(_nextInventoryId + 1);
        if (value == 0)
        {
            throw new InvalidOperationException("Inventory identifier space is exhausted.");
        }

        _nextInventoryId = value;
        var id = new InventoryId(value);
        _inventories.Add(id, new InventoryState(specification));
        return id;
    }

    public bool DestroyInventory(InventoryId inventoryId) =>
        inventoryId.IsSpecified && _inventories.Remove(inventoryId);

    public bool Contains(InventoryId inventoryId) =>
        inventoryId.IsSpecified && _inventories.ContainsKey(inventoryId);

    public double GetTotalCapacity(InventoryId inventoryId) =>
        GetRequiredState(inventoryId).Specification.TotalCapacity;

    public double GetTotalQuantity(InventoryId inventoryId) =>
        GetRequiredState(inventoryId).TotalQuantity;

    public double GetRemainingCapacity(InventoryId inventoryId)
    {
        InventoryState state = GetRequiredState(inventoryId);
        return Math.Max(
            0.0,
            state.Specification.TotalCapacity - state.TotalQuantity);
    }

    public double GetQuantity(
        InventoryId inventoryId,
        ResourceId resourceId)
    {
        ValidateResourceId(resourceId);
        InventoryState state = GetRequiredState(inventoryId);
        return GetValue(state.Quantities, resourceId);
    }

    public double GetReservedQuantity(
        InventoryId inventoryId,
        ResourceId resourceId)
    {
        ValidateResourceId(resourceId);
        InventoryState state = GetRequiredState(inventoryId);
        return GetValue(state.Reservations, resourceId);
    }

    public double GetAvailableQuantity(
        InventoryId inventoryId,
        ResourceId resourceId)
    {
        InventoryState state = GetRequiredState(inventoryId);
        ValidateResourceId(resourceId);
        return Math.Max(
            0.0,
            GetValue(state.Quantities, resourceId) -
            GetValue(state.Reservations, resourceId));
    }

    public bool CanAdd(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);

        return TryGetState(inventoryId, out InventoryState? state) &&
            GetAddableQuantity(state, resourceId, quantity) >= quantity;
    }

    public double GetAddableQuantity(
        InventoryId inventoryId,
        ResourceId resourceId,
        double requestedQuantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(requestedQuantity);

        return TryGetState(inventoryId, out InventoryState? state)
            ? GetAddableQuantity(state, resourceId, requestedQuantity)
            : 0.0;
    }

    public InventoryOperationResult Add(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);
        _addAttempts++;

        if (!TryGetState(inventoryId, out InventoryState? state))
        {
            _addFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InventoryNotFound);
        }

        if (quantity == 0.0)
        {
            return InventoryOperationResult.Success;
        }

        if (!state.Specification.Accepts(resourceId))
        {
            _addFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.ResourceRejected);
        }

        if (GetAddableQuantity(state, resourceId, quantity) < quantity)
        {
            _addFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.CapacityExceeded);
        }

        ApplyAdd(state, resourceId, quantity);
        return InventoryOperationResult.Success;
    }

    public bool CanRemove(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);

        return TryGetState(inventoryId, out InventoryState? state) &&
            GetAvailableQuantity(state, resourceId) >= quantity;
    }

    public InventoryOperationResult Remove(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);
        _removeAttempts++;

        if (!TryGetState(inventoryId, out InventoryState? state))
        {
            _removeFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InventoryNotFound);
        }

        if (GetAvailableQuantity(state, resourceId) < quantity)
        {
            _removeFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InsufficientAvailableQuantity);
        }

        ApplyRemove(state, resourceId, quantity);
        return InventoryOperationResult.Success;
    }

    public InventoryOperationResult Transfer(
        InventoryId sourceInventoryId,
        InventoryId destinationInventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);
        _transferAttempts++;

        if (!TryGetState(sourceInventoryId, out InventoryState? source) ||
            !TryGetState(destinationInventoryId, out InventoryState? destination))
        {
            _transferFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InventoryNotFound);
        }

        if (GetAvailableQuantity(source, resourceId) < quantity)
        {
            _transferFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InsufficientAvailableQuantity);
        }

        if (sourceInventoryId == destinationInventoryId || quantity == 0.0)
        {
            return InventoryOperationResult.Success;
        }

        if (!destination.Specification.Accepts(resourceId))
        {
            _transferFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.ResourceRejected);
        }

        if (GetAddableQuantity(destination, resourceId, quantity) < quantity)
        {
            _transferFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.CapacityExceeded);
        }

        ApplyRemove(source, resourceId, quantity);
        ApplyAdd(destination, resourceId, quantity);
        return InventoryOperationResult.Success;
    }

    public InventoryOperationResult Reserve(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);
        _reservationAttempts++;

        if (!TryGetState(inventoryId, out InventoryState? state))
        {
            _reservationFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InventoryNotFound);
        }

        if (GetAvailableQuantity(state, resourceId) < quantity)
        {
            _reservationFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InsufficientAvailableQuantity);
        }

        state.Reservations[resourceId] =
            GetValue(state.Reservations, resourceId) + quantity;
        return InventoryOperationResult.Success;
    }

    public InventoryOperationResult ReleaseReservation(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);
        _reservationAttempts++;

        if (!TryGetState(inventoryId, out InventoryState? state))
        {
            _reservationFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InventoryNotFound);
        }

        double reserved = GetValue(state.Reservations, resourceId);
        if (reserved < quantity)
        {
            _reservationFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InsufficientReservedQuantity);
        }

        state.Reservations[resourceId] = Math.Max(0.0, reserved - quantity);
        return InventoryOperationResult.Success;
    }

    public InventoryOperationResult ConsumeReserved(
        InventoryId inventoryId,
        ResourceId resourceId,
        double quantity)
    {
        ValidateResourceId(resourceId);
        ValidateQuantity(quantity);
        _reservationAttempts++;

        if (!TryGetState(inventoryId, out InventoryState? state))
        {
            _reservationFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InventoryNotFound);
        }

        double reserved = GetValue(state.Reservations, resourceId);
        double stored = GetValue(state.Quantities, resourceId);
        if (reserved < quantity || stored < quantity)
        {
            _reservationFailures++;
            return InventoryOperationResult.Failed(
                InventoryFailureReason.InsufficientReservedQuantity);
        }

        state.Reservations[resourceId] = Math.Max(0.0, reserved - quantity);
        state.Quantities[resourceId] = Math.Max(0.0, stored - quantity);
        state.TotalQuantity = Math.Max(0.0, state.TotalQuantity - quantity);
        return InventoryOperationResult.Success;
    }

    public InventoryResourceQuantity[] GetResourceQuantities(
        InventoryId inventoryId)
    {
        InventoryState state = GetRequiredState(inventoryId);
        var resources = new HashSet<ResourceId>(state.Quantities.Keys);
        resources.UnionWith(state.Reservations.Keys);

        var entries = new List<InventoryResourceQuantity>(resources.Count);
        foreach (ResourceId resourceId in resources)
        {
            double quantity = GetValue(state.Quantities, resourceId);
            double reserved = GetValue(state.Reservations, resourceId);
            if (quantity == 0.0 && reserved == 0.0)
            {
                continue;
            }

            entries.Add(
                new InventoryResourceQuantity(
                    resourceId,
                    quantity,
                    reserved,
                    Math.Max(0.0, quantity - reserved)));
        }

        entries.Sort(
            static (left, right) =>
                left.ResourceId.CompareTo(right.ResourceId));
        return entries.ToArray();
    }

    private static double GetAddableQuantity(
        InventoryState state,
        ResourceId resourceId,
        double requestedQuantity)
    {
        if (requestedQuantity == 0.0 ||
            !state.Specification.Accepts(resourceId))
        {
            return 0.0;
        }

        double totalHeadroom = Math.Max(
            0.0,
            state.Specification.TotalCapacity - state.TotalQuantity);
        double resourceHeadroom = Math.Max(
            0.0,
            state.Specification.GetResourceCapacity(resourceId) -
            GetValue(state.Quantities, resourceId));

        return Math.Min(
            requestedQuantity,
            Math.Min(totalHeadroom, resourceHeadroom));
    }

    private static double GetAvailableQuantity(
        InventoryState state,
        ResourceId resourceId) =>
        Math.Max(
            0.0,
            GetValue(state.Quantities, resourceId) -
            GetValue(state.Reservations, resourceId));

    private static void ApplyAdd(
        InventoryState state,
        ResourceId resourceId,
        double quantity)
    {
        if (quantity == 0.0)
        {
            return;
        }

        double updatedResourceQuantity =
            GetValue(state.Quantities, resourceId) + quantity;
        double updatedTotalQuantity = state.TotalQuantity + quantity;

        EngineInvariant.Require(
            double.IsFinite(updatedResourceQuantity) &&
            double.IsFinite(updatedTotalQuantity),
            DiagnosticCategory.Simulation,
            "INVENTORY_QUANTITY_OVERFLOW",
            "Inventory quantity arithmetic must remain finite.");

        state.Quantities[resourceId] = updatedResourceQuantity;
        state.TotalQuantity = updatedTotalQuantity;
    }

    private static void ApplyRemove(
        InventoryState state,
        ResourceId resourceId,
        double quantity)
    {
        if (quantity == 0.0)
        {
            return;
        }

        double stored = GetValue(state.Quantities, resourceId);
        state.Quantities[resourceId] = Math.Max(0.0, stored - quantity);
        state.TotalQuantity = Math.Max(0.0, state.TotalQuantity - quantity);
    }

    private InventoryState GetRequiredState(InventoryId inventoryId)
    {
        if (TryGetState(inventoryId, out InventoryState? state))
        {
            return state;
        }

        throw new KeyNotFoundException(
            $"Unknown inventory ID '{inventoryId}'.");
    }

    private bool TryGetState(
        InventoryId inventoryId,
        [NotNullWhen(true)] out InventoryState? state)
    {
        if (!inventoryId.IsSpecified)
        {
            state = null;
            return false;
        }

        return _inventories.TryGetValue(inventoryId, out state);
    }

    private static double GetValue(
        Dictionary<ResourceId, double> values,
        ResourceId resourceId) =>
        values.TryGetValue(resourceId, out double quantity)
            ? quantity
            : 0.0;

    private static void ValidateResourceId(ResourceId resourceId)
    {
        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Inventory operations require a stable resource ID.",
                nameof(resourceId));
        }
    }

    private static void ValidateQuantity(double quantity)
    {
        if (!double.IsFinite(quantity) || quantity < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }
    }

    private sealed class InventoryState
    {
        public InventoryState(InventorySpecification specification)
        {
            Specification = specification;
        }

        public InventorySpecification Specification { get; }

        public Dictionary<ResourceId, double> Quantities { get; } = new();

        public Dictionary<ResourceId, double> Reservations { get; } = new();

        public double TotalQuantity { get; set; }
    }
}
