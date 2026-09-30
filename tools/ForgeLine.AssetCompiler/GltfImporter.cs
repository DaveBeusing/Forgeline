using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class GltfImporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    public static ImportedAssetPayload Import(string path, string sourceRoot)
    {
        using var gltf = Load(path, sourceRoot);
        var root = gltf.Document.RootElement;

        if (!root.TryGetProperty("asset", out var asset) ||
            !asset.TryGetProperty("version", out var version) ||
            version.GetString() is not { } versionString ||
            !versionString.StartsWith('2'))
        {
            throw new InvalidDataException("Only glTF 2.x assets are supported.");
        }

        if (!root.TryGetProperty("meshes", out var meshes) || meshes.GetArrayLength() == 0)
        {
            throw new InvalidDataException("glTF asset does not contain any meshes.");
        }

        var vertices = new List<VertexData>();
        var indices = new List<uint>();
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var minZ = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var maxZ = float.NegativeInfinity;

        foreach (var mesh in meshes.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.GetArrayLength() == 0)
            {
                throw new InvalidDataException("glTF mesh does not contain primitives.");
            }

            foreach (var primitive in primitives.EnumerateArray())
            {
                var mode = primitive.TryGetProperty("mode", out var modeProperty)
                    ? modeProperty.GetInt32()
                    : 4;
                if (mode != 4)
                {
                    throw new InvalidDataException($"glTF primitive mode {mode} is unsupported; triangles are required.");
                }

                if (!primitive.TryGetProperty("attributes", out var attributes) ||
                    !attributes.TryGetProperty("POSITION", out var positionAccessorProperty))
                {
                    throw new InvalidDataException("glTF primitive is missing POSITION data.");
                }

                var positions = ReadFloatAccessor(
                    root,
                    gltf.Buffers,
                    positionAccessorProperty.GetInt32(),
                    "VEC3");

                float[]? normals = null;
                if (attributes.TryGetProperty("NORMAL", out var normalAccessorProperty))
                {
                    normals = ReadFloatAccessor(
                        root,
                        gltf.Buffers,
                        normalAccessorProperty.GetInt32(),
                        "VEC3");

                    if (normals.Length != positions.Length)
                    {
                        throw new InvalidDataException("glTF NORMAL accessor count does not match POSITION.");
                    }
                }

                float[]? texCoords = null;
                if (attributes.TryGetProperty("TEXCOORD_0", out var texCoordAccessorProperty))
                {
                    texCoords = ReadFloatAccessor(
                        root,
                        gltf.Buffers,
                        texCoordAccessorProperty.GetInt32(),
                        "VEC2");

                    if (texCoords.Length / 2 != positions.Length / 3)
                    {
                        throw new InvalidDataException("glTF TEXCOORD_0 accessor count does not match POSITION.");
                    }
                }

                var vertexBase = checked((uint)vertices.Count);
                var vertexCount = positions.Length / 3;
                for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    var positionOffset = vertexIndex * 3;
                    var x = positions[positionOffset];
                    var y = positions[positionOffset + 1];
                    var z = positions[positionOffset + 2];

                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    {
                        throw new InvalidDataException("glTF POSITION data contains a non-finite coordinate.");
                    }

                    minX = MathF.Min(minX, x);
                    minY = MathF.Min(minY, y);
                    minZ = MathF.Min(minZ, z);
                    maxX = MathF.Max(maxX, x);
                    maxY = MathF.Max(maxY, y);
                    maxZ = MathF.Max(maxZ, z);

                    var nx = 0f;
                    var ny = 1f;
                    var nz = 0f;
                    if (normals is not null)
                    {
                        nx = normals[positionOffset];
                        ny = normals[positionOffset + 1];
                        nz = normals[positionOffset + 2];
                    }

                    var u = 0f;
                    var v = 0f;
                    if (texCoords is not null)
                    {
                        var texOffset = vertexIndex * 2;
                        u = texCoords[texOffset];
                        v = texCoords[texOffset + 1];
                    }

                    vertices.Add(new VertexData(x, y, z, nx, ny, nz, u, v));
                }

                uint[] primitiveIndices;
                if (primitive.TryGetProperty("indices", out var indicesProperty))
                {
                    primitiveIndices = ReadIndexAccessor(root, gltf.Buffers, indicesProperty.GetInt32());
                }
                else
                {
                    primitiveIndices = new uint[vertexCount];
                    for (var index = 0; index < vertexCount; index++)
                    {
                        primitiveIndices[index] = (uint)index;
                    }
                }

                if (primitiveIndices.Length == 0 || primitiveIndices.Length % 3 != 0)
                {
                    throw new InvalidDataException("glTF triangle primitive index count must be a non-zero multiple of three.");
                }

                foreach (var index in primitiveIndices)
                {
                    if (index >= vertexCount)
                    {
                        throw new InvalidDataException(
                            $"glTF primitive index {index} exceeds vertex count {vertexCount}.");
                    }

                    indices.Add(checked(vertexBase + index));
                }
            }
        }

        if (vertices.Count == 0)
        {
            throw new InvalidDataException("glTF asset produced no vertices.");
        }

        var bounds = new AssetBounds(minX, minY, minZ, maxX, maxY, maxZ);
        if (!bounds.IsValid)
        {
            throw new InvalidDataException("glTF mesh bounds are invalid.");
        }

        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(vertices.Count);
            writer.Write(indices.Count);

            foreach (var vertex in vertices)
            {
                writer.Write(vertex.X);
                writer.Write(vertex.Y);
                writer.Write(vertex.Z);
                writer.Write(vertex.Nx);
                writer.Write(vertex.Ny);
                writer.Write(vertex.Nz);
                writer.Write(vertex.U);
                writer.Write(vertex.V);
            }

            foreach (var index in indices)
            {
                writer.Write(index);
            }
        }

        return new ImportedAssetPayload(payloadStream.ToArray(), bounds, []);
    }

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

        var components = expectedType == "VEC3" ? 3 : 2;
        var count = accessor.GetProperty("count").GetInt32();
        if (count <= 0)
        {
            throw new InvalidDataException($"glTF accessor {accessorIndex} has invalid count {count}.");
        }

        var viewIndex = accessor.GetProperty("bufferView").GetInt32();
        var view = GetArrayElement(root, "bufferViews", viewIndex);
        var bufferIndex = view.GetProperty("buffer").GetInt32();
        if ((uint)bufferIndex >= buffers.Count)
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
        if ((uint)bufferIndex >= buffers.Count)
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
        float V);
}
