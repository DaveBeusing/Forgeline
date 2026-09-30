using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class RuntimeWorldAssetResources : IDisposable
{
    private const int RuntimeMeshVersion = 1;
    private const int RuntimeMeshVertexStride = 32;

    private readonly IGraphicsDevice _graphics;
    private readonly RuntimeAssetCatalog _catalog;
    private readonly Dictionary<AssetId, RuntimeMeshBuffers> _meshes = [];
    private readonly Dictionary<AssetId, Vector4> _materialTints = [];
    private bool _disposed;

    public RuntimeWorldAssetResources(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog catalog)
    {
        _graphics =
            graphics ??
            throw new ArgumentNullException(nameof(graphics));
        _catalog =
            catalog ??
            throw new ArgumentNullException(nameof(catalog));
    }

    public bool TryGetMesh(
        in WorldFeaturePresentationMetadata feature,
        WorldAssetLod lod,
        out RuntimeMeshBuffers mesh) =>
        TryGetMesh(
            feature,
            lod,
            out mesh,
            out _);

    public bool TryGetMesh(
        in WorldFeaturePresentationMetadata feature,
        WorldAssetLod lod,
        out RuntimeMeshBuffers mesh,
        out AssetId assetId)
    {
        if (!feature.IsSpecified)
        {
            mesh = default;
            assetId = default;
            return false;
        }

        WorldPresentationDefinition definition =
            WorldPresentationCatalog.Get(
                feature.Visual);

        return TryGetMesh(
            lod == WorldAssetLod.Reduced
                ? definition.ReducedLodAssetId
                : definition.MeshAssetId,
            out mesh,
            out assetId);
    }

    public bool TryGetMesh(
        string assetId,
        out RuntimeMeshBuffers mesh) =>
        TryGetMesh(
            assetId,
            out mesh,
            out _);

    public bool TryGetMesh(
        string assetId,
        out RuntimeMeshBuffers mesh,
        out AssetId resolvedAssetId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(
            assetId);

        AssetId id =
            AssetId.Parse(
                assetId);

        if (_meshes.TryGetValue(
                id,
                out mesh))
        {
            resolvedAssetId =
                id;
            return true;
        }

        if (!_catalog.TryGet(
                id,
                out RuntimeAssetRecord? record) ||
            record is null ||
            record.Type != RuntimeAssetType.Mesh)
        {
            mesh = default;
            resolvedAssetId = default;
            return false;
        }

        RuntimeAssetContent content =
            _catalog.Read(
                id);
        mesh =
            CreateMeshBuffers(
                content.Payload);
        _meshes.Add(
            id,
            mesh);
        resolvedAssetId =
            id;
        return true;
    }

    public bool TryGetMaterialTint(
        in WorldFeaturePresentationMetadata feature,
        out Vector4 tint)
    {
        if (!feature.IsSpecified)
        {
            tint = default;
            return false;
        }

        WorldPresentationDefinition definition =
            WorldPresentationCatalog.Get(
                feature.Visual);

        if (!TryGetMaterialTint(
                definition.MaterialAssetId,
                out Vector4 baseTint))
        {
            tint = default;
            return false;
        }

        tint =
            WorldPresentationCatalog.ApplyStateTint(
                feature,
                baseTint);
        return true;
    }

    public bool TryGetMaterialTint(
        string assetId,
        out Vector4 tint)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(
            assetId);

        AssetId id =
            AssetId.Parse(
                assetId);

        if (_materialTints.TryGetValue(
                id,
                out tint))
        {
            return true;
        }

        if (!_catalog.TryGet(
                id,
                out RuntimeAssetRecord? record) ||
            record is null ||
            record.Type != RuntimeAssetType.Material)
        {
            tint = default;
            return false;
        }

        RuntimeAssetContent content =
            _catalog.Read(
                id);
        tint =
            ReadMaterialTint(
                content.Payload);
        _materialTints.Add(
            id,
            tint);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (RuntimeMeshBuffers mesh in _meshes.Values)
        {
            mesh.Dispose();
        }

        _meshes.Clear();
        _materialTints.Clear();
        _disposed = true;
    }

    private RuntimeMeshBuffers CreateMeshBuffers(
        byte[] payload)
    {
        using var stream =
            new MemoryStream(
                payload,
                writable: false);
        using var reader =
            new BinaryReader(
                stream);

        int version =
            reader.ReadInt32();
        if (version != RuntimeMeshVersion)
        {
            throw new InvalidDataException(
                $"Runtime mesh version {version} is unsupported.");
        }

        int vertexCount =
            reader.ReadInt32();
        int indexCount =
            reader.ReadInt32();
        if (vertexCount <= 0 ||
            indexCount <= 0 ||
            indexCount % 3 != 0)
        {
            throw new InvalidDataException(
                "Runtime mesh contains invalid vertex or index counts.");
        }

        int vertexByteCount =
            checked(
                vertexCount *
                RuntimeMeshVertexStride);
        int indexByteCount =
            checked(
                indexCount *
                sizeof(uint));
        long expectedLength =
            12L +
            vertexByteCount +
            indexByteCount;
        if (payload.LongLength != expectedLength)
        {
            throw new InvalidDataException(
                "Runtime mesh payload length does not match its declared counts.");
        }

        byte[] vertexBytes =
            reader.ReadBytes(
                vertexByteCount);
        byte[] indexBytes =
            reader.ReadBytes(
                indexByteCount);

        IGraphicsBuffer vertexBuffer =
            _graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked((ulong)vertexByteCount),
                    GraphicsBufferMemory.Upload));
        IGraphicsBuffer indexBuffer =
            _graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked((ulong)indexByteCount),
                    GraphicsBufferMemory.Upload));

        try
        {
            vertexBuffer.SetData<byte>(
                vertexBytes);
            indexBuffer.SetData<byte>(
                indexBytes);

            return new RuntimeMeshBuffers(
                vertexBuffer,
                indexBuffer,
                RuntimeMeshVertexStride,
                indexCount);
        }
        catch
        {
            indexBuffer.Dispose();
            vertexBuffer.Dispose();
            throw;
        }
    }

    private static Vector4 ReadMaterialTint(
        byte[] payload)
    {
        using JsonDocument document =
            JsonDocument.Parse(
                payload);

        if (!document.RootElement.TryGetProperty(
                "baseColorFactor",
                out JsonElement factor) ||
            factor.ValueKind != JsonValueKind.Array ||
            factor.GetArrayLength() != 4)
        {
            return Vector4.One;
        }

        var values =
            factor.EnumerateArray()
                .Select(
                    static value =>
                        value.GetSingle())
                .ToArray();

        return new Vector4(
            values[0],
            values[1],
            values[2],
            values[3]);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

internal readonly record struct RuntimeMeshBuffers(
    IGraphicsBuffer VertexBuffer,
    IGraphicsBuffer IndexBuffer,
    int VertexStride,
    int IndexCount) : IDisposable
{
    public bool IsValid =>
        VertexBuffer is not null &&
        IndexBuffer is not null &&
        VertexStride > 0 &&
        IndexCount > 0;

    public void Dispose()
    {
        IndexBuffer?.Dispose();
        VertexBuffer?.Dispose();
    }
}
