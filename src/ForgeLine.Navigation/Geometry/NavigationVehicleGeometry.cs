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
    public const int AlgorithmRevision = 6;

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

        IReadOnlyList<NavigationCollisionBody> candidates = frame.Bodies;
        if (movement.PrefilterFixedBodies && frame.BodyPosesAreFixed)
        {
            // Union the exact interval envelopes before scanning fixed bodies. A
            // remote body is charged once rather than once per terrain interval.
            // Keep the original interval checks and every nearby physical veto.
            Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
            float previous = 0;
            for (int interval = 0; interval < _maximumSweepIntervals && interval < intervals; interval++)
            {
                if (!work.TrySpend())
                    return Result(frame, NavigationGeometryStatus.BudgetExhausted, 0, NavigationBlockReason.SearchWork, null, work);
                float fraction = (float)Math.Min(1, (interval + 1.0) / intervals);
                if (fraction <= previous)
                    return Result(frame, NavigationGeometryStatus.BudgetExhausted, 0, NavigationBlockReason.SearchWork, null, work);
                var envelope = Enclose(movement, startRotation, endRotation, angle, radius, previous, fraction);
                envelope = envelope with { HalfExtents = envelope.HalfExtents + new Vector3(movement.Vehicle.ClearanceMeters) };
                if (!Finite(envelope.Center) || !Finite(envelope.HalfExtents) || !FiniteBounds(envelope))
                    return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
                var bounds = envelope.Bounds;
                minimum = Vector3.Min(minimum, bounds.Minimum);
                maximum = Vector3.Max(maximum, bounds.Maximum);
                previous = fraction;
            }
            var relevant = new List<NavigationCollisionBody>();
            foreach (var body in frame.Bodies)
            {
                if (!work.TrySpend())
                    return Result(frame, NavigationGeometryStatus.BudgetExhausted, 0, NavigationBlockReason.SearchWork, null, work);
                if (body.Identity == movement.Mover) continue;
                if (!TryObstacleEnvelope(body, true, movement.DurationSeconds, 0, 1, out var obstacle))
                    return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
                var bounds = obstacle.Bounds;
                if (bounds.Maximum.X < minimum.X || bounds.Minimum.X > maximum.X ||
                    bounds.Maximum.Y < minimum.Y || bounds.Minimum.Y > maximum.Y ||
                    bounds.Maximum.Z < minimum.Z || bounds.Minimum.Z > maximum.Z) continue;
                relevant.Add(body);
            }
            candidates = relevant;
        }

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
            // Terrain and coverage keep the conservative whole clearance envelope.
            var terrainEnvelope = swept with { HalfExtents = swept.HalfExtents + new Vector3(movement.Vehicle.ClearanceMeters) };
            if (!Finite(terrainEnvelope.HalfExtents) || !FiniteBounds(terrainEnvelope))
                return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
            AxisAlignedBounds bounds = terrainEnvelope.Bounds;
            if (!ContainsHorizontal(_snapshot.WorldBounds, bounds))
                return Obstructed(frame, approved, NavigationBlockReason.OutsideWorld, null, work);
            if (!Contains(frame.Coverage, bounds))
                return Result(frame, NavigationGeometryStatus.Unavailable, 0, NavigationBlockReason.MissingGeometry, null, work);

            NavigationObstacleIdentity? blocker = null;
            NavigationBlockReason blockReason = NavigationBlockReason.None;
            foreach (NavigationCollisionBody body in candidates)
            {
                if (!work.TrySpend())
                    return Result(frame, NavigationGeometryStatus.BudgetExhausted, approved, NavigationBlockReason.SearchWork, null, work);
                if (body.Identity == movement.Mover) continue;
                if (!TryObstacleEnvelope(body, frame.BodyPosesAreFixed, movement.DurationSeconds, approved, endFraction, out NavigationCollisionBox obstacle))
                    return Result(frame, NavigationGeometryStatus.InvalidInput, 0, NavigationBlockReason.Unknown, null, work);
                if (!NavigationCollisionBox.Intersects(swept, obstacle, movement.Vehicle.ClearanceMeters)) continue;
                if (frame.BodyPosesAreFixed && SeparatesTranslatedEnvelope(movement, startRotation, angle, radius,
                    approved, endFraction, obstacle)) continue;
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

            NavigationGeometryStatus terrainStatus = _terrain.Check(terrainEnvelope, movement,
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

    // A rounded midpoint can unnecessarily close a narrow but valid parallel gap.
    // Bound the whole translation directly on each SAT axis in double precision.
    // Rotation remains enclosed around the starting body, with the same numerical
    // padding and physical clearance; an endpoint alone never authorizes motion.
    private static bool SeparatesTranslatedEnvelope(in NavigationSweep movement, Quaternion startRotation,
        double angle, double radius, float from, float to, in NavigationCollisionBox obstacle)
    {
        float numerical = MathF.Max(NavigationCollisionBox.NumericalPadding(movement.Start.Position, radius),
            NavigationCollisionBox.NumericalPadding(movement.End.Position, radius));
        double paddedRadius = radius + Math.Sqrt(3) * numerical;
        float expansion = (float)(2 * paddedRadius * Math.Sin(angle * .5)) + numerical;
        var box = NavigationCollisionBox.At(movement.Start with { Rotation = startRotation },
            movement.Vehicle.HalfExtents + new Vector3(expansion));
        Span<Vector3> axes = stackalloc Vector3[15]
        {
            box.AxisX, box.AxisY, box.AxisZ, obstacle.AxisX, obstacle.AxisY, obstacle.AxisZ,
            Vector3.Cross(box.AxisX, obstacle.AxisX), Vector3.Cross(box.AxisX, obstacle.AxisY), Vector3.Cross(box.AxisX, obstacle.AxisZ),
            Vector3.Cross(box.AxisY, obstacle.AxisX), Vector3.Cross(box.AxisY, obstacle.AxisY), Vector3.Cross(box.AxisY, obstacle.AxisZ),
            Vector3.Cross(box.AxisZ, obstacle.AxisX), Vector3.Cross(box.AxisZ, obstacle.AxisY), Vector3.Cross(box.AxisZ, obstacle.AxisZ)
        };
        foreach (var axis in axes)
        {
            if (axis.LengthSquared() <= 1e-12f) continue;
            double origin = ((double)obstacle.Center.X - movement.Start.Position.X) * axis.X +
                ((double)obstacle.Center.Y - movement.Start.Position.Y) * axis.Y +
                ((double)obstacle.Center.Z - movement.Start.Position.Z) * axis.Z;
            double travel = ((double)movement.End.Position.X - movement.Start.Position.X) * axis.X +
                ((double)movement.End.Position.Y - movement.Start.Position.Y) * axis.Y +
                ((double)movement.End.Position.Z - movement.Start.Position.Z) * axis.Z;
            double first = origin - travel * from, last = origin - travel * to;
            double distance = Math.Sign(first) == Math.Sign(last) ? Math.Min(Math.Abs(first), Math.Abs(last)) : 0;
            double extent = Projection(box, axis) + Projection(obstacle, axis) +
                movement.Vehicle.ClearanceMeters * Math.Sqrt(NavigationCollisionBox.Dot(axis, axis));
            if (distance > extent + (distance + extent + 1) * 2e-6) return true;
        }
        return false;

        static double Projection(in NavigationCollisionBox body, Vector3 axis) =>
            Math.Abs(NavigationCollisionBox.Dot(body.AxisX, axis)) * body.HalfExtents.X +
            Math.Abs(NavigationCollisionBox.Dot(body.AxisY, axis)) * body.HalfExtents.Y +
            Math.Abs(NavigationCollisionBox.Dot(body.AxisZ, axis)) * body.HalfExtents.Z;
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
        float numericalPadding = MathF.Max(NavigationCollisionBox.NumericalPadding(pose.Position, radius),
            MathF.Max(NavigationCollisionBox.NumericalPadding(movement.Start.Position, radius),
                NavigationCollisionBox.NumericalPadding(movement.End.Position, radius)));
        double paddedRadius = radius + Math.Sqrt(3) * numericalPadding;
        float rotationExpansion = (float)(2 * paddedRadius * Math.Sin(angle * (to - from) * 0.25));
        float padding = rotationExpansion + numericalPadding;
        // A rounded midpoint can shift the whole enclosure toward one endpoint.
        // Preserve the numerical envelope of the returned poses as well as the
        // physical body: its existing padding cannot also pay for that shift.
        Vector3 first = Vector3.Lerp(movement.Start.Position, movement.End.Position, from);
        Vector3 last = Vector3.Lerp(movement.Start.Position, movement.End.Position, to);
        double exactMiddle = ((double)from + to) * .5;
        double exactHalf = ((double)to - from) * .5;
        double errorX = InterpolationRoundoff(movement.Start.Position.X, movement.End.Position.X,
            pose.Position.X, halfTranslation.X, first.X, last.X, from, to, exactMiddle, exactHalf);
        double errorY = InterpolationRoundoff(movement.Start.Position.Y, movement.End.Position.Y,
            pose.Position.Y, halfTranslation.Y, first.Y, last.Y, from, to, exactMiddle, exactHalf);
        double errorZ = InterpolationRoundoff(movement.Start.Position.Z, movement.End.Position.Z,
            pose.Position.Z, halfTranslation.Z, first.Z, last.Z, from, to, exactMiddle, exactHalf);
        Vector3 extent = box.HalfExtents + new Vector3(
            (float)Math.Abs(NavigationCollisionBox.Dot(halfTranslation, box.AxisX)),
            (float)Math.Abs(NavigationCollisionBox.Dot(halfTranslation, box.AxisY)),
            (float)Math.Abs(NavigationCollisionBox.Dot(halfTranslation, box.AxisZ))) + new Vector3(padding);
        extent = new(
            AddRoundoff(extent.X, box.AxisX, errorX, errorY, errorZ),
            AddRoundoff(extent.Y, box.AxisY, errorX, errorY, errorZ),
            AddRoundoff(extent.Z, box.AxisZ, errorX, errorY, errorZ));
        return box with { HalfExtents = extent };
    }

    private static double InterpolationRoundoff(float start, float end, float middle, float half,
        float first, float last, float from, float to, double exactMiddle, double exactHalf)
    {
        double delta = (double)end - start;
        return Math.Abs(middle - (start + delta * exactMiddle)) +
            Math.Abs(half - delta * exactHalf) +
            Math.Max(Math.Abs(first - (start + delta * from)), Math.Abs(last - (start + delta * to)));
    }

    private static float AddRoundoff(float extent, Vector3 axis, double x, double y, double z)
    {
        double error = Math.Abs(axis.X) * x + Math.Abs(axis.Y) * y + Math.Abs(axis.Z) * z;
        return error > 0 ? MathF.BitIncrement((float)(extent + error)) : extent;
    }

    private static bool TryObstacleEnvelope(in NavigationCollisionBody body, bool poseFixed, float duration,
        float from, float to, out NavigationCollisionBox box)
    {
        bool moving = !poseFixed && (body.Kind == NavigationObstacleKind.Vehicle || body.Velocity != Vector3.Zero);
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
