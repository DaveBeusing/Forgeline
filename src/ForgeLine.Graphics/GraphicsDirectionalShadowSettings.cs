using System.Numerics;

namespace ForgeLine.Graphics;

public enum DirectionalShadowQuality { Disabled, Low, Medium, High }

public readonly record struct GraphicsDirectionalShadowSettings(DirectionalShadowQuality Quality)
{
    public bool Enabled => Quality != DirectionalShadowQuality.Disabled;
    public int Resolution => Quality switch { DirectionalShadowQuality.Low => 1024, DirectionalShadowQuality.Medium => 2048, DirectionalShadowQuality.High => 4096, _ => 0 };
    public int CascadeCount => Enabled ? 1 : 0;
    public float Radius => Quality switch { DirectionalShadowQuality.Low => 180, DirectionalShadowQuality.Medium => 450, DirectionalShadowQuality.High => 750, _ => 0 };
    public int MaximumCasters => Quality switch { DirectionalShadowQuality.Low => 1024, DirectionalShadowQuality.Medium => 4096, DirectionalShadowQuality.High => 8192, _ => 0 };
    public int MaximumDraws => Enabled ? 256 : 0;
    public long PayloadBytes(int buffers) => checked((long)Resolution * Resolution * 4 * buffers);
    public void Validate()
    {
        if (!Enum.IsDefined(Quality)) throw new ArgumentOutOfRangeException(nameof(Quality));
    }
}

public readonly record struct GraphicsDirectionalShadowFrame(Matrix4x4 ViewProjection, Vector3 Center, float Radius, float DepthRange, float ContactStrength = 0.12f)
{
    public void Validate()
    {
        if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y) || !float.IsFinite(Center.Z) ||
            !float.IsFinite(Radius) || Radius <= 0 || !float.IsFinite(DepthRange) || DepthRange <= 0 ||
            !float.IsFinite(ContactStrength) || ContactStrength is < 0 or > 0.15f ||
            !Matrix4x4.Invert(ViewProjection, out Matrix4x4 inverse) || !float.IsFinite(inverse.M11))
            throw new ArgumentException("Shadow frame must contain finite, invertible projection and bounded contact settings.");
    }
}

public readonly record struct GraphicsDirectionalShadowDiagnostics(DirectionalShadowQuality Quality, int Resolution, int CascadeCount,
    long TexturePayloadBytes, int ShaderResourceDescriptors, int DrawCalls, int SubmittedCasters, double? CpuMilliseconds,
    ulong GpuSubmissionFence, double? GpuMilliseconds);
