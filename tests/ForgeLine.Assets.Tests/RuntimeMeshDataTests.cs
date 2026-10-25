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

    [Theory]
    [InlineData(32_000, 3, 1)]
    [InlineData(1, 96_000, 1)]
    [InlineData(1, 3, 32_000)]
    public void TruncatedDeclaredBlocksAreRejectedBeforeProportionalAllocation(int vertices, int indices, int sections)
    {
        byte[] header = new byte[24];
        BitConverter.GetBytes(RuntimeMeshData.CurrentVersion).CopyTo(header, 0);
        BitConverter.GetBytes(vertices).CopyTo(header, 8);
        BitConverter.GetBytes(indices).CopyTo(header, 12);
        BitConverter.GetBytes(sections).CopyTo(header, 20);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => RuntimeMeshData.FromPayload(header));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 65_536);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(179)]
    public void TruncatedPayloadSectionsFail(int length)
    {
        var mesh = new RuntimeMeshData(RuntimeMeshAttributes.None,
            [Vertex(0, 0, 0, 0, 0), Vertex(1, 0, 0, 0, 0), Vertex(0, 1, 0, 0, 0)],
            [0u, 1u, 2u], [], [new(0, 3, -1)]);
        byte[] payload = mesh.ToPayload();
        Assert.Throws<InvalidDataException>(() => RuntimeMeshData.FromPayload(payload[..length]));
        Assert.Throws<InvalidDataException>(() => RuntimeMeshData.FromPayload([.. payload, 0]));
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
