using ForgeLine.Graphics;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ContentEvidenceTests
{
    [Fact]
    public void MaterialAndMeshDevelopmentFallbackAndIdentityMismatchCannotQualify()
    {
        var id = AssetId.Parse("material.test.real");
        var material = default(RuntimeMaterialResources) with { MaterialId = id };
        Assert.True(ContentEvidenceRecorder.IsRealMaterial(id, material));
        Assert.False(ContentEvidenceRecorder.IsRealMaterial(id, material with { UsesDevelopmentFallback = true }));
        Assert.False(ContentEvidenceRecorder.IsRealMaterial(AssetId.Parse("material.test.other"), material));
        Assert.False(ContentEvidenceRecorder.IsRealMaterial(default, default));
        using var buffer = new EvidenceBuffer();
        var mesh = new RuntimeMeshBuffers(buffer, buffer, RuntimeMeshData.VertexStride, 3, default, id, true, false, 1);
        var meshId = AssetId.Parse("mesh.test.real");
        Assert.True(ContentEvidenceRecorder.IsRealMesh(meshId, mesh));
        Assert.False(ContentEvidenceRecorder.IsRealMesh(meshId, mesh with { UsesDevelopmentFallback = true }));
        Assert.False(ContentEvidenceRecorder.IsRealMesh(meshId, mesh with { IndexCount = 0 }));
        Assert.False(ContentEvidenceRecorder.IsRealMesh(default, mesh));
    }
    [Fact]
    public void WarmBoundedObserverStorageDoesNotAllocatePerFrame()
    {
        var recorder = new ContentEvidenceRecorder();
        for (int i = 0; i < 32; i++) Record();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) Record();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Record()
        {
            recorder.Begin(new(1, 10, 1, 1), 0, 1);
            recorder.AddBatch(1, true, true, "material.test.skin", -2);
            recorder.AddBatch(1, true, true, "material.test.mechanical", 0);
            recorder.Seal(1, 2); _ = recorder.Read(new(2, 15, 1, 1), 2);
        }
    }
    private sealed class EvidenceBuffer : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => default;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { }
        public void Dispose() { }
    }
    [Fact]
    public void OnlySuccessfulCurrentMainRecordResolvesItsActualFenceAndCountsDistinctConsumedContent()
    {
        var recorder = new ContentEvidenceRecorder();
        var before = new GraphicsFrameSubmission(10, 100, 2, 3);
        var after = new GraphicsFrameSubmission(11, 150, 2, 3); // Queue uploads can create arbitrary fence gaps.
        Assert.Throws<InvalidDataException>(() => recorder.Read(after, 11));
        recorder.Begin(before, 1, 10);
        recorder.AddBatch(3, true, true, "material.test.skin", -2);
        recorder.AddBatch(2, true, true, "material.test.mechanical", 0);
        recorder.AddBatch(1, true, true, "material.test.skin", -1);
        recorder.AddBatch(0, true, true, "material.stale.batch", -2);
        recorder.Seal(5, 8);
        var proof = recorder.Read(after, 11);
        Assert.Equal(after, proof.ActualSubmission);
        Assert.Equal(2, proof.DistinctRuntimeMaterials); Assert.Equal(3, proof.SkinnedInstances);
        Assert.Equal(2, proof.MechanicalInstances); Assert.Equal(6, proof.MainInstances); Assert.Equal(0, proof.FallbackInstances);
        Assert.Throws<InvalidDataException>(() => recorder.Read(before, 11));
        Assert.Throws<InvalidDataException>(() => recorder.Read(after, 10));
        Assert.Throws<InvalidDataException>(() => recorder.Read(after with { Generation = 4 }, 11));
        Assert.Throws<InvalidDataException>(() => recorder.Read(after with { ResourceGeneration = 4 }, 11));
        recorder.Invalidate();
        Assert.Throws<InvalidDataException>(() => recorder.Read(after, 11));
    }
    [Fact]
    public void FallbackInvalidMaterialAndNewPreparationCannotReuseAcceptedMainProof()
    {
        var recorder = new ContentEvidenceRecorder();
        recorder.Begin(new(1, 10, 1, 1), 1, 1);
        recorder.AddBatch(4, false, true, "material.fake.loaded.only", -2);
        recorder.AddBatch(3, true, false, "material.fake.loaded.only", 0);
        recorder.AddBatch(2, true, true, "NOT A VALID ID", -2);
        recorder.Seal(1, 2);
        var proof = recorder.Read(new(2, 15, 1, 1), 2);
        Assert.Equal(9, proof.FallbackInstances); Assert.Equal(0, proof.DistinctRuntimeMaterials);
        Assert.Equal(0, proof.SkinnedInstances); Assert.Equal(0, proof.MechanicalInstances);
        recorder.Begin(new(2, 15, 1, 1), 1, 2); // Slot reused but no successful new main record/seal.
        Assert.Throws<InvalidDataException>(() => recorder.Read(new(3, 20, 1, 1), 3));
    }
}
