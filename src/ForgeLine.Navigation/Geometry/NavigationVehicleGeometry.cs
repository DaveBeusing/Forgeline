using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

/// <summary>
/// Conservative body-centered clearance over a pinned physical snapshot. The
/// terrain argument must be the authoritative source used to publish snapshot.
/// This service neither discovers live obstacles nor authorizes transform writes.
/// </summary>
public sealed class NavigationVehicleGeometry : INavigationGeometryQuery
{
    public const int AlgorithmRevision = 1;

    private readonly NavigationWorldSnapshot _snapshot;
    private readonly NavigationTerrainEnvelope _terrain;
    private readonly int _maximumSweepIntervals;
    private readonly float _maximumIntervalTravelMeters;

    public NavigationVehicleGeometry(NavigationWorldSnapshot snapshot, TerrainWorld terrain,
        int maximumSweepIntervals, float maximumIntervalTravelMeters)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSweepIntervals);
        if (!float.IsFinite(maximumIntervalTravelMeters) || maximumIntervalTravelMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumIntervalTravelMeters));
        if (!snapshot.HasTerrain || snapshot.WorldBounds.Minimum != terrain.WorldBounds.Minimum ||
            snapshot.WorldBounds.Maximum != terrain.WorldBounds.Maximum)
            throw new ArgumentException("Geometry requires the terrain source of its pinned snapshot.", nameof(terrain));
        _snapshot = snapshot;
        _terrain = new NavigationTerrainEnvelope(terrain);
        _maximumSweepIntervals = maximumSweepIntervals;
        _maximumIntervalTravelMeters = maximumIntervalTravelMeters;
    }

    public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;

    public NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement,
        int workAllowance)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var work = new NavigationGeometryWork(Math.Max(0, workAllowance));
        if (workAllowance < 0 || !Valid(movement))
            return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
        if (!ReferenceEquals(frame.World, _snapshot))
            return Result(frame, NavigationGeometryStatus.Stale, 0, NavigationBlockReason.StaleSnapshot, null, work);
        if (!frame.IsComplete || !frame.World.HasTerrain)
            return Result(frame, NavigationGeometryStatus.Unavailable, 0, NavigationBlockReason.MissingGeometry, null, work);
        if (workAllowance == 0)
            return Result(frame, NavigationGeometryStatus.BudgetExhausted, 0, NavigationBlockReason.SearchWork, null, work);
        if (!ContainsHorizontalPoint(_snapshot.WorldBounds, movement.Start.Position) ||
            !ContainsHorizontalPoint(_snapshot.WorldBounds, movement.End.Position))
            return Result(frame, NavigationGeometryStatus.Blocked, 0, NavigationBlockReason.OutsideWorld, null, work);

        if (!work.TrySpend())
            return Result(frame, NavigationGeometryStatus.BudgetExhausted, 0, NavigationBlockReason.SearchWork, null, work);
        if (!_snapshot.TrySampleHeight(movement.Start.Position.X, movement.Start.Position.Z, out float startHeight))
            return Result(frame, NavigationGeometryStatus.Unavailable, 0, NavigationBlockReason.MissingGeometry, null, work);
        if (!work.TrySpend())
            return Result(frame, NavigationGeometryStatus.BudgetExhausted, 0, NavigationBlockReason.SearchWork, null, work);
        if (!_snapshot.TrySampleHeight(movement.End.Position.X, movement.End.Position.Z, out float endHeight))
            return Result(frame, NavigationGeometryStatus.Unavailable, 0, NavigationBlockReason.MissingGeometry, null, work);

        // Preserve the captured authored height offset; half-height is not a
        // substitute for suspension/grounding configuration. Ground following
        // between these endpoints remains part of the proposed interpolation.
        double startOffset = (double)movement.Start.Position.Y - startHeight;
        double endOffset = (double)movement.End.Position.Y - endHeight;
        float heightTolerance = NavigationCollisionBox.NumericalPadding(movement.End.Position, 0);
        if (Math.Abs(startOffset - endOffset) > heightTolerance)
            return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Terrain, null, work);

        Vector3 halfExtents = movement.Vehicle.HalfExtents;
        double radius = NavigationCollisionBox.Radius(halfExtents);
        Quaternion startRotation = Quaternion.Normalize(movement.Start.Rotation);
        Quaternion endRotation = Quaternion.Normalize(movement.End.Rotation);
        double dot = (double)startRotation.X * endRotation.X + (double)startRotation.Y * endRotation.Y +
            (double)startRotation.Z * endRotation.Z + (double)startRotation.W * endRotation.W;
        if (dot < 0) endRotation = Quaternion.Negate(endRotation);
        // A rounded dot product can equal one for a real small rotation. The
        // quaternion chord retains that rotation instead of giving it no sweep.
        double rotationX = (double)endRotation.X - startRotation.X;
        double rotationY = (double)endRotation.Y - startRotation.Y;
        double rotationZ = (double)endRotation.Z - startRotation.Z;
        double rotationW = (double)endRotation.W - startRotation.W;
        double chord = Math.Sqrt(rotationX * rotationX + rotationY * rotationY +
            rotationZ * rotationZ + rotationW * rotationW);
        double angle = 4 * Math.Asin(Math.Clamp(chord * 0.5, 0, Math.Sqrt(0.5)));
        Vector3 translation = movement.End.Position - movement.Start.Position;
        double distance = NavigationCollisionBox.Radius(translation);
        double intervalTravel = Math.Min(_maximumIntervalTravelMeters,
            Math.Min(Math.Min(halfExtents.X, halfExtents.Y), halfExtents.Z) * 0.25);
        double intervals = Math.Max(1, Math.Ceiling((distance + angle * radius) / intervalTravel));
        if (!double.IsFinite(intervals) || !float.IsFinite((float)radius))
            return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);

        float approved = 0;
        for (int interval = 0; interval < _maximumSweepIntervals && interval < intervals; interval++)
        {
            if (!work.TrySpend())
                return Result(frame, NavigationGeometryStatus.BudgetExhausted, approved, NavigationBlockReason.SearchWork, null, work);
            float endFraction = (float)Math.Min(1, (interval + 1.0) / intervals);
            if (endFraction <= approved)
                return Result(frame, NavigationGeometryStatus.BudgetExhausted, approved, NavigationBlockReason.SearchWork, null, work);
            NavigationCollisionBox swept = Enclose(movement, startRotation, endRotation,
                angle, radius, approved, endFraction);
            if (!Finite(swept.HalfExtents) || !Finite(swept.Center) || !FiniteBounds(swept))
                return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
            AxisAlignedBounds bounds = swept.Bounds;
            if (!ContainsHorizontal(_snapshot.WorldBounds, bounds))
                return Obstructed(frame, approved, NavigationBlockReason.OutsideWorld, null, work);
            if (!Contains(frame.Coverage, bounds))
                return Result(frame, NavigationGeometryStatus.Unavailable, 0, NavigationBlockReason.MissingGeometry, null, work);

            NavigationObstacleIdentity? blocker = null;
            NavigationBlockReason blockReason = NavigationBlockReason.None;
            foreach (NavigationCollisionBody body in frame.Bodies)
            {
                if (!work.TrySpend())
                    return Result(frame, NavigationGeometryStatus.BudgetExhausted, approved, NavigationBlockReason.SearchWork, null, work);
                if (body.Identity == movement.Mover) continue;
                if (!TryObstacleEnvelope(body, movement.DurationSeconds, approved, endFraction, out NavigationCollisionBox obstacle))
                    return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
                if (!NavigationCollisionBox.Intersects(swept, obstacle)) continue;
                // Scan all entries before publishing a blocker so input array
                // order cannot pick different identities under equal contacts.
                if (blocker is null || Compare(body.Identity, blocker.Value) < 0)
                {
                    blocker = body.Identity;
                    blockReason = body.Kind == NavigationObstacleKind.Reservation
                        ? NavigationBlockReason.ReservedCorridor
                        : body.Kind == NavigationObstacleKind.Vehicle || body.Velocity != Vector3.Zero
                            ? NavigationBlockReason.MovingVehicle : NavigationBlockReason.StaticObstacle;
                }
            }
            if (blocker.HasValue) return Obstructed(frame, approved, blockReason, blocker, work);

            NavigationGeometryStatus terrainStatus = _terrain.Check(swept, movement,
                approved, endFraction, startOffset, _snapshot.Capabilities.MaximumSlopeDegrees, ref work);
            if (terrainStatus == NavigationGeometryStatus.BudgetExhausted)
                return Result(frame, terrainStatus, approved, NavigationBlockReason.SearchWork, null, work);
            if (terrainStatus == NavigationGeometryStatus.Unavailable)
                return Result(frame, terrainStatus, 0, NavigationBlockReason.MissingGeometry, null, work);
            if (terrainStatus != NavigationGeometryStatus.Clear)
                return Obstructed(frame, approved, NavigationBlockReason.Terrain, null, work);
            approved = endFraction;
        }
        return approved == 1
            ? Result(frame, NavigationGeometryStatus.Clear, 1, NavigationBlockReason.None, null, work)
            : Result(frame, NavigationGeometryStatus.BudgetExhausted, approved, NavigationBlockReason.SearchWork, null, work);
    }

    private static NavigationCollisionBox Enclose(in NavigationSweep movement,
        Quaternion startRotation, Quaternion endRotation, double angle, double radius, float from, float to)
    {
        float middle = (from + to) * 0.5f;
        var pose = new NavigationPose(Vector3.Lerp(movement.Start.Position, movement.End.Position, middle),
            Quaternion.Slerp(startRotation, endRotation, middle));
        NavigationCollisionBox box = NavigationCollisionBox.At(pose, movement.Vehicle.HalfExtents);
        Vector3 halfTranslation = (movement.End.Position - movement.Start.Position) * ((to - from) * 0.5f);
        // Every point's deviation from the middle rotation is bounded by the
        // chord of the enclosing sphere. Translation is projected separately.
        float rotationExpansion = (float)(2 * radius * Math.Sin(angle * (to - from) * 0.25));
        float padding = rotationExpansion + movement.Vehicle.ClearanceMeters +
            NavigationCollisionBox.NumericalPadding(pose.Position, radius);
        Vector3 extent = box.HalfExtents + new Vector3(
            (float)Math.Abs(NavigationCollisionBox.Dot(halfTranslation, box.AxisX)),
            (float)Math.Abs(NavigationCollisionBox.Dot(halfTranslation, box.AxisY)),
            (float)Math.Abs(NavigationCollisionBox.Dot(halfTranslation, box.AxisZ))) + new Vector3(padding);
        return box with { HalfExtents = extent };
    }

    private static bool TryObstacleEnvelope(in NavigationCollisionBody body, float duration,
        float from, float to, out NavigationCollisionBox box)
    {
        bool moving = body.Kind == NavigationObstacleKind.Vehicle || body.Velocity != Vector3.Zero;
        if (!moving)
        {
            double staticRadius = NavigationCollisionBox.Radius(body.HalfExtents);
            float padding = NavigationCollisionBox.NumericalPadding(body.Pose.Position, staticRadius);
            box = NavigationCollisionBox.At(body.Pose, body.HalfExtents + new Vector3(padding));
        }
        else
        {
            // Revision 1 supplies velocity, but no angular-rate or acceleration
            // prediction. Enclose every rotation while translating linearly;
            // actual simultaneous proposals still require final arbitration.
            Vector3 fromPosition = body.Pose.Position + body.Velocity * (duration * from);
            Vector3 toPosition = body.Pose.Position + body.Velocity * (duration * to);
            Vector3 center = fromPosition * 0.5f + toPosition * 0.5f;
            double radius = NavigationCollisionBox.Radius(body.HalfExtents);
            float padding = NavigationCollisionBox.NumericalPadding(center, radius);
            box = NavigationCollisionBox.AxisAligned(center,
                Vector3.Abs(toPosition - fromPosition) * 0.5f + new Vector3((float)radius + padding));
        }
        return Finite(box.Center) && Finite(box.HalfExtents) && FiniteBounds(box);
    }

    private static bool Valid(in NavigationSweep movement) =>
        movement.Start.IsValid && movement.End.IsValid && movement.Vehicle.IsValid &&
        movement.Mover.Generation != 0 && Enum.IsDefined(movement.Mover.Source) &&
        movement.Mover.Source != NavigationObstacleSource.Unspecified &&
        float.IsFinite(movement.DurationSeconds) && movement.DurationSeconds >= 0 &&
        (movement.DurationSeconds > 0 || movement.Start == movement.End);

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool FiniteBounds(in NavigationCollisionBox box)
    {
        Vector3 extent = Vector3.Abs(box.AxisX) * box.HalfExtents.X +
            Vector3.Abs(box.AxisY) * box.HalfExtents.Y + Vector3.Abs(box.AxisZ) * box.HalfExtents.Z;
        return Finite(box.Center - extent) && Finite(box.Center + extent);
    }

    private static bool ContainsHorizontal(in AxisAlignedBounds outer, in AxisAlignedBounds inner) =>
        inner.Minimum.X >= outer.Minimum.X && inner.Maximum.X <= outer.Maximum.X &&
        inner.Minimum.Z >= outer.Minimum.Z && inner.Maximum.Z <= outer.Maximum.Z;

    private static bool ContainsHorizontalPoint(in AxisAlignedBounds bounds, Vector3 point) =>
        point.X >= bounds.Minimum.X && point.X <= bounds.Maximum.X &&
        point.Z >= bounds.Minimum.Z && point.Z <= bounds.Maximum.Z;

    private static bool Contains(in AxisAlignedBounds outer, in AxisAlignedBounds inner) =>
        ContainsHorizontal(outer, inner) && inner.Minimum.Y >= outer.Minimum.Y && inner.Maximum.Y <= outer.Maximum.Y;

    private static int Compare(NavigationObstacleIdentity left, NavigationObstacleIdentity right)
    {
        int source = left.Source.CompareTo(right.Source);
        if (source != 0) return source;
        int value = left.Value.CompareTo(right.Value);
        return value != 0 ? value : left.Generation.CompareTo(right.Generation);
    }

    private static NavigationSweepResult Obstructed(NavigationCollisionFrame frame, float approved,
        NavigationBlockReason reason, NavigationObstacleIdentity? blocker, in NavigationGeometryWork work) =>
        Result(frame, approved > 0 ? NavigationGeometryStatus.Limited : NavigationGeometryStatus.Blocked,
            approved, reason, blocker, work);

    private static NavigationSweepResult Result(NavigationCollisionFrame frame, NavigationGeometryStatus status,
        float approved, NavigationBlockReason reason, NavigationObstacleIdentity? blocker, in NavigationGeometryWork work) =>
        new(status, frame.World.Revision, frame.Tick, approved, reason, blocker, work.Consumed);
}
