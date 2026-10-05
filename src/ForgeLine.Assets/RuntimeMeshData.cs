using System.Text;

namespace ForgeLine.Assets;

[Flags]
public enum RuntimeMeshAttributes
{
    None = 0,
    Normal = 1 << 0,
    Uv0 = 1 << 1,
    Tangent = 1 << 2,
}

public readonly record struct RuntimeMeshVertex(
    float X,
    float Y,
    float Z,
    float NormalX,
    float NormalY,
    float NormalZ,
    float U,
    float V,
    float TangentX,
    float TangentY,
    float TangentZ,
    float TangentW);

public readonly record struct RuntimeMeshSection(
    int FirstIndex,
    int IndexCount,
    int MaterialSlot);

public sealed class RuntimeMeshData
{
    public const int CurrentVersion = 2;
    public const int VertexStride = 48;

    public RuntimeMeshData(
        RuntimeMeshAttributes attributes,
        IReadOnlyList<RuntimeMeshVertex> vertices,
        IReadOnlyList<uint> indices,
        IReadOnlyList<string> materialIds,
        IReadOnlyList<RuntimeMeshSection> sections)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(materialIds);
        ArgumentNullException.ThrowIfNull(sections);

        Attributes = attributes;
        Vertices = vertices.ToArray();
        Indices = indices.ToArray();
        MaterialIds = materialIds.ToArray();
        Sections = sections.ToArray();
        Validate();
    }

    public RuntimeMeshAttributes Attributes { get; }
    public IReadOnlyList<RuntimeMeshVertex> Vertices { get; }
    public IReadOnlyList<uint> Indices { get; }
    public IReadOnlyList<string> MaterialIds { get; }
    public IReadOnlyList<RuntimeMeshSection> Sections { get; }

    public bool HasNormals => (Attributes & RuntimeMeshAttributes.Normal) != 0;
    public bool HasUv0 => (Attributes & RuntimeMeshAttributes.Uv0) != 0;
    public bool HasTangents => (Attributes & RuntimeMeshAttributes.Tangent) != 0;

    public byte[] ToPayload()
    {
        Validate();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(CurrentVersion);
            writer.Write((int)Attributes);
            writer.Write(Vertices.Count);
            writer.Write(Indices.Count);
            writer.Write(MaterialIds.Count);
            writer.Write(Sections.Count);
            foreach (string materialId in MaterialIds)
            {
                writer.Write(materialId);
            }
            foreach (RuntimeMeshVertex vertex in Vertices)
            {
                writer.Write(vertex.X); writer.Write(vertex.Y); writer.Write(vertex.Z);
                writer.Write(vertex.NormalX); writer.Write(vertex.NormalY); writer.Write(vertex.NormalZ);
                writer.Write(vertex.U); writer.Write(vertex.V);
                writer.Write(vertex.TangentX); writer.Write(vertex.TangentY); writer.Write(vertex.TangentZ); writer.Write(vertex.TangentW);
            }
            foreach (uint index in Indices)
            {
                writer.Write(index);
            }
            foreach (RuntimeMeshSection section in Sections)
            {
                writer.Write(section.FirstIndex);
                writer.Write(section.IndexCount);
                writer.Write(section.MaterialSlot);
            }
        }
        return stream.ToArray();
    }

    public static RuntimeMeshData FromPayload(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (stream.Length < 24)
        {
            throw new InvalidDataException("Runtime mesh payload header is truncated.");
        }

        int version = reader.ReadInt32();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Runtime mesh version {version} is unsupported; recompile source mesh assets for version {CurrentVersion}.");
        }

        RuntimeMeshAttributes attributes = (RuntimeMeshAttributes)reader.ReadInt32();
        if ((attributes & ~(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0 | RuntimeMeshAttributes.Tangent)) != 0)
        {
            throw new InvalidDataException($"Runtime mesh attributes value '{attributes}' contains unsupported flags.");
        }

        int vertexCount = reader.ReadInt32();
        int indexCount = reader.ReadInt32();
        int materialCount = reader.ReadInt32();
        int sectionCount = reader.ReadInt32();
        if (vertexCount <= 0 || indexCount <= 0 || indexCount % 3 != 0 || materialCount < 0 || sectionCount <= 0)
        {
            throw new InvalidDataException("Runtime mesh contains invalid vertex, index, material, or section counts.");
        }

        if (vertexCount > 16_000_000 || indexCount > 96_000_000 || materialCount > 256 || sectionCount > 65_536)
        {
            throw new InvalidDataException("Runtime mesh declared counts exceed supported production limits.");
        }

        var materialIds = new string[materialCount];
        try
        {
            for (int index = 0; index < materialIds.Length; index++)
            {
                string value = reader.ReadString();
                _ = AssetId.Parse(value);
                materialIds[index] = value;
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Runtime mesh material table is truncated.", exception);
        }

        var vertices = new RuntimeMeshVertex[vertexCount];
        try
        {
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = new RuntimeMeshVertex(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Runtime mesh vertex payload is truncated.", exception);
        }

        var indices = new uint[indexCount];
        try
        {
            for (int index = 0; index < indices.Length; index++)
            {
                indices[index] = reader.ReadUInt32();
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Runtime mesh index payload is truncated.", exception);
        }

        var sections = new RuntimeMeshSection[sectionCount];
        try
        {
            for (int index = 0; index < sections.Length; index++)
            {
                sections[index] = new RuntimeMeshSection(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Runtime mesh section table is truncated.", exception);
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Runtime mesh payload contains trailing data.");
        }

        return new RuntimeMeshData(attributes, vertices, indices, materialIds, sections);
    }

    private void Validate()
    {
        if (Vertices.Count == 0)
        {
            throw new InvalidDataException("Runtime mesh must contain at least one vertex.");
        }
        if (Indices.Count == 0 || Indices.Count % 3 != 0)
        {
            throw new InvalidDataException("Runtime mesh triangle index count must be a non-zero multiple of three.");
        }
        if (Sections.Count == 0)
        {
            throw new InvalidDataException("Runtime mesh must contain at least one draw section.");
        }

        foreach (string materialId in MaterialIds)
        {
            _ = AssetId.Parse(materialId);
        }

        for (int index = 0; index < Vertices.Count; index++)
        {
            RuntimeMeshVertex vertex = Vertices[index];
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            {
                throw new InvalidDataException($"Runtime mesh vertex {index} contains a non-finite position.");
            }
            if (HasNormals &&
                (!float.IsFinite(vertex.NormalX) || !float.IsFinite(vertex.NormalY) || !float.IsFinite(vertex.NormalZ) ||
                 LengthSquared(vertex.NormalX, vertex.NormalY, vertex.NormalZ) <= 1e-12f))
            {
                throw new InvalidDataException($"Runtime mesh vertex {index} contains an invalid normal.");
            }
            if (HasUv0 && (!float.IsFinite(vertex.U) || !float.IsFinite(vertex.V)))
            {
                throw new InvalidDataException($"Runtime mesh vertex {index} contains a non-finite UV0 coordinate.");
            }
            if (HasTangents &&
                (!float.IsFinite(vertex.TangentX) || !float.IsFinite(vertex.TangentY) ||
                 !float.IsFinite(vertex.TangentZ) || !float.IsFinite(vertex.TangentW) ||
                 LengthSquared(vertex.TangentX, vertex.TangentY, vertex.TangentZ) <= 1e-12f ||
                 MathF.Abs(vertex.TangentW) < 0.5f))
            {
                throw new InvalidDataException($"Runtime mesh vertex {index} contains an invalid tangent basis.");
            }
        }

        foreach (uint index in Indices)
        {
            if (index >= Vertices.Count)
            {
                throw new InvalidDataException($"Runtime mesh index {index} exceeds vertex count {Vertices.Count}.");
            }
        }

        int expectedFirstIndex = 0;
        foreach (RuntimeMeshSection section in Sections)
        {
            if (section.FirstIndex != expectedFirstIndex ||
                section.IndexCount <= 0 ||
                section.IndexCount % 3 != 0 ||
                section.FirstIndex < 0 ||
                checked(section.FirstIndex + section.IndexCount) > Indices.Count)
            {
                throw new InvalidDataException("Runtime mesh sections must form contiguous triangle ranges over the index buffer.");
            }
            if (section.MaterialSlot < -1 || section.MaterialSlot >= MaterialIds.Count)
            {
                throw new InvalidDataException($"Runtime mesh material slot {section.MaterialSlot} is outside the material table.");
            }
            expectedFirstIndex += section.IndexCount;
        }

        if (expectedFirstIndex != Indices.Count)
        {
            throw new InvalidDataException("Runtime mesh sections do not cover the complete index buffer.");
        }
    }

    private static float LengthSquared(float x, float y, float z) =>
        x * x + y * y + z * z;
}
