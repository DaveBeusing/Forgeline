using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ForgeLine.World;

public sealed record ProceduralTerrainDiagnostics(int ChunkCount, long StoredSampleCount,
    float MinimumHeightMeters, float MaximumHeightMeters, double MaximumSampleGradient,
    long SeamMismatchCount, double GenerationMilliseconds, string TerrainSha256);

public sealed record ProceduralTerrainOutput(TerrainWorld Terrain, ProceduralTerrainDiagnostics Diagnostics);

public static class ProceduralTerrainFactory
{
    public static ProceduralTerrainOutput Create(ProceduralTerrainRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate();
        var timer = Stopwatch.StartNew();
        var generation = recipe.Generation;
        var evaluator = new ProceduralTerrainEvaluator(recipe);
        var chunks = new List<TerrainChunk>(generation.WidthInChunks * generation.HeightInChunks);
        for (int z = 0; z < generation.HeightInChunks; z++)
        for (int x = 0; x < generation.WidthInChunks; x++)
            chunks.Add(CreateChunk(evaluator, new(generation.Origin.X + x, generation.Origin.Z + z)));
        var terrain = new TerrainWorld(new WorldGridSettings { RegionSizeInChunks = generation.RegionSizeInChunks }, chunks);
        var diagnostics = Analyze(recipe, terrain);
        timer.Stop();
        return new(terrain, diagnostics with { GenerationMilliseconds = timer.Elapsed.TotalMilliseconds });
    }

    public static TerrainChunk CreateChunk(ProceduralTerrainRecipe recipe, ChunkCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var evaluator = new ProceduralTerrainEvaluator(recipe);
        var grid = recipe.Generation;
        if (coordinate.X < grid.Origin.X || coordinate.Z < grid.Origin.Z ||
            (long)coordinate.X >= (long)grid.Origin.X + grid.WidthInChunks ||
            (long)coordinate.Z >= (long)grid.Origin.Z + grid.HeightInChunks)
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return CreateChunk(evaluator, coordinate);
    }

    private static TerrainChunk CreateChunk(ProceduralTerrainEvaluator evaluator, ChunkCoordinate coordinate)
    {
        float[] heights = new float[65 * 65];
        for (int z = 0; z < 65; z++)
        for (int x = 0; x < 65; x++)
            heights[z * 65 + x] = evaluator.Evaluate(WorldSampleCoordinate.FromChunk(coordinate, x, z, 65)).HeightMillimeters / 1000f;
        return new(coordinate, new TerrainHeightfield(65, 256, heights));
    }

    private static ProceduralTerrainDiagnostics Analyze(ProceduralTerrainRecipe recipe, TerrainWorld terrain)
    {
        // Explicit parameter bytes precede sorted chunk coordinates and row-major IEEE height bits.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var header = new WorldCanonicalWriter();
        header.WriteUInt32(0x314E544C); // LTN1, terrain diagnostic identity, separate from gameplay identity.
        header.WriteUInt32(recipe.Version);
        header.WriteUInt64(recipe.Generation.Seed);
        header.WriteInt32(recipe.BaseHeightMillimeters);
        header.WriteInt32(recipe.HillHeightMillimeters);
        header.WriteInt32(recipe.RidgeHeightMillimeters);
        header.WriteInt32(recipe.DetailHeightMillimeters);
        header.WriteInt32(recipe.DrainageDepthMillimeters);
        header.WriteInt32(recipe.FlatStrengthPermille);
        hash.AppendData(header.ToArray());
        Span<byte> bytes = stackalloc byte[8];
        double maximumGradient = 0;
        long seams = 0;
        foreach (var chunk in terrain.Chunks)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, chunk.Coordinate.X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], chunk.Coordinate.Z);
            hash.AppendData(bytes);
            var field = chunk.Heightfield;
            terrain.TryGetChunk(new(chunk.Coordinate.X + 1, chunk.Coordinate.Z), out var right);
            terrain.TryGetChunk(new(chunk.Coordinate.X, chunk.Coordinate.Z + 1), out var bottom);
            for (int z = 0; z < 65; z++)
            for (int x = 0; x < 65; x++)
            {
                float h = field.GetHeight(x, z);
                BinaryPrimitives.WriteSingleLittleEndian(bytes, h);
                hash.AppendData(bytes[..4]);
                if (x < 64 && z < 64)
                {
                    // Bilinear-cell partials attain extrema on opposite cell edges.
                    double dx = Math.Max(Math.Abs(field.GetHeight(x + 1, z) - h),
                        Math.Abs(field.GetHeight(x + 1, z + 1) - field.GetHeight(x, z + 1))) / 4;
                    double dz = Math.Max(Math.Abs(field.GetHeight(x, z + 1) - h),
                        Math.Abs(field.GetHeight(x + 1, z + 1) - field.GetHeight(x + 1, z))) / 4;
                    maximumGradient = Math.Max(maximumGradient, Math.Sqrt(dx * dx + dz * dz));
                }
                if (x == 64 && right is not null && BitConverter.SingleToInt32Bits(h) != BitConverter.SingleToInt32Bits(right.Heightfield.GetHeight(0, z))) seams++;
                if (z == 64 && bottom is not null && BitConverter.SingleToInt32Bits(h) != BitConverter.SingleToInt32Bits(bottom.Heightfield.GetHeight(x, 0))) seams++;
            }
        }
        return new(terrain.Chunks.Count, (long)terrain.Chunks.Count * 65 * 65,
            terrain.WorldBounds.Minimum.Y, terrain.WorldBounds.Maximum.Y, maximumGradient,
            seams, 0, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
