using System.Numerics;
using System.Runtime.InteropServices;

namespace ForgeLine.Graphics;

public enum AtmosphereQuality { Disabled, Distance, Height }

/// <summary>Visual extinction only; contains no intelligence or gameplay visibility.</summary>
public readonly record struct GraphicsAtmosphereSettings(
    AtmosphereQuality Quality, Vector3 Color, float Density, float StartDistance,
    float BaseHeight, float HeightFalloff, float MaximumOpacity, float ZoomCompensation)
{
    public GraphicsLocalHazeSettings? LocalHaze { get; init; }
    public bool Enabled => Quality != AtmosphereQuality.Disabled;
    public static GraphicsAtmosphereSettings Default => new(AtmosphereQuality.Disabled,
        new(0.58f, 0.66f, 0.76f), 0.0012f, 100, 0, 0.02f, 0.5f, 0.75f);

    public void Validate()
    {
        if (!Enum.IsDefined(Quality)) throw new ArgumentOutOfRangeException(nameof(Quality));
        Range(Color.X, 0, 4, nameof(Color)); Range(Color.Y, 0, 4, nameof(Color)); Range(Color.Z, 0, 4, nameof(Color));
        Range(Density, 0, 0.01f, nameof(Density)); Range(StartDistance, 0, 2000, nameof(StartDistance));
        Range(BaseHeight, -1000, 10000, nameof(BaseHeight)); Range(HeightFalloff, 0, 0.1f, nameof(HeightFalloff));
        Range(MaximumOpacity, 0, 0.65f, nameof(MaximumOpacity)); Range(ZoomCompensation, 0, 1, nameof(ZoomCompensation));
        LocalHaze?.Validate();
    }

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct GraphicsAtmosphereView(Matrix4x4 InverseViewProjection, Vector3 CameraPosition, float CameraDistance)
{
    public void Validate()
    {
        Matrix4x4 matrix = InverseViewProjection;
        foreach (float value in MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref matrix, 1)))
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(InverseViewProjection));
        if (!Matrix4x4.Invert(matrix, out _) || !float.IsFinite(CameraPosition.X) ||
            !float.IsFinite(CameraPosition.Y) || !float.IsFinite(CameraPosition.Z) ||
            !float.IsFinite(CameraDistance) || CameraDistance <= 0 || CameraDistance > 100000)
            throw new ArgumentOutOfRangeException(nameof(CameraPosition));
    }
}

public readonly record struct GraphicsAtmosphereDiagnostics(AtmosphereQuality Quality, bool Applied,
    int DepthDescriptors, long AdditionalTargetBytes, float EffectiveDensity, ulong CpuSubmission,
    ulong GpuSubmissionFence, double? CompositionGpuMilliseconds, string? UnavailableReason)
{
    public long RequiredScenePayloadBytes { get; init; }
    public GraphicsLocalHazeSettings? LocalHaze { get; init; }
    public int MaximumLocalDensityEvaluations { get; init; }
}
