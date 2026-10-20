namespace ForgeLine.Presentation.TerrainResidency;

internal readonly record struct TerrainIndexRange(int FirstIndex, int IndexCount, int Step);

/// <summary>All levels retain every original outer edge segment. No skirts or neighbour-dependent topology.</summary>
internal sealed class TerrainIndexPatterns
{
    internal uint[] Indices { get; private set; }
    internal TerrainIndexRange[] Ranges { get; }
    internal long Bytes { get; }
    internal long CpuBytes => (long)Indices.Length * sizeof(uint);
    internal void ReleaseCpuIndices() => Indices = [];

    internal TerrainIndexPatterns(int samples, long maximumChunkBytes)
    {
        if (samples < 2 || samples > Math.Sqrt(maximumChunkBytes / 48d))
            throw new ArgumentOutOfRangeException(nameof(samples));
        int cells = checked(samples - 1);
        long fullBytes = checked((long)samples * samples * 48 + (long)cells * cells * 6 * sizeof(uint));
        if (samples < 2 || fullBytes > maximumChunkBytes)
            throw new ArgumentOutOfRangeException(nameof(samples), "Terrain preparation exceeds its per-chunk byte limit.");
        var indices = new List<uint>();
        var ranges = new List<TerrainIndexRange>();
        for (int step = 1; step <= cells && cells % step == 0; step *= 2)
        {
            int first = indices.Count;
            for (int z = 0; z < cells; z += step)
                for (int x = 0; x < cells; x += step)
                {
                    if (step == 1)
                    {
                        Add(x, z); Add(x, z + 1); Add(x + 1, z);
                        Add(x + 1, z); Add(x, z + 1); Add(x + 1, z + 1);
                        continue;
                    }
                    // Clockwise in X/Z, hence positive Y triangle normals.
                    int cx = x + step / 2, cz = z + step / 2;
                    Edge(x, z, x, z + step, x == 0);
                    Edge(x, z + step, x + step, z + step, z + step == cells);
                    Edge(x + step, z + step, x + step, z, x + step == cells);
                    Edge(x + step, z, x, z, z == 0);
                    void Edge(int ax, int az, int bx, int bz, bool outer)
                    {
                        int segments = outer ? step : 1;
                        for (int i = 0; i < segments; i++)
                        {
                            Add(cx, cz);
                            Add(ax + (bx - ax) * i / segments, az + (bz - az) * i / segments);
                            Add(ax + (bx - ax) * (i + 1) / segments, az + (bz - az) * (i + 1) / segments);
                        }
                    }
                }
            ranges.Add(new(first, indices.Count - first, step));
        }
        Indices = indices.ToArray(); Ranges = ranges.ToArray(); Bytes = (long)Indices.Length * sizeof(uint);
        void Add(int x, int z) => indices.Add(checked((uint)(z * samples + x)));
    }
}
