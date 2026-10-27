using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum BuildingPlacementFailureReason
{
    None = 0,
    UnknownBuilding = 1,
    TerrainUnavailable = 2,
    OutsideWorldBounds = 3,
    SlopeTooSteep = 4,
    Obstructed = 5,
    OutsideBuildableArea = 6,
    ResourceDepositRequired = 7,
    LoadingCorridorUnavailable = 8
}

public readonly record struct BuildingPlacementResult(
    bool IsValid,
    BuildingPlacementFailureReason Failure,
    BuildingId BuildingId,
    Vector3 GroundPosition,
    BuildingOrientation Orientation,
    AxisAlignedBounds Bounds,
    EntityId ResourceDeposit)
{
    public AxisAlignedBounds? LoadingCorridor { get; init; }

    public static BuildingPlacementResult Invalid(
        BuildingPlacementFailureReason failure,
        BuildingId buildingId,
        Vector3 groundPosition,
        BuildingOrientation orientation) =>
        new(
            false,
            failure,
            buildingId,
            groundPosition,
            orientation,
            default,
            EntityId.Invalid);
}

public readonly record struct BuildingPlacementPreview(
    BuildingId BuildingId,
    string BuildingKey,
    string DisplayName,
    Vector3 GroundPosition,
    BuildingOrientation Orientation,
    AxisAlignedBounds Bounds,
    bool IsValid,
    BuildingPlacementFailureReason Failure,
    EntityId ResourceDeposit)
{
    public AxisAlignedBounds? LoadingCorridor { get; init; }
    public ConveyorSocket? ConveyorInput { get; init; }
    public ConveyorSocket? ConveyorOutput { get; init; }
    public string ConveyorInputMaterials { get; init; } = string.Empty;
    public string ConveyorOutputMaterials { get; init; } = string.Empty;
}

public interface IBuildableAreaQuery
{
    bool IsBuildable(
        PlayerId issuer,
        in AxisAlignedBounds footprintBounds);
}

public sealed class WorldBoundsBuildableAreaQuery : IBuildableAreaQuery
{
    private readonly ITerrainQuery _terrain;

    public WorldBoundsBuildableAreaQuery(ITerrainQuery terrain)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
    }

    public bool IsBuildable(
        PlayerId issuer,
        in AxisAlignedBounds footprintBounds)
    {
        if (!issuer.IsSpecified)
        {
            return false;
        }

        AxisAlignedBounds world = _terrain.WorldBounds;
        return footprintBounds.Minimum.X >= world.Minimum.X &&
               footprintBounds.Maximum.X <= world.Maximum.X &&
               footprintBounds.Minimum.Z >= world.Minimum.Z &&
               footprintBounds.Maximum.Z <= world.Maximum.Z;
    }
}

public sealed class BuildingPlacementService
{
    private readonly BuildingDefinitionCatalog _definitions;
    private readonly ITerrainQuery _terrain;
    private readonly SpatialGridIndex _spatialIndex;
    private readonly IBuildableAreaQuery _buildableArea;
    private readonly SpatialQueryBuffer _queryBuffer = new();

    public BuildingPlacementService(
        BuildingDefinitionCatalog definitions,
        ITerrainQuery terrain,
        SpatialGridIndex spatialIndex,
        IBuildableAreaQuery? buildableArea = null)
    {
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _spatialIndex = spatialIndex ??
            throw new ArgumentNullException(nameof(spatialIndex));
        _buildableArea =
            buildableArea ??
            new WorldBoundsBuildableAreaQuery(terrain);
    }

    public BuildingPlacementPreview CreatePreview(
        EntityRegistry entities,
        PlayerId issuer,
        BuildingId buildingId,
        Vector3 requestedPosition,
        BuildingOrientation orientation)
    {
        BuildingPlacementResult result = Evaluate(
            entities,
            issuer,
            buildingId,
            requestedPosition,
            orientation);

        if (!_definitions.TryGet(buildingId, out BuildingDefinition? definition))
        {
            return new BuildingPlacementPreview(
                buildingId,
                string.Empty,
                string.Empty,
                result.GroundPosition,
                orientation,
                result.Bounds,
                false,
                result.Failure,
                EntityId.Invalid);
        }

        return new BuildingPlacementPreview(
            buildingId,
            definition.Key,
            definition.DisplayName,
            result.GroundPosition,
            orientation,
            result.Bounds,
            result.IsValid,
            result.Failure,
            result.ResourceDeposit)
        {
            LoadingCorridor = result.LoadingCorridor,
            ConveyorInput = PreviewPort(entities, result, true),
            ConveyorOutput = PreviewPort(entities, result, false)
        };
    }

    private static ConveyorSocket? PreviewPort(EntityRegistry entities, in BuildingPlacementResult result, bool input)
    {
        if (result.BuildingId == BuildingIds.Extractor &&
            (!entities.TryGetComponent(result.ResourceDeposit, out ResourceDeposit deposit) || !ConveyorSimulationSystem.Eligible(deposit.ResourceId)))
            return null;
        return ConveyorGeometry.TryGetBuildingPort(result.BuildingId, result.GroundPosition,
            BuildingFootprint.GetRotation(result.Orientation), input, out var socket) ? socket : null;
    }

    public BuildingPlacementResult Evaluate(
        EntityRegistry entities,
        PlayerId issuer,
        BuildingId buildingId,
        Vector3 requestedPosition,
        BuildingOrientation orientation,
        EntityId ignoredEntity = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!float.IsFinite(requestedPosition.X) ||
            !float.IsFinite(requestedPosition.Y) ||
            !float.IsFinite(requestedPosition.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPosition));
        }

        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(orientation));
        }

        if (!_definitions.TryGet(buildingId, out BuildingDefinition? definition))
        {
            return BuildingPlacementResult.Invalid(
                BuildingPlacementFailureReason.UnknownBuilding,
                buildingId,
                requestedPosition,
                orientation);
        }

        Vector3 halfExtents =
            definition.Footprint.GetHalfExtents(orientation);
        Span<Vector2> samples = stackalloc Vector2[5];
        samples[0] = new Vector2(requestedPosition.X, requestedPosition.Z);
        samples[1] = new Vector2(
            requestedPosition.X - halfExtents.X,
            requestedPosition.Z - halfExtents.Z);
        samples[2] = new Vector2(
            requestedPosition.X + halfExtents.X,
            requestedPosition.Z - halfExtents.Z);
        samples[3] = new Vector2(
            requestedPosition.X - halfExtents.X,
            requestedPosition.Z + halfExtents.Z);
        samples[4] = new Vector2(
            requestedPosition.X + halfExtents.X,
            requestedPosition.Z + halfExtents.Z);

        float maximumHeight = float.NegativeInfinity;
        float maximumSlope = 0.0f;

        for (int index = 0; index < samples.Length; index++)
        {
            Vector2 sample = samples[index];
            if (!_terrain.TrySampleHeight(sample.X, sample.Y, out float height) ||
                !_terrain.TrySampleNormal(sample.X, sample.Y, out Vector3 normal))
            {
                return BuildingPlacementResult.Invalid(
                    BuildingPlacementFailureReason.TerrainUnavailable,
                    buildingId,
                    requestedPosition,
                    orientation);
            }

            maximumHeight = MathF.Max(maximumHeight, height);
            float cosine = Math.Clamp(Vector3.Dot(normal, Vector3.UnitY), -1.0f, 1.0f);
            float slopeDegrees = MathF.Acos(cosine) * (180.0f / MathF.PI);
            maximumSlope = MathF.Max(maximumSlope, slopeDegrees);
        }

        Vector3 groundPosition = new(
            requestedPosition.X,
            maximumHeight,
            requestedPosition.Z);
        Vector3 center = groundPosition + new Vector3(0.0f, halfExtents.Y, 0.0f);
        var bounds = new AxisAlignedBounds(
            center - halfExtents,
            center + halfExtents);

        if (!IsWithinWorldBounds(bounds))
        {
            return new BuildingPlacementResult(
                false,
                BuildingPlacementFailureReason.OutsideWorldBounds,
                buildingId,
                groundPosition,
                orientation,
                bounds,
                EntityId.Invalid);
        }

        if (maximumSlope > definition.MaximumSlopeDegrees)
        {
            return new BuildingPlacementResult(
                false,
                BuildingPlacementFailureReason.SlopeTooSteep,
                buildingId,
                groundPosition,
                orientation,
                bounds,
                EntityId.Invalid);
        }

        if (!_buildableArea.IsBuildable(issuer, bounds))
        {
            return new BuildingPlacementResult(
                false,
                BuildingPlacementFailureReason.OutsideBuildableArea,
                buildingId,
                groundPosition,
                orientation,
                bounds,
                EntityId.Invalid);
        }

        _spatialIndex.QueryAabb(
            bounds,
            _queryBuffer,
            order: SpatialQueryOrder.StableEntityId);

        ReadOnlySpan<EntityId> obstructingEntities = _queryBuffer.Results;
        for (int index = 0; index < obstructingEntities.Length; index++)
        {
            EntityId entity = obstructingEntities[index];
            if (!ignoredEntity.IsValid || entity != ignoredEntity)
            {
                if (IsDisconnectedAdapterAtPort(entities, entity, issuer, definition,
                        groundPosition, orientation, bounds))
                    continue;
                return new BuildingPlacementResult(
                    false,
                    BuildingPlacementFailureReason.Obstructed,
                    buildingId,
                    groundPosition,
                    orientation,
                    bounds,
                    EntityId.Invalid);
            }
        }

        EntityId deposit = EntityId.Invalid;
        if (definition.RequiresResourceDeposit)
        {
            deposit = FindDepositAtPosition(
                entities,
                groundPosition.X,
                groundPosition.Z);

            if (!deposit.IsValid)
            {
                return new BuildingPlacementResult(
                    false,
                    BuildingPlacementFailureReason.ResourceDepositRequired,
                    buildingId,
                    groundPosition,
                    orientation,
                    bounds,
                    EntityId.Invalid);
            }
        }

        // Buildings may not strand an existing bay, including a currently occupied
        // extended approach. This does not add lane shapes to navigation blockers.
        foreach (EntityId occupied in entities.Query<VehicleDockSlot>())
        {
            var slot = entities.GetComponent<VehicleDockSlot>(occupied);
            if (occupied != ignoredEntity && slot.Pose.IntersectsLane(bounds))
                return new(false, BuildingPlacementFailureReason.LoadingCorridorUnavailable, buildingId,
                    groundPosition, orientation, bounds, deposit) { LoadingCorridor = slot.Pose.CorridorBounds };
        }
        _spatialIndex.QueryAabb(new AxisAlignedBounds(bounds.Minimum - new Vector3(64, 16, 64), bounds.Maximum + new Vector3(64, 16, 64)), _queryBuffer);
        foreach (EntityId neighbor in _queryBuffer.Results)
        {
            if (neighbor == ignoredEntity || !entities.TryGetComponent(neighbor, out BuildingLoadingDock existingDock) ||
                !entities.TryGetComponent(neighbor, out WorldTransform existingBuilding)) continue;
            var design = WorldTransform.Identity with { Scale = new Vector3(3.4f, 2.5f, 6.8f) };
            if (VehicleDockPose.TryCreate(existingDock, existingBuilding, design, out var existingPose) && existingPose.IntersectsLane(bounds))
                return new(false, BuildingPlacementFailureReason.LoadingCorridorUnavailable, buildingId,
                    groundPosition, orientation, bounds, deposit) { LoadingCorridor = existingPose.CorridorBounds };
        }

        AxisAlignedBounds? loadingCorridor = null;
        if (definition.LoadingDock is BuildingLoadingDock loadingDock)
        {
            var buildingTransform = new WorldTransform(groundPosition + new Vector3(0, definition.Footprint.Height * 0.5f, 0), BuildingFootprint.GetRotation(orientation), Vector3.One);
            var designTruck = WorldTransform.Identity with { Scale = new Vector3(3.4f, 2.5f, 6.8f) };
            if (!VehicleDockPose.TryCreate(loadingDock, buildingTransform, designTruck, out var dockPose))
                return BuildingPlacementResult.Invalid(BuildingPlacementFailureReason.LoadingCorridorUnavailable, buildingId, groundPosition, orientation);
            loadingCorridor = dockPose.CorridorBounds;
            bool clear = dockPose.HasDrivableTerrain(_terrain) && !dockPose.IntersectsLane(bounds);
            if (clear)
            {
                _spatialIndex.QueryAabb(loadingCorridor.Value, _queryBuffer, order: SpatialQueryOrder.StableEntityId);
                foreach (EntityId occupant in _queryBuffer.Results)
                    if (occupant != ignoredEntity && _spatialIndex.TryGetEntry(occupant, out SpatialEntry entry) &&
                        dockPose.IntersectsLane(entry.Bounds)) { clear = false; break; }
            }
            if (!clear)
                return new BuildingPlacementResult(false, BuildingPlacementFailureReason.LoadingCorridorUnavailable,
                    buildingId, groundPosition, orientation, bounds, deposit) { LoadingCorridor = loadingCorridor };
        }

        return new BuildingPlacementResult(
            true,
            BuildingPlacementFailureReason.None,
            buildingId,
            groundPosition,
            orientation,
            bounds,
            deposit) { LoadingCorridor = loadingCorridor };
    }

    private static bool IsDisconnectedAdapterAtPort(
        EntityRegistry entities, EntityId entity, PlayerId owner, BuildingDefinition definition,
        Vector3 groundPosition, BuildingOrientation orientation, AxisAlignedBounds bounds)
    {
        if (!entities.TryGetComponent(entity, out ConveyorPiece piece) || piece.Owner != owner ||
            piece.Placement.Module is not (ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter) ||
            !piece.Attachment.IsValid || entities.IsAlive(piece.Attachment) ||
            ConveyorGeometry.Overlap(bounds, piece.Placement.Bounds, inset: .0001f))
            return false;

        Vector3 dimensions = ConveyorGeometry.BuildingPortDimensions(definition.Id);
        if (dimensions == default || Vector3.DistanceSquared(dimensions, new Vector3(
                definition.Footprint.Width, definition.Footprint.Height, definition.Footprint.Depth)) > .001f)
            return false;
        bool input = piece.Placement.Module == ConveyorModule.OutputAdapter;
        if (input && definition.Id == BuildingIds.Extractor ||
            !input && (definition.Id == BuildingIds.Barracks || definition.Id == BuildingIds.VehicleFactory))
            return false;

        Quaternion rotation = BuildingFootprint.GetRotation(orientation);
        float sign = input ? -1 : 1;
        Vector3 port = groundPosition + Vector3.Transform(new Vector3(sign * dimensions.X / 2, 5.8f, 0), rotation);
        Vector3 facing = Vector3.Transform(sign * Vector3.UnitX, rotation);
        ConveyorSocket[] sockets = piece.Placement.Sockets;
        ConveyorSocket terminal = input ? sockets[^1] : sockets[0];
        // Only the exact old terminal may touch the replacement footprint. Its attachment remains
        // stale until the player explicitly reconnects it; all support/interior collisions still apply.
        return Vector3.DistanceSquared(port, terminal.Position) < .0004f &&
            Vector3.Dot(facing, terminal.Facing) < -.999f;
    }

    private bool IsWithinWorldBounds(in AxisAlignedBounds bounds)
    {
        AxisAlignedBounds world = _terrain.WorldBounds;
        return bounds.Minimum.X >= world.Minimum.X &&
               bounds.Maximum.X <= world.Maximum.X &&
               bounds.Minimum.Z >= world.Minimum.Z &&
               bounds.Maximum.Z <= world.Maximum.Z;
    }

    private static EntityId FindDepositAtPosition(
        EntityRegistry entities,
        float worldX,
        float worldZ)
    {
        foreach (EntityId entity in
                 entities.Query<ResourceDeposit>(QueryIterationOrder.StableByEntityIndex))
        {
            ResourceDeposit deposit =
                entities.GetComponent<ResourceDeposit>(entity);

            if (!deposit.IsDepleted &&
                worldX >= deposit.Bounds.Minimum.X &&
                worldX <= deposit.Bounds.Maximum.X &&
                worldZ >= deposit.Bounds.Minimum.Z &&
                worldZ <= deposit.Bounds.Maximum.Z)
            {
                return entity;
            }
        }

        return EntityId.Invalid;
    }
}
