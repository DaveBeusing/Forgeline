using System.Diagnostics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public readonly record struct RenderContentEvidence(long RecordingEpoch, int FrameIndex, GraphicsFrameSubmission ActualSubmission,
    int DistinctRuntimeMaterials, int SkinnedInstances, int MechanicalInstances, int MainInstances, int FallbackInstances,
    long ObserverStartTick, long ObserverEndTick);

// Evidence-only opt-in; normal rendering owns no recorder and never scans material identities.
public sealed partial class SimpleInstanceRenderer
{
    private ContentEvidenceRecorder? _contentEvidence;
    public void EnableContentEvidence()
    {
        ThrowIfDisposed();
        if (_graphics is not IGraphicsRenderTelemetry || _graphics.IsRecording) throw new NotSupportedException("Content evidence requires an idle owner with actual submission identity.");
        _contentEvidence ??= new ContentEvidenceRecorder();
    }
    private void RecordMainContentEvidence(IGraphicsCommandContext context)
    {
        if (!_graphics.IsRecording) throw new InvalidOperationException("Only the actual recording owner may stamp main content.");
        var recorder = _contentEvidence!;
        var source = (IGraphicsRenderTelemetry)_graphics;
        var pending = source.LastSubmission ?? throw new InvalidDataException("Actual pending submission identity unavailable.");
        long started = Stopwatch.GetTimestamp();
        recorder.Begin(pending, context.FrameIndex, _graphics.PresentedFrameCount);
        foreach (var pair in _batchLookup)
        {
            var key = pair.Key; var batch = pair.Value;
            recorder.AddBatch(batch.Instances.Count, key.UsesRuntimeMesh && batch.UsesRuntimeMesh && ContentEvidenceRecorder.IsRealMesh(key.RuntimeMeshId, batch.RuntimeMesh),
                key.UsesRuntimeMaterial && batch.UsesRuntimeMaterial && ContentEvidenceRecorder.IsRealMaterial(key.RuntimeMaterialId, batch.RuntimeMaterial), key.RuntimeMaterialId.Value, key.PartIndex);
        }
        recorder.Seal(started, Stopwatch.GetTimestamp());
    }
    // Last-successful-frame evidence: callers must use Prepare -> record -> successful Present,
    // and match this actual tuple to their newly acknowledged frame. Re-reading a prior success
    // after a later failed/unprepared attempt does not attest a new successful presentation.
    public RenderContentEvidence CaptureContentEvidence()
    {
        ThrowIfDisposed();
        if (_graphics.IsRecording || _contentEvidence is null || _graphics is not IGraphicsRenderTelemetry source || source.LastSubmission is not { } actual)
            throw new InvalidOperationException("Only an opted-in successfully completed main frame can expose consumed content.");
        return _contentEvidence.Read(actual, _graphics.PresentedFrameCount);
    }
}

internal sealed class ContentEvidenceRecorder
{
    private const int MaximumMaterials = 4096;
    private readonly HashSet<string> _materials = new(MaximumMaterials, StringComparer.Ordinal);
    private long _epoch, _sealedEpoch;
    private GraphicsFrameSubmission _pending;
    private ulong _presentedBefore;
    private int _frame, _skin, _mechanical, _instances, _fallback;
    private long _start, _end;
    internal static bool IsRealMesh(AssetId key, in RuntimeMeshBuffers mesh) =>
        mesh.IsValid && !mesh.UsesDevelopmentFallback && AssetId.TryParse(key.Value, out _);
    internal static bool IsRealMaterial(AssetId key, in RuntimeMaterialResources material) =>
        !material.UsesDevelopmentFallback && key == material.MaterialId && AssetId.TryParse(key.Value, out _);
    internal void Invalidate() { _epoch = checked(_epoch + 1); _sealedEpoch = 0; }
    internal void Begin(GraphicsFrameSubmission previous, int frameIndex, ulong presentedBefore)
    {
        Invalidate(); _pending = previous; _frame = frameIndex; _presentedBefore = presentedBefore;
        _materials.Clear(); _skin = _mechanical = _instances = _fallback = 0;
    }
    internal void AddBatch(int instances, bool actualRuntimeMesh, bool actualRuntimeMaterial, string material, int partIndex)
    {
        if (_sealedEpoch != 0 || _epoch == 0) throw new InvalidOperationException("Consumed batches require an unsealed current recording epoch.");
        if (instances < 0) throw new InvalidDataException("Negative consumed batch size.");
        if (instances == 0) return;
        _instances = checked(_instances + instances);
        if (!actualRuntimeMesh || !actualRuntimeMaterial || !AssetId.TryParse(material, out _)) { _fallback = checked(_fallback + instances); return; }
        if (_materials.Count == MaximumMaterials && !_materials.Contains(material)) throw new InvalidDataException("Content evidence material capacity exhausted; no truncation or content trimming permitted.");
        _materials.Add(material);
        if (partIndex <= -2) _skin = checked(_skin + instances);
        else if (partIndex >= 0) _mechanical = checked(_mechanical + instances);
    }
    internal void Seal(long start, long end)
    {
        if (start > end || _epoch == 0 || _sealedEpoch != 0) throw new InvalidDataException("Content evidence recording bracket invalid.");
        _sealedEpoch = _epoch; _start = start; _end = end;
    }
    internal RenderContentEvidence Read(GraphicsFrameSubmission actual, ulong presented)
    {
        // The pending token is the previous actual frame, never a guessed next fence.
        // Resolve only after one new real frame submission AND successful Present.
        if (_sealedEpoch == 0 || _sealedEpoch != _epoch || actual.Submission != checked(_pending.Submission + 1) ||
            actual.Generation == 0 || actual.Generation != _pending.Generation || actual.ResourceGeneration == 0 || actual.ResourceGeneration != _pending.ResourceGeneration ||
            actual.SubmittedFence <= _pending.SubmittedFence || presented != checked(_presentedBefore + 1))
            throw new InvalidDataException("Content evidence is unprepared/shadow-only/stale, failed-present or belongs to another actual submission.");
        return new(_epoch, _frame, actual, _materials.Count, _skin, _mechanical, _instances, _fallback, _start, _end);
    }
}
