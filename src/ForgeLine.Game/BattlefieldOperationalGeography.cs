using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed record BattlefieldOperationalGeographyReport(
    string MapKey,
    int ReachabilityChecks,
    int AlternateNavigationChecks,
    int AlternateRoadChecks,
    float ShortestRouteMeters,
    float LongestRouteMeters,
    float MinimumTerrainHeightMeters,
    float MaximumTerrainHeightMeters,
    int BuildableZoneCount,
    int ContestedResourceCount)
{
    public float TerrainElevationRangeMeters =>
        MaximumTerrainHeightMeters -
        MinimumTerrainHeightMeters;
}

public static class BattlefieldOperationalGeographyValidator
{
    private static readonly ResourceId[] ExpansionResources =
    [
        ResourceIds.FerrousOre,
        ResourceIds.Volatiles,
        ResourceIds.Silicates
    ];

    public static BattlefieldOperationalGeographyReport Validate(
        PrototypeBattlefieldDefinition definition,
        TerrainWorld terrain,
        NavigationGridSettings? gridSettings = null,
        NavigationSectorSettings? sectorSettings = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(terrain);

        PrototypeBattlefieldValidator.ValidateDefinition(
            definition);

        NavigationGridSettings resolvedGrid =
            gridSettings ??
            new NavigationGridSettings
            {
                CellSizeMeters = 16.0f,
                StaticObstacleClearanceMeters = 0.5f
            };
        NavigationSectorSettings resolvedSectors =
            sectorSettings ??
            new NavigationSectorSettings
            {
                SectorSizeCells = 8
            };

        var errors = new List<string>();
        int reachabilityChecks = 0;
        int alternateNavigationChecks = 0;
        int alternateRoadChecks = 0;
        float shortestRoute = float.PositiveInfinity;
        float longestRoute = 0.0f;

        NavigationWorld baselineWorld =
            NavigationWorld.Build(
                terrain,
                definition.CreateNavigationObstacles(),
                resolvedGrid,
                resolvedSectors);
        var baselinePathfinder =
            new HierarchicalPathfinder(
                baselineWorld);

        BattlefieldStartPosition[] starts =
            definition.Starts.ToArray();

        for (int startIndex = 0;
             startIndex < starts.Length;
             startIndex++)
        {
            BattlefieldStartPosition start =
                starts[startIndex];

            for (int siteIndex = 0;
                 siteIndex < definition.Sites.Count;
                 siteIndex++)
            {
                BattlefieldSiteDefinition site =
                    definition.Sites[siteIndex];

                CheckRoute(
                    baselinePathfinder,
                    start.Position,
                    site.Position,
                    NavigationMovementClass.Tracked,
                    $"start {start.Player} to site '{site.Key}'",
                    errors,
                    ref reachabilityChecks,
                    ref shortestRoute,
                    ref longestRoute);

                CheckRoute(
                    baselinePathfinder,
                    start.Position,
                    site.Position,
                    NavigationMovementClass.Wheeled,
                    $"wheeled start {start.Player} to site '{site.Key}'",
                    errors,
                    ref reachabilityChecks,
                    ref shortestRoute,
                    ref longestRoute);
            }

            for (int resourceIndex = 0;
                 resourceIndex < definition.Resources.Count;
                 resourceIndex++)
            {
                BattlefieldResourceDepositDefinition resource =
                    definition.Resources[resourceIndex];
                NavigationCapabilities tracked =
                    NavigationCapabilities.For(
                        NavigationMovementClass.Tracked);

                if (!TryResolveResourceApproach(
                        baselineWorld.Grid,
                        resource,
                        tracked,
                        out Vector3 approach))
                {
                    reachabilityChecks++;
                    errors.Add(
                        $"Resource '{resource.Key}' has no traversable tracked approach.");
                    continue;
                }

                CheckRoute(
                    baselinePathfinder,
                    start.Position,
                    approach,
                    NavigationMovementClass.Tracked,
                    $"start {start.Player} to resource '{resource.Key}' approach",
                    errors,
                    ref reachabilityChecks,
                    ref shortestRoute,
                    ref longestRoute);
            }
        }

        if (starts.Length == 2)
        {
            CheckRoute(
                baselinePathfinder,
                starts[0].Position,
                starts[1].Position,
                NavigationMovementClass.Tracked,
                "opposing starts",
                errors,
                ref reachabilityChecks,
                ref shortestRoute,
                ref longestRoute);

            for (int crossingIndex = 0;
                 crossingIndex < definition.Crossings.Count;
                 crossingIndex++)
            {
                BattlefieldCrossingDefinition unavailable =
                    definition.Crossings[crossingIndex];
                var availability =
                    new Dictionary<string, bool>(
                        StringComparer.Ordinal);

                for (int index = 0;
                     index < definition.Crossings.Count;
                     index++)
                {
                    availability.Add(
                        definition.Crossings[index].Key,
                        index != crossingIndex);
                }

                NavigationWorld alternateWorld =
                    NavigationWorld.Build(
                        terrain,
                        definition.CreateNavigationObstacles(
                            availability),
                        resolvedGrid,
                        resolvedSectors);
                var alternatePathfinder =
                    new HierarchicalPathfinder(
                        alternateWorld);

                CheckRoute(
                    alternatePathfinder,
                    starts[0].Position,
                    starts[1].Position,
                    NavigationMovementClass.Tracked,
                    $"opposing starts with '{unavailable.Key}' unavailable",
                    errors,
                    ref alternateNavigationChecks,
                    ref shortestRoute,
                    ref longestRoute);

                if (!HasRoadRoute(
                        definition,
                        "west.start",
                        "east.start",
                        unavailable.LogisticsEdgeKey))
                {
                    errors.Add(
                        $"Road topology loses all east-west routes when crossing '{unavailable.Key}' is unavailable.");
                }

                alternateRoadChecks++;
            }
        }

        ValidateExpansionPressure(
            definition,
            errors);
        ValidateBuildableAreas(
            definition,
            errors);

        (float minimumHeight, float maximumHeight) =
            SampleElevationRange(
                definition,
                terrain);

        if (maximumHeight - minimumHeight < 20.0f)
        {
            errors.Add(
                "Terrain does not provide enough elevation variation for defensible positions.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Operational geography validation failed:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(
                        static error => "- " + error)));
        }

        return new BattlefieldOperationalGeographyReport(
            definition.Metadata.Key,
            reachabilityChecks,
            alternateNavigationChecks,
            alternateRoadChecks,
            float.IsPositiveInfinity(shortestRoute)
                ? 0.0f
                : shortestRoute,
            longestRoute,
            minimumHeight,
            maximumHeight,
            definition.Starts.Count +
            definition.Sites.Count +
            definition.Resources.Count,
            definition.Resources.Count(
                static resource => resource.Contested));
    }

    private static void CheckRoute(
        HierarchicalPathfinder pathfinder,
        Vector3 start,
        Vector3 destination,
        NavigationMovementClass movementClass,
        string label,
        List<string> errors,
        ref int checkCount,
        ref float shortestRoute,
        ref float longestRoute)
    {
        NavigationSearchResult result =
            pathfinder.FindPath(
                start,
                destination,
                NavigationCapabilities.For(
                    movementClass),
                projectBlockedEndpoints: true);

        checkCount++;

        if (!result.Succeeded ||
            result.Path is null)
        {
            errors.Add(
                $"{label} is unreachable for {movementClass}: {result.FailureReason}.");
            return;
        }

        float length =
            result.Path.Diagnostics.RouteLengthMeters;
        shortestRoute =
            MathF.Min(
                shortestRoute,
                length);
        longestRoute =
            MathF.Max(
                longestRoute,
                length);
    }

    private static bool TryResolveResourceApproach(
        NavigationGrid grid,
        in BattlefieldResourceDepositDefinition resource,
        in NavigationCapabilities capabilities,
        out Vector3 approach)
    {
        approach = default;

        if (!grid.TryWorldToCell(
                resource.Center,
                out NavigationCellCoordinate origin))
        {
            return false;
        }

        float maximumApproachDistance =
            MathF.Max(
                resource.HalfExtents.X,
                resource.HalfExtents.Z) +
            128.0f;
        int cellRadius =
            checked(
                (int)MathF.Ceiling(
                    maximumApproachDistance /
                    grid.Settings.CellSizeMeters));
        float maximumDistanceSquared =
            maximumApproachDistance *
            maximumApproachDistance;
        float bestDistanceSquared =
            float.PositiveInfinity;

        for (int z = origin.Z - cellRadius;
             z <= origin.Z + cellRadius;
             z++)
        {
            for (int x = origin.X - cellRadius;
                 x <= origin.X + cellRadius;
                 x++)
            {
                var candidate =
                    new NavigationCellCoordinate(
                        x,
                        z);

                if (!grid.IsTraversable(
                        candidate,
                        capabilities))
                {
                    continue;
                }

                Vector3 center =
                    grid.GetCellCenter(
                        candidate);
                float deltaX =
                    center.X -
                    resource.Center.X;
                float deltaZ =
                    center.Z -
                    resource.Center.Z;
                float distanceSquared =
                    deltaX * deltaX +
                    deltaZ * deltaZ;

                if (distanceSquared >
                        maximumDistanceSquared ||
                    distanceSquared >=
                        bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared =
                    distanceSquared;
                approach =
                    center;
            }
        }

        return float.IsFinite(
            bestDistanceSquared);
    }

    private static void ValidateExpansionPressure(
        PrototypeBattlefieldDefinition definition,
        List<string> errors)
    {
        for (int resourceIndex = 0;
             resourceIndex < ExpansionResources.Length;
             resourceIndex++)
        {
            ResourceId resourceId =
                ExpansionResources[resourceIndex];
            double richestContested =
                definition.Resources
                    .Where(
                        resource =>
                            resource.Contested &&
                            resource.ResourceId == resourceId)
                    .Select(
                        static resource => resource.TotalQuantity)
                    .DefaultIfEmpty(0.0)
                    .Max();

            if (richestContested <= 0.0)
            {
                errors.Add(
                    $"Expansion resource {resourceId} has no contested reserve.");
                continue;
            }

            for (int startIndex = 0;
                 startIndex < definition.Starts.Count;
                 startIndex++)
            {
                BattlefieldStartPosition start =
                    definition.Starts[startIndex];
                BattlefieldResourceDepositDefinition? local =
                    definition.Resources
                        .Cast<BattlefieldResourceDepositDefinition?>()
                        .Where(
                            deposit =>
                                deposit.HasValue &&
                                !deposit.Value.Contested &&
                                deposit.Value.ResourceId == resourceId)
                        .OrderBy(
                            deposit =>
                                HorizontalDistance(
                                    deposit!.Value.Center,
                                    start.Position))
                        .FirstOrDefault();

                if (!local.HasValue ||
                    HorizontalDistance(
                        local.Value.Center,
                        start.Position) > 450.0f)
                {
                    errors.Add(
                        $"Start {start.Player} has no local reserve for expansion resource {resourceId}.");
                    continue;
                }

                if (richestContested <=
                    local.Value.TotalQuantity)
                {
                    errors.Add(
                        $"Contested reserve for resource {resourceId} must exceed the local reserve at start {start.Player}.");
                }
            }
        }
    }

    private static void ValidateBuildableAreas(
        PrototypeBattlefieldDefinition definition,
        List<string> errors)
    {
        var query =
            new BattlefieldBuildableAreaQuery(
                definition);

        for (int index = 0;
             index < definition.Starts.Count;
             index++)
        {
            BattlefieldStartPosition start =
                definition.Starts[index];

            if (!query.IsBuildable(
                    start.Player,
                    PointFootprint(
                        start.CommandCorePosition)))
            {
                errors.Add(
                    $"Start {start.Player} Command Core is outside explicit buildable geography.");
            }
        }

        PlayerId referencePlayer =
            definition.Starts[0].Player;

        for (int index = 0;
             index < definition.Sites.Count;
             index++)
        {
            BattlefieldSiteDefinition site =
                definition.Sites[index];

            if (!query.IsBuildable(
                    referencePlayer,
                    PointFootprint(
                        site.Position)))
            {
                errors.Add(
                    $"Strategic site '{site.Key}' is outside explicit buildable geography.");
            }
        }

        for (int index = 0;
             index < definition.Resources.Count;
             index++)
        {
            BattlefieldResourceDepositDefinition resource =
                definition.Resources[index];

            if (!query.IsBuildable(
                    referencePlayer,
                    PointFootprint(
                        resource.Center)))
            {
                errors.Add(
                    $"Resource '{resource.Key}' has no explicit mining build zone.");
            }
        }

        Vector3 deliberatelyRestricted =
            new(
                definition.Metadata.WidthMeters * 0.5f,
                0.0f,
                320.0f);

        if (query.IsBuildable(
                referencePlayer,
                PointFootprint(
                    deliberatelyRestricted)))
        {
            errors.Add(
                "The canonical map must contain explicitly non-buildable geography.");
        }
    }

    private static AxisAlignedBounds PointFootprint(
        Vector3 position) =>
        new(
            new Vector3(
                position.X - 1.0f,
                -64.0f,
                position.Z - 1.0f),
            new Vector3(
                position.X + 1.0f,
                128.0f,
                position.Z + 1.0f));

    private static bool HasRoadRoute(
        PrototypeBattlefieldDefinition definition,
        string startKey,
        string destinationKey,
        string excludedEdgeKey)
    {
        var adjacency =
            new Dictionary<string, List<string>>(
                StringComparer.Ordinal);

        for (int index = 0;
             index < definition.RoadNodes.Count;
             index++)
        {
            adjacency.Add(
                definition.RoadNodes[index].Key,
                new List<string>());
        }

        for (int index = 0;
             index < definition.RoadEdges.Count;
             index++)
        {
            BattlefieldRoadEdgeDefinition edge =
                definition.RoadEdges[index];

            if (string.Equals(
                    edge.Key,
                    excludedEdgeKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            adjacency[edge.SourceNodeKey].Add(
                edge.DestinationNodeKey);
            adjacency[edge.DestinationNodeKey].Add(
                edge.SourceNodeKey);
        }

        if (!adjacency.ContainsKey(startKey) ||
            !adjacency.ContainsKey(destinationKey))
        {
            return false;
        }

        var visited =
            new HashSet<string>(
                StringComparer.Ordinal)
            {
                startKey
            };
        var pending =
            new Queue<string>();
        pending.Enqueue(
            startKey);

        while (pending.Count > 0)
        {
            string current =
                pending.Dequeue();

            if (string.Equals(
                    current,
                    destinationKey,
                    StringComparison.Ordinal))
            {
                return true;
            }

            List<string> neighbors =
                adjacency[current];

            for (int index = 0;
                 index < neighbors.Count;
                 index++)
            {
                if (visited.Add(
                        neighbors[index]))
                {
                    pending.Enqueue(
                        neighbors[index]);
                }
            }
        }

        return false;
    }

    private static (float Minimum, float Maximum) SampleElevationRange(
        PrototypeBattlefieldDefinition definition,
        TerrainWorld terrain)
    {
        const int samplesPerAxis = 17;
        float minimum =
            float.PositiveInfinity;
        float maximum =
            float.NegativeInfinity;

        for (int z = 0;
             z < samplesPerAxis;
             z++)
        {
            float worldZ =
                definition.Metadata.HeightMeters *
                (z + 0.5f) /
                samplesPerAxis;

            for (int x = 0;
                 x < samplesPerAxis;
                 x++)
            {
                float worldX =
                    definition.Metadata.WidthMeters *
                    (x + 0.5f) /
                    samplesPerAxis;

                if (!terrain.TrySampleHeight(
                        worldX,
                        worldZ,
                        out float height))
                {
                    throw new InvalidOperationException(
                        "Terrain sampling failed inside canonical map bounds.");
                }

                minimum =
                    MathF.Min(
                        minimum,
                        height);
                maximum =
                    MathF.Max(
                        maximum,
                        height);
            }
        }

        return (minimum, maximum);
    }

    private static float HorizontalDistance(
        Vector3 left,
        Vector3 right)
    {
        Vector2 delta =
            new(
                left.X - right.X,
                left.Z - right.Z);
        return delta.Length();
    }
}
