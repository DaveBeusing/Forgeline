using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

/// <summary>
/// Reads original immutable bilinear heightfields. Point-sampled grid normals
/// alone cannot certify the terrain below a complete vehicle footprint.
/// </summary>
internal sealed class NavigationTerrainEnvelope
{
    private readonly TerrainWorld _terrain;

    public NavigationTerrainEnvelope(TerrainWorld terrain)
    {
        _terrain = new TerrainWorld(terrain.Settings, terrain.Chunks);
    }

    public NavigationGeometryStatus Check(in NavigationCollisionBox swept, in NavigationSweep movement,
        float from, float to, double groundOffset, float maximumSlopeDegrees, ref NavigationGeometryWork work)
    {
        AxisAlignedBounds bounds = swept.Bounds;
        float chunkSize = _terrain.Settings.ChunkSizeMeters;
        if (!TryChunk(bounds.Minimum.X, chunkSize, out int firstX) ||
            !TryChunk(MathF.BitDecrement(bounds.Maximum.X), chunkSize, out int lastX) ||
            !TryChunk(bounds.Minimum.Z, chunkSize, out int firstZ) ||
            !TryChunk(MathF.BitDecrement(bounds.Maximum.Z), chunkSize, out int lastZ) ||
            firstX > lastX || firstZ > lastZ)
            return NavigationGeometryStatus.Unavailable;

        double maximumGradient = Math.Tan(maximumSlopeDegrees * (Math.PI / 180));
        double maximumGradientSquared = maximumGradient * maximumGradient;
        for (long z = firstZ; z <= lastZ; z++)
        {
            for (long x = firstX; x <= lastX; x++)
            {
                if (!work.TrySpend()) return NavigationGeometryStatus.BudgetExhausted;
                if (!_terrain.TryGetChunk(new ChunkCoordinate((int)x, (int)z), out TerrainChunk chunk))
                    return NavigationGeometryStatus.Unavailable;
                NavigationGeometryStatus cells = CheckCells(swept, bounds, chunk, movement,
                    from, to, groundOffset, maximumGradientSquared, ref work);
                if (cells != NavigationGeometryStatus.Clear) return cells;
                NavigationGeometryStatus seams = CheckSeams(bounds, chunk, ref work);
                if (seams != NavigationGeometryStatus.Clear) return seams;
            }
        }
        return NavigationGeometryStatus.Clear;
    }

    private static NavigationGeometryStatus CheckCells(in NavigationCollisionBox swept,
        in AxisAlignedBounds bounds, TerrainChunk chunk, in NavigationSweep movement,
        float from, float to, double groundOffset, double maximumGradientSquared, ref NavigationGeometryWork work)
    {
        TerrainHeightfield heights = chunk.Heightfield;
        double spacing = heights.SampleSpacing;
        GetCells(bounds.Minimum.X - chunk.Bounds.Minimum.X, bounds.Maximum.X - chunk.Bounds.Minimum.X,
            spacing, heights.SamplesPerSide, out int firstX, out int lastX);
        GetCells(bounds.Minimum.Z - chunk.Bounds.Minimum.Z, bounds.Maximum.Z - chunk.Bounds.Minimum.Z,
            spacing, heights.SamplesPerSide, out int firstZ, out int lastZ);
        for (int z = firstZ; z <= lastZ; z++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                if (!work.TrySpend()) return NavigationGeometryStatus.BudgetExhausted;
                Vector3 center = new(
                    (float)(chunk.Bounds.Minimum.X + (x + 0.5) * spacing),
                    swept.Center.Y,
                    (float)(chunk.Bounds.Minimum.Z + (z + 0.5) * spacing));
                // The prism contains the entire body's Y range, so this is an
                // oriented projected-footprint test, not a center-cell test.
                var cell = NavigationCollisionBox.AxisAligned(center,
                    new Vector3((float)(spacing * 0.5), bounds.Extents.Y + 1, (float)(spacing * 0.5)));
                if (!NavigationCollisionBox.Intersects(swept, cell)) continue;

                double h00 = heights.GetHeight(x, z);
                double h10 = heights.GetHeight(x + 1, z);
                double h01 = heights.GetHeight(x, z + 1);
                double h11 = heights.GetHeight(x + 1, z + 1);
                // Each derivative of a bilinear cell varies linearly between
                // the opposite edges. Their independent maxima give a safe
                // upper bound throughout the touched cell, including its interior.
                double dx = Math.Max(Math.Abs(h10 - h00), Math.Abs(h11 - h01)) / spacing;
                double dz = Math.Max(Math.Abs(h01 - h00), Math.Abs(h11 - h10)) / spacing;
                if (!double.IsFinite(dx) || !double.IsFinite(dz) ||
                    dx * dx + dz * dz > maximumGradientSquared)
                    return NavigationGeometryStatus.Blocked;
                if (!GroundedThroughCell(movement, from, to, groundOffset,
                    chunk.Bounds.Minimum.X + x * spacing, chunk.Bounds.Minimum.Z + z * spacing,
                    spacing, h00, h10, h01, h11))
                    return NavigationGeometryStatus.Blocked;
            }
        }
        return NavigationGeometryStatus.Clear;
    }

    private static bool GroundedThroughCell(in NavigationSweep movement, double from, double to,
        double groundOffset, double cellX, double cellZ, double spacing,
        double h00, double h10, double h01, double h11)
    {
        Vector3 start = movement.Start.Position;
        Vector3 end = movement.End.Position;
        double dx = (double)end.X - start.X;
        double dz = (double)end.Z - start.Z;
        if (!Clip(start.X, dx, cellX, cellX + spacing, ref from, ref to) ||
            !Clip(start.Z, dz, cellZ, cellZ + spacing, ref from, ref to)) return true;

        // On a bilinear native cell, height along a straight XZ segment is a
        // quadratic. Check its exact residual against the proposed linear Y,
        // including the interior extremum. Endpoint samples miss gentle humps
        // and valleys even when every native cell satisfies the slope limit.
        double x = (start.X + dx * from - cellX) / spacing;
        double z = (start.Z + dz * from - cellZ) / spacing;
        double vx = dx / spacing;
        double vz = dz / spacing;
        double dy = (double)end.Y - start.Y;
        double hx = h10 - h00;
        double hz = h01 - h00;
        double hxz = h11 - h10 - h01 + h00;
        double constant = start.Y + dy * from - groundOffset - h00 - hx * x - hz * z - hxz * x * z;
        double linear = dy - hx * vx - hz * vz - hxz * (x * vz + vx * z);
        double quadratic = -hxz * vx * vz;
        double length = to - from;
        double tolerance = Math.Max(NavigationCollisionBox.NumericalPadding(start, 0),
            NavigationCollisionBox.NumericalPadding(end, 0));
        if (!double.IsFinite(constant) || !double.IsFinite(linear) || !double.IsFinite(quadratic) ||
            Math.Abs(constant) > tolerance ||
            Math.Abs((quadratic * length + linear) * length + constant) > tolerance) return false;
        if (quadratic == 0) return true;
        double extremum = -linear / (2 * quadratic);
        return extremum <= 0 || extremum >= length ||
            Math.Abs((quadratic * extremum + linear) * extremum + constant) <= tolerance;
    }

    private static bool Clip(double origin, double direction, double minimum, double maximum,
        ref double from, ref double to)
    {
        if (direction == 0) return origin >= minimum && origin <= maximum;
        double first = (minimum - origin) / direction;
        double last = (maximum - origin) / direction;
        if (first > last) (first, last) = (last, first);
        from = Math.Max(from, first);
        to = Math.Min(to, last);
        return from <= to;
    }

    private NavigationGeometryStatus CheckSeams(in AxisAlignedBounds footprint, TerrainChunk chunk,
        ref NavigationGeometryWork work)
    {
        if (footprint.Minimum.X < chunk.Bounds.Maximum.X && footprint.Maximum.X > chunk.Bounds.Maximum.X)
        {
            if (!work.TrySpend()) return NavigationGeometryStatus.BudgetExhausted;
            if (chunk.Coordinate.X == int.MaxValue ||
                !_terrain.TryGetChunk(new ChunkCoordinate(chunk.Coordinate.X + 1, chunk.Coordinate.Z), out TerrainChunk right))
                return NavigationGeometryStatus.Unavailable;
            double from = Math.Max(0, footprint.Minimum.Z - chunk.Bounds.Minimum.Z);
            double to = Math.Min(chunk.Heightfield.ChunkSizeMeters, footprint.Maximum.Z - chunk.Bounds.Minimum.Z);
            NavigationGeometryStatus status = CheckSeam(chunk.Heightfield, right.Heightfield,
                alongZ: true, from, to, ref work);
            if (status != NavigationGeometryStatus.Clear) return status;
        }
        if (footprint.Minimum.Z < chunk.Bounds.Maximum.Z && footprint.Maximum.Z > chunk.Bounds.Maximum.Z)
        {
            if (!work.TrySpend()) return NavigationGeometryStatus.BudgetExhausted;
            if (chunk.Coordinate.Z == int.MaxValue ||
                !_terrain.TryGetChunk(new ChunkCoordinate(chunk.Coordinate.X, chunk.Coordinate.Z + 1), out TerrainChunk forward))
                return NavigationGeometryStatus.Unavailable;
            double from = Math.Max(0, footprint.Minimum.X - chunk.Bounds.Minimum.X);
            double to = Math.Min(chunk.Heightfield.ChunkSizeMeters, footprint.Maximum.X - chunk.Bounds.Minimum.X);
            NavigationGeometryStatus status = CheckSeam(chunk.Heightfield, forward.Heightfield,
                alongZ: false, from, to, ref work);
            if (status != NavigationGeometryStatus.Clear) return status;
        }
        return NavigationGeometryStatus.Clear;
    }

    private static NavigationGeometryStatus CheckSeam(TerrainHeightfield first, TerrainHeightfield second,
        bool alongZ, double from, double to, ref NavigationGeometryWork work)
    {
        if (to <= from) return NavigationGeometryStatus.Clear;
        NavigationGeometryStatus status = CheckSeamPoint(first, second, alongZ, (float)from, ref work);
        if (status != NavigationGeometryStatus.Clear) return status;
        status = CheckSeamPoint(first, second, alongZ, (float)to, ref work);
        if (status != NavigationGeometryStatus.Clear) return status;
        // Chunks may use different sample counts. Check the union of both sets
        // of edge knots: between these knots the height difference is linear.
        status = CheckSeamKnots(first, second, first.SampleSpacing, alongZ, from, to, ref work);
        return status == NavigationGeometryStatus.Clear
            ? CheckSeamKnots(first, second, second.SampleSpacing, alongZ, from, to, ref work)
            : status;
    }

    private static NavigationGeometryStatus CheckSeamKnots(TerrainHeightfield first, TerrainHeightfield second,
        double spacing, bool alongZ, double from, double to, ref NavigationGeometryWork work)
    {
        int firstKnot = (int)Math.Floor(from / spacing) + 1;
        int lastKnot = (int)Math.Ceiling(to / spacing) - 1;
        for (int knot = firstKnot; knot <= lastKnot; knot++)
        {
            NavigationGeometryStatus status = CheckSeamPoint(first, second, alongZ, (float)(knot * spacing), ref work);
            if (status != NavigationGeometryStatus.Clear) return status;
        }
        return NavigationGeometryStatus.Clear;
    }

    private static NavigationGeometryStatus CheckSeamPoint(TerrainHeightfield first, TerrainHeightfield second,
        bool alongZ, float position, ref NavigationGeometryWork work)
    {
        if (!work.TrySpend()) return NavigationGeometryStatus.BudgetExhausted;
        float leftHeight = alongZ ? first.SampleHeight(first.ChunkSizeMeters, position)
            : first.SampleHeight(position, first.ChunkSizeMeters);
        float rightHeight = alongZ ? second.SampleHeight(0, position) : second.SampleHeight(position, 0);
        double tolerance = (Math.Max(Math.Abs(leftHeight), Math.Abs(rightHeight)) + 1) * 2e-6;
        return Math.Abs((double)leftHeight - rightHeight) <= tolerance
            ? NavigationGeometryStatus.Clear : NavigationGeometryStatus.Blocked;
    }

    private static void GetCells(double minimum, double maximum, double spacing, int samples,
        out int first, out int last)
    {
        first = (int)Math.Clamp(Math.Floor(minimum / spacing), 0, samples - 2);
        last = (int)Math.Clamp(Math.Ceiling(maximum / spacing) - 1, 0, samples - 2);
    }

    private static bool TryChunk(float position, float chunkSize, out int coordinate)
    {
        double value = Math.Floor((double)position / chunkSize);
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            coordinate = 0;
            return false;
        }
        coordinate = (int)value;
        return true;
    }
}
