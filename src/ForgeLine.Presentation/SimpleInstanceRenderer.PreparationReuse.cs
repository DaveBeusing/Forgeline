namespace ForgeLine.Presentation;

public readonly record struct RenderPreparationReuseMetrics(int MeshLookupHits, int MaterialLookupHits,
    int DeformedPoses, int ReusedPoses, int ReusedSkinUploads);

public sealed partial class SimpleInstanceRenderer
{
    public RenderPreparationReuseMetrics PreparationReuse => new(
        _runtimeAssets?.ReusedMeshLookups ?? 0,
        _runtimeAssets?.ReusedMaterialLookups ?? 0,
        _skinFrames?.DeformationEvaluations ?? 0,
        _skinFrames?.ReusedPoses ?? 0,
        _skinFrames?.ReusedUploads ?? 0);
}
