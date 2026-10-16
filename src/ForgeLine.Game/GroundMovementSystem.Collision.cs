using System.Numerics;
using ForgeLine.Core;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed partial class GroundMovementSystem
{
    private float _obstacleQueryRadius;

    private void QueryMovementNeighborhood(Vector3 position, float maximumTravel, in GroundMovement movement)
    {
        if (_spatialIndex is null)
        {
            _neighborBuffer.Clear();
            _obstacleBuffer.Clear();
            _obstacleQueryRadius = 0;
            return;
        }

        // Collision candidates must survive disabled steering and must cover
        // the entire tick, not just the optional avoidance look-ahead point.
        _spatialIndex.QueryRadius(position,
            MathF.Max(movement.SeparationRadius, maximumTravel + movement.Radius),
            _neighborBuffer,
            new SpatialQueryFilter(Mobility: SpatialMobility.Mobile),
            SpatialQueryOrder.StableEntityId);
        _obstacleQueryRadius = MathF.Max(movement.ObstacleLookAhead, maximumTravel) +
            movement.Radius + _options.ObstacleClearanceMeters;
        _spatialIndex.QueryRadius(position, _obstacleQueryRadius, _obstacleBuffer,
            new SpatialQueryFilter(Mobility: SpatialMobility.Static),
            SpatialQueryOrder.StableEntityId);
    }

    private void ConstrainStaticStep(EntityId entity, Vector3 position,
        ref Vector3 candidate, in GroundMovement movement)
    {
        if (_spatialIndex is null) return;
        Vector3 displacement = candidate - position;
        float distance = Horizontal(displacement).Length();
        if (distance <= 0.000001f) return;
        float clearance = movement.Radius + _options.ObstacleClearanceMeters;

        // Dense mobile penetration correction can exceed the initial travel
        // envelope. Re-query only in that case, using the same reusable buffer.
        if (distance + clearance > _obstacleQueryRadius)
        {
            _spatialIndex.QueryRadius(position, distance + clearance, _obstacleBuffer,
                new SpatialQueryFilter(Mobility: SpatialMobility.Static),
                SpatialQueryOrder.StableEntityId);
        }

        float earliest = 1;
        bool blocked = false;
        foreach (EntityId obstacle in _obstacleBuffer.Results)
        {
            if (obstacle == entity || !_spatialIndex.TryGetEntry(obstacle, out SpatialEntry entry)) continue;
            if (GroundMovementStep.TrySweepCircle(position, candidate, entry.Bounds, clearance, out float fraction))
            {
                earliest = MathF.Min(earliest, fraction);
                blocked = true;
            }
        }
        if (blocked)
        {
            const float contactSkinMeters = 0.001f;
            candidate = position + displacement * MathF.Max(0, earliest - contactSkinMeters / distance);
        }
    }
}
