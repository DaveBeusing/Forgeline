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
    ResourceDepositRequired = 7
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
    EntityId ResourceDeposit);

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
            result.ResourceDeposit);
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

        return new BuildingPlacementResult(
            true,
            BuildingPlacementFailureReason.None,
            buildingId,
            groundPosition,
            orientation,
            bounds,
            deposit);
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
