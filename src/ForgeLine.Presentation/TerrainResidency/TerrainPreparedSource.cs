using System.Diagnostics;
using System.Numerics;
using ForgeLine.World;
using RenderVertex = ForgeLine.Presentation.TerrainRenderer.TerrainRenderVertex;

namespace ForgeLine.Presentation.TerrainResidency;

internal sealed record PreparedTerrainChunk(RenderVertex[] Vertices, TerrainChunkSplatData Splat,
    float[] HeightErrors, float[] NormalErrors, float[] TintErrors, bool ProtectedSurface, double MeshMilliseconds, double SplatMilliseconds)
{
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
                float spanX = 2 * step, spanZ = 2 * step;
                if (localX < step && Neighbour(index, -1, 0) < 0) spanX -= step - localX;
                if (localX + step > size && Neighbour(index, 1, 0) < 0) spanX -= localX + step - size;
                if (localZ < step && Neighbour(index, 0, -1) < 0) spanZ -= step - localZ;
                if (localZ + step > size && Neighbour(index, 0, 1) < 0) spanZ -= localZ + step - size;
                Vector3 normal = Vector3.Normalize(new((left - right) / spanX, 1, (back - forward) / spanZ));
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
