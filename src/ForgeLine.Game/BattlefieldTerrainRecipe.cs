using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct TerrainWave(double Frequency, double Amplitude);
public readonly record struct TerrainGap(double Center, double Sigma);
public readonly record struct TerrainMound(double Amplitude, double CenterX, double CenterZ, double SigmaX, double SigmaZ);

// Version 1 fixes the evaluation order: X sine, Z cosine, diagonal sine,
// a Gaussian ridge with two passes, then three Gaussian surface features.
public sealed record BattlefieldTerrainRecipe(
    int Version, WorldGridSettings Grid, TerrainWave[] Waves,
    double RidgeCenter, double RidgeSigma, double RidgeAmplitude,
    double GapStrength, TerrainGap[] Gaps, TerrainMound[] Mounds)
{
    public const int CurrentVersion = 1;

    public static BattlefieldTerrainRecipe CentralDivide() => new(
        CurrentVersion, new WorldGridSettings(),
        [new(0.0032, 5), new(0.0036, 4), new(0.0021, 2.5)],
        1536, 90, 58, 0.94,
        [new(920, 120), new(2200, 145)],
        [new(17, 1160, 720, 260, 220), new(15, 1920, 2390, 280, 220), new(-6, 1536, 1560, 520, 520)]);

    public BattlefieldTerrainRecipe Copy() => this with
    {
        Grid = Grid with { }, Waves = Waves.ToArray(), Gaps = Gaps.ToArray(), Mounds = Mounds.ToArray()
    };

    public void Validate(BattlefieldMapMetadata metadata)
    {
        if (Version != CurrentVersion || Grid is null || Waves is not { Length: 3 } ||
            Gaps is not { Length: 2 } || Mounds is not { Length: 3 })
            throw new InvalidOperationException("Unsupported terrain recipe or collection sizes.");
        Grid.Validate();
        double x = metadata.WidthMeters / Grid.ChunkSizeMeters;
        double z = metadata.HeightMeters / Grid.ChunkSizeMeters;
        if (!double.IsFinite(x) || !double.IsFinite(z) || x < 1 || z < 1 ||
            x > 256 || z > 256 || Math.Abs(x - Math.Round(x)) > 0.0001 ||
            Math.Abs(z - Math.Round(z)) > 0.0001 || Grid.HeightSamplesPerSide > 129 ||
            Grid.RegionSizeInChunks > 32 || x * z * Grid.HeightSamplesPerSide * Grid.HeightSamplesPerSide > 4_000_000)
            throw new InvalidOperationException("Terrain grid exceeds the supported allocation budget.");
        static bool Bounded(double value) => double.IsFinite(value) && Math.Abs(value) <= 100_000;
        static bool Sigma(double value) => double.IsFinite(value) && value >= 1 && value <= 100_000;
        if (!Bounded(RidgeCenter) || !Sigma(RidgeSigma) || !Bounded(RidgeAmplitude) ||
            !double.IsFinite(GapStrength) || GapStrength < 0 || GapStrength > 1 ||
            Waves.Any(w => !double.IsFinite(w.Frequency) || Math.Abs(w.Frequency) > 1 || !Bounded(w.Amplitude)) ||
            Gaps.Any(g => !Bounded(g.Center) || !Sigma(g.Sigma)) ||
            Mounds.Any(m => !Bounded(m.Amplitude) || !Bounded(m.CenterX) || !Bounded(m.CenterZ) || !Sigma(m.SigmaX) || !Sigma(m.SigmaZ)))
            throw new InvalidOperationException("Terrain recipe parameters must be finite and bounded.");
    }

    internal float Evaluate(double x, double z)
    {
        double broad = Math.Sin(x * Waves[0].Frequency) * Waves[0].Amplitude +
            Math.Cos(z * Waves[1].Frequency) * Waves[1].Amplitude +
            Math.Sin((x + z) * Waves[2].Frequency) * Waves[2].Amplitude;
        double distance = (x - RidgeCenter) / RidgeSigma;
        double ridge = RidgeAmplitude * Math.Exp(-0.5 * distance * distance);
        double gap = Math.Max(Gaussian(z, Gaps[0].Center, Gaps[0].Sigma), Gaussian(z, Gaps[1].Center, Gaps[1].Sigma));
        ridge *= 1.0 - GapStrength * gap;
        double Mound(TerrainMound m) => m.Amplitude * (Gaussian(x, m.CenterX, m.SigmaX) * Gaussian(z, m.CenterZ, m.SigmaZ));
        return (float)(broad + ridge + Mound(Mounds[0]) + Mound(Mounds[1]) + Mound(Mounds[2]));
    }

    private static double Gaussian(double value, double center, double sigma)
    {
        double normalized = (value - center) / sigma;
        return Math.Exp(-0.5 * normalized * normalized);
    }
}
