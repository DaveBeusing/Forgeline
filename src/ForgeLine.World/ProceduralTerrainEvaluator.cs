namespace ForgeLine.World;

public readonly record struct ProceduralTerrainSample(int HeightMillimeters, ushort FlatPermille, ushort DrainagePermille);

/// <summary>Stateless fixed-point value noise; coordinates and streams never depend on chunk order.</summary>
public sealed class ProceduralTerrainEvaluator
{
    private const long One = 1L << 20;
    private readonly ProceduralTerrainRecipe _recipe;
    private readonly WorldGenerationRandom _random;

    public ProceduralTerrainEvaluator(ProceduralTerrainRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate();
        _recipe = recipe;
        _random = new(recipe.Generation);
    }

    public ProceduralTerrainSample Evaluate(WorldSampleCoordinate coordinate)
    {
        // Public samples are restricted to the same bounded global lattice as the factory.
        ArgumentOutOfRangeException.ThrowIfLessThan(coordinate.X, -262_144L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coordinate.X, 262_144L);
        ArgumentOutOfRangeException.ThrowIfLessThan(coordinate.Z, -262_144L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coordinate.Z, 262_144L);
        long x = coordinate.X * 4000, z = coordinate.Z * 4000;
        long hills = Noise(x, z, 2_048_000, 1);
        long ridge = One - Math.Abs(Noise(x, z, 1_024_000, 2));
        long detail = Noise(x, z, 128_000, 3);
        long flat = (Noise(x, z, 2_048_000, 4) + One) / 2;
        long drainage = Math.Max(0, One - 4 * Math.Abs(Noise(x, z, 2_048_000, 5)));
        long relief = (hills * _recipe.HillHeightMillimeters + ridge * _recipe.RidgeHeightMillimeters +
            detail * _recipe.DetailHeightMillimeters) / One;
        long retained = One - flat * _recipe.FlatStrengthPermille / 1000;
        int height = checked((int)(_recipe.BaseHeightMillimeters + relief * retained / One -
            drainage * _recipe.DrainageDepthMillimeters / One));
        return new(height, (ushort)(flat * 1000 / One), (ushort)(drainage * 1000 / One));
    }

    private long Noise(long x, long z, int size, ulong stream)
    {
        static (long Cell, long Fraction) Split(long value, int divisor)
        {
            long cell = Math.DivRem(value, divisor, out long remainder);
            if (remainder < 0) { cell--; remainder += divisor; }
            long t = remainder * One / divisor;
            return (cell, t * t / One * (3 * One - 2 * t) / One);
        }
        var a = Split(x, size);
        var b = Split(z, size);
        long At(long cx, long cz) => (long)(_random.At(WorldGenerationDomain.Terrain, new(cx, cz), streamId: stream) >> 43) - One;
        static long Lerp(long left, long right, long t) => left + (right - left) * t / One;
        return Lerp(Lerp(At(a.Cell, b.Cell), At(a.Cell + 1, b.Cell), a.Fraction),
            Lerp(At(a.Cell, b.Cell + 1), At(a.Cell + 1, b.Cell + 1), a.Fraction), b.Fraction);
    }
}
