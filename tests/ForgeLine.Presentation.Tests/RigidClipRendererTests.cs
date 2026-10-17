using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RigidClipRendererTests
{
    [Theory]
    [InlineData(100)][InlineData(1000)]
    public void LoadedClipSharesResourcesAndInstancedDrawsAcrossEntities(int count)
    {
        string root = Path.Combine(Path.GetTempPath(), "rigid-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0,
                [Vertex(-.1f, -.1f, 0), Vertex(.1f, -.1f, 0), Vertex(0, .1f, 0),
                 Vertex(-.5f, -.5f, -.5f), Vertex(.5f, -.5f, .5f), Vertex(0, .5f, 0)],
                [0, 1, 2, 3, 4, 5], [], [new(0, 6, -1)]);
            var clip = new RuntimeAnimationData("translation", [new("arm", -1)],
                [new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0, 1, 2], [Vector4.Zero, new(.1f, 0, 0, 0), Vector4.Zero])]);
            string meshId = UnitPresentationCatalog.Get(UnitIds.MainBattleTank).MeshAssetId;
            var record = Write(root, meshId, RuntimeAssetType.Mesh, mesh.ToPayload()) with
            {
                Dependencies = ["animation.test.arm"], AnimationReferences = ["animation.test.arm"],
                Sockets = [new("pivot", 0, 0, 0)], MechanicalParts =
                [new("arm", 0, 3, -1, MechanicalMotionKind.Static, "pivot"), new("frame", 3, 3, -1, MechanicalMotionKind.Static, "pivot")]
            };
            RuntimeAssetRecord[] records = [record,
                Write(root, "mesh.test.other", RuntimeAssetType.Mesh, mesh.ToPayload()) with
                { Dependencies = record.Dependencies, AnimationReferences = record.AnimationReferences, Sockets = record.Sockets, MechanicalParts = record.MechanicalParts },
                Write(root, "animation.test.arm", RuntimeAssetType.Animation, clip.ToPayload())];
            File.WriteAllText(Path.Combine(root, RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.Serialize(new RuntimeAssetManifest { CompilerVersion = "test", Assets = records }, RuntimeAssetCatalog.CreateJsonOptions()));
            var catalog = RuntimeAssetCatalog.Load(root); var device = new FakeDevice();
            using (var resources = new RuntimeWorldAssetResources(device, catalog))
            {
                Assert.True(resources.TryGetMesh(meshId, out var first));
                Assert.True(resources.TryGetMesh("mesh.test.other", out var second));
                Assert.Same(first.Clip, second.Clip);
                Assert.True(resources.TryGetMesh(meshId, out var again));
                Assert.Same(first.VertexBuffer, again.VertexBuffer);
                Assert.Equal(2, resources.MaterialDiagnostics.LoadedMeshCount);
                Assert.Equal(1, resources.MaterialDiagnostics.LoadedAnimationCount);
                Assert.Equal(clip.ToPayload().Length, resources.MaterialDiagnostics.ResidentAnimationBytes);
            }
            Assert.All(device.Buffers, b => Assert.True(b.Disposed));
            using var renderer = new SimpleInstanceRenderer(new FakeDevice(), catalog);
            var instances = Enumerable.Range(0, count).Select(i => new RenderInstance(new EntityId((uint)i + 1, 1),
                new RenderTransform(new(0, 0, 0), Quaternion.Identity, new(10)), new(1), RenderMaterialHandle.Default,
                RenderVisibilityMask.World, UnitFeature: new(UnitIds.MainBattleTank, UnitPresentationDamageState.Intact),
                MechanicalPose: new(true, 0, 1, 1, -1, 0, 0))).ToArray();
            var snapshots = new PresentationSnapshotBuffer();
            snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, instances));
            var world = new RenderWorld(); Assert.True(world.Update(snapshots));
            var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero, InitialDistance = 20, MinimumDistance = 1 });
            var context = new FakeContext(); renderer.Render(context, camera, world, 1);
            Assert.Equal(2, renderer.LastDiagnostics.DrawCalls);
            Assert.Equal(count * 2, context.Instances);
            Assert.Equal(0, renderer.LastDiagnostics.FallbackMeshInstances);
            Assert.NotNull(context.InstanceBuffer);
            ReadOnlySpan<float> upload = MemoryMarshal.Cast<byte, float>(context.InstanceBuffer!.Upload);
            Assert.Equal(count * 2 * 28, upload.Length);
            // First batch is the moving arm; the containing frame stays at the origin.
            Assert.InRange(upload[12], .9999f, 1.0001f);
            Assert.Equal(0, upload[count * 28 + 12]);
        }
        finally { Directory.Delete(root, true); }
    }
    private static RuntimeMeshVertex Vertex(float x, float y, float z) => new(x, y, z, 0, 1, 0, 0, 0, 1, 0, 0, 1);
    private static RuntimeAssetRecord Write(string root, string id, RuntimeAssetType type, byte[] payload)
    {
        string path = id + ".flasset"; RuntimeAssetFile.Write(Path.Combine(root, path), type, [], payload);
        return new() { Id = id, Type = type, SourcePath = path, RuntimePath = path, SourceHash = "test", BuildHash = "test", RuntimeVersion = 4, CompilerVersion = "test" };
    }
    private sealed class FakeDevice : IGraphicsDevice
    {
        public List<FakeBuffer> Buffers { get; } = [];
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new FakePipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) { var buffer = new FakeBuffer(description); Buffers.Add(buffer); return buffer; }
        public IGraphicsTexture CreateTexture(GraphicsTextureData texture) => new FakeTexture(texture.Description);
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) => throw new NotSupportedException();
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class FakeBuffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description;
        public byte[] Upload { get; private set; } = [];
        public bool Disposed { get; private set; }
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged => Upload = MemoryMarshal.AsBytes(data).ToArray();
        public void Dispose() => Disposed = true;
    }
    private sealed class FakePipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class FakeTexture(GraphicsTextureDescription description) : IGraphicsTexture
    { public GraphicsTextureDescription Description => description; public void Dispose() { } }
    private sealed class FakeContext : IGraphicsCommandContext
    {
        public int Width => 1920; public int Height => 1080; public int FrameIndex => 0;
        public int Instances { get; private set; }
        public FakeBuffer? InstanceBuffer { get; private set; }
        public void SetViewport(float x, float y, float width, float height) { }
        public void SetScissor(int left, int top, int right, int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) { }
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0) { if (inputSlot == 1) InstanceBuffer = (FakeBuffer)buffer; }
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void SetPixelTexture(int slot, IGraphicsTexture texture) { }
        public void Draw(int vertexCount, int startVertex = 0) { }
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) { }
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) => Instances += instanceCount;
    }
}
