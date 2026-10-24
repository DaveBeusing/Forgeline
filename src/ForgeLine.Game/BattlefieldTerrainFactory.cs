using ForgeLine.World;

namespace ForgeLine.Game;

public static class BattlefieldTerrainFactory
{
    public static TerrainWorld Create(
        BattlefieldDefinition definition,
        BattlefieldTerrainRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(definition);

        recipe.Validate(definition.Metadata);
        WorldGridSettings resolved = recipe.Grid;
        resolved.Validate();

        int chunksX = CheckedChunkCount(
            definition.Metadata.WidthMeters,
            resolved.ChunkSizeMeters,
            nameof(definition.Metadata.WidthMeters));
        int chunksZ = CheckedChunkCount(
            definition.Metadata.HeightMeters,
            resolved.ChunkSizeMeters,
            nameof(definition.Metadata.HeightMeters));

        var chunks =
            new List<TerrainChunk>(
                checked(chunksX * chunksZ));

        for (int chunkZ = 0; chunkZ < chunksZ; chunkZ++)
        {
            for (int chunkX = 0; chunkX < chunksX; chunkX++)
            {
                ChunkCoordinate coordinate =
                    new(chunkX, chunkZ);
                chunks.Add(
                    CreateChunk(
                        coordinate,
                        resolved, recipe));
            }
        }

        return new TerrainWorld(
            resolved,
            chunks);
    }

    private static TerrainChunk CreateChunk(
        ChunkCoordinate coordinate,
        WorldGridSettings settings, BattlefieldTerrainRecipe recipe)
    {
        int sampleCount = settings.HeightSamplesPerSide;
        int intervals = sampleCount - 1;
        float[] heights =
            new float[checked(sampleCount * sampleCount)];
        double spacing =
            (double)settings.ChunkSizeMeters / intervals;

        for (int sampleZ = 0; sampleZ < sampleCount; sampleZ++)
        {
            long globalZ =
                (long)coordinate.Z * intervals + sampleZ;
            double worldZ = globalZ * spacing;

            for (int sampleX = 0; sampleX < sampleCount; sampleX++)
            {
                long globalX =
                    (long)coordinate.X * intervals + sampleX;
                double worldX = globalX * spacing;

                heights[sampleZ * sampleCount + sampleX] =
                    recipe.Evaluate(
                        worldX,
                        worldZ);
            }
        }

        return new TerrainChunk(
            coordinate,
            new TerrainHeightfield(
                sampleCount,
                settings.ChunkSizeMeters,
                heights));
    }

    private static int CheckedChunkCount(
        float extent,
        float chunkSize,
        string parameterName)
    {
        float ratio = extent / chunkSize;
        float rounded = MathF.Round(ratio);

        if (rounded < 1.0f ||
            MathF.Abs(ratio - rounded) > 0.0001f)
        {
            throw new InvalidOperationException(
                $"{parameterName} must be an exact multiple of the world chunk size.");
        }

        return checked((int)rounded);
    }
}
