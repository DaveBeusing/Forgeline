using ForgeLine.Core;

namespace ForgeLine.Economy;

public readonly record struct InventoryId(uint Value) : IComparable<InventoryId>
{
    public static InventoryId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(InventoryId other) => Value.CompareTo(other.Value);

    public static bool operator <(InventoryId left, InventoryId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(InventoryId left, InventoryId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(InventoryId left, InventoryId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(InventoryId left, InventoryId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum InventoryFailureReason
{
    None = 0,
    InventoryNotFound = 1,
    ResourceRejected = 2,
    CapacityExceeded = 3,
    InsufficientAvailableQuantity = 4,
    InsufficientReservedQuantity = 5
}

public readonly record struct InventoryOperationResult(
    bool Succeeded,
    InventoryFailureReason Failure)
{
    public static InventoryOperationResult Success => new(true, InventoryFailureReason.None);

    public static InventoryOperationResult Failed(InventoryFailureReason failure) =>
        new(false, failure);
}

public sealed class InventorySpecification
{
    private readonly ResourceId[] _acceptedResources;
    private readonly Dictionary<ResourceId, double> _perResourceCapacities;

    public InventorySpecification(
        double totalCapacity,
        IEnumerable<ResourceId>? acceptedResources = null,
        IReadOnlyDictionary<ResourceId, double>? perResourceCapacities = null)
    {
        if (!double.IsFinite(totalCapacity) || totalCapacity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCapacity));
        }

        TotalCapacity = totalCapacity;

        _acceptedResources = acceptedResources is null
            ? []
            : acceptedResources.Distinct().Order().ToArray();

        for (int index = 0; index < _acceptedResources.Length; index++)
        {
            if (!_acceptedResources[index].IsSpecified)
            {
                throw new ArgumentException(
                    "Accepted resources must use stable resource IDs.",
                    nameof(acceptedResources));
            }
        }

        _perResourceCapacities = new Dictionary<ResourceId, double>();
        if (perResourceCapacities is null)
        {
            return;
        }

        foreach ((ResourceId resourceId, double capacity) in perResourceCapacities)
        {
            if (!resourceId.IsSpecified)
            {
                throw new ArgumentException(
                    "Per-resource capacities require stable resource IDs.",
                    nameof(perResourceCapacities));
            }

            if (!double.IsFinite(capacity) || capacity <= 0.0 || capacity > totalCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(perResourceCapacities),
                    "Per-resource capacity must be finite, positive, and no greater than total capacity.");
            }

            if (_acceptedResources.Length != 0 && !Accepts(resourceId))
            {
                throw new ArgumentException(
                    $"Per-resource capacity was configured for rejected resource '{resourceId}'.",
                    nameof(perResourceCapacities));
            }

            _perResourceCapacities.Add(resourceId, capacity);
        }
    }

    public double TotalCapacity { get; }

    public bool HasAcceptedResourceFilter => _acceptedResources.Length != 0;

    internal bool Accepts(ResourceId resourceId) =>
        _acceptedResources.Length == 0 ||
        Array.BinarySearch(_acceptedResources, resourceId) >= 0;

    internal double GetResourceCapacity(ResourceId resourceId) =>
        _perResourceCapacities.TryGetValue(resourceId, out double capacity)
            ? capacity
            : TotalCapacity;
}

public readonly record struct InventoryStorage
{
    public InventoryStorage(InventoryId inventoryId)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Inventory storage requires a valid inventory ID.",
                nameof(inventoryId));
        }

        InventoryId = inventoryId;
    }

    public InventoryId InventoryId { get; }
}

public enum StorageDepotState
{
    Operational = 0,
    Disabled = 1
}

public readonly record struct StorageDepot
{
    public StorageDepot(
        InventoryId inventoryId,
        FactionId owner = default,
        StorageDepotState state = StorageDepotState.Operational)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Storage depots require a valid inventory ID.",
                nameof(inventoryId));
        }

        InventoryId = inventoryId;
        Owner = owner;
        State = state;
    }

    public InventoryId InventoryId { get; }

    public FactionId Owner { get; }

    public StorageDepotState State { get; }

    public StorageDepot WithState(StorageDepotState state) =>
        new(InventoryId, Owner, state);
}

public readonly record struct InventoryResourceQuantity(
    ResourceId ResourceId,
    double Quantity,
    double ReservedQuantity,
    double AvailableQuantity);

public readonly record struct InventoryStoreMetrics(
    int InventoryCount,
    long AddAttempts,
    long AddFailures,
    long RemoveAttempts,
    long RemoveFailures,
    long TransferAttempts,
    long TransferFailures,
    long ReservationAttempts,
    long ReservationFailures);
