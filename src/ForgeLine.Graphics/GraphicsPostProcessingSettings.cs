namespace ForgeLine.Graphics;

public enum SpatialAntialiasing { Disabled, Low, High }

public readonly record struct GraphicsPostProcessingSettings
{
    public GraphicsPostProcessingSettings() { }
    public SpatialAntialiasing Antialiasing { get; init; }
    public float WhiteBalance { get; init; }
    public float Tint { get; init; }
    public float Contrast { get; init; } = 1;
    public float Saturation { get; init; } = 1;
    public static GraphicsPostProcessingSettings Neutral => new();
    public float EdgeBlend => Antialiasing switch { SpatialAntialiasing.Low => .2f, SpatialAntialiasing.High => .3f, _ => 0 };
    public void Validate()
    {
        if (!Enum.IsDefined(Antialiasing)) throw new ArgumentOutOfRangeException(nameof(Antialiasing));
        Require(WhiteBalance, -.1f, .1f, nameof(WhiteBalance));
        Require(Tint, -.1f, .1f, nameof(Tint));
        Require(Contrast, .95f, 1.08f, nameof(Contrast));
        Require(Saturation, .95f, 1.05f, nameof(Saturation));
    }
    private static void Require(float value, float minimum, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct GraphicsPostProcessingDiagnostics(bool Supported, SpatialAntialiasing Antialiasing,
    float ResolutionScale, int SceneTargets, long ScenePayloadBytes, int IntermediateTargets,
    int HistoryTargets, int MaximumSceneSamples, ulong GpuSubmissionFence, double? GpuMilliseconds);
