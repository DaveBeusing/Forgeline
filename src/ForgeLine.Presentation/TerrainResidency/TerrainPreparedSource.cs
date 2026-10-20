using System.Diagnostics;
using System.Numerics;
using ForgeLine.World;
using RenderVertex = ForgeLine.Presentation.TerrainRenderer.TerrainRenderVertex;

namespace ForgeLine.Presentation.TerrainResidency;

internal sealed record PreparedTerrainChunk(RenderVertex[] Vertices, TerrainChunkSplatData Splat,
    float[] HeightErrors, float[] NormalErrors, float[] TintErrors, bool ProtectedSurface, double MeshMilliseconds, double SplatMilliseconds)
{
    internal const long ControlPayloadBytes = 5720;
    internal long ControlBytes => Splat.Mips.Sum(static m => (long)m.Pixels.Length);
    internal long Bytes => (long)Vertices.Length * RenderVertex.SizeInBytes + ControlBytes +
        (HeightErrors.Length + NormalErrors.Length + TintErrors.Length) * sizeof(float);
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
        long payloadBytes = checked((long)_samples * _samples * RenderVertex.SizeInBytes +
            PreparedTerrainChunk.ControlPayloadBytes + Patterns.Ranges.Length * 3L * sizeof(float));
        if (payloadBytes > policy.MaximumChunkPreparationBytes)
            throw new ArgumentOutOfRangeException(nameof(policy), "Terrain vertex, control and error payload exceeds the per-chunk preparation limit.");
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
        float size = chunk.Heightfield.ChunkSizeMeters;
        for (int z = 0; z < _samples; z++)
        {
            cancellation.ThrowIfCancellationRequested(); AssetPreparationScope.ThrowIfStopped();
            float localZ = z / (float)(_samples - 1) * size;
            for (int x = 0; x < _samples; x++)
            {
                float localX = x / (float)(_samples - 1) * size;
                Vector3 position = new(chunk.Coordinate.X * size + localX,
                    chunk.Heightfield.SampleHeight(localX, localZ), chunk.Coordinate.Z * size + localZ);
                Vector3 normal = SampleNormal(index, chunk, localX, localZ);
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                    !float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
                    throw new InvalidDataException("Terrain interpolation produced a nonfinite render vertex.");
                float macro = TerrainRenderer.CalculateMacroVariation(position);
                vertices[z * _samples + x] = new(position, normal,
                    new(x / (float)(_samples - 1), z / (float)(_samples - 1)), new(macro, macro, macro, 1));
            }
        }
        var errors = new float[Patterns.Ranges.Length]; var normals = new float[errors.Length]; var tints = new float[errors.Length];
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
                    float tintOrigin = vertices[z * _samples + x].MacroTint.X;
                    float tintX = (vertices[z * _samples + x + stride].MacroTint.X - tintOrigin) / stride;
                    float tintZ = (vertices[(z + stride) * _samples + x].MacroTint.X - tintOrigin) / stride;
                    float tintLow = 0, tintHigh = 0;
                    Vector3 normalOrigin = vertices[z * _samples + x].Normal;
                    Vector3 normalX = (vertices[z * _samples + x + stride].Normal - normalOrigin) / stride;
                    Vector3 normalZ = (vertices[(z + stride) * _samples + x].Normal - normalOrigin) / stride;
                    Vector3 residualLow = Vector3.Zero, residualHigh = Vector3.Zero;
                    Vector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
                    for (int dz = 0; dz <= stride; dz++)
                        for (int dx = 0; dx <= stride; dx++)
                        {
                            RenderVertex v = vertices[(z + dz) * _samples + x + dx];
                            double residual = v.Position.Y - (origin + dx * sx + dz * sz);
                            min = Math.Min(min, residual); max = Math.Max(max, residual);
                            low = Vector3.Min(low, v.Normal); high = Vector3.Max(high, v.Normal);
                            Vector3 normalResidual = v.Normal - (normalOrigin + dx * normalX + dz * normalZ);
                            residualLow = Vector3.Min(residualLow, normalResidual); residualHigh = Vector3.Max(residualHigh, normalResidual);
                            float tintResidual = v.MacroTint.X - (tintOrigin + dx * tintX + dz * tintZ);
                            tintLow = Math.Min(tintLow, tintResidual); tintHigh = Math.Max(tintHigh, tintResidual);
                        }
                    // Subtracting an affine plane preserves interpolation. Both triangulations' residuals
                    // are convex combinations inside this range, including intersections between edges.
                    errors[level] = Math.Max(errors[level], MathF.BitIncrement((float)(max - min)));
                    float minimumNormalLength = .99999f - (high - low).Length();
                    float normalError = minimumNormalLength <= 0 ? float.PositiveInfinity :
                        2 * (residualHigh - residualLow).Length() / minimumNormalLength;
                    normals[level] = Math.Max(normals[level], MathF.BitIncrement(normalError));
                    tints[level] = Math.Max(tints[level], MathF.BitIncrement(tintHigh - tintLow));
                }
        }
        double meshTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        cancellation.ThrowIfCancellationRequested(); start = Stopwatch.GetTimestamp();
        TerrainChunkSplatData splat = TerrainSplatMapBuilder.Build(chunk, _profile);
        cancellation.ThrowIfCancellationRequested();
        bool protectedSurface = _profile.SurfaceMarkPriority(chunk.Bounds) != 0;
        for (int layer = 0; layer < splat.Palette.Count; layer++)
            if (splat.Palette[layer] is TerrainMaterialSlot.Gravel or TerrainMaterialSlot.Concrete or TerrainMaterialSlot.IndustrialGround)
                for (int pixel = layer; pixel < splat.Mips[0].Pixels.Length; pixel += 4)
                    protectedSurface |= splat.Mips[0].Pixels[pixel] >= 16;
        protectedSurface |= chunk.Bounds.Minimum.Y < 0 && chunk.Bounds.Maximum.Y >= 0;
        return new(vertices, splat, errors, normals, tints, protectedSurface, meshTime, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private Vector3 SampleNormal(int index, TerrainChunk chunk, float x, float z)
    {
        float size = chunk.Heightfield.ChunkSizeMeters, step = chunk.Heightfield.SampleSpacing;
        int left = x < step ? Neighbour(index, -1, 0) : -1;
        int right = x + step > size ? Neighbour(index, 1, 0) : -1;
        int back = z < step ? Neighbour(index, 0, -1) : -1;
        int forward = z + step > size ? Neighbour(index, 0, 1) : -1;
        // Keep the original derivative kernel and arithmetic wherever its window
        // does not cross an available neighbour, including outer map boundaries.
        if (left < 0 && right < 0 && back < 0 && forward < 0)
            return chunk.Heightfield.SampleNormal(x, z);
        if (x == 0 || x == size || z == 0 || z == size)
        {
            // Both sides choose the same spacing even for differently sampled
            // immutable heightfields. Corners include the diagonal source.
            for (int dz = z == 0 ? -1 : 0; dz <= (z == size ? 1 : 0); dz++)
                for (int dx = x == 0 ? -1 : 0; dx <= (x == size ? 1 : 0); dx++)
                {
                    int neighbour = Neighbour(index, dx, dz);
                    if (neighbour >= 0) step = Math.Min(step, Chunks[neighbour].Heightfield.SampleSpacing);
                }
        }
        float spanX = (right >= 0 ? x + step : Math.Min(x + step, size)) -
            (left >= 0 ? x - step : Math.Max(x - step, 0));
        float spanZ = (forward >= 0 ? z + step : Math.Min(z + step, size)) -
            (back >= 0 ? z - step : Math.Max(z - step, 0));
        float derivativeX = (Height(chunk, x + step, z) - Height(chunk, x - step, z)) / Math.Max(spanX, float.Epsilon);
        float derivativeZ = (Height(chunk, x, z + step) - Height(chunk, x, z - step)) / Math.Max(spanZ, float.Epsilon);
        return Vector3.Normalize(new(-derivativeX, 1, -derivativeZ));
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
