using System.Numerics;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class RuntimeMaterialDataTests
{
    [Fact]
    public void ParsesTextureBindingsAndMaterialParameters()
    {
        RuntimeMaterialData material =
            RuntimeMaterialData.FromPayload(
                """
                {
                  "baseColorTexture": "texture.test.base",
                  "normalTexture": "texture.test.normal",
                  "ormTexture": "texture.test.orm",
                  "emissiveTexture": "texture.test.emissive",
                  "baseColorFactor": [0.8, 0.7, 0.6, 1.0],
                  "metallicFactor": 0.25,
                  "roughnessFactor": 0.65,
                  "emissiveMultiplier": 2.5,
                  "uvScale": [2.0, 3.0],
                  "uvOffset": [0.25, -0.5]
                }
                """u8);

        Assert.Equal(
            AssetId.Parse(
                "texture.test.base"),
            material.BaseColorTexture);
        Assert.Equal(
            AssetId.Parse(
                "texture.test.normal"),
            material.NormalTexture);
        Assert.Equal(
            AssetId.Parse(
                "texture.test.orm"),
            material.OrmTexture);
        Assert.Equal(
            AssetId.Parse(
                "texture.test.emissive"),
            material.EmissiveTexture);
        Assert.Equal(
            new Vector4(
                0.8f,
                0.7f,
                0.6f,
                1.0f),
            material.BaseColorFactor);
        Assert.Equal(
            0.25f,
            material.MetallicFactor);
        Assert.Equal(
            0.65f,
            material.RoughnessFactor);
        Assert.Equal(
            2.5f,
            material.EmissiveMultiplier);
        Assert.Equal(
            new Vector2(
                2.0f,
                3.0f),
            material.UvScale);
        Assert.Equal(new Vector2(0.25f, -0.5f), material.UvOffset);
    }

    [Fact]
    public void MissingOptionalBindingsUseDeterministicDefaults()
    {
        RuntimeMaterialData material =
            RuntimeMaterialData.FromPayload(
                "{}"u8);

        Assert.Null(
            material.BaseColorTexture);
        Assert.Null(
            material.NormalTexture);
        Assert.Null(
            material.OrmTexture);
        Assert.Null(
            material.EmissiveTexture);
        Assert.Equal(
            Vector4.One,
            material.BaseColorFactor);
        Assert.Equal(
            1.0f,
            material.MetallicFactor);
        Assert.Equal(
            1.0f,
            material.RoughnessFactor);
        Assert.Equal(
            1.0f,
            material.EmissiveMultiplier);
        Assert.Equal(
            Vector2.One,
            material.UvScale);
        Assert.Equal(Vector2.Zero, material.UvOffset);
    }

    [Fact]
    public void InvalidTextureIdFailsClearly()
    {
        Assert.Throws<InvalidDataException>(
            static () =>
                RuntimeMaterialData.FromPayload(
                    """
                    {
                      "baseColorTexture": "Invalid Texture"
                    }
                    """u8));
    }

    [Fact]
    public void InvalidUvScaleFailsClearly()
    {
        Assert.Throws<InvalidDataException>(
            static () =>
                RuntimeMaterialData.FromPayload(
                    """
                    {
                      "uvScale": [1.0, 0.0]
                    }
                    """u8));
    }

    [Theory]
    [InlineData("{\"uvOffset\":[1]}")]
    [InlineData("{\"uvOffset\":[1,\"x\"]}")]
    [InlineData("{\"uvOffset\":[1,2,3]}")]
    public void InvalidAtlasOffsetFailsClearly(string json)
    {
        Assert.Throws<InvalidDataException>(() => RuntimeMaterialData.FromPayload(
            System.Text.Encoding.UTF8.GetBytes(json)));
    }
}
