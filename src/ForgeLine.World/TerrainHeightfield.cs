using System.Numerics;

namespace ForgeLine.World;

public sealed class TerrainHeightfield
{
    private readonly float[] _heights;

    public TerrainHeightfield(
        int samplesPerSide,
        float chunkSizeMeters,
        ReadOnlySpan<float> heights)
    {
        if (samplesPerSide < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerSide));
        }

        if (!float.IsFinite(chunkSizeMeters) || chunkSizeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeMeters));
        }

        int expectedCount = checked(samplesPerSide * samplesPerSide);
        if (heights.Length != expectedCount)
        {
            throw new ArgumentException(
                $"Expected {expectedCount} height samples, received {heights.Length}.",
                nameof(heights));
        }

        _heights = heights.ToArray();

        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;

        foreach (float height in _heights)
        {
            if (!float.IsFinite(height))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heights),
                    "Terrain height samples must be finite.");
            }

            minimum = MathF.Min(minimum, height);
            maximum = MathF.Max(maximum, height);
        }

        SamplesPerSide = samplesPerSide;
        ChunkSizeMeters = chunkSizeMeters;
        MinimumHeight = minimum;
        MaximumHeight = maximum;
    }

    public int SamplesPerSide { get; }

    public float ChunkSizeMeters { get; }

    public float SampleSpacing =>
        ChunkSizeMeters / (SamplesPerSide - 1);

    public float MinimumHeight { get; }

    public float MaximumHeight { get; }

    public float GetHeight(int sampleX, int sampleZ)
    {
        if ((uint)sampleX >= (uint)SamplesPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleX));
        }

        if ((uint)sampleZ >= (uint)SamplesPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleZ));
        }

        return _heights[sampleZ * SamplesPerSide + sampleX];
    }

    public float SampleHeight(float localX, float localZ)
    {
        ValidateLocalCoordinate(localX, nameof(localX));
        ValidateLocalCoordinate(localZ, nameof(localZ));

        float clampedX = Math.Clamp(localX, 0.0f, ChunkSizeMeters);
        float clampedZ = Math.Clamp(localZ, 0.0f, ChunkSizeMeters);

        float gridX = clampedX / SampleSpacing;
        float gridZ = clampedZ / SampleSpacing;

        int x0 = Math.Min((int)MathF.Floor(gridX), SamplesPerSide - 1);
        int z0 = Math.Min((int)MathF.Floor(gridZ), SamplesPerSide - 1);
        int x1 = Math.Min(x0 + 1, SamplesPerSide - 1);
        int z1 = Math.Min(z0 + 1, SamplesPerSide - 1);

        float tx = gridX - x0;
        float tz = gridZ - z0;

        float top = MathF.Lerp(
            GetHeight(x0, z0),
            GetHeight(x1, z0),
            tx);
        float bottom = MathF.Lerp(
            GetHeight(x0, z1),
            GetHeight(x1, z1),
            tx);

        return MathF.Lerp(top, bottom, tz);
    }

    public Vector3 SampleNormal(float localX, float localZ)
    {
        ValidateLocalCoordinate(localX, nameof(localX));
        ValidateLocalCoordinate(localZ, nameof(localZ));

        float step = SampleSpacing;
        float left = SampleHeight(MathF.Max(localX - step, 0.0f), localZ);
        float right = SampleHeight(MathF.Min(localX + step, ChunkSizeMeters), localZ);
        float back = SampleHeight(localX, MathF.Max(localZ - step, 0.0f));
        float forward = SampleHeight(localX, MathF.Min(localZ + step, ChunkSizeMeters));

        float xDistance =
            MathF.Min(localX + step, ChunkSizeMeters) -
            MathF.Max(localX - step, 0.0f);
        float zDistance =
            MathF.Min(localZ + step, ChunkSizeMeters) -
            MathF.Max(localZ - step, 0.0f);

        float derivativeX = (right - left) / MathF.Max(xDistance, float.Epsilon);
        float derivativeZ = (forward - back) / MathF.Max(zDistance, float.Epsilon);

        return Vector3.Normalize(new Vector3(-derivativeX, 1.0f, -derivativeZ));
    }

    private static void ValidateLocalCoordinate(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
