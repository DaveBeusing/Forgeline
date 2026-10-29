using System.Numerics;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

public sealed partial class SimpleInstanceRenderer
{
    private bool _decorationPass;
    internal double DecorationCullingMilliseconds { get; private set; }
    internal double DecorationUploadMilliseconds { get; private set; }
    private readonly Dictionary<(string, bool), string> _decorationMeshes = [];
    private readonly Dictionary<(AssetId, float), Matrix4x4> _decorationNormalizations = [];
    internal void ResetDecorationBindings() { _decorationMeshes.Clear(); _decorationNormalizations.Clear(); _worldLods.Clear(); _previousLods.Clear(); _currentLods.Clear(); }
    private Func<SimpleInstanceRenderer>? _decorationFactory;
    private ChunkDecorationRenderer? _decorations;
    private RuntimeAssetCatalog? _decorationCatalog;
    public ChunkDecorationRenderSettings DecorationSettings { get; set; } = new();
    public ChunkDecorationDiagnostics DecorationDiagnostics => _decorations?.Diagnostics ?? default;
    internal long ResidentInstanceBufferBytes => _frameInstanceBuffers.Values.Sum(b => (long)b.Capacity * InstanceStride) +
        _shadowFrameInstanceBuffers.Values.Sum(b => (long)b.Capacity * InstanceStride);

    private void PrepareDecorations(RtsCamera camera, RenderWorld world, in ForgeLine.Presentation.RenderContracts.RenderPreparationRequest request)
    {
        if (world.CurrentSnapshot?.ChunkDecorations is null) return;
        _decorations ??= new(_decorationFactory!());
        _decorations.Prepare(camera, world, request, DecorationSettings);
    }

    private string DecorationMesh(in DecorationRenderMetadata decoration, bool reduced)
    {
        string high = decoration.MeshAssetId!;
        if (_decorationMeshes.TryGetValue((high, reduced), out string? cached)) return cached;
        string result = high;
        if (reduced && _decorationCatalog is not null && _decorationCatalog.TryGet(AssetId.Parse(high), out var record))
            result = record!.Lods.Where(l => l.Level > 0).OrderBy(l => l.Level).Select(l => l.AssetId).FirstOrDefault() ?? high;
        if (_decorationMeshes.Count == 128) throw new InvalidOperationException("Decoration LOD binding budget exceeded.");
        _decorationMeshes.Add((high, reduced), result);
        return result;
    }

    private Matrix4x4 DecorationNormalization(in DecorationRenderMetadata decoration, AssetId mesh)
    {
        if (_decorationCatalog is null)
            return Matrix4x4.CreateTranslation(0, .5f - decoration.PivotHeight, 0);
        if (_decorationNormalizations.TryGetValue((mesh, decoration.PivotHeight), out var normalization)) return normalization;
        if (!_decorationCatalog.TryGet(mesh, out var record) || record!.Bounds is not { IsValid: true } box)
            throw new InvalidOperationException("Decoration requires valid compiled mesh bounds for every admitted LOD.");
        normalization = NormalizeDecorationBounds(box, decoration.PivotHeight);
        if (_decorationNormalizations.Count == 128) throw new InvalidOperationException("Decoration normalization budget exceeded.");
        _decorationNormalizations.Add((mesh, decoration.PivotHeight), normalization);
        return normalization;
    }

    internal static Matrix4x4 NormalizeDecorationBounds(AssetBounds box, float pivotHeight)
    {
        if (!box.IsValid || !float.IsFinite(pivotHeight) || pivotHeight is < 0 or > 1)
            throw new InvalidOperationException("Decoration requires valid bounds and a normalized pivot.");
        Vector3 size = new(box.MaxX - box.MinX, box.MaxY - box.MinY, box.MaxZ - box.MinZ);
        if (size.X <= .000001f || size.Y <= .000001f || size.Z <= .000001f)
            throw new InvalidOperationException("Decoration mesh bounds must have positive physical dimensions.");
        Vector3 pivot = new((box.MinX + box.MaxX) * .5f, box.MinY + size.Y * pivotHeight, (box.MinZ + box.MaxZ) * .5f);
        return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale(Vector3.One / size);
    }
}
