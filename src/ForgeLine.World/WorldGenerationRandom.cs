namespace ForgeLine.World;

// Numeric values are persistent domain identifiers; never renumber or reuse them.
public enum WorldGenerationDomain : uint
{
    Terrain = 1,
    Climate = 2,
    Geography = 3,
    Vegetation = 4,
    Props = 5
}

public readonly record struct WorldSampleLocation(ChunkCoordinate Chunk, int LocalX, int LocalZ);

/// <summary>Integer lattice coordinates, independent of floating point world positions.</summary>
public readonly record struct WorldSampleCoordinate(long X, long Z)
{
    public static WorldSampleCoordinate FromChunk(ChunkCoordinate chunk, int localX, int localZ, int samplesPerSide)
    {
        ValidateSamples(samplesPerSide);
        ArgumentOutOfRangeException.ThrowIfNegative(localX);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(localX, samplesPerSide);
        ArgumentOutOfRangeException.ThrowIfNegative(localZ);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(localZ, samplesPerSide);
        int intervals = samplesPerSide - 1;
        return new((long)chunk.X * intervals + localX, (long)chunk.Z * intervals + localZ);
    }

    /// <summary>Boundary samples belong to the following chunk at local zero.</summary>
    public WorldSampleLocation ToChunk(int samplesPerSide)
    {
        ValidateSamples(samplesPerSide);
        int intervals = samplesPerSide - 1;
        static (int Chunk, int Local) Split(long value, int divisor)
        {
            long quotient = value / divisor;
            int remainder = (int)(value % divisor);
            if (remainder < 0) { quotient--; remainder += divisor; }
            return (checked((int)quotient), remainder);
        }
        var x = Split(X, intervals);
        var z = Split(Z, intervals);
        return new(new(x.Chunk, z.Chunk), x.Local, z.Local);
    }

    private static void ValidateSamples(int samplesPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerSide, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(samplesPerSide, 129);
    }
}

/// <summary>Version 1 SplitMix64 derivation; no mutable shared state or runtime string hashes.</summary>
public sealed class WorldGenerationRandom
{
    private const ulong Increment = 0x9E3779B97F4A7C15UL;
    private readonly ulong _seed;
    private readonly uint _version;

    public WorldGenerationRandom(WorldGenerationRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate();
        _seed = recipe.Seed;
        _version = recipe.GeneratorVersion;
    }

    public ulong At(WorldGenerationDomain domain, WorldSampleCoordinate coordinate,
        ulong sampleIndex = 0, ulong streamId = 0)
    {
        if (!Enum.IsDefined(domain)) throw new ArgumentOutOfRangeException(nameof(domain));
        unchecked
        {
            ulong key = Mix(_seed ^ 0x464F5247454C494EUL);
            key = Mix(key ^ _version);
            key = Mix(key ^ (uint)domain);
            key = Mix(key ^ (ulong)coordinate.X);
            key = Mix(key ^ (ulong)coordinate.Z);
            key = Mix(key ^ streamId);
            return Mix(key + Increment * (sampleIndex + 1));
        }
    }

    public WorldGenerationSequence CreateStream(WorldGenerationDomain domain,
        WorldSampleCoordinate coordinate, ulong streamId = 0)
    {
        // Validate the domain before handing a stream to a consumer.
        _ = At(domain, coordinate, streamId: streamId);
        return new(this, domain, coordinate, streamId);
    }

    private static ulong Mix(ulong value)
    {
        // Same SplitMix64 finalizer as SimulationRandom; World cannot depend on Simulation.
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}

/// <summary>One consumer owns one stream. Parallel consumers use separate streams or indexed At calls.</summary>
public sealed class WorldGenerationSequence
{
    private readonly WorldGenerationRandom _random;
    private readonly WorldGenerationDomain _domain;
    private readonly WorldSampleCoordinate _coordinate;
    private readonly ulong _streamId;
    private ulong _index;

    internal WorldGenerationSequence(WorldGenerationRandom random, WorldGenerationDomain domain,
        WorldSampleCoordinate coordinate, ulong streamId)
    {
        _random = random;
        _domain = domain;
        _coordinate = coordinate;
        _streamId = streamId;
    }

    public ulong NextUInt64()
    {
        ulong result = _random.At(_domain, _coordinate, _index, _streamId);
        _index = unchecked(_index + 1);
        return result;
    }
}
