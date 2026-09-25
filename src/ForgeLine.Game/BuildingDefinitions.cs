using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

public readonly record struct BuildingId(uint Value) : IComparable<BuildingId>
{
    public static BuildingId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(BuildingId other) => Value.CompareTo(other.Value);

    public static bool operator <(BuildingId left, BuildingId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(BuildingId left, BuildingId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(BuildingId left, BuildingId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(BuildingId left, BuildingId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class BuildingIds
{
    public static readonly BuildingId CommandCore = new(1);
    public static readonly BuildingId PowerPlant = new(2);
    public static readonly BuildingId Extractor = new(3);
    public static readonly BuildingId StorageDepot = new(4);
    public static readonly BuildingId Smelter = new(5);
    public static readonly BuildingId Refinery = new(6);
    public static readonly BuildingId ElectronicsPlant = new(7);
    public static readonly BuildingId LogisticsHub = new(8);
}

public enum BuildingOrientation : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}

[Flags]
public enum BuildingCapability : uint
{
    None = 0,
    Command = 1 << 0,
    PowerGeneration = 1 << 1,
    PowerConsumption = 1 << 2,
    Extraction = 1 << 3,
    Storage = 1 << 4,
    Processing = 1 << 5,
    Distribution = 1 << 6
}

public readonly record struct BuildingFootprint
{
    public BuildingFootprint(float width, float depth, float height)
    {
        if (!float.IsFinite(width) || width <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (!float.IsFinite(depth) || depth <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        if (!float.IsFinite(height) || height <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Depth = depth;
        Height = height;
    }

    public float Width { get; }

    public float Depth { get; }

    public float Height { get; }

    public Vector3 GetHalfExtents(BuildingOrientation orientation)
    {
        ValidateOrientation(orientation);

        bool rotated =
            orientation is BuildingOrientation.East or BuildingOrientation.West;
        float width = rotated ? Depth : Width;
        float depth = rotated ? Width : Depth;

        return new Vector3(width * 0.5f, Height * 0.5f, depth * 0.5f);
    }

    public static Quaternion GetRotation(BuildingOrientation orientation)
    {
        ValidateOrientation(orientation);

        float yaw = orientation switch
        {
            BuildingOrientation.North => 0.0f,
            BuildingOrientation.East => MathF.PI * 0.5f,
            BuildingOrientation.South => MathF.PI,
            BuildingOrientation.West => MathF.PI * 1.5f,
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };

        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    }

    private static void ValidateOrientation(BuildingOrientation orientation)
    {
        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(orientation));
        }
    }
}

public readonly record struct BuildingResourceCost
{
    public BuildingResourceCost(ResourceId resourceId, double quantity)
    {
        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Building costs require a stable resource ID.",
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

public sealed record BuildingDefinition
{
    public required BuildingId Id { get; init; }

    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required BuildingFootprint Footprint { get; init; }

    public required uint ConstructionTicks { get; init; }

    public required IReadOnlyList<BuildingResourceCost> Costs { get; init; }

    public BuildingCapability Capabilities { get; init; }

    public float MaximumSlopeDegrees { get; init; } = 12.0f;

    public uint VisualId { get; init; } = 1;

    public double PowerGeneration { get; init; }

    public double PowerDemand { get; init; }

    public PowerPriority PowerPriority { get; init; } = PowerPriority.Industrial;

    public double StorageCapacity { get; init; }

    public double ExtractionRatePerSecond { get; init; }

    public bool RequiresResourceDeposit { get; init; }

    public ProductionCapability ProductionCapabilities { get; init; }

    public double ProductionInputCapacity { get; init; }

    public double ProductionOutputCapacity { get; init; }

    public void Validate()
    {
        if (!Id.IsSpecified)
        {
            throw new InvalidOperationException(
                "Building definitions require a stable ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        if (ConstructionTicks == 0)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' must require at least one construction tick.");
        }

        if (!float.IsFinite(MaximumSlopeDegrees) ||
            MaximumSlopeDegrees < 0.0f ||
            MaximumSlopeDegrees >= 90.0f)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' has an invalid maximum slope.");
        }

        if (VisualId == 0)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' requires a non-zero visual ID.");
        }

        if (!double.IsFinite(PowerGeneration) || PowerGeneration < 0.0 ||
            !double.IsFinite(PowerDemand) || PowerDemand < 0.0 ||
            !double.IsFinite(StorageCapacity) || StorageCapacity < 0.0 ||
            !double.IsFinite(ExtractionRatePerSecond) || ExtractionRatePerSecond < 0.0 ||
            !double.IsFinite(ProductionInputCapacity) || ProductionInputCapacity < 0.0 ||
            !double.IsFinite(ProductionOutputCapacity) || ProductionOutputCapacity < 0.0)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' contains invalid capability values.");
        }

        if (Costs.Count == 0)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' requires at least one construction resource.");
        }

        var resourceIds = new HashSet<ResourceId>();
        for (int index = 0; index < Costs.Count; index++)
        {
            BuildingResourceCost cost = Costs[index];
            if (!resourceIds.Add(cost.ResourceId))
            {
                throw new InvalidOperationException(
                    $"Building '{Key}' contains duplicate resource costs.");
            }
        }

        ValidateCapability(
            BuildingCapability.PowerGeneration,
            PowerGeneration > 0.0,
            nameof(PowerGeneration));
        ValidateCapability(
            BuildingCapability.PowerConsumption,
            PowerDemand > 0.0,
            nameof(PowerDemand));
        ValidateCapability(
            BuildingCapability.Storage,
            StorageCapacity > 0.0,
            nameof(StorageCapacity));
        ValidateCapability(
            BuildingCapability.Extraction,
            ExtractionRatePerSecond > 0.0 && RequiresResourceDeposit,
            nameof(ExtractionRatePerSecond));

        bool processingEnabled =
            Capabilities.HasFlag(BuildingCapability.Processing);
        bool processingConfigured =
            ProductionCapabilities != ProductionCapability.None &&
            ProductionInputCapacity > 0.0 &&
            ProductionOutputCapacity > 0.0;

        if (processingEnabled != processingConfigured)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' processing capability does not match its production configuration.");
        }

        if (!processingEnabled &&
            (ProductionCapabilities != ProductionCapability.None ||
             ProductionInputCapacity != 0.0 ||
             ProductionOutputCapacity != 0.0))
        {
            throw new InvalidOperationException(
                $"Building '{Key}' defines production values without the processing capability.");
        }
    }

    private void ValidateCapability(
        BuildingCapability capability,
        bool configured,
        string propertyName)
    {
        bool enabled = Capabilities.HasFlag(capability);
        if (enabled != configured)
        {
            throw new InvalidOperationException(
                $"Building '{Key}' capability '{capability}' does not match '{propertyName}'.");
        }
    }
}

public sealed class BuildingDefinitionCatalog
{
    private readonly Dictionary<BuildingId, BuildingDefinition> _byId;
    private readonly Dictionary<string, BuildingId> _byKey;

    public BuildingDefinitionCatalog(IEnumerable<BuildingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byId = new Dictionary<BuildingId, BuildingDefinition>();
        _byKey = new Dictionary<string, BuildingId>(StringComparer.Ordinal);

        foreach (BuildingDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate building ID '{definition.Id}'.",
                    nameof(definitions));
            }

            if (!_byKey.TryAdd(definition.Key, definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate building key '{definition.Key}'.",
                    nameof(definitions));
            }
        }
    }

    public int Count => _byId.Count;

    public BuildingDefinition this[BuildingId id] =>
        _byId.TryGetValue(id, out BuildingDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown building ID '{id}'.");

    public bool TryGet(
        BuildingId id,
        [NotNullWhen(true)] out BuildingDefinition? definition) =>
        _byId.TryGetValue(id, out definition);

    public bool TryResolve(string key, out BuildingId id)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.TryGetValue(key, out id);
    }
}

public static class InitialBuildingDefinitions
{
    public static BuildingDefinitionCatalog CreateCatalog()
    {
        return new BuildingDefinitionCatalog(
            [
                new BuildingDefinition
                {
                    Id = BuildingIds.CommandCore,
                    Key = "building.command_core",
                    DisplayName = "Command Core",
                    Footprint = new BuildingFootprint(20.0f, 20.0f, 12.0f),
                    ConstructionTicks = 160,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 240.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 80.0),
                        new BuildingResourceCost(ResourceIds.Volatiles, 40.0)
                    ],
                    Capabilities =
                        BuildingCapability.Command |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 20.0,
                    PowerPriority = PowerPriority.Critical
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.PowerPlant,
                    Key = "building.power_plant",
                    DisplayName = "Power Plant",
                    Footprint = new BuildingFootprint(16.0f, 16.0f, 10.0f),
                    ConstructionTicks = 100,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 160.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 60.0),
                        new BuildingResourceCost(ResourceIds.Volatiles, 20.0)
                    ],
                    Capabilities = BuildingCapability.PowerGeneration,
                    PowerGeneration = 100.0
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.Extractor,
                    Key = "building.extractor",
                    DisplayName = "Mine / Extractor",
                    Footprint = new BuildingFootprint(12.0f, 12.0f, 8.0f),
                    ConstructionTicks = 80,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 100.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 40.0)
                    ],
                    Capabilities =
                        BuildingCapability.Extraction |
                        BuildingCapability.Storage |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 15.0,
                    StorageCapacity = 250.0,
                    ExtractionRatePerSecond = 10.0,
                    RequiresResourceDeposit = true
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.StorageDepot,
                    Key = "building.storage_depot",
                    DisplayName = "Storage Depot",
                    Footprint = new BuildingFootprint(14.0f, 14.0f, 8.0f),
                    ConstructionTicks = 70,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 120.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 40.0)
                    ],
                    Capabilities =
                        BuildingCapability.Storage |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 5.0,
                    StorageCapacity = 5_000.0
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.LogisticsHub,
                    Key = "building.logistics_hub",
                    DisplayName = "Logistics Hub",
                    Footprint = new BuildingFootprint(18.0f, 18.0f, 9.0f),
                    ConstructionTicks = 100,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 180.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 80.0),
                        new BuildingResourceCost(ResourceIds.Volatiles, 20.0)
                    ],
                    Capabilities =
                        BuildingCapability.Storage |
                        BuildingCapability.Distribution |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 10.0,
                    StorageCapacity = 3_000.0
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.Smelter,
                    Key = "building.smelter",
                    DisplayName = "Smelter",
                    Footprint = new BuildingFootprint(18.0f, 16.0f, 10.0f),
                    ConstructionTicks = 120,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 180.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 80.0),
                        new BuildingResourceCost(ResourceIds.Volatiles, 30.0)
                    ],
                    Capabilities =
                        BuildingCapability.Processing |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 30.0,
                    ProductionCapabilities = ProductionCapability.SteelProcessing,
                    ProductionInputCapacity = 1_000.0,
                    ProductionOutputCapacity = 1_000.0
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.Refinery,
                    Key = "building.refinery",
                    DisplayName = "Refinery",
                    Footprint = new BuildingFootprint(18.0f, 16.0f, 10.0f),
                    ConstructionTicks = 110,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 160.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 60.0),
                        new BuildingResourceCost(ResourceIds.Volatiles, 40.0)
                    ],
                    Capabilities =
                        BuildingCapability.Processing |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 25.0,
                    ProductionCapabilities = ProductionCapability.FuelProcessing,
                    ProductionInputCapacity = 1_000.0,
                    ProductionOutputCapacity = 1_000.0
                },
                new BuildingDefinition
                {
                    Id = BuildingIds.ElectronicsPlant,
                    Key = "building.electronics_plant",
                    DisplayName = "Electronics Plant",
                    Footprint = new BuildingFootprint(18.0f, 18.0f, 10.0f),
                    ConstructionTicks = 130,
                    Costs =
                    [
                        new BuildingResourceCost(ResourceIds.FerrousOre, 170.0),
                        new BuildingResourceCost(ResourceIds.Silicates, 100.0),
                        new BuildingResourceCost(ResourceIds.Volatiles, 20.0)
                    ],
                    Capabilities =
                        BuildingCapability.Processing |
                        BuildingCapability.PowerConsumption,
                    PowerDemand = 35.0,
                    ProductionCapabilities =
                        ProductionCapability.ElectronicsProcessing,
                    ProductionInputCapacity = 1_000.0,
                    ProductionOutputCapacity = 1_000.0
                }
            ]);
    }
}
