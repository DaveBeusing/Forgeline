using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed partial class SimpleInstanceRenderer
{
    // Source-ordered final retained scratch only; no per-instance/entity/root records or earlier-pass reconstruction.
    public GraphicsPostFrameBatch[] CapturePostFrameMainIntent(GraphicsPostFrameRoster roster, int actualCapturedSlot)
    {
        ArgumentNullException.ThrowIfNull(roster); ThrowIfDisposed();
        if (_graphics.IsRecording || actualCapturedSlot < 0 || actualCapturedSlot >= _graphics.Diagnostics.Surface.BufferCount)
            throw new InvalidOperationException("Late instance intent requires the actual completed captured slot.");
        int maximum = _decorationPass ? GraphicsPostFrameLimits.DecorationBatches : GraphicsPostFrameLimits.MainBatches;
        if (_activeBatchCount > maximum) throw new InvalidDataException("Late retained batch capacity exceeded.");
        var keys = new Dictionary<InstanceBatch, InstanceBatchKey>();
        foreach (var pair in _batchLookup) keys.Add(pair.Value, pair.Key);
        int usedInstances = 0;
        for (int i = 0; i < _activeBatchCount; i++)
        {
            var batch = _batches[i];
            if (batch.Instances.Count != 0) usedInstances = Math.Max(usedInstances, checked(batch.InstanceOffset + batch.Instances.Count));
        }
        if (usedInstances == 0) return [];
        if (!_frameInstanceBuffers.TryGetValue(actualCapturedSlot, out var frame) || frame.Buffer is null || usedInstances > frame.Capacity)
            throw new InvalidDataException("Actual captured-slot main instance buffer/prefix unavailable.");
        int instanceId = roster.AddBuffer(_decorationPass ? "decoration-captured-slot-instances" : "main-captured-slot-instances",
            frame.Buffer, 0, checked((ulong)usedInstances * InstanceStride));
        var result = new List<GraphicsPostFrameBatch>(_activeBatchCount);
        for (int index = 0; index < _activeBatchCount; index++)
        {
            var batch = _batches[index];
            if (batch.Instances.Count == 0) continue;
            if (!keys.TryGetValue(batch, out var key)) throw new InvalidDataException("Final retained batch key unavailable.");
            var references = new List<GraphicsPostFrameResourceReference>(7)
            {
                new(instanceId, "captured-slot-instance-prefix"),
                new(roster.AddBuffer("retained-batch-vertices", batch.UsesRuntimeMesh ? batch.RuntimeMesh.VertexBuffer : _vertexBuffer), "mesh-vertex"),
                new(roster.AddBuffer("retained-batch-indices", batch.UsesRuntimeMesh ? batch.RuntimeMesh.IndexBuffer : _indexBuffer), "mesh-index")
            };
            if (batch.UsesRuntimeMaterial)
            {
                var material = batch.RuntimeMaterial;
                references.Add(new(roster.AddTexture("retained-main-base-color", material.BaseColorTexture), "base-color"));
                references.Add(new(roster.AddTexture("retained-main-normal", material.NormalTexture), "normal"));
                references.Add(new(roster.AddTexture("retained-main-orm", material.OrmTexture), "orm"));
                references.Add(new(roster.AddTexture("retained-main-emissive", material.EmissiveTexture), "emissive"));
            }
            result.Add(new(result.Count, key.UsesRuntimeMesh ? key.RuntimeMeshId.Value : null,
                key.UsesRuntimeMaterial ? key.RuntimeMaterialId.Value : null, key.PartIndex,
                batch.Instances.Count, batch.InstanceOffset, InstanceStride,
                batch.UsesRuntimeMesh ? batch.RuntimeMesh.VertexStride : FallbackVertexStride,
                batch.UsesRuntimeMesh ? batch.RuntimeMesh.FirstIndex : 0, batch.UsesRuntimeMesh ? batch.RuntimeMesh.IndexCount : 36,
                batch.UsesRuntimeMesh ? "ThirtyTwoBit" : "SixteenBit",
                "Final retained scratch after source-ordered native World callback; no independent pass/draw/entity or earlier scratch attestation.", references.ToArray()));
        }
        return result.ToArray();
    }
    public GraphicsPostFrameBatch[] CapturePostFrameDecorationIntent(GraphicsPostFrameRoster roster, int actualCapturedSlot)
    {
        ArgumentNullException.ThrowIfNull(roster); ThrowIfDisposed();
        return _decorations?.CapturePostFrameIntent(roster, actualCapturedSlot) ?? [];
    }
}
