using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

// Each facade pins one device/catalog/profile scope; the last lease disposes through native fences.
internal sealed class RuntimeWorldAssetResources : IDisposable
{
    private readonly AssetResidencyLease _lease;
    private bool _disposed;
    private ResidentAssetStore Store { get { ObjectDisposedException.ThrowIf(_disposed, this); return _lease.Store; } }
    public RuntimeWorldAssetResources(IGraphicsDevice graphics, RuntimeAssetCatalog catalog, TextureDetailQuality textureDetail = TextureDetailQuality.Full)
        => _lease = AssetResidencyLease.Acquire(graphics, catalog, textureDetail);
    internal void PrepareRequiredAssets(CancellationToken cancellationToken) => Store.PrepareRequiredAssets(cancellationToken);
    internal AssetResidencyProgress Residency => _lease.Progress;
    public RuntimeMaterialDiagnostics MaterialDiagnostics => Store.MaterialDiagnostics;
    public bool TryGetMesh(string id, out RuntimeMeshBuffers mesh) => Store.TryGetMesh(id, out mesh);
    public bool TryGetMesh(string id, out RuntimeMeshBuffers mesh, out AssetId resolved) => Store.TryGetMesh(id, out mesh, out resolved);
    public bool TryGetMesh(in WorldFeaturePresentationMetadata feature, WorldAssetLod lod, out RuntimeMeshBuffers mesh) => Store.TryGetMesh(feature, lod, out mesh);
    public bool TryGetMesh(in WorldFeaturePresentationMetadata feature, WorldAssetLod lod, out RuntimeMeshBuffers mesh, out AssetId resolved) => Store.TryGetMesh(feature, lod, out mesh, out resolved);
    public RuntimeMaterialResources ResolveMaterial(string id) => Store.ResolveMaterial(id);
    public bool TryGetMaterialTint(string id, out Vector4 tint) => Store.TryGetMaterialTint(id, out tint);
    public bool TryGetMaterialTint(in WorldFeaturePresentationMetadata feature, out Vector4 tint) => Store.TryGetMaterialTint(feature, out tint);
    public void Dispose() { if (_disposed) return; _lease.Dispose(); _disposed = true; }
}

public readonly record struct RuntimeMaterialDiagnostics(
    int LoadedMaterialCount,
    int LoadedAssetTextureCount,
    long BindingFailureCount)
{
    public int LoadedMeshCount { get; init; }
    public long ResidentMeshBytes { get; init; }
    public int LoadedSkinCount { get; init; }
    public long ResidentSkinBytes { get; init; }
    public int LoadedAnimationCount { get; init; }
    public long ResidentAnimationBytes { get; init; }
}

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
    bool UsesDevelopmentFallback)
{
    public Vector2 UvOffset { get; init; }
}

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
    public int FirstIndex { get; init; }
    public RuntimeAnimationData? Clip { get; init; }
    public RuntimeSkinBinding? Skin { get; init; }
    public MechanicalPartBinding[]? MechanicalParts { get; init; }

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
