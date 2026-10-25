using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Simulation-thread adapters; the caller owns neighborhood completeness and capture timing.</summary>
internal static class NavigationMotionCapture
{
    public static bool TryCaptureVehicle(EntityRegistry entities, EntityId entity,
        GroundMovementSystemOptions options, out NavigationVehicleObservation observation)
    {
        ArgumentNullException.ThrowIfNull(options);
        return NavigationReadAdapter.TryCaptureVehicle(entities, entity,
            options.ObstacleClearanceMeters, out observation);
    }

    /// <summary>Copies physical geometry without visibility, ownership or health-based omission.</summary>
    public static bool TryCaptureBody(EntityRegistry entities, in SpatialEntry entry,
        out NavigationCollisionBody body)
    {
        ArgumentNullException.ThrowIfNull(entities);
        body = default;
        if (!entry.Entity.IsValid || !entities.IsAlive(entry.Entity) ||
            !Enum.IsDefined(entry.Metadata.Mobility) ||
            !entities.TryGetComponent(entry.Entity, out WorldTransform transform) ||
            !entities.TryGetComponent(entry.Entity, out SpatialPresence presence)) return false;

        if (!new NavigationPose(transform.Position, transform.Rotation).IsValid ||
            !Finite(transform.Position - presence.HalfExtents) ||
            !Finite(transform.Position + presence.HalfExtents)) return false;

        // Stale spatial entries cannot certify a complete physical neighborhood.
        SpatialEntry current = presence.CreateEntry(entry.Entity, transform);
        if (current.Metadata != entry.Metadata || current.Bounds.Minimum != entry.Bounds.Minimum ||
            current.Bounds.Maximum != entry.Bounds.Maximum) return false;

        NavigationObstacleIdentity identity = NavigationReadAdapter.GetObstacleIdentity(entry.Entity);
        if (entities.TryGetComponent(entry.Entity, out ConveyorPiece conveyor))
        {
            if (!Enum.IsDefined(conveyor.Placement.Module) || conveyor.Placement.Module == ConveyorModule.Route ||
                !Enum.IsDefined(conveyor.Placement.Orientation) || conveyor.Placement.Tier is < 0 or > 1 ||
                !Finite(conveyor.Placement.Pivot) || !float.IsFinite(conveyor.Placement.SpanMeters) ||
                conveyor.Placement.SpanMeters != 0 && (conveyor.Placement.Module != ConveyorModule.ShortSegment ||
                    conveyor.Placement.SpanMeters is < 1 or > 4)) return false;
            // Conveyor transforms carry source-mesh scale, not physical carrier dimensions.
            AxisAlignedBounds bounds = conveyor.Placement.Bounds;
            body = FromBounds(identity, bounds);
        }
        else if (entry.Metadata.Mobility == SpatialMobility.Static)
        {
            body = FromBounds(identity, entry.Bounds);
        }
        else
        {
            if (!entities.TryGetComponent(entry.Entity, out GroundMovementState movement)) return false;
            body = new NavigationCollisionBody(identity, NavigationObstacleKind.Vehicle,
                new NavigationPose(transform.Position, transform.Rotation), transform.Scale * 0.5f,
                movement.Velocity);
        }
        return IsValid(body);
    }

    /// <summary>
    /// Entries must cover every physical source that can enter coverage during the planning horizon.
    /// This method performs no spatial query and never repairs a truncated or stale neighborhood.
    /// Each copied candidate and ordering comparison consumes one work unit.
    /// </summary>
    public static NavigationCollisionFrame CaptureCollisionFrame(EntityRegistry entities,
        NavigationWorldSnapshot world, SimulationTick tick, AxisAlignedBounds coverage,
        bool neighborhoodComplete, ReadOnlySpan<SpatialEntry> entries,
        ReadOnlySpan<NavigationCollisionBody> additionalBodies, int maximumBodies,
        int workAllowance, out int workConsumed)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBodies);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        workConsumed = 0;
        long requested = (long)entries.Length + additionalBodies.Length;
        int capacity = (int)Math.Min(Math.Min(requested, maximumBodies), workAllowance);
        var bodies = new NavigationCollisionBody[capacity];
        var identities = new HashSet<NavigationObstacleIdentity>(capacity);
        bool complete = neighborhoodComplete && requested <= capacity;
        int count = 0;

        foreach (SpatialEntry entry in entries)
        {
            if (count == capacity || !Spend(workAllowance, ref workConsumed))
            {
                complete = false;
                break;
            }
            if (!TryCaptureBody(entities, entry, out NavigationCollisionBody body) || !identities.Add(body.Identity))
            {
                complete = false;
                continue;
            }
            bodies[count++] = body;
        }
        foreach (NavigationCollisionBody body in additionalBodies)
        {
            if (count == capacity || !Spend(workAllowance, ref workConsumed))
            {
                complete = false;
                break;
            }
            if (!IsValid(body) || !identities.Add(body.Identity))
            {
                complete = false;
                continue;
            }
            bodies[count++] = body;
        }

        // Bounded insertion ordering avoids dependence on caller collection order.
        // An unfinished ordering makes the whole frame incomplete, never partially authoritative.
        for (int index = 1; index < count; index++)
        {
            NavigationCollisionBody item = bodies[index];
            int previous = index - 1;
            while (previous >= 0)
            {
                if (!Spend(workAllowance, ref workConsumed))
                {
                    bodies[previous + 1] = item;
                    complete = false;
                    return new NavigationCollisionFrame(world, tick.Value, coverage, complete,
                        bodies.AsSpan(0, count), maximumBodies);
                }
                if (Compare(bodies[previous].Identity, item.Identity) <= 0) break;
                bodies[previous + 1] = bodies[previous];
                previous--;
            }
            bodies[previous + 1] = item;
        }
        return new NavigationCollisionFrame(world, tick.Value, coverage, complete,
            bodies.AsSpan(0, count), maximumBodies);
    }

    private static NavigationCollisionBody FromBounds(NavigationObstacleIdentity identity,
        in AxisAlignedBounds bounds) => new(identity, NavigationObstacleKind.Structure,
            new NavigationPose(bounds.Center, Quaternion.Identity), bounds.Extents, Vector3.Zero);

    internal static bool IsValid(in NavigationCollisionBody body) => body.Pose.IsValid &&
        Finite(body.HalfExtents) && body.HalfExtents.X > 0 && body.HalfExtents.Y > 0 && body.HalfExtents.Z > 0 &&
        Finite(body.Velocity) && body.Identity.Generation != 0 && Enum.IsDefined(body.Identity.Source) &&
        body.Identity.Source != NavigationObstacleSource.Unspecified && Enum.IsDefined(body.Kind) &&
        body.Kind != NavigationObstacleKind.Unspecified;

    private static int Compare(NavigationObstacleIdentity first, NavigationObstacleIdentity second)
    {
        int comparison = first.Source.CompareTo(second.Source);
        if (comparison == 0) comparison = first.Value.CompareTo(second.Value);
        return comparison != 0 ? comparison : first.Generation.CompareTo(second.Generation);
    }

    internal static bool Spend(int allowance, ref int consumed)
    {
        if (consumed >= allowance) return false;
        consumed++;
        return true;
    }

    internal static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
