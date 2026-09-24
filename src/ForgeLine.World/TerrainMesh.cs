using System.Numerics;
using System.Runtime.InteropServices;

namespace ForgeLine.World;

[StructLayout(LayoutKind.Sequential)]
public readonly struct TerrainVertex
{
    public const int SizeInBytes = 32;

    public TerrainVertex(
        Vector3 position,
        Vector3 normal,
        Vector2 localUv)
    {
        Position = position;
        Normal = normal;
        LocalUv = localUv;
    }

    public readonly Vector3 Position;

    public readonly Vector3 Normal;

    public readonly Vector2 LocalUv;
}

public sealed record TerrainMeshSettings
{
    public const int DefaultVertexSamplesPerSide = 33;

    public int VertexSamplesPerSide { get; init; } =
        DefaultVertexSamplesPerSide;

    public void Validate()
    {
        if (VertexSamplesPerSide < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VertexSamplesPerSide));
        }
    }
}

public sealed class TerrainMeshData
{
    public TerrainMeshData(
        TerrainVertex[] vertices,
        uint[] indices,
        AxisAlignedBounds bounds)
    {
        Vertices = vertices ??
            throw new ArgumentNullException(nameof(vertices));
        Indices = indices ??
            throw new ArgumentNullException(nameof(indices));
        Bounds = bounds;
    }

    public TerrainVertex[] Vertices { get; }

    public uint[] Indices { get; }

    public AxisAlignedBounds Bounds { get; }

    public int TriangleCount => Indices.Length / 3;
}

public static class TerrainMeshGenerator
{
    public static TerrainMeshData Generate(
        TerrainChunk chunk,
        TerrainMeshSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        TerrainMeshSettings resolvedSettings =
            settings ?? new TerrainMeshSettings();
        resolvedSettings.Validate();

        int samplesPerSide = resolvedSettings.VertexSamplesPerSide;
        int vertexCount = checked(samplesPerSide * samplesPerSide);
        TerrainVertex[] vertices = new TerrainVertex[vertexCount];

        float chunkSize = chunk.Heightfield.ChunkSizeMeters;
        Vector3 chunkOrigin = new(
            chunk.Coordinate.X * chunkSize,
            0.0f,
            chunk.Coordinate.Z * chunkSize);

        for (int z = 0; z < samplesPerSide; z++)
        {
            float normalizedZ = z / (float)(samplesPerSide - 1);
            float localZ = normalizedZ * chunkSize;

            for (int x = 0; x < samplesPerSide; x++)
            {
                float normalizedX = x / (float)(samplesPerSide - 1);
                float localX = normalizedX * chunkSize;
                float height = chunk.Heightfield.SampleHeight(localX, localZ);
                Vector3 normal = chunk.Heightfield.SampleNormal(localX, localZ);

                vertices[z * samplesPerSide + x] =
                    new TerrainVertex(
                        new Vector3(
                            chunkOrigin.X + localX,
                            height,
                            chunkOrigin.Z + localZ),
                        normal,
                        new Vector2(normalizedX, normalizedZ));
            }
        }

        int quadsPerSide = samplesPerSide - 1;
        uint[] indices = new uint[
            checked(quadsPerSide * quadsPerSide * 6)];

        int index = 0;
        for (int z = 0; z < quadsPerSide; z++)
        {
            for (int x = 0; x < quadsPerSide; x++)
            {
                uint topLeft = checked((uint)(z * samplesPerSide + x));
                uint topRight = topLeft + 1;
                uint bottomLeft =
                    checked((uint)((z + 1) * samplesPerSide + x));
                uint bottomRight = bottomLeft + 1;

                indices[index++] = topLeft;
                indices[index++] = bottomLeft;
                indices[index++] = topRight;

                indices[index++] = topRight;
                indices[index++] = bottomLeft;
                indices[index++] = bottomRight;
            }
        }

        return new TerrainMeshData(vertices, indices, chunk.Bounds);
    }
}
