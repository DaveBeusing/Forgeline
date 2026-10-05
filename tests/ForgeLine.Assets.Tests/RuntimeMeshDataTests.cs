using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class RuntimeMeshDataTests
{
    [Fact]
    public void ProductionMeshRoundTripsUvTangentsAndMaterialSections()
    {
        var source =
            new RuntimeMeshData(
                RuntimeMeshAttributes.Normal |
                RuntimeMeshAttributes.Uv0 |
                RuntimeMeshAttributes.Tangent,
                [
                    Vertex(0f, 0f, 0f, 0f, 0f),
                    Vertex(1f, 0f, 0f, 1f, 0f),
                    Vertex(0f, 0f, 1f, 0f, 1f)
                ],
                [0u, 1u, 2u],
                ["material.test.surface"],
                [new RuntimeMeshSection(0, 3, 0)]);

        RuntimeMeshData decoded =
            RuntimeMeshData.FromPayload(
                source.ToPayload());

        Assert.True(decoded.HasNormals);
        Assert.True(decoded.HasUv0);
        Assert.True(decoded.HasTangents);
        Assert.Equal(RuntimeMeshData.VertexStride, 48);
        Assert.Equal(3, decoded.Vertices.Count);
        Assert.Equal([0u, 1u, 2u], decoded.Indices);
        Assert.Equal(
            "material.test.surface",
            Assert.Single(decoded.MaterialIds));
        Assert.Equal(
            new RuntimeMeshSection(0, 3, 0),
            Assert.Single(decoded.Sections));
    }

    [Fact]
    public void LegacyMeshVersionRequiresExplicitRecompile()
    {
        byte[] payload =
            new byte[24];
        BitConverter.GetBytes(1)
            .CopyTo(
                payload,
                0);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    RuntimeMeshData.FromPayload(
                        payload));

        Assert.Contains(
            "recompile",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsOutOfRangeMaterialSlot()
    {
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    new RuntimeMeshData(
                        RuntimeMeshAttributes.Normal,
                        [
                            Vertex(0f, 0f, 0f, 0f, 0f),
                            Vertex(1f, 0f, 0f, 0f, 0f),
                            Vertex(0f, 0f, 1f, 0f, 0f)
                        ],
                        [0u, 1u, 2u],
                        [],
                        [new RuntimeMeshSection(0, 3, 0)]));

        Assert.Contains(
            "material slot",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeMeshVertex Vertex(
        float x,
        float y,
        float z,
        float u,
        float v) =>
        new(
            x,
            y,
            z,
            0f,
            1f,
            0f,
            u,
            v,
            1f,
            0f,
            0f,
            -1f);
}
