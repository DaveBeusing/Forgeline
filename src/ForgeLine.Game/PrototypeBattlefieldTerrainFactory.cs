using ForgeLine.World;

namespace ForgeLine.Game;

public static class PrototypeBattlefieldTerrainFactory
{
    public static TerrainWorld Create(
        PrototypeBattlefieldDefinition definition,
        WorldGridSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        WorldGridSettings resolved =
            settings ?? new WorldGridSettings();
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
                        resolved));
            }
        }

        return new TerrainWorld(
            resolved,
            chunks);
    }

    private static TerrainChunk CreateChunk(
        ChunkCoordinate coordinate,
        WorldGridSettings settings)
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
                    EvaluateHeight(
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

    private static float EvaluateHeight(
        double worldX,
        double worldZ)
    {
        double broadTerrain =
            Math.Sin(worldX * 0.0032) * 5.0 +
            Math.Cos(worldZ * 0.0036) * 4.0 +
            Math.Sin((worldX + worldZ) * 0.0021) * 2.5;

        double divideDistance =
            (worldX - 1_536.0) / 90.0;
        double ridge =
            58.0 *
            Math.Exp(
                -0.5 *
                divideDistance *
                divideDistance);

        double northGap =
            Gaussian(
                worldZ,
                920.0,
                120.0);
        double southGap =
            Gaussian(
                worldZ,
                2_200.0,
                145.0);
        double gap =
            Math.Max(
                northGap,
                southGap);

        ridge *= 1.0 - 0.94 * gap;

        double northOverlook =
            17.0 *
            Gaussian2D(
                worldX,
                worldZ,
                1_160.0,
                720.0,
                260.0,
                220.0);
        double southOverlook =
            15.0 *
            Gaussian2D(
                worldX,
                worldZ,
                1_920.0,
                2_390.0,
                280.0,
                220.0);
        double centerLowland =
            -6.0 *
            Gaussian2D(
                worldX,
                worldZ,
                1_536.0,
                1_560.0,
                520.0,
                520.0);

        return (float)(
            broadTerrain +
            ridge +
            northOverlook +
            southOverlook +
            centerLowland);
    }

    private static double Gaussian(
        double value,
        double center,
        double sigma)
    {
        double normalized =
            (value - center) / sigma;
        return Math.Exp(
            -0.5 *
            normalized *
            normalized);
    }

    private static double Gaussian2D(
        double x,
        double z,
        double centerX,
        double centerZ,
        double sigmaX,
        double sigmaZ) =>
        Gaussian(x, centerX, sigmaX) *
        Gaussian(z, centerZ, sigmaZ);

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
