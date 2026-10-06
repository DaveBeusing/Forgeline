using System.Numerics;

namespace ForgeLine.Presentation;

public enum SceneToneMappingMode : byte
{
    None = 0,
    AcesFitted = 1
}

public readonly record struct SceneLightingSettings(
    Vector3 DirectionToLight,
    float DirectionalIntensity,
    Vector3 DirectionalColor,
    float AmbientIntensity,
    Vector3 AmbientColor,
    float Exposure,
    float GroundAmbientFactor,
    SceneToneMappingMode ToneMapping)
{
    internal const int ShaderConstantCount = 14;

    public static SceneLightingSettings Default =>
        new(
            Vector3.Normalize(
                new Vector3(
                    0.35f,
                    0.85f,
                    -0.25f)),
            1.10f,
            new Vector3(
                1.00f,
                0.96f,
                0.90f),
            0.52f,
            new Vector3(
                0.58f,
                0.66f,
                0.76f),
            1.15f,
            0.42f,
            SceneToneMappingMode.AcesFitted);

    public void Validate()
    {
        if (!IsFinite(
                DirectionToLight) ||
            DirectionToLight.LengthSquared() <
                1e-6f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DirectionToLight),
                "Scene light direction must be finite and non-zero.");
        }

        ValidateColor(
            DirectionalColor,
            nameof(DirectionalColor));
        ValidateColor(
            AmbientColor,
            nameof(AmbientColor));
        ValidateNonNegative(
            DirectionalIntensity,
            nameof(DirectionalIntensity));
        ValidateNonNegative(
            AmbientIntensity,
            nameof(AmbientIntensity));

        if (!float.IsFinite(
                Exposure) ||
            Exposure <= 0.0f ||
            Exposure > 16.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Exposure),
                "Scene exposure must be finite and in the range (0, 16].");
        }

        if (!float.IsFinite(
                GroundAmbientFactor) ||
            GroundAmbientFactor < 0.0f ||
            GroundAmbientFactor > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(GroundAmbientFactor),
                "Ground ambient factor must be finite and in the range [0, 1].");
        }

        if (!Enum.IsDefined(
                ToneMapping))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ToneMapping),
                ToneMapping,
                "Unsupported scene tone-mapping mode.");
        }
    }

    internal void WriteShaderConstants(
        Span<float> destination)
    {
        if (destination.Length <
            ShaderConstantCount)
        {
            throw new ArgumentException(
                $"Scene lighting constants require at least {ShaderConstantCount} floats.",
                nameof(destination));
        }

        Validate();

        Vector3 direction =
            Vector3.Normalize(
                DirectionToLight);

        destination[0] =
            direction.X;
        destination[1] =
            direction.Y;
        destination[2] =
            direction.Z;
        destination[3] =
            DirectionalIntensity;

        destination[4] =
            DirectionalColor.X;
        destination[5] =
            DirectionalColor.Y;
        destination[6] =
            DirectionalColor.Z;
        destination[7] =
            AmbientIntensity;

        destination[8] =
            AmbientColor.X;
        destination[9] =
            AmbientColor.Y;
        destination[10] =
            AmbientColor.Z;
        destination[11] =
            Exposure;

        destination[12] =
            GroundAmbientFactor;
        destination[13] =
            (float)ToneMapping;
    }

    private static void ValidateColor(
        in Vector3 color,
        string parameterName)
    {
        if (!IsFinite(
                color) ||
            color.X < 0.0f ||
            color.Y < 0.0f ||
            color.Z < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Scene-lighting colors must contain finite, non-negative components.");
        }
    }

    private static void ValidateNonNegative(
        float value,
        string parameterName)
    {
        if (!float.IsFinite(
                value) ||
            value < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Scene-lighting intensity values must be finite and non-negative.");
        }
    }

    private static bool IsFinite(
        in Vector3 value) =>
        float.IsFinite(
            value.X) &&
        float.IsFinite(
            value.Y) &&
        float.IsFinite(
            value.Z);
}
