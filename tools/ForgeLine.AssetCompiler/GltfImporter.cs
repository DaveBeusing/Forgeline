using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class GltfImporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    public static ImportedAssetPayload Import(
        string path,
        string sourceRoot,
        IReadOnlyList<string> materialIds,
        IReadOnlyList<MeshMaterialRequirement> materialRequirements)
    {
        ArgumentNullException.ThrowIfNull(materialIds);
        ArgumentNullException.ThrowIfNull(materialRequirements);

        if (materialIds.Count != materialRequirements.Count)
        {
            throw new ArgumentException(
                "Mesh material IDs and import requirements must have matching counts.");
        }

        using var gltf = Load(path, sourceRoot);
        JsonElement root = gltf.Document.RootElement;

        if (!root.TryGetProperty("asset", out JsonElement asset) ||
            !asset.TryGetProperty("version", out JsonElement version) ||
            version.GetString() is not { } versionString ||
            !versionString.StartsWith('2'))
        {
            throw new InvalidDataException("Only glTF 2.x assets are supported.");
        }

        if (!root.TryGetProperty("meshes", out JsonElement meshes) ||
            meshes.GetArrayLength() == 0)
        {
            throw new InvalidDataException("glTF asset does not contain any meshes.");
        }

        var vertices = new List<VertexData>();
        var indices = new List<uint>();
        var sections = new List<RuntimeMeshSection>();
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;
        bool allHaveUv0 = true;
        bool allHaveTangents = true;
        int generatedNormalVertexCount = 0;
        int generatedTangentVertexCount = 0;
        int fallbackSectionCount = 0;

        foreach (JsonElement mesh in meshes.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives) ||
                primitives.GetArrayLength() == 0)
            {
                throw new InvalidDataException("glTF mesh does not contain primitives.");
            }

            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode =
                    primitive.TryGetProperty("mode", out JsonElement modeProperty)
                        ? modeProperty.GetInt32()
                        : 4;
                if (mode != 4)
                {
                    throw new InvalidDataException(
                        $"glTF primitive mode {mode} is unsupported; triangles are required.");
                }

                if (!primitive.TryGetProperty("attributes", out JsonElement attributes) ||
                    !attributes.TryGetProperty("POSITION", out JsonElement positionAccessorProperty))
                {
                    throw new InvalidDataException("glTF primitive is missing POSITION data.");
                }

                float[] positions =
                    ReadFloatAccessor(
                        root,
                        gltf.Buffers,
                        positionAccessorProperty.GetInt32(),
                        "VEC3");

                float[]? normals = null;
                if (attributes.TryGetProperty("NORMAL", out JsonElement normalAccessorProperty))
                {
                    normals =
                        ReadFloatAccessor(
                            root,
                            gltf.Buffers,
                            normalAccessorProperty.GetInt32(),
                            "VEC3");
                    if (normals.Length != positions.Length)
                    {
                        throw new InvalidDataException(
                            "glTF NORMAL accessor count does not match POSITION.");
                    }
                }

                float[]? texCoords = null;
                if (attributes.TryGetProperty("TEXCOORD_0", out JsonElement texCoordAccessorProperty))
                {
                    texCoords =
                        ReadFloatAccessor(
                            root,
                            gltf.Buffers,
                            texCoordAccessorProperty.GetInt32(),
                            "VEC2");
                    if (texCoords.Length / 2 != positions.Length / 3)
                    {
                        throw new InvalidDataException(
                            "glTF TEXCOORD_0 accessor count does not match POSITION.");
                    }

                    if (texCoords.Any(static value => !float.IsFinite(value)))
                    {
                        throw new InvalidDataException(
                            "glTF TEXCOORD_0 data contains a non-finite coordinate.");
                    }
                }

                float[]? tangents = null;
                if (attributes.TryGetProperty("TANGENT", out JsonElement tangentAccessorProperty))
                {
                    tangents =
                        ReadFloatAccessor(
                            root,
                            gltf.Buffers,
                            tangentAccessorProperty.GetInt32(),
                            "VEC4");
                    if (tangents.Length / 4 != positions.Length / 3)
                    {
                        throw new InvalidDataException(
                            "glTF TANGENT accessor count does not match POSITION.");
                    }
                }

                int vertexBase = vertices.Count;
                int vertexCount = positions.Length / 3;
                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    int positionOffset = vertexIndex * 3;
                    float x = positions[positionOffset];
                    float y = positions[positionOffset + 1];
                    float z = positions[positionOffset + 2];

                    if (!float.IsFinite(x) ||
                        !float.IsFinite(y) ||
                        !float.IsFinite(z))
                    {
                        throw new InvalidDataException(
                            "glTF POSITION data contains a non-finite coordinate.");
                    }

                    minX = MathF.Min(minX, x);
                    minY = MathF.Min(minY, y);
                    minZ = MathF.Min(minZ, z);
                    maxX = MathF.Max(maxX, x);
                    maxY = MathF.Max(maxY, y);
                    maxZ = MathF.Max(maxZ, z);

                    float nx = normals is null ? 0f : normals[positionOffset];
                    float ny = normals is null ? 0f : normals[positionOffset + 1];
                    float nz = normals is null ? 0f : normals[positionOffset + 2];

                    float u = 0f;
                    float v = 0f;
                    if (texCoords is not null)
                    {
                        int texOffset = vertexIndex * 2;
                        u = texCoords[texOffset];
                        v = texCoords[texOffset + 1];
                    }

                    float tx = 0f;
                    float ty = 0f;
                    float tz = 0f;
                    float tw = 0f;
                    if (tangents is not null)
                    {
                        int tangentOffset = vertexIndex * 4;
                        tx = tangents[tangentOffset];
                        ty = tangents[tangentOffset + 1];
                        tz = tangents[tangentOffset + 2];
                        tw = tangents[tangentOffset + 3];
                    }

                    vertices.Add(
                        new VertexData(
                            x,
                            y,
                            z,
                            nx,
                            ny,
                            nz,
                            u,
                            v,
                            tx,
                            ty,
                            tz,
                            tw));
                }

                uint[] primitiveIndices;
                if (primitive.TryGetProperty("indices", out JsonElement indicesProperty))
                {
                    primitiveIndices =
                        ReadIndexAccessor(
                            root,
                            gltf.Buffers,
                            indicesProperty.GetInt32());
                }
                else
                {
                    primitiveIndices =
                        new uint[vertexCount];
                    for (int index = 0; index < vertexCount; index++)
                    {
                        primitiveIndices[index] = (uint)index;
                    }
                }

                if (primitiveIndices.Length == 0 ||
                    primitiveIndices.Length % 3 != 0)
                {
                    throw new InvalidDataException(
                        "glTF triangle primitive index count must be a non-zero multiple of three.");
                }

                foreach (uint index in primitiveIndices)
                {
                    if (index >= vertexCount)
                    {
                        throw new InvalidDataException(
                            $"glTF primitive index {index} exceeds vertex count {vertexCount}.");
                    }
                }

                if (normals is null ||
                    !TryNormalizeNormals(
                        vertices,
                        vertexBase,
                        vertexCount))
                {
                    GenerateNormals(
                        vertices,
                        vertexBase,
                        vertexCount,
                        primitiveIndices);
                    generatedNormalVertexCount +=
                        vertexCount;
                }

                int materialSlot =
                    ResolveMaterialSlot(
                        primitive,
                        materialIds.Count);
                MeshMaterialRequirement requirement =
                    materialSlot >= 0
                        ? materialRequirements[materialSlot]
                        : default;
                bool fallbackSection =
                    false;

                if (texCoords is null)
                {
                    allHaveUv0 = false;
                    if (requirement.RequiresUv0)
                    {
                        fallbackSection = true;
                    }
                }

                bool hasTangents =
                    tangents is not null &&
                    TryNormalizeTangents(
                        vertices,
                        vertexBase,
                        vertexCount);

                if (!hasTangents &&
                    requirement.RequiresTangents &&
                    texCoords is not null)
                {
                    hasTangents =
                        GenerateTangents(
                            vertices,
                            vertexBase,
                            vertexCount,
                            primitiveIndices);
                    if (hasTangents)
                    {
                        generatedTangentVertexCount +=
                            vertexCount;
                    }
                }

                if (!hasTangents)
                {
                    allHaveTangents = false;
                    if (requirement.RequiresTangents)
                    {
                        fallbackSection = true;
                    }
                }

                int sectionFirstIndex =
                    indices.Count;
                foreach (uint index in primitiveIndices)
                {
                    indices.Add(
                        checked(
                            (uint)vertexBase +
                            index));
                }

                if (fallbackSection)
                {
                    materialSlot = -1;
                    fallbackSectionCount++;
                }

                sections.Add(
                    new RuntimeMeshSection(
                        sectionFirstIndex,
                        primitiveIndices.Length,
                        materialSlot));
            }
        }

        if (vertices.Count == 0)
        {
            throw new InvalidDataException("glTF asset produced no vertices.");
        }

        var bounds =
            new AssetBounds(
                minX,
                minY,
                minZ,
                maxX,
                maxY,
                maxZ);
        if (!bounds.IsValid)
        {
            throw new InvalidDataException("glTF mesh bounds are invalid.");
        }

        RuntimeMeshAttributes runtimeAttributes =
            RuntimeMeshAttributes.Normal;
        if (allHaveUv0)
        {
            runtimeAttributes |=
                RuntimeMeshAttributes.Uv0;
        }

        if (allHaveTangents)
        {
            runtimeAttributes |=
                RuntimeMeshAttributes.Tangent;
        }

        var runtimeVertices =
            vertices
                .Select(
                    static vertex =>
                        new RuntimeMeshVertex(
                            vertex.X,
                            vertex.Y,
                            vertex.Z,
                            vertex.Nx,
                            vertex.Ny,
                            vertex.Nz,
                            vertex.U,
                            vertex.V,
                            vertex.Tx,
                            vertex.Ty,
                            vertex.Tz,
                            vertex.Tw))
                .ToArray();
        var runtimeMesh =
            new RuntimeMeshData(
                runtimeAttributes,
                runtimeVertices,
                indices,
                materialIds,
                sections);

        return new ImportedAssetPayload(
            runtimeMesh.ToPayload(),
            bounds,
            [],
            new MeshImportSummary(
                runtimeVertices.Length,
                indices.Count,
                sections.Count,
                materialIds.Count,
                runtimeMesh.HasUv0,
                runtimeMesh.HasTangents,
                generatedNormalVertexCount,
                generatedTangentVertexCount,
                fallbackSectionCount));
    }

    private static int ResolveMaterialSlot(
        JsonElement primitive,
        int materialCount)
    {
        if (primitive.TryGetProperty(
                "material",
                out JsonElement materialProperty))
        {
            int slot =
                materialProperty.GetInt32();
            if (slot < 0 ||
                slot >= materialCount)
            {
                throw new InvalidDataException(
                    $"glTF primitive material slot {slot} has no matching stable material reference.");
            }

            return slot;
        }

        return materialCount switch
        {
            0 => -1,
            1 => 0,
            _ => throw new InvalidDataException(
                "glTF primitive omits its material slot while the mesh declares multiple stable material references."),
        };
    }

    private static bool TryNormalizeNormals(
        List<VertexData> vertices,
        int vertexBase,
        int vertexCount)
    {
        for (int index = 0; index < vertexCount; index++)
        {
            VertexData vertex =
                vertices[vertexBase + index];
            var normal =
                new Vector3(
                    vertex.Nx,
                    vertex.Ny,
                    vertex.Nz);
            if (!IsFinite(normal) ||
                normal.LengthSquared() <= 1e-12f)
            {
                return false;
            }

            normal =
                Vector3.Normalize(
                    normal);
            vertices[vertexBase + index] =
                vertex with
                {
                    Nx = normal.X,
                    Ny = normal.Y,
                    Nz = normal.Z
                };
        }

        return true;
    }

    private static void GenerateNormals(
        List<VertexData> vertices,
        int vertexBase,
        int vertexCount,
        uint[] indices)
    {
        var sums =
            new Vector3[vertexCount];

        for (int index = 0; index < indices.Length; index += 3)
        {
            int a = checked((int)indices[index]);
            int b = checked((int)indices[index + 1]);
            int c = checked((int)indices[index + 2]);

            Vector3 p0 =
                Position(vertices[vertexBase + a]);
            Vector3 p1 =
                Position(vertices[vertexBase + b]);
            Vector3 p2 =
                Position(vertices[vertexBase + c]);
            Vector3 face =
                Vector3.Cross(
                    p1 - p0,
                    p2 - p0);

            if (face.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            sums[a] += face;
            sums[b] += face;
            sums[c] += face;
        }

        for (int index = 0; index < vertexCount; index++)
        {
            Vector3 normal =
                sums[index];
            if (normal.LengthSquared() <= 1e-12f)
            {
                normal =
                    Vector3.UnitY;
            }
            else
            {
                normal =
                    Vector3.Normalize(
                        normal);
            }

            VertexData vertex =
                vertices[vertexBase + index];
            vertices[vertexBase + index] =
                vertex with
                {
                    Nx = normal.X,
                    Ny = normal.Y,
                    Nz = normal.Z
                };
        }
    }

    private static bool TryNormalizeTangents(
        List<VertexData> vertices,
        int vertexBase,
        int vertexCount)
    {
        for (int index = 0; index < vertexCount; index++)
        {
            VertexData vertex =
                vertices[vertexBase + index];
            var tangent =
                new Vector3(
                    vertex.Tx,
                    vertex.Ty,
                    vertex.Tz);
            if (!IsFinite(tangent) ||
                tangent.LengthSquared() <= 1e-12f ||
                !float.IsFinite(vertex.Tw) ||
                MathF.Abs(vertex.Tw) <= 1e-6f)
            {
                return false;
            }

            tangent =
                Vector3.Normalize(
                    tangent);
            vertices[vertexBase + index] =
                vertex with
                {
                    Tx = tangent.X,
                    Ty = tangent.Y,
                    Tz = tangent.Z,
                    Tw = vertex.Tw < 0f ? -1f : 1f
                };
        }

        return true;
    }

    private static bool GenerateTangents(
        List<VertexData> vertices,
        int vertexBase,
        int vertexCount,
        uint[] indices)
    {
        var tangentSums =
            new Vector3[vertexCount];
        var bitangentSums =
            new Vector3[vertexCount];
        bool generatedAny =
            false;

        for (int index = 0; index < indices.Count; index += 3)
        {
            int a = checked((int)indices[index]);
            int b = checked((int)indices[index + 1]);
            int c = checked((int)indices[index + 2]);
            VertexData v0 = vertices[vertexBase + a];
            VertexData v1 = vertices[vertexBase + b];
            VertexData v2 = vertices[vertexBase + c];

            Vector3 edge1 = Position(v1) - Position(v0);
            Vector3 edge2 = Position(v2) - Position(v0);
            float du1 = v1.U - v0.U;
            float dv1 = v1.V - v0.V;
            float du2 = v2.U - v0.U;
            float dv2 = v2.V - v0.V;
            float determinant =
                du1 * dv2 -
                dv1 * du2;

            if (!float.IsFinite(determinant) ||
                MathF.Abs(determinant) <= 1e-10f)
            {
                continue;
            }

            float reciprocal =
                1f /
                determinant;
            Vector3 tangent =
                (edge1 * dv2 -
                 edge2 * dv1) *
                reciprocal;
            Vector3 bitangent =
                (edge2 * du1 -
                 edge1 * du2) *
                reciprocal;

            if (!IsFinite(tangent) ||
                !IsFinite(bitangent))
            {
                continue;
            }

            tangentSums[a] += tangent;
            tangentSums[b] += tangent;
            tangentSums[c] += tangent;
            bitangentSums[a] += bitangent;
            bitangentSums[b] += bitangent;
            bitangentSums[c] += bitangent;
            generatedAny = true;
        }

        if (!generatedAny)
        {
            return false;
        }

        for (int index = 0; index < vertexCount; index++)
        {
            VertexData vertex =
                vertices[vertexBase + index];
            var normal =
                Vector3.Normalize(
                    new Vector3(
                        vertex.Nx,
                        vertex.Ny,
                        vertex.Nz));
            Vector3 tangent =
                tangentSums[index] -
                normal *
                Vector3.Dot(
                    normal,
                    tangentSums[index]);

            if (!IsFinite(tangent) ||
                tangent.LengthSquared() <= 1e-12f)
            {
                return false;
            }

            tangent =
                Vector3.Normalize(
                    tangent);
            float handedness =
                Vector3.Dot(
                    Vector3.Cross(
                        normal,
                        tangent),
                    bitangentSums[index]) <
                0f
                    ? -1f
                    : 1f;

            vertices[vertexBase + index] =
                vertex with
                {
                    Tx = tangent.X,
                    Ty = tangent.Y,
                    Tz = tangent.Z,
                    Tw = handedness
                };
        }

        return true;
    }

    private static Vector3 Position(
        VertexData vertex) =>
        new(
            vertex.X,
            vertex.Y,
            vertex.Z);

    private static bool IsFinite(
        Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static LoadedGltf Load(string path, string sourceRoot)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            var document = JsonDocument.Parse(File.ReadAllBytes(path));
            try
            {
                var buffers = LoadBuffers(document.RootElement, path, sourceRoot, null);
                return new LoadedGltf(document, buffers);
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        if (!extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Mesh format '{extension}' is not supported.");
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != GlbMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
        {
            throw new InvalidDataException("GLB header is invalid or not version 2.");
        }

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (declaredLength != bytes.Length)
        {
            throw new InvalidDataException(
                $"GLB declared length {declaredLength} does not match file length {bytes.Length}.");
        }

        byte[]? jsonChunk = null;
        byte[]? binaryChunk = null;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;

            if (chunkLength > int.MaxValue || offset + (long)chunkLength > bytes.Length)
            {
                throw new InvalidDataException("GLB chunk length is invalid.");
            }

            var chunk = bytes.AsSpan(offset, (int)chunkLength).ToArray();
            if (chunkType == JsonChunkType && jsonChunk is null)
            {
                jsonChunk = TrimJsonPadding(chunk);
            }
            else if (chunkType == BinChunkType && binaryChunk is null)
            {
                binaryChunk = chunk;
            }

            offset += (int)chunkLength;
        }

        if (jsonChunk is null)
        {
            throw new InvalidDataException("GLB does not contain a JSON chunk.");
        }

        var glbDocument = JsonDocument.Parse(jsonChunk);
        try
        {
            var buffers = LoadBuffers(glbDocument.RootElement, path, sourceRoot, binaryChunk);
            return new LoadedGltf(glbDocument, buffers);
        }
        catch
        {
            glbDocument.Dispose();
            throw;
        }
    }

    private static byte[][] LoadBuffers(
        JsonElement root,
        string gltfPath,
        string sourceRoot,
        byte[]? binaryChunk)
    {
        if (!root.TryGetProperty("buffers", out var buffersElement))
        {
            return [];
        }

        var sourceDirectory = Path.GetDirectoryName(gltfPath)
            ?? throw new InvalidDataException($"Could not resolve source directory for '{gltfPath}'.");

        var buffers = new byte[buffersElement.GetArrayLength()][];
        var bufferIndex = 0;
        foreach (var bufferElement in buffersElement.EnumerateArray())
        {
            byte[] data;
            if (bufferElement.TryGetProperty("uri", out var uriProperty))
            {
                var uri = uriProperty.GetString()
                    ?? throw new InvalidDataException("glTF buffer URI is null.");

                data = uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? DecodeDataUri(uri)
                    : File.ReadAllBytes(
                        AssetHashing.ResolveWithinRoot(
                            sourceRoot,
                            Path.Combine(
                                sourceDirectory,
                                Uri.UnescapeDataString(uri.Replace('/', Path.DirectorySeparatorChar)))));
            }
            else
            {
                data = binaryChunk
                    ?? throw new InvalidDataException(
                        "glTF buffer has no URI and no GLB binary chunk is available.");
            }

            if (bufferElement.TryGetProperty("byteLength", out var byteLengthProperty))
            {
                var byteLength = byteLengthProperty.GetInt32();
                if (byteLength < 0 || data.Length < byteLength)
                {
                    throw new InvalidDataException(
                        $"glTF buffer {bufferIndex} contains {data.Length} bytes but declares {byteLength}.");
                }
            }

            buffers[bufferIndex++] = data;
        }

        return buffers;
    }

    private static float[] ReadFloatAccessor(
        JsonElement root,
        byte[][] buffers,
        int accessorIndex,
        string expectedType)
    {
        var accessor = GetArrayElement(root, "accessors", accessorIndex);
        if (accessor.TryGetProperty("sparse", out _))
        {
            throw new InvalidDataException("Sparse glTF accessors are not supported.");
        }

        var componentType = accessor.GetProperty("componentType").GetInt32();
        var type = accessor.GetProperty("type").GetString();
        if (componentType != 5126 || !string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"glTF accessor {accessorIndex} must be FLOAT {expectedType}.");
        }

        int components =
            expectedType switch
            {
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                _ => throw new InvalidDataException(
                    $"Unsupported floating-point accessor type '{expectedType}'.")
            };
        var count = accessor.GetProperty("count").GetInt32();
        if (count <= 0)
        {
            throw new InvalidDataException($"glTF accessor {accessorIndex} has invalid count {count}.");
        }

        var viewIndex = accessor.GetProperty("bufferView").GetInt32();
        var view = GetArrayElement(root, "bufferViews", viewIndex);
        var bufferIndex = view.GetProperty("buffer").GetInt32();
        if ((uint)bufferIndex >= buffers.Length)
        {
            throw new InvalidDataException($"glTF buffer view {viewIndex} references missing buffer {bufferIndex}.");
        }

        var viewOffset = view.TryGetProperty("byteOffset", out var viewOffsetProperty)
            ? viewOffsetProperty.GetInt32()
            : 0;
        var accessorOffset = accessor.TryGetProperty("byteOffset", out var accessorOffsetProperty)
            ? accessorOffsetProperty.GetInt32()
            : 0;
        var elementSize = checked(components * sizeof(float));
        var stride = view.TryGetProperty("byteStride", out var strideProperty)
            ? strideProperty.GetInt32()
            : elementSize;

        if (stride < elementSize)
        {
            throw new InvalidDataException($"glTF buffer view {viewIndex} byteStride is smaller than the accessor element.");
        }

        var buffer = buffers[bufferIndex];
        var values = new float[checked(count * components)];
        for (var item = 0; item < count; item++)
        {
            var itemOffset = checked(viewOffset + accessorOffset + (item * stride));
            EnsureRange(buffer, itemOffset, elementSize, $"accessor {accessorIndex}");

            for (var component = 0; component < components; component++)
            {
                var bits = BinaryPrimitives.ReadInt32LittleEndian(
                    buffer.AsSpan(itemOffset + (component * sizeof(float)), sizeof(float)));
                values[(item * components) + component] = BitConverter.Int32BitsToSingle(bits);
            }
        }

        return values;
    }

    private static uint[] ReadIndexAccessor(
        JsonElement root,
        byte[][] buffers,
        int accessorIndex)
    {
        var accessor = GetArrayElement(root, "accessors", accessorIndex);
        if (accessor.TryGetProperty("sparse", out _))
        {
            throw new InvalidDataException("Sparse glTF index accessors are not supported.");
        }

        if (!string.Equals(accessor.GetProperty("type").GetString(), "SCALAR", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"glTF index accessor {accessorIndex} must be SCALAR.");
        }

        var componentType = accessor.GetProperty("componentType").GetInt32();
        var componentSize = componentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            _ => throw new InvalidDataException(
                $"glTF index accessor {accessorIndex} component type {componentType} is unsupported."),
        };

        var count = accessor.GetProperty("count").GetInt32();
        if (count <= 0)
        {
            throw new InvalidDataException($"glTF index accessor {accessorIndex} has invalid count {count}.");
        }

        var viewIndex = accessor.GetProperty("bufferView").GetInt32();
        var view = GetArrayElement(root, "bufferViews", viewIndex);
        var bufferIndex = view.GetProperty("buffer").GetInt32();
        if ((uint)bufferIndex >= buffers.Length)
        {
            throw new InvalidDataException($"glTF buffer view {viewIndex} references missing buffer {bufferIndex}.");
        }

        var viewOffset = view.TryGetProperty("byteOffset", out var viewOffsetProperty)
            ? viewOffsetProperty.GetInt32()
            : 0;
        var accessorOffset = accessor.TryGetProperty("byteOffset", out var accessorOffsetProperty)
            ? accessorOffsetProperty.GetInt32()
            : 0;
        var stride = view.TryGetProperty("byteStride", out var strideProperty)
            ? strideProperty.GetInt32()
            : componentSize;

        if (stride < componentSize)
        {
            throw new InvalidDataException($"glTF buffer view {viewIndex} byteStride is invalid for indices.");
        }

        var buffer = buffers[bufferIndex];
        var values = new uint[count];
        for (var item = 0; item < count; item++)
        {
            var itemOffset = checked(viewOffset + accessorOffset + (item * stride));
            EnsureRange(buffer, itemOffset, componentSize, $"index accessor {accessorIndex}");
            values[item] = componentType switch
            {
                5121 => buffer[itemOffset],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(itemOffset, 2)),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(itemOffset, 4)),
                _ => throw new InvalidOperationException("Unsupported index component type reached after validation."),
            };
        }

        return values;
    }

    private static JsonElement GetArrayElement(JsonElement root, string propertyName, int index)
    {
        if (!root.TryGetProperty(propertyName, out var array) ||
            index < 0 ||
            index >= array.GetArrayLength())
        {
            throw new InvalidDataException($"glTF {propertyName} index {index} is invalid.");
        }

        return array[index];
    }

    private static byte[] DecodeDataUri(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only base64 glTF data URIs are supported.");
        }

        try
        {
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("glTF data URI contains invalid base64 data.", exception);
        }
    }

    private static byte[] TrimJsonPadding(byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 0 && (bytes[length - 1] == 0 || bytes[length - 1] == 0x20))
        {
            length--;
        }

        return bytes.AsSpan(0, length).ToArray();
    }

    private static void EnsureRange(byte[] buffer, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset + (long)length > buffer.Length)
        {
            throw new InvalidDataException($"glTF {description} exceeds its buffer bounds.");
        }
    }

    private sealed class LoadedGltf(JsonDocument document, byte[][] buffers) : IDisposable
    {
        public JsonDocument Document { get; } = document;

        public byte[][] Buffers { get; } = buffers;

        public void Dispose() => Document.Dispose();
    }

    private readonly record struct VertexData(
        float X,
        float Y,
        float Z,
        float Nx,
        float Ny,
        float Nz,
        float U,
        float V,
        float Tx,
        float Ty,
        float Tz,
        float Tw);
}

internal readonly record struct MeshMaterialRequirement(
    bool RequiresUv0,
    bool RequiresTangents);
