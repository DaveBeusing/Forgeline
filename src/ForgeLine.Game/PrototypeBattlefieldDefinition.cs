using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct BattlefieldMapMetadata(
    string Key,
    string DisplayName,
    float WidthMeters,
    float HeightMeters,
    int RecommendedPlayers);

public readonly record struct BattlefieldStartPosition(
    PlayerId Player,
    Vector3 Position,
    Vector3 CommandCorePosition,
    AxisAlignedBounds BuildArea);

public readonly record struct BattlefieldResourceDepositDefinition(
    string Key,
    ResourceId ResourceId,
    Vector3 Center,
    Vector3 HalfExtents,
    double TotalQuantity,
    double ExtractionRatePerSecond,
    bool Contested);

public enum BattlefieldSiteKind : byte
{
    Expansion = 1,
    MiningOutpost = 2,
    ForwardOperatingBase = 3
}

public readonly record struct BattlefieldSiteDefinition(
    string Key,
    string DisplayName,
    BattlefieldSiteKind Kind,
    Vector3 Position,
    AxisAlignedBounds BuildArea);

public readonly record struct BattlefieldRoadNodeDefinition(
    string Key,
    Vector3 Position);

public readonly record struct BattlefieldRoadEdgeDefinition(
    string Key,
    string SourceNodeKey,
    string DestinationNodeKey,
    double BaseCost,
    double CapacityPerSecond);

public readonly record struct BattlefieldCrossingDefinition(
    string Key,
    string DisplayName,
    Vector3 Position,
    AxisAlignedBounds PassageBounds,
    string LogisticsEdgeKey,
    bool InitiallyOperational,
    bool Restorable,
    uint RestorationTicks);

public readonly record struct BattlefieldObjectiveDefinition(
    string Key,
    PlayerId Owner,
    Vector3 CommandCorePosition);

public sealed class PrototypeBattlefieldDefinition
{
    public const float WidthMeters = 3_072.0f;
    public const float HeightMeters = 3_072.0f;
    public const float BarrierMinimumX = 1_488.0f;
    public const float BarrierMaximumX = 1_584.0f;

    private readonly AxisAlignedBounds[] _staticNavigationObstacles;

    private PrototypeBattlefieldDefinition(
        BattlefieldMapMetadata metadata,
        BattlefieldStartPosition[] starts,
        BattlefieldResourceDepositDefinition[] resources,
        BattlefieldSiteDefinition[] sites,
        BattlefieldRoadNodeDefinition[] roadNodes,
        BattlefieldRoadEdgeDefinition[] roadEdges,
        BattlefieldCrossingDefinition[] crossings,
        BattlefieldObjectiveDefinition[] objectives,
        AxisAlignedBounds[] staticNavigationObstacles)
    {
        Metadata = metadata;
        Starts = starts;
        Resources = resources;
        Sites = sites;
        RoadNodes = roadNodes;
        RoadEdges = roadEdges;
        Crossings = crossings;
        Objectives = objectives;
        _staticNavigationObstacles = staticNavigationObstacles;
    }

    public BattlefieldMapMetadata Metadata { get; }

    public IReadOnlyList<BattlefieldStartPosition> Starts { get; }

    public IReadOnlyList<BattlefieldResourceDepositDefinition> Resources { get; }

    public IReadOnlyList<BattlefieldSiteDefinition> Sites { get; }

    public IReadOnlyList<BattlefieldRoadNodeDefinition> RoadNodes { get; }

    public IReadOnlyList<BattlefieldRoadEdgeDefinition> RoadEdges { get; }

    public IReadOnlyList<BattlefieldCrossingDefinition> Crossings { get; }

    public IReadOnlyList<BattlefieldObjectiveDefinition> Objectives { get; }

    public IReadOnlyList<AxisAlignedBounds> StaticNavigationObstacles =>
        _staticNavigationObstacles;

    public static PrototypeBattlefieldDefinition Create()
    {
        var definition = new PrototypeBattlefieldDefinition(
            new BattlefieldMapMetadata(
                "prototype.vertical_slice",
                "Central Divide",
                WidthMeters,
                HeightMeters,
                RecommendedPlayers: 2),
            CreateStarts(),
            CreateResources(),
            CreateSites(),
            CreateRoadNodes(),
            CreateRoadEdges(),
            CreateCrossings(),
            CreateObjectives(),
            CreateBarrierObstacles());

        PrototypeBattlefieldValidator.ValidateDefinition(definition);
        return definition;
    }

    public BattlefieldCrossingDefinition GetCrossing(string key)
    {
        for (int index = 0; index < Crossings.Count; index++)
        {
            BattlefieldCrossingDefinition crossing = Crossings[index];
            if (string.Equals(crossing.Key, key, StringComparison.Ordinal))
            {
                return crossing;
            }
        }

        throw new KeyNotFoundException(
            $"Unknown battlefield crossing '{key}'.");
    }

    public BattlefieldRoadEdgeDefinition GetRoadEdge(string key)
    {
        for (int index = 0; index < RoadEdges.Count; index++)
        {
            BattlefieldRoadEdgeDefinition edge = RoadEdges[index];
            if (string.Equals(edge.Key, key, StringComparison.Ordinal))
            {
                return edge;
            }
        }

        throw new KeyNotFoundException(
            $"Unknown battlefield road edge '{key}'.");
    }

    public IReadOnlyList<AxisAlignedBounds> CreateNavigationObstacles(
        IReadOnlyDictionary<string, bool>? crossingAvailability = null)
    {
        var obstacles = new List<AxisAlignedBounds>(
            _staticNavigationObstacles.Length + Crossings.Count);
        obstacles.AddRange(_staticNavigationObstacles);

        for (int index = 0; index < Crossings.Count; index++)
        {
            BattlefieldCrossingDefinition crossing = Crossings[index];
            bool operational =
                crossingAvailability is null ||
                !crossingAvailability.TryGetValue(
                    crossing.Key,
                    out bool explicitAvailability)
                    ? crossing.InitiallyOperational
                    : explicitAvailability;

            if (!operational)
            {
                obstacles.Add(crossing.PassageBounds);
            }
        }

        return obstacles;
    }

    private static BattlefieldStartPosition[] CreateStarts() =>
    [
        new(
            new PlayerId(1),
            new Vector3(420.0f, 0.0f, 1_536.0f),
            new Vector3(470.0f, 0.0f, 1_536.0f),
            Bounds2D(220.0f, 1_300.0f, 720.0f, 1_772.0f)),
        new(
            new PlayerId(2),
            new Vector3(2_652.0f, 0.0f, 1_536.0f),
            new Vector3(2_602.0f, 0.0f, 1_536.0f),
            Bounds2D(2_352.0f, 1_300.0f, 2_852.0f, 1_772.0f))
    ];

    private static BattlefieldResourceDepositDefinition[] CreateResources() =>
    [
        Deposit("west.ferrous", ResourceIds.FerrousOre, 620, 1_390, 5_500, 7.5, false),
        Deposit("west.volatiles", ResourceIds.Volatiles, 560, 1_690, 3_500, 5.0, false),
        Deposit("west.silicates", ResourceIds.Silicates, 760, 1_560, 4_000, 5.5, false),

        Deposit("east.ferrous", ResourceIds.FerrousOre, 2_452, 1_390, 5_500, 7.5, false),
        Deposit("east.volatiles", ResourceIds.Volatiles, 2_512, 1_690, 3_500, 5.0, false),
        Deposit("east.silicates", ResourceIds.Silicates, 2_312, 1_560, 4_000, 5.5, false),

        Deposit("north.contested.ferrous", ResourceIds.FerrousOre, 1_360, 760, 12_000, 10.0, true),
        Deposit("north.contested.volatiles", ResourceIds.Volatiles, 1_720, 690, 8_000, 7.0, true),
        Deposit("center.contested.silicates", ResourceIds.Silicates, 1_536, 1_580, 10_000, 8.0, true),
        Deposit("south.contested.ferrous", ResourceIds.FerrousOre, 1_720, 2_430, 12_000, 10.0, true),
        Deposit("south.contested.volatiles", ResourceIds.Volatiles, 1_330, 2_360, 8_000, 7.0, true),

        Deposit("west.outpost.silicates", ResourceIds.Silicates, 700, 520, 9_000, 7.0, true),
        Deposit("east.outpost.silicates", ResourceIds.Silicates, 2_372, 2_560, 9_000, 7.0, true)
    ];

    private static BattlefieldSiteDefinition[] CreateSites() =>
    [
        Site("northwest.expansion", "Northwest Expansion", BattlefieldSiteKind.Expansion, 900, 760, 180),
        Site("northeast.expansion", "Northeast Expansion", BattlefieldSiteKind.Expansion, 2_160, 760, 180),
        Site("southwest.expansion", "Southwest Expansion", BattlefieldSiteKind.Expansion, 900, 2_330, 180),
        Site("southeast.expansion", "Southeast Expansion", BattlefieldSiteKind.Expansion, 2_160, 2_330, 180),
        Site("west.mining_outpost", "Western Mining Outpost", BattlefieldSiteKind.MiningOutpost, 650, 520, 150),
        Site("east.mining_outpost", "Eastern Mining Outpost", BattlefieldSiteKind.MiningOutpost, 2_420, 2_560, 150),
        Site("northwest.fob", "Northwest FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_250, 930, 120),
        Site("northeast.fob", "Northeast FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_820, 930, 120),
        Site("southwest.fob", "Southwest FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_250, 2_190, 120),
        Site("southeast.fob", "Southeast FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_820, 2_190, 120)
    ];

    private static BattlefieldRoadNodeDefinition[] CreateRoadNodes() =>
    [
        new("west.start", new Vector3(520.0f, 0.0f, 1_536.0f)),
        new("west.junction", new Vector3(1_020.0f, 0.0f, 1_536.0f)),
        new("north.west", new Vector3(1_360.0f, 0.0f, 920.0f)),
        new("north.east", new Vector3(1_712.0f, 0.0f, 920.0f)),
        new("south.west", new Vector3(1_360.0f, 0.0f, 2_200.0f)),
        new("south.east", new Vector3(1_712.0f, 0.0f, 2_200.0f)),
        new("east.junction", new Vector3(2_052.0f, 0.0f, 1_536.0f)),
        new("east.start", new Vector3(2_552.0f, 0.0f, 1_536.0f))
    ];

    private static BattlefieldRoadEdgeDefinition[] CreateRoadEdges() =>
    [
        new("west.start-link", "west.start", "west.junction", 1.0, 140.0),
        new("north.west-link", "west.junction", "north.west", 1.0, 120.0),
        new("north.bridge", "north.west", "north.east", 0.8, 160.0),
        new("north.east-link", "north.east", "east.junction", 1.0, 120.0),
        new("south.west-link", "west.junction", "south.west", 1.2, 110.0),
        new("south.ford", "south.west", "south.east", 1.4, 90.0),
        new("south.east-link", "south.east", "east.junction", 1.2, 110.0),
        new("east.start-link", "east.junction", "east.start", 1.0, 140.0)
    ];

    private static BattlefieldCrossingDefinition[] CreateCrossings() =>
    [
        new(
            "crossing.north_bridge",
            "North Bridge",
            new Vector3(1_536.0f, 0.0f, 920.0f),
            Bounds2D(BarrierMinimumX, 840.0f, BarrierMaximumX, 1_000.0f),
            "north.bridge",
            InitiallyOperational: true,
            Restorable: true,
            RestorationTicks: 80),
        new(
            "crossing.south_ford",
            "South Ford",
            new Vector3(1_536.0f, 0.0f, 2_200.0f),
            Bounds2D(BarrierMinimumX, 2_100.0f, BarrierMaximumX, 2_300.0f),
            "south.ford",
            InitiallyOperational: true,
            Restorable: true,
            RestorationTicks: 120)
    ];

    private static BattlefieldObjectiveDefinition[] CreateObjectives() =>
    [
        new("objective.west_command_core", new PlayerId(1), new Vector3(470.0f, 0.0f, 1_536.0f)),
        new("objective.east_command_core", new PlayerId(2), new Vector3(2_602.0f, 0.0f, 1_536.0f))
    ];

    private static AxisAlignedBounds[] CreateBarrierObstacles() =>
    [
        Bounds2D(BarrierMinimumX, 0.0f, BarrierMaximumX, 840.0f),
        Bounds2D(BarrierMinimumX, 1_000.0f, BarrierMaximumX, 2_100.0f),
        Bounds2D(BarrierMinimumX, 2_300.0f, BarrierMaximumX, HeightMeters)
    ];

    private static BattlefieldResourceDepositDefinition Deposit(
        string key,
        ResourceId resource,
        float x,
        float z,
        double quantity,
        double rate,
        bool contested) =>
        new(
            key,
            resource,
            new Vector3(x, 0.0f, z),
            new Vector3(42.0f, 4.0f, 42.0f),
            quantity,
            rate,
            contested);

    private static BattlefieldSiteDefinition Site(
        string key,
        string displayName,
        BattlefieldSiteKind kind,
        float x,
        float z,
        float halfSize) =>
        new(
            key,
            displayName,
            kind,
            new Vector3(x, 0.0f, z),
            Bounds2D(
                x - halfSize,
                z - halfSize,
                x + halfSize,
                z + halfSize));

    internal static AxisAlignedBounds Bounds2D(
        float minimumX,
        float minimumZ,
        float maximumX,
        float maximumZ) =>
        new(
            new Vector3(minimumX, -64.0f, minimumZ),
            new Vector3(maximumX, 128.0f, maximumZ));
}
