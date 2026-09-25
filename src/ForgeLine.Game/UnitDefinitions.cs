using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

public readonly record struct UnitId(uint Value) : IComparable<UnitId>
{
    public static UnitId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(UnitId other) => Value.CompareTo(other.Value);

    public static bool operator <(UnitId left, UnitId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(UnitId left, UnitId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(UnitId left, UnitId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(UnitId left, UnitId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class UnitIds
{
    public static readonly UnitId RifleSquad = new(1);
    public static readonly UnitId CombatEngineer = new(2);
    public static readonly UnitId ScoutVehicle = new(3);
    public static readonly UnitId MainBattleTank = new(4);
    public static readonly UnitId MobileArtillery = new(5);
    public static readonly UnitId CargoTruck = new(6);
    public static readonly UnitId SupplyTruck = new(7);
}

[Flags]
public enum UnitProductionCapability : byte
{
    None = 0,
    Infantry = 1 << 0,
    Vehicle = 1 << 1,
    Logistics = 1 << 2,
    All = Infantry | Vehicle | Logistics
}

public readonly record struct UnitResourceCost
{
    public UnitResourceCost(ResourceId resourceId, double quantity)
    {
        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Unit costs require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(quantity) || quantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ResourceId = resourceId;
        Quantity = quantity;
    }

    public ResourceId ResourceId { get; }

    public double Quantity { get; }
}

public sealed record UnitDefinition
{
    public required UnitId Id { get; init; }

    public required FactionId Faction { get; init; }

    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required UnitProductionCapability RequiredProductionCapability
    {
        get;
        init;
    }

    public required uint ProductionTicks { get; init; }

    public required IReadOnlyList<UnitResourceCost> Costs { get; init; }

    public required NavigationMovementClass MovementClass { get; init; }

    public required GroundMovement Movement { get; init; }

    public required Vector3 VisualScale { get; init; }

    public required uint VisualId { get; init; }

    public required TargetClass TargetClass { get; init; }

    public required double MaximumHealth { get; init; }

    public ArmorProfileId ArmorProfileId { get; init; }

    public WeaponId WeaponId { get; init; }

    public WeaponId ArtilleryWeaponId { get; init; }

    public float VisualSensorRangeMeters { get; init; }

    public int VisualSensorUpdateIntervalTicks { get; init; } = 1;

    public float RadarDetectionRangeMeters { get; init; }

    public float RadarIdentificationRangeMeters { get; init; }

    public int RadarUpdateIntervalTicks { get; init; } = 4;

    public double FuelCapacity { get; init; }

    public double AmmunitionCapacity { get; init; }

    public double FuelConsumptionPerMeter { get; init; }

    public double InitialFuelFraction { get; init; } = 1.0;

    public double InitialAmmunitionFraction { get; init; } = 1.0;

    public BattlefieldSupplyPriority SupplyPriority { get; init; } =
        BattlefieldSupplyPriority.Normal;

    public bool AutomaticResupply { get; init; } = true;

    public double CargoCapacity { get; init; }

    public bool IsSupplyTruck { get; init; }

    public double SupplyFuelTarget { get; init; }

    public double SupplyAmmunitionTarget { get; init; }

    public float SupplyLoadRangeMeters { get; init; } = 12.0f;

    public float SupplyRangeMeters { get; init; } = 16.0f;

    public int TargetPriority { get; init; }

    public bool IsCargoTransport => CargoCapacity > 0.0;

    public bool HasDirectWeapon => WeaponId.IsSpecified;

    public bool HasArtilleryWeapon => ArtilleryWeaponId.IsSpecified;

    public bool HasRadar => RadarDetectionRangeMeters > 0.0f;

    public void Validate()
    {
        if (!Id.IsSpecified)
        {
            throw new InvalidOperationException(
                "Unit definitions require a stable ID.");
        }

        if (!Faction.IsSpecified)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' requires a valid faction.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        if (RequiredProductionCapability == UnitProductionCapability.None ||
            (RequiredProductionCapability & ~UnitProductionCapability.All) != 0 ||
            (((byte)RequiredProductionCapability &
              ((byte)RequiredProductionCapability - 1)) != 0))
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' requires exactly one supported production capability.");
        }

        if (ProductionTicks == 0)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' requires at least one production tick.");
        }

        ArgumentNullException.ThrowIfNull(Costs);
        if (Costs.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' requires at least one production resource.");
        }

        var resourceIds = new HashSet<ResourceId>();
        for (int index = 0; index < Costs.Count; index++)
        {
            if (!resourceIds.Add(Costs[index].ResourceId))
            {
                throw new InvalidOperationException(
                    $"Unit '{Key}' contains duplicate resource costs.");
            }
        }

        if (!Enum.IsDefined(MovementClass))
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has an invalid movement class.");
        }

        if (!IsFinitePositive(VisualScale))
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has invalid presentation scale.");
        }

        if (VisualId == 0)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' requires a non-zero visual ID.");
        }

        if (!Enum.IsDefined(TargetClass))
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has an invalid target class.");
        }

        if (!double.IsFinite(MaximumHealth) || MaximumHealth <= 0.0)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' requires positive finite health.");
        }

        if (!float.IsFinite(VisualSensorRangeMeters) ||
            VisualSensorRangeMeters < 0.0f)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has an invalid visual sensor range.");
        }

        if (VisualSensorRangeMeters > 0.0f &&
            VisualSensorUpdateIntervalTicks < 1)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has an invalid visual sensor update interval.");
        }

        if (!float.IsFinite(RadarDetectionRangeMeters) ||
            RadarDetectionRangeMeters < 0.0f ||
            !float.IsFinite(RadarIdentificationRangeMeters) ||
            RadarIdentificationRangeMeters < 0.0f ||
            RadarIdentificationRangeMeters > RadarDetectionRangeMeters)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has invalid radar ranges.");
        }

        if (HasRadar && RadarUpdateIntervalTicks < 1)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has an invalid radar update interval.");
        }

        ValidatePositiveFinite(FuelCapacity, nameof(FuelCapacity));
        ValidatePositiveFinite(AmmunitionCapacity, nameof(AmmunitionCapacity));

        if (!double.IsFinite(FuelConsumptionPerMeter) ||
            FuelConsumptionPerMeter < 0.0)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has invalid fuel consumption.");
        }

        ValidateFraction(InitialFuelFraction, nameof(InitialFuelFraction));
        ValidateFraction(
            InitialAmmunitionFraction,
            nameof(InitialAmmunitionFraction));

        if (!Enum.IsDefined(SupplyPriority))
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has an invalid supply priority.");
        }

        if (!double.IsFinite(CargoCapacity) || CargoCapacity < 0.0)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' has invalid cargo capacity.");
        }

        if (IsSupplyTruck && !IsCargoTransport)
        {
            throw new InvalidOperationException(
                $"Unit '{Key}' is a supply truck without cargo capacity.");
        }

        if (IsSupplyTruck)
        {
            ValidatePositiveFinite(
                SupplyFuelTarget,
                nameof(SupplyFuelTarget));
            ValidatePositiveFinite(
                SupplyAmmunitionTarget,
                nameof(SupplyAmmunitionTarget));

            if (!float.IsFinite(SupplyLoadRangeMeters) ||
                SupplyLoadRangeMeters <= 0.0f ||
                !float.IsFinite(SupplyRangeMeters) ||
                SupplyRangeMeters <= 0.0f)
            {
                throw new InvalidOperationException(
                    $"Unit '{Key}' has invalid supply ranges.");
            }

            if (SupplyFuelTarget + SupplyAmmunitionTarget >
                CargoCapacity)
            {
                throw new InvalidOperationException(
                    $"Unit '{Key}' supply targets exceed cargo capacity.");
            }
        }
    }

    private static void ValidatePositiveFinite(
        double value,
        string propertyName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new InvalidOperationException(
                $"Unit property '{propertyName}' must be positive and finite.");
        }
    }

    private static void ValidateFraction(
        double value,
        string propertyName)
    {
        if (!double.IsFinite(value) ||
            value < 0.0 ||
            value > 1.0)
        {
            throw new InvalidOperationException(
                $"Unit property '{propertyName}' must be between zero and one.");
        }
    }

    private static bool IsFinitePositive(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        value.X > 0.0f &&
        value.Y > 0.0f &&
        value.Z > 0.0f;
}

public sealed class UnitDefinitionCatalog
{
    private readonly Dictionary<UnitId, UnitDefinition> _byId;
    private readonly Dictionary<string, UnitId> _byKey;

    public UnitDefinitionCatalog(IEnumerable<UnitDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byId = new Dictionary<UnitId, UnitDefinition>();
        _byKey = new Dictionary<string, UnitId>(StringComparer.Ordinal);

        foreach (UnitDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate unit ID '{definition.Id}'.",
                    nameof(definitions));
            }

            if (!_byKey.TryAdd(definition.Key, definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate unit key '{definition.Key}'.",
                    nameof(definitions));
            }
        }
    }

    public int Count => _byId.Count;

    public IEnumerable<UnitDefinition> Definitions =>
        _byId.OrderBy(static pair => pair.Key).Select(static pair => pair.Value);

    public UnitDefinition this[UnitId id] =>
        _byId.TryGetValue(id, out UnitDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown unit ID '{id}'.");

    public bool TryGet(
        UnitId id,
        [NotNullWhen(true)] out UnitDefinition? definition) =>
        _byId.TryGetValue(id, out definition);

    public bool TryResolve(string key, out UnitId id)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.TryGetValue(key, out id);
    }
}
