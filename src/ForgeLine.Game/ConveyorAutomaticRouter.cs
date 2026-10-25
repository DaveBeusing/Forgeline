using System.Numerics;

namespace ForgeLine.Game;

/// <summary>Bounded, deterministic geometry proposals; construction remains the placement authority.</summary>
internal static class ConveyorAutomaticRouter
{
    internal const int MaximumCandidates = 72;
    internal const float MinimumSpanMeters = 1;
    private const float Tolerance = .001f;
    private static readonly float[] s_stubLengths = [4, 12, 24, 40];

    internal static IReadOnlyList<ConveyorRouteCandidate> CreateCandidates(
        Vector3 start, Vector3 end, Vector3? startDirection, Vector3? endDirection,
        int tier, bool xFirst, float maximumLength)
    {
        var candidates = new List<ConveyorRouteCandidate>();
        var paths = new List<Vector3[]>();
        Add([start, end]);
        AddElbows(start, end, [], []);

        Vector3 heading = startDirection ?? PreferredDirection(end - start, xFirst);
        Vector3 arrival = endDirection ?? PreferredDirection(end - start, !xFirst);
        foreach (float departureLength in s_stubLengths)
        {
            foreach (float arrivalLength in s_stubLengths)
            {
                Vector3 a = start + heading * departureLength;
                Vector3 b = end - arrival * arrivalLength;
                AddElbows(a, b, [start], [end]);
            }
        }

        // Outer corridors also handle a small lateral offset or ports facing away
        // from one another. Their finite extent is deliberate, not a global pathfinder.
        (float Departure, float Arrival)[] detourStubs =
            [(4, 4), (12, 12), (24, 24), (40, 40), (4, 12), (12, 4), (12, 24), (24, 12)];
        foreach (var stub in detourStubs)
        {
            Vector3 a = start + heading * stub.Departure;
            Vector3 b = end - arrival * stub.Arrival;
            float minimumX = MathF.Min(a.X, b.X) - 12;
            float maximumX = MathF.Max(a.X, b.X) + 12;
            float minimumZ = MathF.Min(a.Z, b.Z) - 12;
            float maximumZ = MathF.Max(a.Z, b.Z) + 12;
            if (xFirst)
            {
                AcrossZ(minimumZ); AcrossZ(maximumZ);
                AcrossX(minimumX); AcrossX(maximumX);
            }
            else
            {
                AcrossX(minimumX); AcrossX(maximumX);
                AcrossZ(minimumZ); AcrossZ(maximumZ);
            }

            void AcrossX(float x) => Add([start, a, new(x, a.Y, a.Z), new(x, b.Y, b.Z), b, end]);
            void AcrossZ(float z) => Add([start, a, new(a.X, a.Y, z), new(b.X, b.Y, z), b, end]);
        }

        return candidates.OrderBy(candidate => candidate.LengthMeters)
            .ThenBy(candidate => candidate.Pieces.Count)
            .ThenBy(candidate => candidate.Order).ToArray();

        void AddElbows(Vector3 a, Vector3 b, Vector3[] prefix, Vector3[] suffix)
        {
            Vector3 xCorner = new(b.X, a.Y, a.Z);
            Vector3 zCorner = new(a.X, a.Y, b.Z);
            Add([.. prefix, a, xFirst ? xCorner : zCorner, b, .. suffix]);
            Add([.. prefix, a, xFirst ? zCorner : xCorner, b, .. suffix]);
        }

        void Add(Vector3[] proposed)
        {
            if (paths.Count >= MaximumCandidates) return;
            int order = paths.Count;
            paths.Add(proposed);
            if (!TryCreate(proposed, startDirection, endDirection, tier, maximumLength,
                    out ConveyorPlacement[] pieces, out float length)) return;
            if (candidates.Any(candidate => candidate.Pieces.SequenceEqual(pieces))) return;
            candidates.Add(new(pieces, length, order));
        }
    }

    internal static bool IsConnected(IReadOnlyList<ConveyorPlacement> pieces)
    {
        for (int i = 1; i < pieces.Count; i++)
        {
            ConveyorSocket output = pieces[i - 1].Sockets[^1];
            ConveyorSocket input = pieces[i].Sockets[0];
            if (output.Input || !input.Input ||
                Vector3.DistanceSquared(output.Position, input.Position) >= .0004f ||
                Vector3.Dot(output.Facing, input.Facing) > -.999f) return false;
        }
        return true;
    }

    internal static bool HasSelfIntersection(IReadOnlyList<ConveyorPlacement> pieces)
    {
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 2; j < pieces.Count; j++)
                if (ConveyorGeometry.Overlap(pieces[i].Bounds, pieces[j].Bounds)) return true;
        return false;
    }

    private static bool TryCreate(Vector3[] proposed, Vector3? startDirection, Vector3? endDirection,
        int tier, float maximumLength, out ConveyorPlacement[] result, out float length)
    {
        result = [];
        length = 0;
        var points = new List<Vector3>();
        foreach (Vector3 point in proposed)
        {
            if (points.Count != 0 && Vector3.DistanceSquared(points[^1], point) < Tolerance * Tolerance)
                continue;
            if (points.Count >= 2)
            {
                Vector3 previous = points[^1] - points[^2];
                Vector3 next = point - points[^1];
                if (!TryDirection(previous, out Vector3 priorHeading) ||
                    !TryDirection(next, out Vector3 nextHeading)) return false;
                float alignment = Vector3.Dot(priorHeading, nextHeading);
                if (alignment < -.999f) return false;
                if (alignment > .999f) points.RemoveAt(points.Count - 1);
            }
            points.Add(point);
        }
        if (points.Count < 2)
            return startDirection is not { } initial || endDirection is not { } final ||
                Vector3.Dot(initial, final) > .999f;

        var directions = new Vector3[points.Count - 1];
        for (int i = 0; i < directions.Length; i++)
        {
            if (!TryDirection(points[i + 1] - points[i], out directions[i])) return false;
            length += Vector3.Dot(points[i + 1] - points[i], directions[i]);
        }
        if (!float.IsFinite(length) || length > maximumLength + Tolerance ||
            startDirection is { } departure && Vector3.Dot(departure, directions[0]) < .999f ||
            endDirection is { } arrival && Vector3.Dot(arrival, directions[^1]) < .999f) return false;

        var pieces = new List<ConveyorPlacement>();
        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 direction = directions[i];
            Vector3 a = points[i] + (i == 0 ? Vector3.Zero : direction * 4);
            Vector3 b = points[i + 1] - (i == directions.Length - 1 ? Vector3.Zero : direction * 4);
            float span = Vector3.Dot(b - a, direction);
            if (span < -Tolerance || span > Tolerance && span < MinimumSpanMeters - Tolerance) return false;
            AddStraight(a, b, direction, tier, pieces);
            if (i == directions.Length - 1) continue;

            BuildingOrientation orientation = ConveyorGeometry.Facing(direction);
            Quaternion rotation = BuildingFootprint.GetRotation(orientation);
            bool left = Vector3.Dot(Vector3.Transform(Vector3.UnitX, rotation), directions[i + 1]) > 0;
            ConveyorModule module = left ? ConveyorModule.CurveLeft : ConveyorModule.CurveRight;
            Vector3 localInput = ConveyorGeometry.Sockets(module)[0].Position - new Vector3(0, 5.8f, 0);
            pieces.Add(new(module, b - Vector3.Transform(localInput, rotation), orientation, tier));
        }
        if (!IsConnected(pieces) || HasSelfIntersection(pieces)) return false;
        result = pieces.ToArray();
        return true;
    }

    private static void AddStraight(Vector3 start, Vector3 end, Vector3 direction, int tier,
        List<ConveyorPlacement> pieces)
    {
        float remaining = Vector3.Dot(end - start, direction);
        BuildingOrientation orientation = ConveyorGeometry.Facing(direction);
        while (remaining > Tolerance)
        {
            float span;
            if (remaining >= 8 - Tolerance &&
                (remaining <= 8 + Tolerance || remaining - 8 >= MinimumSpanMeters - Tolerance)) span = 8;
            else if (remaining > 4 + Tolerance)
                span = remaining - 4 >= MinimumSpanMeters - Tolerance ? 4 : remaining / 2;
            else span = remaining;
            // Normalize authored lengths, while preserving the real final endpoint.
            if (MathF.Abs(span - 1) < Tolerance) span = 1;
            if (MathF.Abs(span - 4) < Tolerance) span = 4;
            if (MathF.Abs(span - 8) < Tolerance) span = 8;
            ConveyorModule module = span == 8 ? ConveyorModule.Standard : ConveyorModule.ShortSegment;
            pieces.Add(new(module, start + direction * (span / 2), orientation, tier,
                SpanMeters: span is 4 or 8 ? 0 : span));
            start += direction * span;
            remaining = Vector3.Dot(end - start, direction);
        }
    }

    private static bool TryDirection(Vector3 delta, out Vector3 direction)
    {
        direction = default;
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Z) || MathF.Abs(delta.Y) > Tolerance ||
            MathF.Abs(delta.X) > Tolerance && MathF.Abs(delta.Z) > Tolerance) return false;
        if (MathF.Abs(delta.X) > Tolerance) direction = delta.X > 0 ? Vector3.UnitX : -Vector3.UnitX;
        else if (MathF.Abs(delta.Z) > Tolerance) direction = delta.Z > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
        return direction != default;
    }

    private static Vector3 PreferredDirection(Vector3 delta, bool xFirst) =>
        xFirst && MathF.Abs(delta.X) > Tolerance || MathF.Abs(delta.Z) <= Tolerance
            ? delta.X >= 0 ? Vector3.UnitX : -Vector3.UnitX
            : delta.Z >= 0 ? Vector3.UnitZ : -Vector3.UnitZ;
}

internal sealed record ConveyorRouteCandidate(IReadOnlyList<ConveyorPlacement> Pieces, float LengthMeters, int Order);
