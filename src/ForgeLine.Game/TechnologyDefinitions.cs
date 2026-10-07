using System.Diagnostics.CodeAnalysis;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

public readonly record struct TechnologyId(uint Value) : IComparable<TechnologyId>
{
    public static TechnologyId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(TechnologyId other) =>
        Value.CompareTo(other.Value);

    public static bool operator <(TechnologyId left, TechnologyId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(TechnologyId left, TechnologyId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(TechnologyId left, TechnologyId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(TechnologyId left, TechnologyId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct TechnologyCapabilityId(uint Value) :
    IComparable<TechnologyCapabilityId>
{
    public static TechnologyCapabilityId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(TechnologyCapabilityId other) =>
        Value.CompareTo(other.Value);

    public static bool operator <(
        TechnologyCapabilityId left,
        TechnologyCapabilityId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(
        TechnologyCapabilityId left,
        TechnologyCapabilityId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(
        TechnologyCapabilityId left,
        TechnologyCapabilityId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(
        TechnologyCapabilityId left,
        TechnologyCapabilityId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
}

public enum TechnologyDomain : byte
{
    Industry = 0,
    Logistics = 1,
    Warfare = 2,
    Intelligence = 3
}

public enum TechnologyPhase : byte
{
    Bootstrap = 0,
    IndustrialFoundation = 1,
    MechanizedWarfare = 2,
    IntegratedWarfare = 3,
    StrategicWarfare = 4,
    IndustrialSupremacy = 5
}

public readonly record struct TechnologyResourceCost
{
    public TechnologyResourceCost(
        ResourceId resourceId,
        double quantity)
    {
        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Technology costs require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(quantity) ||
            quantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity));
        }

        ResourceId = resourceId;
        Quantity = quantity;
    }

    public ResourceId ResourceId { get; }

    public double Quantity { get; }
}

public sealed record TechnologyDefinition
{
    public required TechnologyId Id { get; init; }

    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required TechnologyDomain Domain { get; init; }

    public required TechnologyPhase Phase { get; init; }

    public required uint ResearchTicks { get; init; }

    public required IReadOnlyList<TechnologyResourceCost> Costs { get; init; }

    public IReadOnlyList<TechnologyId> Prerequisites { get; init; } =
        Array.Empty<TechnologyId>();

    public required BuildingId RequiredFacility { get; init; }

    public double RequiredPowerFraction { get; init; } = 1.0;

    public IReadOnlyList<TechnologyCapabilityId> Unlocks { get; init; } =
        Array.Empty<TechnologyCapabilityId>();

    public void Validate()
    {
        if (!Id.IsSpecified)
        {
            throw new InvalidOperationException(
                "Technology definitions require a stable ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        if (!Enum.IsDefined(Domain))
        {
            throw new InvalidOperationException(
                $"Technology '{Key}' has an invalid domain.");
        }

        if (!Enum.IsDefined(Phase))
        {
            throw new InvalidOperationException(
                $"Technology '{Key}' has an invalid phase.");
        }

        if (ResearchTicks == 0)
        {
            throw new InvalidOperationException(
                $"Technology '{Key}' must require at least one simulation tick.");
        }

        if (!RequiredFacility.IsSpecified)
        {
            throw new InvalidOperationException(
                $"Technology '{Key}' requires a concrete research facility.");
        }

        if (!double.IsFinite(RequiredPowerFraction) ||
            RequiredPowerFraction < 0.0 ||
            RequiredPowerFraction > 1.0)
        {
            throw new InvalidOperationException(
                $"Technology '{Key}' has an invalid power requirement.");
        }

        ArgumentNullException.ThrowIfNull(Costs);
        ArgumentNullException.ThrowIfNull(Prerequisites);
        ArgumentNullException.ThrowIfNull(Unlocks);

        if (Costs.Count == 0)
        {
            throw new InvalidOperationException(
                $"Technology '{Key}' requires at least one physical material cost.");
        }

        var resources = new HashSet<ResourceId>();
        for (int index = 0;
             index < Costs.Count;
             index++)
        {
            if (!resources.Add(
                    Costs[index].ResourceId))
            {
                throw new InvalidOperationException(
                    $"Technology '{Key}' contains duplicate material costs.");
            }
        }

        var prerequisites = new HashSet<TechnologyId>();
        for (int index = 0;
             index < Prerequisites.Count;
             index++)
        {
            TechnologyId prerequisite =
                Prerequisites[index];

            if (!prerequisite.IsSpecified ||
                prerequisite == Id ||
                !prerequisites.Add(prerequisite))
            {
                throw new InvalidOperationException(
                    $"Technology '{Key}' contains an invalid prerequisite.");
            }
        }

        var unlocks = new HashSet<TechnologyCapabilityId>();
        for (int index = 0;
             index < Unlocks.Count;
             index++)
        {
            TechnologyCapabilityId capability =
                Unlocks[index];

            if (!capability.IsSpecified ||
                !unlocks.Add(capability))
            {
                throw new InvalidOperationException(
                    $"Technology '{Key}' contains an invalid capability unlock.");
            }
        }
    }
}

public sealed class TechnologyDefinitionCatalog
{
    private readonly Dictionary<TechnologyId, TechnologyDefinition> _byId;
    private readonly Dictionary<string, TechnologyId> _byKey;

    public TechnologyDefinitionCatalog(
        IEnumerable<TechnologyDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byId =
            new Dictionary<TechnologyId, TechnologyDefinition>();
        _byKey =
            new Dictionary<string, TechnologyId>(
                StringComparer.Ordinal);

        foreach (TechnologyDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!_byId.TryAdd(
                    definition.Id,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate technology ID '{definition.Id}'.",
                    nameof(definitions));
            }

            if (!_byKey.TryAdd(
                    definition.Key,
                    definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate technology key '{definition.Key}'.",
                    nameof(definitions));
            }
        }

        ValidateGraph();
    }

    public int Count => _byId.Count;

    public IEnumerable<TechnologyDefinition> Definitions =>
        _byId
            .OrderBy(
                static pair =>
                    pair.Value.Domain)
            .ThenBy(
                static pair =>
                    pair.Value.Phase)
            .ThenBy(
                static pair =>
                    pair.Key)
            .Select(
                static pair =>
                    pair.Value);

    public TechnologyDefinition this[TechnologyId id] =>
        _byId.TryGetValue(
            id,
            out TechnologyDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown technology ID '{id}'.");

    public bool TryGet(
        TechnologyId id,
        [NotNullWhen(true)] out TechnologyDefinition? definition) =>
        _byId.TryGetValue(
            id,
            out definition);

    public bool TryResolve(
        string key,
        out TechnologyId id)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.TryGetValue(
            key,
            out id);
    }

    private void ValidateGraph()
    {
        foreach (TechnologyDefinition definition in
                 _byId.Values)
        {
            for (int index = 0;
                 index < definition.Prerequisites.Count;
                 index++)
            {
                TechnologyId prerequisite =
                    definition.Prerequisites[index];

                if (!_byId.ContainsKey(
                        prerequisite))
                {
                    throw new ArgumentException(
                        $"Technology '{definition.Key}' references unknown prerequisite '{prerequisite}'.");
                }
            }
        }

        var visiting = new HashSet<TechnologyId>();
        var visited = new HashSet<TechnologyId>();

        foreach (TechnologyId technology in
                 _byId.Keys)
        {
            Visit(
                technology,
                visiting,
                visited);
        }
    }

    private void Visit(
        TechnologyId technology,
        HashSet<TechnologyId> visiting,
        HashSet<TechnologyId> visited)
    {
        if (visited.Contains(
                technology))
        {
            return;
        }

        if (!visiting.Add(
                technology))
        {
            throw new ArgumentException(
                "Technology prerequisites contain a cycle.");
        }

        TechnologyDefinition definition =
            _byId[technology];

        for (int index = 0;
             index < definition.Prerequisites.Count;
             index++)
        {
            Visit(
                definition.Prerequisites[index],
                visiting,
                visited);
        }

        visiting.Remove(
            technology);
        visited.Add(
            technology);
    }
}

public static class TechnologyIds
{
    public static readonly TechnologyId IndustrialStandardization = new(1);
    public static readonly TechnologyId LogisticsCoordination = new(2);
    public static readonly TechnologyId MechanizedSystems = new(3);
    public static readonly TechnologyId SensorFusion = new(4);
}

public static class TechnologyCapabilityIds
{
    public static readonly TechnologyCapabilityId FieldEngineering = new(1);
    public static readonly TechnologyCapabilityId LogisticsCoordination = new(2);
    public static readonly TechnologyCapabilityId MechanizedSystems = new(3);
    public static readonly TechnologyCapabilityId SensorFusion = new(4);
}

public static class DirectorateTechnologyDefinitions
{
    public static TechnologyDefinitionCatalog CreateCatalog() =>
        new(
            [
                new TechnologyDefinition
                {
                    Id =
                        TechnologyIds.IndustrialStandardization,
                    Key =
                        "directorate.technology.industrial_standardization",
                    DisplayName =
                        "Industrial Standardization",
                    Domain =
                        TechnologyDomain.Industry,
                    Phase =
                        TechnologyPhase.IndustrialFoundation,
                    ResearchTicks = 120,
                    Costs =
                    [
                        new TechnologyResourceCost(
                            ResourceIds.Steel,
                            80.0),
                        new TechnologyResourceCost(
                            ResourceIds.Electronics,
                            20.0)
                    ],
                    RequiredFacility =
                        BuildingIds.CommandCore,
                    RequiredPowerFraction = 1.0,
                    Unlocks =
                    [
                        TechnologyCapabilityIds.FieldEngineering
                    ]
                },
                new TechnologyDefinition
                {
                    Id =
                        TechnologyIds.LogisticsCoordination,
                    Key =
                        "directorate.technology.logistics_coordination",
                    DisplayName =
                        "Logistics Coordination",
                    Domain =
                        TechnologyDomain.Logistics,
                    Phase =
                        TechnologyPhase.IndustrialFoundation,
                    ResearchTicks = 100,
                    Costs =
                    [
                        new TechnologyResourceCost(
                            ResourceIds.Steel,
                            60.0),
                        new TechnologyResourceCost(
                            ResourceIds.Electronics,
                            25.0)
                    ],
                    Prerequisites =
                    [
                        TechnologyIds.IndustrialStandardization
                    ],
                    RequiredFacility =
                        BuildingIds.CommandCore,
                    RequiredPowerFraction = 1.0,
                    Unlocks =
                    [
                        TechnologyCapabilityIds.LogisticsCoordination
                    ]
                },
                new TechnologyDefinition
                {
                    Id =
                        TechnologyIds.MechanizedSystems,
                    Key =
                        "directorate.technology.mechanized_systems",
                    DisplayName =
                        "Mechanized Systems",
                    Domain =
                        TechnologyDomain.Warfare,
                    Phase =
                        TechnologyPhase.MechanizedWarfare,
                    ResearchTicks = 160,
                    Costs =
                    [
                        new TechnologyResourceCost(
                            ResourceIds.Steel,
                            100.0),
                        new TechnologyResourceCost(
                            ResourceIds.Electronics,
                            50.0)
                    ],
                    Prerequisites =
                    [
                        TechnologyIds.IndustrialStandardization
                    ],
                    RequiredFacility =
                        BuildingIds.VehicleFactory,
                    RequiredPowerFraction = 1.0,
                    Unlocks =
                    [
                        TechnologyCapabilityIds.MechanizedSystems
                    ]
                },
                new TechnologyDefinition
                {
                    Id =
                        TechnologyIds.SensorFusion,
                    Key =
                        "directorate.technology.sensor_fusion",
                    DisplayName =
                        "Sensor Fusion",
                    Domain =
                        TechnologyDomain.Intelligence,
                    Phase =
                        TechnologyPhase.MechanizedWarfare,
                    ResearchTicks = 140,
                    Costs =
                    [
                        new TechnologyResourceCost(
                            ResourceIds.Steel,
                            60.0),
                        new TechnologyResourceCost(
                            ResourceIds.Electronics,
                            70.0)
                    ],
                    Prerequisites =
                    [
                        TechnologyIds.IndustrialStandardization
                    ],
                    RequiredFacility =
                        BuildingIds.Radar,
                    RequiredPowerFraction = 1.0,
                    Unlocks =
                    [
                        TechnologyCapabilityIds.SensorFusion
                    ]
                }
            ]);
}
