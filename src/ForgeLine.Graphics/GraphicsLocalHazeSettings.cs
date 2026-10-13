using System.Globalization;
using System.Numerics;

namespace ForgeLine.Graphics;

/// <summary>One bounded static density region, approximated in the existing depth composite.</summary>
public readonly record struct GraphicsLocalHazeSettings(Vector3 Center, float RadiusMeters, float Density)
{
    public void Validate()
    {
        if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y) || !float.IsFinite(Center.Z))
            throw new ArgumentOutOfRangeException(nameof(Center));
        if (!float.IsFinite(RadiusMeters) || RadiusMeters is < 10 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(RadiusMeters));
        if (!float.IsFinite(Density) || Density is < 0 or > .01f)
            throw new ArgumentOutOfRangeException(nameof(Density));
    }

    internal string ShaderConstants()
    {
        Validate();
        return string.Create(CultureInfo.InvariantCulture,
            $"#define LOCAL_HAZE 1\nstatic const float3 HazeCenter=float3({Center.X:R},{Center.Y:R},{Center.Z:R});\nstatic const float HazeRadius={RadiusMeters:R};\nstatic const float HazeDensity={Density:R};\n");
    }
}
