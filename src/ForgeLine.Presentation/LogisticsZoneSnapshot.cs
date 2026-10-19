using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum LogisticsZoneKind : byte { Loading, Holding }

/// <summary>Terrain-sampled world strokes copied from authorized dock and queue components.</summary>
public readonly record struct LogisticsZoneStroke(
    EntityId Building, EntityId Vehicle, LogisticsZoneKind Kind, Vector3 Start, Vector3 End,
    bool Unavailable = false);

public sealed class LogisticsZoneSnapshot
{
    internal const int MaximumZones = 128;
    internal const int MaximumStrokes = 4096;
    private readonly LogisticsZoneStroke[] _strokes;

    internal LogisticsZoneSnapshot(LogisticsZoneStroke[] strokes) => _strokes = strokes;
    public ReadOnlySpan<LogisticsZoneStroke> Strokes => _strokes;

    internal static LogisticsZoneSnapshot Capture(EntityRegistry entities, PlayerId player, ITerrainQuery terrain,
        IReadOnlyList<EntityId>? selected = null, EntityId hovered = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(terrain);
        if (selected is { Count: 0 } && !hovered.IsValid) return new([]);
        HashSet<EntityId>? interest = selected is { Count: > 0 } ? new(selected) : null;
        bool Interested(EntityId building, EntityId vehicle) =>
            selected is null && !hovered.IsValid || hovered == building || hovered == vehicle ||
            (interest?.Contains(building) ?? false) || (interest?.Contains(vehicle) ?? false);
        List<LogisticsZoneStroke>? strokes = null;
        HashSet<EntityId>? publishedBays = null;
        int zones = 0;

        foreach (EntityId building in entities.Query<VehicleDockSlot>(QueryIterationOrder.StableByEntityIndex))
        {
            if (zones >= MaximumZones) break;
            if (!OwnedBuilding(entities, building, player)) continue;
            var slot = entities.GetComponent<VehicleDockSlot>(building);
            if (slot.Owner != player || !OwnedVehicle(entities, slot.Truck, player) ||
                !Interested(building, slot.Truck)) continue;
            strokes ??= new(128);
            publishedBays ??= [];
            if (!AddLoading(strokes, terrain, building, slot.Truck, slot.Pose)) continue;
            publishedBays.Add(building);
            zones++;
        }

        foreach (EntityId truck in entities.Query<VehicleDockVisit>(QueryIterationOrder.StableByEntityIndex))
        {
            if (zones >= MaximumZones) break;
            if (!OwnedVehicle(entities, truck, player)) continue;
            var visit = entities.GetComponent<VehicleDockVisit>(truck);
            if (visit.Status is VehicleDockStatus.Busy or VehicleDockStatus.Blocked ||
                !Interested(visit.Building, truck) ||
                !OwnedBuilding(entities, visit.Building, player) ||
                publishedBays?.Contains(visit.Building) == true) continue;
            strokes ??= new(128);
            publishedBays ??= [];
            if (!AddLoading(strokes, terrain, visit.Building, truck, visit.Pose)) continue;
            publishedBays.Add(visit.Building);
            zones++;
        }

        foreach (EntityId truck in entities.Query<VehicleQueueEntry>(QueryIterationOrder.StableByEntityIndex))
        {
            if (zones >= MaximumZones) break;
            if (!OwnedVehicle(entities, truck, player)) continue;
            var queue = entities.GetComponent<VehicleQueueEntry>(truck);
            if (queue.Owner != player || !Interested(queue.Building, truck) || queue.ArrivedAtService ||
                queue.WaitReason == VehicleQueueWaitReason.GeometryBlocked ||
                !OwnedBuilding(entities, queue.Building, player) ||
                !entities.TryGetComponent(truck, out WorldTransform vehicle)) continue;
            strokes ??= new(128);
            if (!AddHolding(strokes, terrain, queue.Building, truck, queue.Target, queue.Heading,
                vehicle.Scale.X * .5f, vehicle.Scale.Z * .5f,
                queue.WaitReason is VehicleQueueWaitReason.Full or VehicleQueueWaitReason.Unavailable)) continue;
            zones++;
        }

        foreach (EntityId truck in entities.Query<VehicleQueueHolding>(QueryIterationOrder.StableByEntityIndex))
        {
            if (zones >= MaximumZones) break;
            if (!OwnedVehicle(entities, truck, player)) continue;
            var holding = entities.GetComponent<VehicleQueueHolding>(truck);
            if (holding.Reason == VehicleQueueWaitReason.GeometryBlocked ||
                !Interested(holding.Building, truck) ||
                !OwnedBuilding(entities, holding.Building, player) ||
                entities.HasComponent<VehicleQueueEntry>(truck) ||
                !entities.TryGetComponent(truck, out WorldTransform vehicle)) continue;
            strokes ??= new(128);
            // Only a validated holding target, never an inferred free space.
            float heading = MathF.Atan2(Vector3.Transform(Vector3.UnitZ, vehicle.Rotation).X,
                Vector3.Transform(Vector3.UnitZ, vehicle.Rotation).Z);
            if (!AddHolding(strokes, terrain, holding.Building, truck, holding.Target, heading,
                vehicle.Scale.X * .5f, vehicle.Scale.Z * .5f,
                holding.Reason is VehicleQueueWaitReason.Full or VehicleQueueWaitReason.Unavailable)) continue;
            zones++;
        }

        return new((strokes is { Count: > 0 } ? strokes.ToArray() : []));
    }

    private static bool OwnedBuilding(EntityRegistry entities, EntityId building, PlayerId player) =>
        building.IsValid && entities.IsAlive(building) &&
        entities.TryGetComponent(building, out CompletedBuilding complete) && complete.Owner == player &&
        entities.TryGetComponent(building, out VisualIdentity visual) &&
        ((uint)visual.Visibility & (uint)RenderVisibilityMask.World) != 0;

    private static bool OwnedVehicle(EntityRegistry entities, EntityId vehicle, PlayerId player) =>
        vehicle.IsValid && entities.IsAlive(vehicle) &&
        entities.TryGetComponent(vehicle, out ControllableEntity control) && control.Owner == player &&
        entities.TryGetComponent(vehicle, out VisualIdentity visual) &&
        ((uint)visual.Visibility & (uint)RenderVisibilityMask.World) != 0;

    private static bool AddLoading(List<LogisticsZoneStroke> strokes, ITerrainQuery terrain,
        EntityId building, EntityId truck, in VehicleDockPose pose)
    {
        if (!Finite(pose.Entry) || !Finite(pose.Service) || !Finite(pose.Exit) ||
            !float.IsFinite(pose.Heading) || !float.IsFinite(pose.HalfLength) ||
            !float.IsFinite(pose.HalfWidth) || pose.HalfLength <= 0 || pose.HalfWidth <= 0) return false;
        Vector3 direction = new(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
        float half = Vector3.Dot(pose.Exit - pose.Entry, direction) * .5f + pose.HalfLength;
        Vector3 center = (pose.Entry + pose.Exit) * .5f;
        return AddHatching(strokes, terrain, building, truck, LogisticsZoneKind.Loading,
            center, pose.Heading, half, pose.HalfWidth + 1, false);
    }

    private static bool AddHolding(List<LogisticsZoneStroke> strokes, ITerrainQuery terrain,
        EntityId building, EntityId truck, Vector3 target, float heading, float halfWidth,
        float halfLength, bool unavailable) =>
        AddHatching(strokes, terrain, building, truck, LogisticsZoneKind.Holding,
            target, heading, halfLength, halfWidth, unavailable);

    internal static bool AddHatching(List<LogisticsZoneStroke> strokes, ITerrainQuery terrain,
        EntityId building, EntityId truck, LogisticsZoneKind kind,
        Vector3 center, float heading, float halfLength, float halfWidth, bool unavailable)
    {
        if (!Finite(center) || !float.IsFinite(heading) || !float.IsFinite(halfLength) ||
            !float.IsFinite(halfWidth) || halfLength <= 0 || halfWidth <= 0 ||
            halfLength > 1000 || halfWidth > 1000 || strokes.Count >= MaximumStrokes - 64) return false;

        Vector3 forward = new(MathF.Sin(heading), 0, MathF.Cos(heading));
        Vector3 side = new(forward.Z, 0, -forward.X);
        Vector3 At(float u, float v) => center + forward * u + side * v;
        int first = strokes.Count;
        Add(At(-halfLength, -halfWidth), At(halfLength, -halfWidth));
        Add(At(halfLength, -halfWidth), At(halfLength, halfWidth));
        Add(At(halfLength, halfWidth), At(-halfLength, halfWidth));
        Add(At(-halfLength, halfWidth), At(-halfLength, -halfWidth));
        float limit = halfLength + halfWidth;
        float spacing = kind == LogisticsZoneKind.Loading ? 2.5f : 1.7f;
        int count = Math.Min(40, (int)MathF.Floor(2 * limit / spacing));
        for (int i = 0; i <= count; i++)
        {
            float t = -limit + i * (2 * limit / Math.Max(1, count));
            float ua = MathF.Max(-halfLength, t - halfWidth);
            float ub = MathF.Min(halfLength, t + halfWidth);
            if (ub - ua < .1f) continue;
            float va = kind == LogisticsZoneKind.Loading ? ua - t : t - ua;
            float vb = kind == LogisticsZoneKind.Loading ? ub - t : t - ub;
            Add(At(ua, va), At(ub, vb));
        }
        return strokes.Count > first;

        void Add(Vector3 start, Vector3 end)
        {
            if (strokes.Count >= MaximumStrokes ||
                !terrain.TrySampleHeight(start.X, start.Z, out float a) ||
                !terrain.TrySampleHeight(end.X, end.Z, out float b) ||
                !float.IsFinite(a) || !float.IsFinite(b)) return;
            strokes.Add(new(building, truck, kind, start with { Y = a + .08f },
                end with { Y = b + .08f }, unavailable));
        }
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
