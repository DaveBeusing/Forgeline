namespace ForgeLine.World;

public static class DevelopmentTerrainFactory
{
    public const int DefaultChunkRadius = 6;

    public static TerrainWorld CreateRepresentativeWorld(
        WorldGridSettings? settings = null,
        int chunkRadius = DefaultChunkRadius)
    {
        WorldGridSettings resolvedSettings = settings ?? new WorldGridSettings();
        resolvedSettings.Validate();

        if (chunkRadius < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkRadius));
        }

        int diameter = checked(chunkRadius * 2 + 1);
        var chunks = new List<TerrainChunk>(checked(diameter * diameter));

        for (int chunkZ = -chunkRadius; chunkZ <= chunkRadius; chunkZ++)
        {
            for (int chunkX = -chunkRadius; chunkX <= chunkRadius; chunkX++)
            {
                ChunkCoordinate coordinate = new(chunkX, chunkZ);
                chunks.Add(CreateChunk(coordinate, resolvedSettings));
            }
        }

        return new TerrainWorld(resolvedSettings, chunks);
    }

    public static TerrainChunk CreateChunk(
        ChunkCoordinate coordinate,
        WorldGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        int sampleCount = settings.HeightSamplesPerSide;
        int intervals = sampleCount - 1;
        float[] heights = new float[checked(sampleCount * sampleCount)];

        double sampleSpacing =
            (double)settings.ChunkSizeMeters / intervals;

        for (int sampleZ = 0; sampleZ < sampleCount; sampleZ++)
        {
            long globalSampleZ =
                (long)coordinate.Z * intervals + sampleZ;
            double worldZ = globalSampleZ * sampleSpacing;

            for (int sampleX = 0; sampleX < sampleCount; sampleX++)
            {
                long globalSampleX =
                    (long)coordinate.X * intervals + sampleX;
                double worldX = globalSampleX * sampleSpacing;

                heights[sampleZ * sampleCount + sampleX] =
                    EvaluateDevelopmentHeight(worldX, worldZ);
            }
        }

        return new TerrainChunk(
            coordinate,
            new TerrainHeightfield(
                sampleCount,
                settings.ChunkSizeMeters,
                heights));
    }

    private static float EvaluateDevelopmentHeight(
        double worldX,
        double worldZ)
    {
        double broadHills =
            Math.Sin(worldX * 0.0045) * 8.0 +
            Math.Cos(worldZ * 0.0040) * 6.0;
        double crossingRidge =
            Math.Sin((worldX + worldZ) * 0.0080) * 2.0;
        double basinX = worldX - 420.0;
        double basinZ = worldZ + 280.0;
        double basin =
            -7.0 *
            Math.Exp(
                -((basinX * basinX) + (basinZ * basinZ)) /
                (2.0 * 520.0 * 520.0));

        return (float)(broadHills + crossingRidge + basin);
    }
}
