namespace ForgeLine.World;

/// <summary>Cell-owned candidates with local-priority thinning. All per-call scratch is private.</summary>
public sealed class EnvironmentScatterGenerator
{
    public const int CellSizeMillimeters = 4000;
    private readonly WorldGenerationRecipe _recipe;
    private readonly EnvironmentScatterProfile _profile;
    private readonly IEnvironmentQuery _environment;
    private readonly IScatterClearance _clearance;
    private readonly WorldGenerationRandom _random;
    private readonly int _halo;
    private readonly long _minX, _minZ, _maxX, _maxZ;
    private readonly byte[] _header;

    public EnvironmentScatterGenerator(WorldGenerationRecipe recipe, EnvironmentScatterProfile profile,
        IEnvironmentQuery environment, IScatterClearance clearance)
    {
        ArgumentNullException.ThrowIfNull(recipe); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(environment); ArgumentNullException.ThrowIfNull(clearance);
        recipe.Validate();
        _recipe = recipe; _profile = profile; _environment = environment; _clearance = clearance;
        _minX = (long)recipe.Origin.X * recipe.ChunkSizeMillimeters;
        _minZ = (long)recipe.Origin.Z * recipe.ChunkSizeMillimeters;
        _maxX = ((long)recipe.Origin.X + recipe.WidthInChunks) * recipe.ChunkSizeMillimeters;
        _maxZ = ((long)recipe.Origin.Z + recipe.HeightInChunks) * recipe.ChunkSizeMillimeters;
        if (recipe.ChunkSizeMillimeters != 256_000 || recipe.SamplesPerSide != 65 ||
            _minX < -1_000_000_000 || _minZ < -1_000_000_000 || _maxX > 1_000_000_000 || _maxZ > 1_000_000_000 ||
            clearance.Sha256 is not { Length: 64 } || !clearance.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Unsupported scatter grid, coordinates or clearance identity.");
        _random = new(recipe);
        _halo = (2 * profile.Species.Max(s => s.RadiusMillimeters) + CellSizeMillimeters - 1) / CellSizeMillimeters;
        var writer = new WorldCanonicalWriter(); writer.WriteUInt32(0x31435453); // STC1.
        writer.WriteBytes(WorldGenerationFingerprint.Gameplay(recipe, []).GetCanonicalBytes());
        profile.WriteTo(writer); writer.WriteBytes(Convert.FromHexString(clearance.Sha256));
        _header = writer.ToArray();
    }

    public EnvironmentScatterChunk GenerateChunk(ChunkCoordinate coordinate)
    {
        if (coordinate.X < _recipe.Origin.X || coordinate.Z < _recipe.Origin.Z ||
            (long)coordinate.X >= (long)_recipe.Origin.X + _recipe.WidthInChunks ||
            (long)coordinate.Z >= (long)_recipe.Origin.Z + _recipe.HeightInChunks)
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        int side = 64 + 2 * _halo;
        long cellX = (long)coordinate.X * 64 - _halo, cellZ = (long)coordinate.Z * 64 - _halo;
        var candidates = new Candidate[side * side];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
            candidates[z * side + x] = CandidateAt(cellX + x, cellZ + z);
        var accepted = new List<Candidate>();
        for (int z = _halo; z < _halo + 64; z++) for (int x = _halo; x < _halo + 64; x++)
        {
            var candidate = candidates[z * side + x];
            if (!candidate.Eligible) continue;
            bool retained = true;
            for (int nz = z - _halo; nz <= z + _halo && retained; nz++)
            for (int nx = x - _halo; nx <= x + _halo; nx++)
            {
                var other = candidates[nz * side + nx];
                if (!other.Eligible || !Precedes(other, candidate)) continue;
                long dx = other.X - candidate.X, dz = other.Z - candidate.Z;
                long distance = (long)other.Radius + candidate.Radius;
                if (dx * dx + dz * dz < distance * distance) { retained = false; break; }
            }
            if (retained) accepted.Add(candidate);
        }
        int chunks = _recipe.WidthInChunks * _recipe.HeightInChunks;
        int ordinal = (coordinate.Z - _recipe.Origin.Z) * _recipe.WidthInChunks + coordinate.X - _recipe.Origin.X;
        int quota = Math.Min(_profile.MaximumInstancesPerChunk,
            _profile.MaximumTotalInstances / chunks + (ordinal < _profile.MaximumTotalInstances % chunks ? 1 : 0));
        var selected = accepted.OrderBy(c => c.Priority).ThenBy(c => c.Instance.StableId).Take(quota)
            .Select(c => c.Instance).OrderBy(i => i.StableId).ToArray();
        return new(coordinate, _profile, selected, _header);
    }

    private Candidate CandidateAt(long cx, long cz)
    {
        long x = cx * CellSizeMillimeters, z = cz * CellSizeMillimeters;
        if (x < _minX || z < _minZ || x >= _maxX || z >= _maxZ) return default;
        bool cellYard = _clearance.IsYard(x + 2000, z + 2000, 0);
        ulong random = _random.At(cellYard ? WorldGenerationDomain.Props : WorldGenerationDomain.Vegetation, new(cx, cz), streamId: 0x534301);
        x += 250 + (long)(random & 65535) % 3501; z += 250 + (long)((random >> 16) & 65535) % 3501;
        int cluster = Cluster(x, z);
        if ((random >> 32) % 1000 >= (ulong)(_profile.DensityPermille * (10_000L + cluster * 55_535L / 65535) / 65535)) return default;
        if (!_environment.TrySample(x, z, out var sample)) return default;
        if (!Enum.IsDefined(sample.Surface) || sample.HeightMillimeters is < -1_000_000_000 or > 1_000_000_000 ||
            sample.TemperatureMilliCelsius is < -50_000 or > 50_000 ||
            sample.Surface is EnvironmentSurface.Natural or EnvironmentSurface.Rock && sample.Weights.Sum != 65535)
            throw new InvalidOperationException("Invalid immutable environment sample.");
        if (sample.Surface == EnvironmentSurface.Water) return default;
        bool yard = _clearance.IsYard(x, z, 0);
        if (sample.Surface == EnvironmentSurface.Built && !yard) return default;
        Span<long> weights = stackalloc long[_profile.Species.Count];
        weights.Clear();
        long sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            var s = _profile.Species[i];
            if (yard != (s.Classification == ScatterClassification.IndustrialProp) || sample.Slope > s.MaximumSlope ||
                sample.SoilFertility < s.MinimumSoil || sample.Moisture < s.MinimumMoisture || sample.Moisture > s.MaximumMoisture ||
                sample.TemperatureMilliCelsius < s.MinimumTemperatureMilliCelsius || sample.TemperatureMilliCelsius > s.MaximumTemperatureMilliCelsius) continue;
            long w = yard ? 1000 : ((long)s.Affinity.Forest * sample.Weights.Forest + (long)s.Affinity.Meadow * sample.Weights.Meadow +
                (long)s.Affinity.DrySlope * sample.Weights.DrySlope + (long)s.Affinity.Rock * sample.Weights.Rock +
                (long)s.Affinity.WetDepression * sample.Weights.WetDepression) / 65535;
            w = w * (s.Classification == ScatterClassification.Tree ? cluster :
                s.Classification == ScatterClassification.Shrub ? 65535 - Math.Abs(cluster - 32767) : 65535 - cluster / 2) / 65535;
            weights[i] = w; sum += w;
        }
        if (sum == 0) return default;
        ulong variation = _random.At(yard ? WorldGenerationDomain.Props : WorldGenerationDomain.Vegetation,
            new(cx, cz), streamId: 0x534302);
        long pick = (long)(variation % (ulong)sum);
        int selected = 0;
        for (; selected < weights.Length - 1; selected++) { if (pick < weights[selected]) break; pick -= weights[selected]; }
        var species = _profile.Species[selected]; int radius = species.RadiusMillimeters;
        if (x - radius < _minX || z - radius < _minZ || x + radius >= _maxX || z + radius >= _maxZ ||
            _clearance.IsBlocked(x, z, radius) ||
            (yard ? !_clearance.IsYard(x, z, radius) : _clearance.OverlapsYard(x, z, radius))) return default;
        ushort scale = (ushort)(species.MinimumScalePermille + (variation >> 16) %
            (ulong)(species.MaximumScalePermille - species.MinimumScalePermille + 1));
        int height = checked(sample.HeightMillimeters + (int)((long)species.HeightMillimeters * scale * species.PivotHeightPermille / 1_000_000));
        ulong id = ((ulong)unchecked((uint)cz) << 32) | unchecked((uint)cx);
        static long Origin(long cell) => (cell >= 0 ? cell / 64 : (cell + 1) / 64 - 1) * 256_000;
        int localX = (int)(x - Origin(cx)), localZ = (int)(z - Origin(cz));
        var instance = new ScatterInstance(id, localX, height, localZ, (ushort)selected,
            (ushort)((variation >> 32) % 36000), scale, (byte)(218 + (variation >> 48) % 38), species.Classification);
        return new(true, x, z, radius, _random.At(yard ? WorldGenerationDomain.Props : WorldGenerationDomain.Vegetation,
            new(cx, cz), streamId: 0x534303), instance);
    }

    private int Cluster(long x, long z)
    {
        int period = _profile.ClusterPeriodMillimeters;
        static (long Cell, long T) Split(long value, int divisor)
        {
            long cell = value / divisor, rem = value % divisor;
            if (rem < 0) { cell--; rem += divisor; }
            long t = rem * 65535 / divisor;
            return (cell, t * t * (3 * 65535 - 2 * t) / (65535L * 65535));
        }
        var a = Split(x, period); var b = Split(z, period);
        long At(long px, long pz) => (long)(_random.At(WorldGenerationDomain.Vegetation, new(px, pz), streamId: 0x534304) >> 48);
        static long Lerp(long l, long r, long t) => (l * (65535 - t) + r * t) / 65535;
        int noise = (int)Lerp(Lerp(At(a.Cell, b.Cell), At(a.Cell + 1, b.Cell), a.T),
            Lerp(At(a.Cell, b.Cell + 1), At(a.Cell + 1, b.Cell + 1), a.T), b.T);
        return (int)Math.Clamp((noise - 12000) * 65535L / 45000, 0, 65535);
    }

    private static bool Precedes(Candidate a, Candidate b)
    {
        static int Layer(ScatterClassification c) => c switch
        { ScatterClassification.Tree => 0, ScatterClassification.Rock or ScatterClassification.IndustrialProp => 1, ScatterClassification.Shrub => 2, _ => 3 };
        int comparison = Layer(a.Instance.Classification).CompareTo(Layer(b.Instance.Classification));
        return comparison < 0 || comparison == 0 && (a.Priority < b.Priority ||
            a.Priority == b.Priority && a.Instance.StableId < b.Instance.StableId);
    }

    private readonly record struct Candidate(bool Eligible, long X, long Z, int Radius, ulong Priority, ScatterInstance Instance);
}
