using System.Diagnostics;
using System.Numerics;
using ForgeLine.World;
using RenderVertex = ForgeLine.Presentation.TerrainRenderer.TerrainRenderVertex;

namespace ForgeLine.Presentation.TerrainResidency;

internal sealed record PreparedTerrainChunk(RenderVertex[] Vertices, TerrainChunkSplatData Splat,
    float[] HeightErrors, float[] NormalErrors, double MeshMilliseconds, double SplatMilliseconds)
{
    internal long ControlBytes => Splat.Mips.Sum(static m => (long)m.Pixels.Length);
    internal long Bytes => (long)Vertices.Length * RenderVertex.SizeInBytes + ControlBytes +
        (HeightErrors.Length + NormalErrors.Length) * sizeof(float);
}

/// <summary>One immutable source generation: copied chunk table, immutable heightfields, fixed mesh/profile.</summary>
internal sealed class TerrainPreparedSource
{
    internal TerrainChunk[] Chunks { get; }
    private readonly Dictionary<ChunkCoordinate, int> _coordinates;
    private readonly TerrainPresentationProfile _profile;
    private readonly int _samples;
    internal TerrainIndexPatterns Patterns { get; }

    internal TerrainPreparedSource(TerrainWorld world, TerrainMeshSettings settings,
        TerrainPresentationProfile profile, TerrainPreparationSettings policy)
    {
        Chunks = world.Chunks.ToArray(); _profile = profile; _samples = settings.VertexSamplesPerSide;
        Patterns = new(_samples, policy.MaximumChunkPreparationBytes);
        _coordinates = new(Chunks.Length);
        for (int i = 0; i < Chunks.Length; i++) _coordinates.Add(Chunks[i].Coordinate, i);
    }

    internal int Neighbour(int index, int dx, int dz)
    {
        ChunkCoordinate p = Chunks[index].Coordinate;
        long x = (long)p.X + dx, z = (long)p.Z + dz;
        return x is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue ? -1 :
            _coordinates.GetValueOrDefault(new((int)x, (int)z), -1);
    }

    internal PreparedTerrainChunk Build(int index, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        long start = Stopwatch.GetTimestamp();
        TerrainChunk chunk = Chunks[index];
        var vertices = new RenderVertex[checked(_samples * _samples)];
        float size = chunk.Heightfield.ChunkSizeMeters, step = size / (_samples - 1);
        for (int z = 0; z < _samples; z++)
        {
            cancellation.ThrowIfCancellationRequested(); AssetPreparationScope.ThrowIfStopped();
            float localZ = z / (float)(_samples - 1) * size;
            for (int x = 0; x < _samples; x++)
            {
                float localX = x / (float)(_samples - 1) * size;
                Vector3 position = new(chunk.Coordinate.X * size + localX,
                    chunk.Heightfield.SampleHeight(localX, localZ), chunk.Coordinate.Z * size + localZ);
                // Sample both sides of an immutable chunk boundary; both neighbours get the same normal.
                float left = Height(chunk, localX - step, localZ), right = Height(chunk, localX + step, localZ);
                float back = Height(chunk, localX, localZ - step), forward = Height(chunk, localX, localZ + step);
                Vector3 normal = Vector3.Normalize(new(left - right, 2 * step, back - forward));
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                    !float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
                    throw new InvalidDataException("Terrain interpolation produced a nonfinite render vertex.");
                float macro = TerrainRenderer.CalculateMacroVariation(position);
                vertices[z * _samples + x] = new(position, normal,
                    new(x / (float)(_samples - 1), z / (float)(_samples - 1)), new(macro, macro, macro, 1));
            }
        }
        var errors = new float[Patterns.Ranges.Length]; var normals = new float[errors.Length];
        for (int level = 1; level < errors.Length; level++)
        {
            int stride = Patterns.Ranges[level].Step;
            for (int z = 0; z < _samples - 1; z += stride)
                for (int x = 0; x < _samples - 1; x += stride)
                {
                    cancellation.ThrowIfCancellationRequested();
                    double origin = vertices[z * _samples + x].Position.Y;
                    double sx = (vertices[z * _samples + x + stride].Position.Y - origin) / stride;
                    double sz = (vertices[(z + stride) * _samples + x].Position.Y - origin) / stride;
                    double min = 0, max = 0;
                    Vector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
                    for (int dz = 0; dz <= stride; dz++)
                        for (int dx = 0; dx <= stride; dx++)
                        {
                            RenderVertex v = vertices[(z + dz) * _samples + x + dx];
                            double residual = v.Position.Y - (origin + dx * sx + dz * sz);
                            min = Math.Min(min, residual); max = Math.Max(max, residual);
                            low = Vector3.Min(low, v.Normal); high = Vector3.Max(high, v.Normal);
                        }
                    // Subtracting an affine plane preserves interpolation. Both triangulations' residuals
                    // are convex combinations inside this range, including intersections between edges.
                    errors[level] = Math.Max(errors[level], MathF.BitIncrement((float)(max - min)));
                    normals[level] = Math.Max(normals[level], (high - low).Length());
                }
        }
        double meshTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        cancellation.ThrowIfCancellationRequested(); start = Stopwatch.GetTimestamp();
        TerrainChunkSplatData splat = TerrainSplatMapBuilder.Build(chunk, _profile);
        cancellation.ThrowIfCancellationRequested();
        return new(vertices, splat, errors, normals, meshTime, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private float Height(TerrainChunk chunk, float x, float z)
    {
        float size = chunk.Heightfield.ChunkSizeMeters;
        int dx = x < 0 ? -1 : x > size ? 1 : 0, dz = z < 0 ? -1 : z > size ? 1 : 0;
        long cx = (long)chunk.Coordinate.X + dx, cz = (long)chunk.Coordinate.Z + dz;
        if (cx is >= int.MinValue and <= int.MaxValue && cz is >= int.MinValue and <= int.MaxValue &&
            _coordinates.TryGetValue(new((int)cx, (int)cz), out int neighbour))
            return Chunks[neighbour].Heightfield.SampleHeight(x - dx * size, z - dz * size);
        return chunk.Heightfield.SampleHeight(x, z);
    }
}
