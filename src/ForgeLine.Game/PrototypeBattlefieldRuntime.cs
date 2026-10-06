using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class PrototypeBattlefieldRuntime
{
    private readonly Dictionary<string, LogisticsNodeId> _roadNodes;
    private readonly Dictionary<string, LogisticsEdgeId> _roadEdges;
    private readonly Dictionary<string, EntityId> _crossingEntities;
    private readonly EntityId[] _worldPresentationEntities;

    private PrototypeBattlefieldRuntime(
        PrototypeBattlefieldDefinition definition,
        TerrainWorld terrain,
        LogisticsNetwork logistics,
        IReadOnlyList<EntityId> resourceEntities,
        IReadOnlyList<EntityId> worldPresentationEntities,
        Dictionary<string, LogisticsNodeId> roadNodes,
        Dictionary<string, LogisticsEdgeId> roadEdges,
        Dictionary<string, EntityId> crossingEntities,
        EntityId matchStateEntity)
    {
        Definition = definition;
        Terrain = terrain;
        Logistics = logistics;
        ResourceEntities = resourceEntities;
        _worldPresentationEntities = worldPresentationEntities.ToArray();
        _roadNodes = roadNodes;
        _roadEdges = roadEdges;
        _crossingEntities = crossingEntities;
        MatchStateEntity = matchStateEntity;
    }

    public PrototypeBattlefieldDefinition Definition { get; }

    public TerrainWorld Terrain { get; }

    public LogisticsNetwork Logistics { get; }

    public IReadOnlyList<EntityId> ResourceEntities { get; }

    public IReadOnlyList<EntityId> WorldPresentationEntities =>
        _worldPresentationEntities;

    public IReadOnlyDictionary<string, LogisticsNodeId> RoadNodes =>
        _roadNodes;

    public IReadOnlyDictionary<string, LogisticsEdgeId> RoadEdges =>
        _roadEdges;

    public IReadOnlyDictionary<string, EntityId> CrossingEntities =>
        _crossingEntities;

    public EntityId MatchStateEntity { get; }

    public static PrototypeBattlefieldRuntime Load(
        EntityRegistry entities,
        PrototypeBattlefieldDefinition definition,
        TerrainWorld terrain,
        LogisticsNetwork logistics)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(logistics);

        PrototypeBattlefieldValidator.ValidateDefinition(
            definition);

        var resources =
            new List<EntityId>(
                definition.Resources.Count);

        for (int index = 0;
             index < definition.Resources.Count;
             index++)
        {
            BattlefieldResourceDepositDefinition resource =
                definition.Resources[index];

            Vector3 center =
                SampleTerrainPosition(
                    terrain,
                    resource.Center);
            AxisAlignedBounds bounds =
                new(
                    center - resource.HalfExtents,
                    center + resource.HalfExtents);

            EntityId entity =
                ResourceDepositSpawner.Place(
                    entities,
                    new ResourceDepositPlacement(
                        resource.ResourceId,
                        bounds,
                        resource.TotalQuantity,
                        resource.ExtractionRatePerSecond));

            WorldPresentationIdentity presentation =
                WorldPresentationIdentity.ForResource(
                    resource.ResourceId);
            entities.AddComponent(
                entity,
                new WorldTransform(
                    center + Vector3.UnitY * resource.HalfExtents.Y,
                    Quaternion.Identity,
                    new Vector3(
                        resource.HalfExtents.X * 1.2f,
                        resource.HalfExtents.Y * 2.0f,
                        resource.HalfExtents.Z * 1.2f)));
            entities.AddComponent(
                entity,
                new VisualIdentity(
                    checked(4_000u + (uint)presentation.Visual)));
            entities.AddComponent(
                entity,
                presentation);
            resources.Add(entity);
        }

        var worldPresentationEntities =
            new List<EntityId>(
                definition.WorldObjects.Count);

        for (int index = 0;
             index < definition.WorldObjects.Count;
             index++)
        {
            BattlefieldWorldObjectDefinition worldObject =
                definition.WorldObjects[index];
            Vector3 ground =
                SampleTerrainPosition(
                    terrain,
                    worldObject.Position);
            float heightOffset =
                worldObject.Kind == WorldPresentationKind.Decal
                    ? 0.04f
                    : worldObject.Scale.Y * 0.5f;
            float radians =
                worldObject.RotationDegrees *
                (MathF.PI / 180.0f);

            EntityId entity =
                entities.CreateEntity();
            entities.AddComponent(
                entity,
                new WorldTransform(
                    ground + Vector3.UnitY * heightOffset,
                    Quaternion.CreateFromAxisAngle(
                        Vector3.UnitY,
                        radians),
                    worldObject.Scale));
            entities.AddComponent(
                entity,
                new VisualIdentity(
                    checked(5_000u + (uint)worldObject.Visual)));
            entities.AddComponent(
                entity,
                new WorldPresentationIdentity(
                    worldObject.Visual,
                    worldObject.Kind));

            worldPresentationEntities.Add(entity);
        }

        var nodeEntities =
            new Dictionary<string, EntityId>(
                StringComparer.Ordinal);
        var roadNodes =
            new Dictionary<string, LogisticsNodeId>(
                StringComparer.Ordinal);
        var roadNodePositions =
            new Dictionary<string, Vector3>(
                StringComparer.Ordinal);

        for (int index = 0;
             index < definition.RoadNodes.Count;
             index++)
        {
            BattlefieldRoadNodeDefinition roadNode =
                definition.RoadNodes[index];
            Vector3 position =
                SampleTerrainPosition(
                    terrain,
                    roadNode.Position);

            EntityId entity =
                entities.CreateEntity();
            entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));

            LogisticsNodeId node =
                logistics.AddNode(
                    entity,
                    position,
                    LogisticsNodeKind.LogisticsHub,
                    LogisticsNodeCapabilities.CargoSource |
                    LogisticsNodeCapabilities.CargoDestination |
                    LogisticsNodeCapabilities.Distribution,
                    throughputCapacityPerSecond: 200.0);

            nodeEntities.Add(
                roadNode.Key,
                entity);
            roadNodes.Add(
                roadNode.Key,
                node);
            roadNodePositions.Add(
                roadNode.Key,
                position);
        }

        var roadEdges =
            new Dictionary<string, LogisticsEdgeId>(
                StringComparer.Ordinal);

        for (int index = 0;
             index < definition.RoadEdges.Count;
             index++)
        {
            BattlefieldRoadEdgeDefinition roadEdge =
                definition.RoadEdges[index];

            LogisticsNodeId source =
                roadNodes[roadEdge.SourceNodeKey];
            LogisticsNodeId destination =
                roadNodes[roadEdge.DestinationNodeKey];

            Vector3 sourcePosition =
                roadNodePositions[
                    roadEdge.SourceNodeKey];
            Vector3 destinationPosition =
                roadNodePositions[
                    roadEdge.DestinationNodeKey];

            double distance =
                HorizontalDistance(
                    sourcePosition,
                    destinationPosition);

            roadEdges.Add(
                roadEdge.Key,
                logistics.AddEdge(
                    source,
                    destination,
                    LogisticsTransportMode.GroundRoad,
                    distance,
                    roadEdge.BaseCost,
                    roadEdge.CapacityPerSecond,
                    bidirectional: true,
                    enabled: true));

            if (!IsCrossingEdge(
                    definition,
                    roadEdge.Key))
            {
                CreateRoadPresentationEntities(
                    entities,
                    roadEdge,
                    sourcePosition,
                    destinationPosition,
                    checked((uint)(6_000 + index)));
            }
        }

        CreateRoadNodePresentationEntities(
            entities,
            definition,
            roadNodePositions);

        var crossingEntities =
            new Dictionary<string, EntityId>(
                StringComparer.Ordinal);

        for (int index = 0;
             index < definition.Crossings.Count;
             index++)
        {
            BattlefieldCrossingDefinition crossing =
                definition.Crossings[index];
            EntityId entity =
                entities.CreateEntity();
            Vector3 position =
                SampleTerrainPosition(
                    terrain,
                    crossing.Position);

            entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    new Vector3(
                        18.0f,
                        4.0f,
                        42.0f)));
            entities.AddComponent(
                entity,
                new VisualIdentity(
                    checked((uint)(3_100 + index))));
            entities.AddComponent(
                entity,
                new StrategicInfrastructure(
                    crossing.Key,
                    roadEdges[crossing.LogisticsEdgeKey],
                    crossing.PassageBounds,
                    crossing.Restorable,
                    crossing.RestorationTicks));
            entities.AddComponent(
                entity,
                crossing.InitiallyOperational
                    ? StrategicInfrastructureState.Operational
                    : StrategicInfrastructureState.Disabled);
            entities.AddComponent(
                entity,
                new InfrastructurePresentationIdentity(
                    ResolveCrossingPresentationKind(
                        crossing),
                    crossing.Key));

            if (!crossing.InitiallyOperational)
            {
                logistics.SetEdgeEnabled(
                    roadEdges[crossing.LogisticsEdgeKey],
                    enabled: false);
            }

            crossingEntities.Add(
                crossing.Key,
                entity);
        }

        EntityId matchState =
            MatchObjectiveSystem.CreateMatchStateEntity(
                entities);

        return new PrototypeBattlefieldRuntime(
            definition,
            terrain,
            logistics,
            resources,
            worldPresentationEntities,
            roadNodes,
            roadEdges,
            crossingEntities,
            matchState);
    }

    public IReadOnlyList<EntityId> AttachCommandCoreObjectives(
        EntityRegistry entities,
        IReadOnlyDictionary<PlayerId, EntityId> commandCores)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(commandCores);

        var objectives =
            new List<EntityId>(
                Definition.Objectives.Count);

        for (int index = 0;
             index < Definition.Objectives.Count;
             index++)
        {
            BattlefieldObjectiveDefinition definition =
                Definition.Objectives[index];

            if (!commandCores.TryGetValue(
                    definition.Owner,
                    out EntityId commandCore))
            {
                throw new InvalidOperationException(
                    $"Missing Command Core for player {definition.Owner}.");
            }

            objectives.Add(
                MatchObjectiveSystem.AttachCommandCoreObjective(
                    entities,
                    definition,
                    commandCore));
        }

        MatchObjectiveSystem.MarkMatchReady(
            entities,
            MatchStateEntity);

        return objectives;
    }

    private static bool IsCrossingEdge(
        PrototypeBattlefieldDefinition definition,
        string roadEdgeKey)
    {
        for (int index = 0;
             index < definition.Crossings.Count;
             index++)
        {
            if (string.Equals(
                    definition.Crossings[index].LogisticsEdgeKey,
                    roadEdgeKey,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void CreateRoadPresentationEntities(
        EntityRegistry entities,
        in BattlefieldRoadEdgeDefinition roadEdge,
        Vector3 sourcePosition,
        Vector3 destinationPosition,
        uint visualId)
    {
        const float RoadSurfaceWidth = 12.0f;
        const float ShoulderWidth = 2.4f;
        const float ShoulderSourceWidth = 0.16f;

        Vector3 delta =
            destinationPosition -
            sourcePosition;
        float horizontalLength =
            MathF.Sqrt(
                delta.X *
                    delta.X +
                delta.Z *
                    delta.Z);

        if (horizontalLength <= 0.001f)
        {
            return;
        }

        float spatialLength =
            delta.Length();
        float yaw =
            MathF.Atan2(
                delta.X,
                delta.Z);
        float pitch =
            -MathF.Atan2(
                delta.Y,
                horizontalLength);
        Quaternion rotation =
            Quaternion.CreateFromYawPitchRoll(
                yaw,
                pitch,
                0.0f);
        Vector3 midpoint =
            (sourcePosition +
             destinationPosition) *
            0.5f;
        Vector3 right =
            new(
                delta.Z /
                    horizontalLength,
                0.0f,
                -delta.X /
                    horizontalLength);

        CreateInfrastructurePresentationEntity(
            entities,
            midpoint,
            rotation,
            new Vector3(
                RoadSurfaceWidth,
                0.35f,
                spatialLength),
            InfrastructurePresentationKind.RoadSegment,
            roadEdge.Key,
            visualId);

        float shoulderOffset =
            RoadSurfaceWidth *
                0.5f +
            ShoulderWidth *
                0.5f;
        float shoulderScale =
            ShoulderWidth /
            ShoulderSourceWidth;

        CreateInfrastructurePresentationEntity(
            entities,
            midpoint -
            right *
                shoulderOffset,
            rotation,
            new Vector3(
                shoulderScale,
                0.30f,
                spatialLength),
            InfrastructurePresentationKind.RoadShoulder,
            $"{roadEdge.Key}.shoulder.left",
            checked(
                visualId +
                10_000U));
        CreateInfrastructurePresentationEntity(
            entities,
            midpoint +
            right *
                shoulderOffset,
            rotation,
            new Vector3(
                shoulderScale,
                0.30f,
                spatialLength),
            InfrastructurePresentationKind.RoadShoulder,
            $"{roadEdge.Key}.shoulder.right",
            checked(
                visualId +
                20_000U));
    }

    private static void CreateRoadNodePresentationEntities(
        EntityRegistry entities,
        PrototypeBattlefieldDefinition definition,
        IReadOnlyDictionary<string, Vector3> roadNodePositions)
    {
        const float MinimumCurveDegrees = 8.0f;
        const float ShortCurveDegrees = 35.0f;

        for (int nodeIndex = 0;
             nodeIndex < definition.RoadNodes.Count;
             nodeIndex++)
        {
            BattlefieldRoadNodeDefinition node =
                definition.RoadNodes[nodeIndex];
            Vector3 position =
                roadNodePositions[
                    node.Key];
            List<Vector3> directions =
                GetRoadNodeDirections(
                    definition,
                    roadNodePositions,
                    node.Key,
                    position);

            InfrastructurePresentationKind kind;
            float yaw;
            float scale;

            if (directions.Count >= 4)
            {
                kind =
                    InfrastructurePresentationKind.RoadJunctionCross;
                yaw =
                    MathF.Atan2(
                        directions[0].X,
                        directions[0].Z);
                scale =
                    18.0f;
            }
            else if (directions.Count == 3)
            {
                kind =
                    InfrastructurePresentationKind.RoadJunctionT;
                yaw =
                    ResolveTJunctionYaw(
                        directions);
                scale =
                    18.0f;
            }
            else if (directions.Count == 2)
            {
                float dot =
                    Math.Clamp(
                        Vector3.Dot(
                            directions[0],
                            directions[1]),
                        -1.0f,
                        1.0f);
                float deflection =
                    MathF.PI -
                    MathF.Acos(
                        dot);
                float degrees =
                    deflection *
                    (180.0f /
                     MathF.PI);

                if (degrees <
                    MinimumCurveDegrees)
                {
                    continue;
                }

                kind =
                    degrees >=
                    ShortCurveDegrees
                        ? InfrastructurePresentationKind.RoadCurveShort
                        : InfrastructurePresentationKind.RoadCurveLong;

                Vector3 bisector =
                    directions[0] +
                    directions[1];

                if (bisector.LengthSquared() <=
                    0.0001f)
                {
                    continue;
                }

                bisector =
                    Vector3.Normalize(
                        bisector);
                yaw =
                    MathF.Atan2(
                        bisector.X,
                        bisector.Z) -
                    MathF.PI *
                    0.25f;
                scale =
                    16.0f;
            }
            else
            {
                continue;
            }

            CreateInfrastructurePresentationEntity(
                entities,
                position,
                Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    yaw),
                new Vector3(
                    scale,
                    0.35f,
                    scale),
                kind,
                $"{node.Key}.surface",
                checked(
                    36_000U +
                    (uint)nodeIndex));
        }
    }

    private static List<Vector3> GetRoadNodeDirections(
        PrototypeBattlefieldDefinition definition,
        IReadOnlyDictionary<string, Vector3> roadNodePositions,
        string nodeKey,
        Vector3 nodePosition)
    {
        var directions =
            new List<Vector3>(
                4);

        for (int edgeIndex = 0;
             edgeIndex < definition.RoadEdges.Count;
             edgeIndex++)
        {
            BattlefieldRoadEdgeDefinition edge =
                definition.RoadEdges[edgeIndex];
            string? otherKey =
                string.Equals(
                    edge.SourceNodeKey,
                    nodeKey,
                    StringComparison.Ordinal)
                    ? edge.DestinationNodeKey
                    : string.Equals(
                        edge.DestinationNodeKey,
                        nodeKey,
                        StringComparison.Ordinal)
                        ? edge.SourceNodeKey
                        : null;

            if (otherKey is null)
            {
                continue;
            }

            Vector3 delta =
                roadNodePositions[
                    otherKey] -
                nodePosition;
            delta.Y =
                0.0f;

            if (delta.LengthSquared() <=
                0.0001f)
            {
                continue;
            }

            directions.Add(
                Vector3.Normalize(
                    delta));
        }

        return directions;
    }

    private static float ResolveTJunctionYaw(
        IReadOnlyList<Vector3> directions)
    {
        int oppositeLeft = 0;
        int oppositeRight = 1;
        float mostOpposed =
            Vector3.Dot(
                directions[0],
                directions[1]);

        for (int left = 0;
             left < directions.Count - 1;
             left++)
        {
            for (int right = left + 1;
                 right < directions.Count;
                 right++)
            {
                float dot =
                    Vector3.Dot(
                        directions[left],
                        directions[right]);

                if (dot <
                    mostOpposed)
                {
                    mostOpposed =
                        dot;
                    oppositeLeft =
                        left;
                    oppositeRight =
                        right;
                }
            }
        }

        int stemIndex =
            3 -
            oppositeLeft -
            oppositeRight;
        Vector3 stem =
            directions[
                stemIndex];

        return MathF.Atan2(
            stem.X,
            stem.Z);
    }

    private static void CreateInfrastructurePresentationEntity(
        EntityRegistry entities,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        InfrastructurePresentationKind kind,
        string key,
        uint visualId)
    {
        EntityId entity =
            entities.CreateEntity();
        entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                rotation,
                scale));
        entities.AddComponent(
            entity,
            new VisualIdentity(
                visualId));
        entities.AddComponent(
            entity,
            new InfrastructurePresentationIdentity(
                kind,
                key));
    }

    private static InfrastructurePresentationKind ResolveCrossingPresentationKind(
        in BattlefieldCrossingDefinition crossing) =>
        crossing.LogisticsEdgeKey.Contains(
            "bridge",
            StringComparison.OrdinalIgnoreCase)
            ? InfrastructurePresentationKind.RoadBridge
            : InfrastructurePresentationKind.Ford;

    private static Vector3 SampleTerrainPosition(
        TerrainWorld terrain,
        Vector3 requested)
    {
        if (!terrain.TrySampleHeight(
                requested.X,
                requested.Z,
                out float height))
        {
            throw new InvalidOperationException(
                $"Battlefield point ({requested.X}, {requested.Z}) lies outside terrain.");
        }

        return new Vector3(
            requested.X,
            height,
            requested.Z);
    }

    private static double HorizontalDistance(
        Vector3 left,
        Vector3 right)
    {
        double x =
            left.X - right.X;
        double z =
            left.Z - right.Z;
        return Math.Sqrt(
            x * x +
            z * z);
    }
}
