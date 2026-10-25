using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed partial class UnitProductionSystem
{
    // Production reserves its paid inputs while all bounded exit positions are occupied.
    // Existing units are never displaced or omitted according to owner/visibility/health.
    private bool TryFindSpawn(EntityRegistry entities, EntityId facilityEntity,
        in UnitProductionFacility facility, UnitDefinition definition, out Vector3 position)
    {
        position = default;
        if (!entities.TryGetComponent(facilityEntity, out WorldTransform transform)) return false;
        var bounds = new List<AxisAlignedBounds>();
        foreach (var entity in entities.Query<WorldTransform, SpatialPresence>())
        {
            if (bounds.Count == 4096) return false;
            var presence = entities.GetComponent<SpatialPresence>(entity);
            bounds.Add(presence.CreateEntry(entity, entities.GetComponent<WorldTransform>(entity)).Bounds);
        }
        Vector3 origin = transform.Position + Vector3.Transform(facility.SpawnOffset, transform.Rotation);
        float spacing = MathF.Max(8, MathF.Max(definition.VisualScale.X, definition.VisualScale.Z) + 2);
        Vector3 half = definition.VisualScale * .5f + new Vector3(.5f, 0, .5f);
        for (int ring = 0; ring <= 2; ring++)
        for (int z = -ring; z <= ring; z++)
        for (int x = -ring; x <= ring; x++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) != ring) continue;
            Vector3 candidate = origin + Vector3.Transform(new Vector3(x * spacing, 0, z * spacing), transform.Rotation);
            if (!_unitFactory.TryGroundProductionPosition(definition, candidate, out candidate)) continue;
            bool occupied = false;
            foreach (var obstacle in bounds)
                if (candidate.X + half.X >= obstacle.Minimum.X && candidate.X - half.X <= obstacle.Maximum.X &&
                    candidate.Z + half.Z >= obstacle.Minimum.Z && candidate.Z - half.Z <= obstacle.Maximum.Z)
                { occupied = true; break; }
            if (occupied) continue;
            position = candidate;
            return true;
        }
        return false;
    }
}
