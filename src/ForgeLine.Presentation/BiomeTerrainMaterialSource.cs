using System.Collections.ObjectModel;
using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed record TerrainBiomePaletteDiagnostics(
    ChunkCoordinate Coordinate, uint RequiredMaterialMask, ReadOnlyCollection<TerrainMaterialSlot> Palette,
    int InfrastructureRegions, int ControlSamples);

/// <summary>One immutable CPU palette generation. No GPU resources, quality settings or simulation mutation.</summary>
internal sealed class BiomeTerrainMaterialSource
{
    private const int Side = TerrainChunkSplatData.ControlSamplesPerSide;
    private sealed class Entry(TerrainChunk chunk, int[] regions)
    {
        internal TerrainChunk Chunk { get; } = chunk;
        internal int[] Regions { get; } = regions;
        internal uint Required;
        internal TerrainMaterialSlot[] Palette = [];
    }
    private readonly TerrainWorld _world;
    private readonly IEnvironmentQuery _environment;
    private readonly TerrainInfrastructureMask _infrastructure;
    private readonly Dictionary<ChunkCoordinate, Entry> _entries = [];
    private readonly int _minX, _minZ, _maxX, _maxZ;
    private readonly float _padding;
    private TerrainPresentationProfile? _profile;

    internal BiomeTerrainMaterialSource(TerrainWorld world, IEnvironmentQuery environment, TerrainInfrastructureMask infrastructure)
    {
        _world = world; _environment = environment; _infrastructure = infrastructure;
        if (world.Settings.ChunkSizeMeters != 256 || world.Chunks.Count > 256 ||
            MathF.Abs(world.WorldBounds.Minimum.X) > 1_000_000 || MathF.Abs(world.WorldBounds.Maximum.X) > 1_000_000 ||
            MathF.Abs(world.WorldBounds.Minimum.Z) > 1_000_000 || MathF.Abs(world.WorldBounds.Maximum.Z) > 1_000_000)
            throw new ArgumentException("Biome visual version 1 supports at most 256 complete 256m chunks within the environment extent.", nameof(world));
        _minX = world.Chunks.Min(c => c.Coordinate.X); _maxX = world.Chunks.Max(c => c.Coordinate.X);
        _minZ = world.Chunks.Min(c => c.Coordinate.Z); _maxZ = world.Chunks.Max(c => c.Coordinate.Z);
        if (((long)_maxX - _minX + 1) * ((long)_maxZ - _minZ + 1) != world.Chunks.Count)
            throw new ArgumentException("Biome visual palettes require a complete immutable terrain rectangle.", nameof(world));
        _padding = 256f / (Side - 1) * .5f * MathF.Sqrt(2);
        foreach (var chunk in world.Chunks)
        {
            int[] regions = Enumerable.Range(0, infrastructure.Regions.Length)
                .Where(i => infrastructure.Regions[i].Touches(chunk.Bounds.Minimum.X, chunk.Bounds.Minimum.Z,
                    chunk.Bounds.Maximum.X, chunk.Bounds.Maximum.Z, _padding)).ToArray();
            if (regions.Length > TerrainInfrastructureMask.MaximumRegionsPerChunk)
                throw new InvalidOperationException("Infrastructure candidate budget exceeds 64 regions per chunk.");
            _entries.Add(chunk.Coordinate, new(chunk, regions));
        }
    }

    internal BiomeTerrainMaterialSource Copy() => new(_world, _environment, _infrastructure);

    internal void Initialize(TerrainPresentationProfile profile)
    {
        _profile = profile;
        Span<float> weights = stackalloc float[8];
        foreach (var entry in _entries.Values.OrderBy(e => e.Chunk.Coordinate.Z).ThenBy(e => e.Chunk.Coordinate.X))
        {
            var totals = new double[8];
            entry.Required = profile.SurfaceMarkPriority(entry.Chunk.Bounds);
            for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
            {
                Vector3 point = Position(entry.Chunk, x, z);
                profile.CalculateMaterialWeights(point, Vector3.UnitY, weights);
                int dominant = 0;
                for (int slot = 0; slot < 8; slot++)
                {
                    totals[slot] += weights[slot];
                    if (weights[slot] > weights[dominant]) dominant = slot;
                }
                entry.Required |= 1u << dominant;
            }
            if (BitOperations.PopCount(entry.Required) > 4)
                throw new InvalidOperationException($"Chunk {entry.Chunk.Coordinate} requires more than four locally dominant/infrastructure materials (mask {entry.Required:X2}).");
            entry.Palette = Enumerable.Range(0, 8).OrderByDescending(i => (entry.Required & (1u << i)) != 0)
                .ThenByDescending(i => totals[i]).ThenBy(i => i).Take(4).OrderBy(i => i)
                .Select(i => (TerrainMaterialSlot)i).ToArray();
        }
    }

    internal uint InfrastructurePriority(AxisAlignedBounds bounds)
    {
        uint mask = 0;
        foreach (var r in _infrastructure.Regions)
            if (r.Touches(bounds.Minimum.X, bounds.Minimum.Z, bounds.Maximum.X, bounds.Maximum.Z, _padding))
                mask |= 1u << (int)r.Value.Slot;
        return mask;
    }

    internal void Calculate(Vector3 position, Span<float> destination)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Z) ||
            position.X < _world.WorldBounds.Minimum.X || position.X > _world.WorldBounds.Maximum.X ||
            position.Z < _world.WorldBounds.Minimum.Z || position.Z > _world.WorldBounds.Maximum.Z)
            throw new ArgumentOutOfRangeException(nameof(position));
        long x = checked((long)Math.Round((double)position.X * 1000, MidpointRounding.ToEven));
        long z = checked((long)Math.Round((double)position.Z * 1000, MidpointRounding.ToEven));
        if (!_environment.TrySample(x, z, out var sample))
            throw new InvalidOperationException($"Environment sample unavailable at ({x},{z}) millimeters.");
        if (!Enum.IsDefined(sample.Surface) ||
            (sample.Surface is EnvironmentSurface.Natural or EnvironmentSurface.Rock ? sample.Weights.Sum != ushort.MaxValue : sample.Weights.Sum != 0))
            throw new InvalidOperationException("Invalid environment surface or normalized biome weights.");
        destination.Clear();
        if (sample.Surface == EnvironmentSurface.Built) destination[(int)TerrainMaterialSlot.IndustrialGround] = 1;
        else if (sample.Surface == EnvironmentSurface.Rock) destination[(int)TerrainMaterialSlot.Rock] = 1;
        else if (sample.Surface == EnvironmentSurface.Water)
        {
            destination[(int)TerrainMaterialSlot.Mud] = .85f; destination[(int)TerrainMaterialSlot.Rock] = .15f;
        }
        else
        {
            float forest = sample.Weights.Forest / 65535f, meadow = sample.Weights.Meadow / 65535f;
            float dry = sample.Weights.DrySlope / 65535f, rock = sample.Weights.Rock / 65535f;
            float wet = sample.Weights.WetDepression / 65535f;
            destination[(int)TerrainMaterialSlot.GrassGround] = forest * .35f + meadow * .9f;
            destination[(int)TerrainMaterialSlot.Dirt] = forest * .65f + meadow * .1f + dry * .8f + wet * .2f;
            destination[(int)TerrainMaterialSlot.Mud] = wet * .8f;
            destination[(int)TerrainMaterialSlot.Rock] = rock * .9f;
            destination[(int)TerrainMaterialSlot.Gravel] = dry * .2f + rock * .1f;
            float slopeExposure = Math.Clamp((sample.Slope / 65535f - .04f) / .18f, 0, .75f);
            float altitudeExposure = Math.Clamp((sample.HeightMillimeters / 1000f - 750) / 750, 0, 1) * .5f;
            Blend(destination, TerrainMaterialSlot.Rock, MathF.Max(slopeExposure, altitudeExposure));
            float wetness = Math.Clamp((sample.Moisture / 65535f - .6f) * .5f, 0, .15f);
            Blend(destination, TerrainMaterialSlot.Mud, wetness);
        }
        int cx = Math.Clamp(checked((int)MathF.Floor(position.X / 256)), _minX, _maxX);
        int cz = Math.Clamp(checked((int)MathF.Floor(position.Z / 256)), _minZ, _maxZ);
        foreach (int index in _entries[new(cx, cz)].Regions)
        {
            var region = _infrastructure.Regions[index];
            Blend(destination, region.Value.Slot, region.Weight(new(position.X, position.Z), _padding));
        }
    }

    internal TerrainBiomePaletteDiagnostics Diagnostics(ChunkCoordinate coordinate)
    {
        if (!_entries.TryGetValue(coordinate, out var entry)) throw new ArgumentOutOfRangeException(nameof(coordinate));
        return new(coordinate, entry.Required, Array.AsReadOnly(entry.Palette.ToArray()), entry.Regions.Length, Side * Side);
    }

    internal TerrainChunkSplatData Build(TerrainChunk chunk)
    {
        if (!_entries.TryGetValue(chunk.Coordinate, out var entry) || !ReferenceEquals(entry.Chunk, chunk))
            throw new ArgumentException("Chunk does not belong to this immutable biome palette generation.", nameof(chunk));
        var pixels = new byte[Side * Side * 4];
        Span<float> all = stackalloc float[8];
        Span<float> local = stackalloc float[4];
        for (int z = 0; z < Side; z++)
        for (int x = 0; x < Side; x++)
        {
            _profile!.CalculateMaterialWeights(Position(chunk, x, z), Vector3.UnitY, all);
            uint allowed = PaletteMask(entry.Palette);
            // Both sides use the same palette intersection; corner ownership includes the diagonal.
            int dx0 = x == 0 ? -1 : 0, dx1 = x == Side - 1 ? 1 : 0;
            int dz0 = z == 0 ? -1 : 0, dz1 = z == Side - 1 ? 1 : 0;
            for (int dz = dz0; dz <= dz1; dz++)
            for (int dx = dx0; dx <= dx1; dx++)
            {
                long nx = (long)chunk.Coordinate.X + dx, nz = (long)chunk.Coordinate.Z + dz;
                if (nx is >= int.MinValue and <= int.MaxValue && nz is >= int.MinValue and <= int.MaxValue &&
                    _entries.TryGetValue(new((int)nx, (int)nz), out var neighbour))
                    allowed &= PaletteMask(neighbour.Palette);
            }
            float retained = 0;
            for (int i = 0; i < 4; i++)
            {
                int slot = (int)entry.Palette[i];
                local[i] = (allowed & (1u << slot)) == 0 ? 0 : all[slot];
                retained += local[i];
            }
            if (retained <= 0) throw new InvalidOperationException("Biome seam lost its required dominant material.");
            TerrainSplatMapBuilder.NormalizeControlWeights(local, pixels.AsSpan((z * Side + x) * 4, 4));
        }
        uint preserve = 0;
        for (int layer = 0; layer < 4; layer++)
            if ((entry.Required & (1u << (int)entry.Palette[layer])) != 0) preserve |= 1u << layer;
        return new(entry.Palette, TerrainSplatMapBuilder.BuildMipChain(Side, Side, pixels, preserve));
    }

    private static Vector3 Position(TerrainChunk chunk, int x, int z) =>
        new(chunk.Bounds.Minimum.X + x * 8, 0, chunk.Bounds.Minimum.Z + z * 8);

    private static uint PaletteMask(IEnumerable<TerrainMaterialSlot> palette)
    {
        uint mask = 0; foreach (var slot in palette) mask |= 1u << (int)slot; return mask;
    }

    private static void Blend(Span<float> weights, TerrainMaterialSlot slot, float amount)
    {
        for (int i = 0; i < 8; i++) weights[i] *= 1 - amount;
        weights[(int)slot] += amount;
    }
}
