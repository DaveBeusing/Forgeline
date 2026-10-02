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
                CreateRoadPresentationEntity(
                    entities,
                    roadEdge,
                    sourcePosition,
                    destinationPosition,
                    checked((uint)(6_000 + index)));
            }
        }

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

    private static void CreateRoadPresentationEntity(
        EntityRegistry entities,
        in BattlefieldRoadEdgeDefinition roadEdge,
        Vector3 sourcePosition,
        Vector3 destinationPosition,
        uint visualId)
    {
        Vector3 delta =
            destinationPosition -
            sourcePosition;
        float length =
            MathF.Sqrt(
                (delta.X * delta.X) +
                (delta.Z * delta.Z));

        if (length <= 0.001f)
        {
            return;
        }

        float yaw =
            MathF.Atan2(
                delta.X,
                delta.Z);
        Vector3 midpoint =
            (sourcePosition +
             destinationPosition) *
            0.5f;

        EntityId entity =
            entities.CreateEntity();
        entities.AddComponent(
            entity,
            new WorldTransform(
                midpoint,
                Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    yaw),
                new Vector3(
                    12.0f,
                    0.35f,
                    length)));
        entities.AddComponent(
            entity,
            new VisualIdentity(
                visualId));
        entities.AddComponent(
            entity,
            new InfrastructurePresentationIdentity(
                InfrastructurePresentationKind.RoadSegment,
                roadEdge.Key));
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
