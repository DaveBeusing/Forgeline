using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SceneLightingSettingsTests
{
    [Fact]
    public void DefaultLightingWritesNormalizedExplicitShaderConstants()
    {
        SceneLightingSettings settings =
            SceneLightingSettings.Default;
        var constants =
            new float[
                SceneLightingSettings.ShaderConstantCount];

        settings.Validate();
        settings.WriteShaderConstants(
            constants);

        var direction =
            new Vector3(
                constants[0],
                constants[1],
                constants[2]);

        Assert.InRange(
            direction.Length(),
            0.9999f,
            1.0001f);
        Assert.Equal(
            settings.DirectionalIntensity,
            constants[3]);
        Assert.Equal(
            settings.DirectionalColor.X,
            constants[4]);
        Assert.Equal(
            settings.DirectionalColor.Y,
            constants[5]);
        Assert.Equal(
            settings.DirectionalColor.Z,
            constants[6]);
        Assert.Equal(
            settings.AmbientIntensity,
            constants[7]);
        Assert.Equal(
            settings.AmbientColor.X,
            constants[8]);
        Assert.Equal(
            settings.AmbientColor.Y,
            constants[9]);
        Assert.Equal(
            settings.AmbientColor.Z,
            constants[10]);
        Assert.Equal(
            settings.Exposure,
            constants[11]);
        Assert.Equal(
            settings.GroundAmbientFactor,
            constants[12]);
        Assert.Equal(
            (float)SceneToneMappingMode.AcesFitted,
            constants[13]);
    }

    [Fact]
    public void ZeroLightDirectionIsRejected()
    {
        SceneLightingSettings settings =
            SceneLightingSettings.Default with
            {
                DirectionToLight =
                    Vector3.Zero
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            settings.Validate);
    }

    [Fact]
    public void NonPositiveExposureIsRejected()
    {
        SceneLightingSettings settings =
            SceneLightingSettings.Default with
            {
                Exposure =
                    0.0f
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            settings.Validate);
    }

    [Fact]
    public void InvalidToneMappingModeIsRejected()
    {
        SceneLightingSettings settings =
            SceneLightingSettings.Default with
            {
                ToneMapping =
                    (SceneToneMappingMode)byte.MaxValue
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            settings.Validate);
    }

    [Fact]
    public void ShaderConstantDestinationMustFitCompleteSceneState()
    {
        SceneLightingSettings settings =
            SceneLightingSettings.Default;
        var constants =
            new float[
                SceneLightingSettings.ShaderConstantCount -
                1];

        Assert.Throws<ArgumentException>(
            () =>
                settings.WriteShaderConstants(
                    constants));
    }
}
