using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class RuntimeWorldAssetResources : IDisposable
{
    private readonly IGraphicsDevice _graphics;
    private readonly RuntimeAssetCatalog _catalog;
    private readonly Dictionary<AssetId, RuntimeMeshBuffers> _meshes = [];
    private readonly Dictionary<AssetId, RuntimeMaterialResources> _materials = [];
    private readonly Dictionary<AssetId, RuntimeTextureResource> _textures = [];
    private readonly IGraphicsTexture _whiteBaseColor;
    private readonly IGraphicsTexture _flatNormal;
    private readonly IGraphicsTexture _neutralOrm;
    private readonly IGraphicsTexture _blackEmissive;
    private readonly IGraphicsTexture _missingBaseColor;
    private long _materialBindingFailures;
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

        IGraphicsTexture? whiteBaseColor = null;
        IGraphicsTexture? flatNormal = null;
        IGraphicsTexture? neutralOrm = null;
        IGraphicsTexture? blackEmissive = null;
        IGraphicsTexture? missingBaseColor = null;

        try
        {
            whiteBaseColor =
                CreateSolidTexture(
                    255,
                    255,
                    255,
                    255,
                    GraphicsTextureColorSpace.Srgb);
            flatNormal =
                CreateSolidTexture(
                    128,
                    128,
                    255,
                    255,
                    GraphicsTextureColorSpace.Linear);
            neutralOrm =
                CreateSolidTexture(
                    255,
                    255,
                    0,
                    255,
                    GraphicsTextureColorSpace.Linear);
            blackEmissive =
                CreateSolidTexture(
                    0,
                    0,
                    0,
                    255,
                    GraphicsTextureColorSpace.Srgb);
            missingBaseColor =
                CreateSolidTexture(
                    255,
                    0,
                    255,
                    255,
                    GraphicsTextureColorSpace.Srgb);

            _whiteBaseColor =
                whiteBaseColor!;
            _flatNormal =
                flatNormal!;
            _neutralOrm =
                neutralOrm!;
            _blackEmissive =
                blackEmissive!;
            _missingBaseColor =
                missingBaseColor!;
        }
        catch
        {
            missingBaseColor?.Dispose();
            blackEmissive?.Dispose();
            neutralOrm?.Dispose();
            flatNormal?.Dispose();
            whiteBaseColor?.Dispose();
            throw;
        }
    }

    public RuntimeMaterialDiagnostics MaterialDiagnostics
    {
        get
        {
            ThrowIfDisposed();

            return new RuntimeMaterialDiagnostics(
                _materials.Count,
                _textures.Count,
                _materialBindingFailures);
        }
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

    public RuntimeMaterialResources ResolveMaterial(
        string assetId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(
            assetId);

        AssetId id =
            AssetId.Parse(
                assetId);

        if (_materials.TryGetValue(
                id,
                out RuntimeMaterialResources cached))
        {
            return cached;
        }

        RuntimeMaterialResources material;

        try
        {
            if (!_catalog.TryGet(
                    id,
                    out RuntimeAssetRecord? record) ||
                record is null ||
                record.Type != RuntimeAssetType.Material)
            {
                RecordMaterialBindingFailure(
                    id,
                    "material asset is missing or has the wrong runtime type");
                material =
                    CreateMissingMaterial(
                        id);
            }
            else
            {
                RuntimeAssetContent content =
                    _catalog.Read(
                        id);
                RuntimeMaterialData data =
                    RuntimeMaterialData.FromPayload(
                        content.Payload);
                material =
                    CreateMaterialResources(
                        id,
                        data);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            ArgumentException or
            KeyNotFoundException)
        {
            RecordMaterialBindingFailure(
                id,
                exception.Message);
            material =
                CreateMissingMaterial(
                    id);
        }

        _materials.Add(
            id,
            material);
        return material;
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
        Vector4 baseTint =
            ResolveMaterial(
                definition.MaterialAssetId)
            .BaseColorFactor;

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
        tint =
            ResolveMaterial(
                assetId)
            .BaseColorFactor;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (RuntimeMeshBuffers mesh in
                 _meshes.Values)
        {
            mesh.Dispose();
        }

        foreach (RuntimeTextureResource texture in
                 _textures.Values)
        {
            texture.Texture.Dispose();
        }

        _missingBaseColor.Dispose();
        _blackEmissive.Dispose();
        _neutralOrm.Dispose();
        _flatNormal.Dispose();
        _whiteBaseColor.Dispose();

        _meshes.Clear();
        _materials.Clear();
        _textures.Clear();
        _disposed = true;
    }

    private RuntimeMaterialResources CreateMaterialResources(
        AssetId materialId,
        RuntimeMaterialData data)
    {
        bool hasBindingFailure =
            false;

        IGraphicsTexture baseColor =
            ResolveTexture(
                data.BaseColorTexture,
                RuntimeTextureUsage.Color,
                _whiteBaseColor,
                ref hasBindingFailure);
        IGraphicsTexture normal =
            ResolveTexture(
                data.NormalTexture,
                RuntimeTextureUsage.Normal,
                _flatNormal,
                ref hasBindingFailure);
        IGraphicsTexture orm =
            ResolveTexture(
                data.OrmTexture,
                RuntimeTextureUsage.Orm,
                _neutralOrm,
                ref hasBindingFailure);
        IGraphicsTexture emissive =
            ResolveTexture(
                data.EmissiveTexture,
                RuntimeTextureUsage.Emissive,
                _blackEmissive,
                ref hasBindingFailure);

        Vector4 baseColorFactor =
            hasBindingFailure
                ? new Vector4(
                    1.0f,
                    0.0f,
                    1.0f,
                    1.0f)
                : data.BaseColorFactor;

        if (hasBindingFailure)
        {
            baseColor =
                _missingBaseColor;
        }

        return new RuntimeMaterialResources(
            materialId,
            baseColor,
            normal,
            orm,
            emissive,
            baseColorFactor,
            data.RoughnessFactor,
            data.MetallicFactor,
            data.EmissiveMultiplier,
            data.UvScale,
            hasBindingFailure);
    }

    private RuntimeMaterialResources CreateMissingMaterial(
        AssetId materialId) =>
        new(
            materialId,
            _missingBaseColor,
            _flatNormal,
            _neutralOrm,
            _blackEmissive,
            new Vector4(
                1.0f,
                0.0f,
                1.0f,
                1.0f),
            1.0f,
            0.0f,
            0.0f,
            Vector2.One,
            true);

    private IGraphicsTexture ResolveTexture(
        AssetId? textureId,
        RuntimeTextureUsage expectedUsage,
        IGraphicsTexture fallback,
        ref bool hasBindingFailure)
    {
        if (textureId is null)
        {
            return fallback;
        }

        AssetId id =
            textureId.Value;

        if (_textures.TryGetValue(
                id,
                out RuntimeTextureResource cached))
        {
            if (cached.Usage == expectedUsage)
            {
                return cached.Texture;
            }

            hasBindingFailure =
                true;
            RecordMaterialBindingFailure(
                id,
                $"texture usage {cached.Usage} does not match required usage {expectedUsage}");
            return fallback;
        }

        try
        {
            if (!_catalog.TryGet(
                    id,
                    out RuntimeAssetRecord? record) ||
                record is null ||
                record.Type != RuntimeAssetType.Texture)
            {
                hasBindingFailure =
                    true;
                RecordMaterialBindingFailure(
                    id,
                    "texture asset is missing or has the wrong runtime type");
                return fallback;
            }

            RuntimeAssetContent content =
                _catalog.Read(
                    id);
            RuntimeTextureData data =
                RuntimeTextureData.FromPayload(
                    content.Payload);

            if (data.Usage != expectedUsage)
            {
                hasBindingFailure =
                    true;
                RecordMaterialBindingFailure(
                    id,
                    $"texture usage {data.Usage} does not match required usage {expectedUsage}");
                return fallback;
            }

            IGraphicsTexture texture =
                CreateGraphicsTexture(
                    data);
            _textures.Add(
                id,
                new RuntimeTextureResource(
                    texture,
                    data.Usage));
            return texture;
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            ArgumentException or
            KeyNotFoundException)
        {
            hasBindingFailure =
                true;
            RecordMaterialBindingFailure(
                id,
                exception.Message);
            return fallback;
        }
    }

    private IGraphicsTexture CreateGraphicsTexture(
        RuntimeTextureData data) =>
        _graphics.CreateTexture(
            new GraphicsTextureData(
                new GraphicsTextureDescription(
                    data.Width,
                    data.Height,
                    GraphicsTextureFormat.Rgba8Unorm,
                    data.ColorSpace ==
                    RuntimeTextureColorSpace.Srgb
                        ? GraphicsTextureColorSpace.Srgb
                        : GraphicsTextureColorSpace.Linear,
                    data.Mips.Count),
                data.Mips.Select(
                    static mip =>
                        new GraphicsTextureMipData(
                            mip.Width,
                            mip.Height,
                            mip.RowPitch,
                            mip.Pixels))));

    private IGraphicsTexture CreateSolidTexture(
        byte red,
        byte green,
        byte blue,
        byte alpha,
        GraphicsTextureColorSpace colorSpace) =>
        _graphics.CreateTexture(
            new GraphicsTextureData(
                new GraphicsTextureDescription(
                    1,
                    1,
                    GraphicsTextureFormat.Rgba8Unorm,
                    colorSpace,
                    1),
                [
                    new GraphicsTextureMipData(
                        1,
                        1,
                        4,
                        [
                            red,
                            green,
                            blue,
                            alpha
                        ])
                ]));

    private void RecordMaterialBindingFailure(
        AssetId assetId,
        string reason)
    {
        _materialBindingFailures++;

        Console.WriteLine(
            $"[graphics:material] fallback asset=\"{assetId}\" reason=\"{reason}\"");
    }

    private RuntimeMeshBuffers CreateMeshBuffers(
        byte[] payload)
    {
        RuntimeMeshData data =
            RuntimeMeshData.FromPayload(
                payload);
        RuntimeMeshVertex[] vertices =
            data.Vertices.ToArray();
        uint[] indices =
            data.Indices.ToArray();
        int vertexByteCount =
            checked(
                vertices.Length *
                RuntimeMeshData.VertexStride);
        int indexByteCount =
            checked(
                indices.Length *
                sizeof(uint));

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
            vertexBuffer.SetData<RuntimeMeshVertex>(
                vertices);
            indexBuffer.SetData<uint>(
                indices);

            int[] materialSlots =
                data.Sections
                    .Select(
                        static section =>
                            section.MaterialSlot)
                    .Distinct()
                    .ToArray();
            bool usesDevelopmentFallback =
                materialSlots.Any(
                    static slot =>
                        slot < 0) ||
                materialSlots.Length != 1;
            bool hasMaterial =
                !usesDevelopmentFallback &&
                materialSlots.Length == 1 &&
                materialSlots[0] >= 0 &&
                materialSlots[0] < data.MaterialIds.Count;
            AssetId materialId =
                hasMaterial
                    ? AssetId.Parse(
                        data.MaterialIds[
                            materialSlots[0]])
                    : default;

            if (materialSlots.Length > 1)
            {
                Console.WriteLine(
                    "[graphics:mesh] development fallback reason=\"multiple material slots require split draw support\"");
            }

            return new RuntimeMeshBuffers(
                vertexBuffer,
                indexBuffer,
                RuntimeMeshData.VertexStride,
                indices.Length,
                data.Attributes,
                materialId,
                hasMaterial,
                usesDevelopmentFallback,
                data.Sections.Count);
        }
        catch
        {
            indexBuffer.Dispose();
            vertexBuffer.Dispose();
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

public readonly record struct RuntimeMaterialDiagnostics(
    int LoadedMaterialCount,
    int LoadedAssetTextureCount,
    long BindingFailureCount);

internal readonly record struct RuntimeMaterialResources(
    AssetId MaterialId,
    IGraphicsTexture BaseColorTexture,
    IGraphicsTexture NormalTexture,
    IGraphicsTexture OrmTexture,
    IGraphicsTexture EmissiveTexture,
    Vector4 BaseColorFactor,
    float RoughnessFactor,
    float MetallicFactor,
    float EmissiveMultiplier,
    Vector2 UvScale,
    bool UsesDevelopmentFallback);

internal readonly record struct RuntimeTextureResource(
    IGraphicsTexture Texture,
    RuntimeTextureUsage Usage);

internal readonly record struct RuntimeMeshBuffers(
    IGraphicsBuffer VertexBuffer,
    IGraphicsBuffer IndexBuffer,
    int VertexStride,
    int IndexCount,
    RuntimeMeshAttributes Attributes,
    AssetId MaterialId,
    bool HasMaterial,
    bool UsesDevelopmentFallback,
    int SectionCount) : IDisposable
{
    public bool IsValid =>
        VertexBuffer is not null &&
        IndexBuffer is not null &&
        VertexStride == RuntimeMeshData.VertexStride &&
        IndexCount > 0 &&
        SectionCount > 0;

    public bool HasUv0 =>
        (Attributes & RuntimeMeshAttributes.Uv0) != 0;

    public bool HasTangents =>
        (Attributes & RuntimeMeshAttributes.Tangent) != 0;

    public bool SupportsTexturedMaterial =>
        HasUv0 &&
        !UsesDevelopmentFallback;

    public void Dispose()
    {
        IndexBuffer?.Dispose();
        VertexBuffer?.Dispose();
    }
}
