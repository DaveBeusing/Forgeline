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

public enum BattlefieldTerrainControlEncoding : byte
{
    RgbaFourLayer = 1
}

public sealed record BattlefieldTerrainVisualDefinition(
    string ProfileId,
    BattlefieldTerrainControlEncoding ControlEncoding,
    int ActiveLayerLimit,
    int ControlSamplesPerSide,
    string[] MaterialAssetIds)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            ProfileId);
        ArgumentNullException.ThrowIfNull(
            MaterialAssetIds);

        if (ControlEncoding !=
            BattlefieldTerrainControlEncoding.RgbaFourLayer)
        {
            throw new InvalidOperationException(
                $"Unsupported terrain control encoding {ControlEncoding}.");
        }

        if (ActiveLayerLimit != 4)
        {
            throw new InvalidOperationException(
                "The current terrain renderer requires exactly four active layers per chunk.");
        }

        if (ControlSamplesPerSide < 2)
        {
            throw new InvalidOperationException(
                "Terrain control maps require at least two samples per side.");
        }

        if (MaterialAssetIds.Length == 0 ||
            MaterialAssetIds.Any(
                static id =>
                    string.IsNullOrWhiteSpace(
                        id)) ||
            MaterialAssetIds.Distinct(
                StringComparer.Ordinal)
            .Count() !=
            MaterialAssetIds.Length)
        {
            throw new InvalidOperationException(
                "Terrain material asset IDs must be non-empty and unique.");
        }
    }
}


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

public readonly record struct BattlefieldWorldObjectDefinition(
    string Key,
    WorldVisualId Visual,
    WorldPresentationKind Kind,
    Vector3 Position,
    Vector3 Scale,
    float RotationDegrees);

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

public sealed class BattlefieldDefinition
{
    private readonly AxisAlignedBounds[] _staticNavigationObstacles;

    public BattlefieldDefinition(
        BattlefieldMapMetadata metadata,
        BattlefieldStartPosition[] starts,
        BattlefieldResourceDepositDefinition[] resources,
        BattlefieldWorldObjectDefinition[] worldObjects,
        BattlefieldSiteDefinition[] sites,
        BattlefieldRoadNodeDefinition[] roadNodes,
        BattlefieldRoadEdgeDefinition[] roadEdges,
        BattlefieldCrossingDefinition[] crossings,
        BattlefieldObjectiveDefinition[] objectives,
        AxisAlignedBounds[] staticNavigationObstacles,
        BattlefieldTerrainVisualDefinition terrainVisual)
    {
        Metadata = metadata;
        Starts = starts;
        Resources = resources;
        WorldObjects = worldObjects;
        Sites = sites;
        RoadNodes = roadNodes;
        RoadEdges = roadEdges;
        Crossings = crossings;
        Objectives = objectives;
        _staticNavigationObstacles = staticNavigationObstacles;
        TerrainVisual = terrainVisual ??
            throw new ArgumentNullException(nameof(terrainVisual));
        TerrainVisual.Validate();
    }

    public BattlefieldMapMetadata Metadata { get; }

    public IReadOnlyList<BattlefieldStartPosition> Starts { get; }

    public IReadOnlyList<BattlefieldResourceDepositDefinition> Resources { get; }

    public IReadOnlyList<BattlefieldWorldObjectDefinition> WorldObjects { get; }

    public IReadOnlyList<BattlefieldSiteDefinition> Sites { get; }

    public IReadOnlyList<BattlefieldRoadNodeDefinition> RoadNodes { get; }

    public IReadOnlyList<BattlefieldRoadEdgeDefinition> RoadEdges { get; }

    public IReadOnlyList<BattlefieldCrossingDefinition> Crossings { get; }

    public IReadOnlyList<BattlefieldObjectiveDefinition> Objectives { get; }

    public BattlefieldTerrainVisualDefinition TerrainVisual { get; }

    public IReadOnlyList<AxisAlignedBounds> StaticNavigationObstacles =>
        _staticNavigationObstacles;

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

}
