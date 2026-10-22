using System.Numerics;
using System.Text.Json;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RuntimeDecalMaterialTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ResourceVariantsKeepSurfaceOverridesAndFallbackAsCompleteLodPairs(bool completePair, bool fieldParts)
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-resource-bindings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var visual = WorldVisualId.ResourceFerrousOre;
            var canonical = WorldPresentationCatalog.Get(visual);
            var feature = new WorldFeaturePresentationMetadata(visual, WorldPresentationKind.ResourceDeposit,
                ResourceDepositPresentationState.Untouched, true)
            { ResourceVariant = ResourceDepositVariantCatalog.SelectAuthored(visual, 11, 2) };
            Assert.True(ResourceDepositVariantCatalog.TryResolve(feature, out var binding));
            var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0 | RuntimeMeshAttributes.Tangent,
                [new(0,0,0,0,1,0,.8f,.6f,1,0,0,1), new(.5f,0,0,0,1,0,.9f,.6f,1,0,0,1), new(0,0,.5f,0,1,0,.8f,.7f,1,0,0,1)],
                [0,2,1], [canonical.MaterialAssetId], [new(0,3,0)]);
            var records = new List<RuntimeAssetRecord>
            {
                Write(root, canonical.MeshAssetId, RuntimeAssetType.Mesh, mesh.ToPayload()),
                Write(root, canonical.ReducedLodAssetId, RuntimeAssetType.Mesh, mesh.ToPayload()),
                Write(root, binding.HighMeshAssetId, RuntimeAssetType.Mesh, mesh.ToPayload()),
                Write(root, canonical.MaterialAssetId, RuntimeAssetType.Material, "{}"u8.ToArray()),
                Write(root, binding.MaterialAssetId, RuntimeAssetType.Material, """{"uvScale":[2,2],"uvOffset":[-1.5,-0.5]}"""u8.ToArray())
            };
            if (completePair) records.Add(Write(root, binding.ReducedMeshAssetId, RuntimeAssetType.Mesh, mesh.ToPayload()));
            File.WriteAllText(Path.Combine(root, RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.Serialize(new RuntimeAssetManifest { CompilerVersion = "test", Assets = records }, RuntimeAssetCatalog.CreateJsonOptions()));
            var camera = new RtsCamera(new() { InitialDistance = 12 });
            ResourceDepositField? field = fieldParts ? ResourceDepositFieldComposition.WithRemaining(
            [
                new(0, new(camera.Target, Quaternion.Identity, new(4)), binding.HighMeshAssetId, binding.ReducedMeshAssetId,
                    new(.96f,.95f,.92f,1), true) { Variant = feature.ResourceVariant },
                new(1, new(camera.Target, Quaternion.Identity, new(4)), binding.HighMeshAssetId, binding.ReducedMeshAssetId,
                    new(.96f,.95f,.92f,1), false) { Variant = feature.ResourceVariant }
            ], 1) : null;
            var rootFeature = fieldParts ? feature with
            {
                ResourceVariant = ResourceDepositVariantCatalog.SelectAuthored(visual, 0, 1),
                ResourceTint = new(1.04f, 1.02f, 1)
            } : feature;
            var instances = Enumerable.Range(0, 100).Select(i => new RenderInstance(new((uint)i + 1, 1),
                new(camera.Target, Quaternion.Identity, new(4)), new(1), RenderMaterialHandle.Default,
                RenderVisibilityMask.World, WorldFeature: rootFeature, ResourceField: field)).ToArray();
            var snapshots = new PresentationSnapshotBuffer();
            snapshots.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), instances.Length, instances));
            var world = new RenderWorld();
            Assert.True(world.Update(snapshots));
            using var renderer = new SimpleInstanceRenderer(new FakeDevice(), RuntimeAssetCatalog.Load(root));
            var context = new FakeContext();
            renderer.Render(context, camera, world, 1);
            var batch = Assert.Single(renderer.GetResourceBatchDiagnostics());
            Assert.Equal(completePair ? binding.HighMeshAssetId : canonical.MeshAssetId, batch.Mesh);
            Assert.Equal(completePair ? binding.MaterialAssetId : canonical.MaterialAssetId, batch.Material);
            int expectedParts = fieldParts ? 200 : 100;
            Assert.Equal(expectedParts, batch.Instances);
            Assert.Equal(expectedParts * 112L, batch.InstanceUploadBytes);
            Assert.Equal(100, renderer.LastDiagnostics.VisibleInstances);
            Assert.Equal(1, renderer.LastDiagnostics.DrawCalls);
            Assert.Equal(completePair ? expectedParts : 0, renderer.ResourceDiagnostics.VariantInstances);
            Assert.Equal(completePair ? 0 : expectedParts, renderer.ResourceDiagnostics.CanonicalFallbacks);
            Assert.NotNull(context.InstanceBuffer);
            var uploaded = MemoryMarshal.Cast<byte, float>(context.InstanceBuffer!.Upload);
            Assert.Equal(fieldParts ? .96f : 1, uploaded[16]);
            Assert.Equal(completePair ? -1.5f : 0, uploaded[25]);
            Assert.Equal(completePair ? -.5f : 0, uploaded[26]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingDecalAssetsSkipTheMarkWithoutOpaquePrimitiveFallback()
    {
        var instance = new RenderInstance(new EntityId(1, 1), RenderTransform.Identity,
            new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
            WorldFeature: new(WorldVisualId.DecalShellImpact, WorldPresentationKind.Decal, default, false),
            DecalFeature: new(DecalKind.Crater, .5f));
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, [instance]));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        using var renderer = new SimpleInstanceRenderer(new FakeDevice());
        renderer.Render(new FakeContext(), new RtsCamera(), world, 1);
        Assert.Equal(0, renderer.LastDiagnostics.DrawCalls);
        Assert.Equal(0, renderer.LastDiagnostics.FallbackMeshInstances);
        Assert.Equal(1, renderer.LastDiagnostics.SkippedDecalResources);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CriticalBuildingUploadsItsStateMaterialInsteadOfTheStaticMeshMaterial(bool commandCore)
    {
        BuildingId building = commandCore ? BuildingIds.CommandCore : BuildingIds.VehicleFactory;
        string materialId = commandCore ? "material.directorate.building.command_core.critical" : BuildingPresentationCatalog.CriticalMaterialAssetId;
        string root = Path.Combine(Path.GetTempPath(), "forgeline-building-state-material-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0,
                [new(0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0),
                 new(1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0),
                 new(0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0)],
                [0, 2, 1], [BuildingPresentationCatalog.StructuralMaterialAssetId], [new(0, 3, 0)]);
            RuntimeAssetRecord[] records =
            [
                Write(root, BuildingPresentationCatalog.Get(building).MeshAssetId, RuntimeAssetType.Mesh, mesh.ToPayload()),
                Write(root, BuildingPresentationCatalog.StructuralMaterialAssetId, RuntimeAssetType.Material, "{}"u8.ToArray()),
                Write(root, materialId, RuntimeAssetType.Material,
                    """{"uvScale":[0.214,0.214],"uvOffset":[0.018,0.518],"emissiveMultiplier":0.35}"""u8.ToArray())
            ];
            File.WriteAllText(Path.Combine(root, RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.Serialize(new RuntimeAssetManifest { CompilerVersion = "test", Assets = records },
                    RuntimeAssetCatalog.CreateJsonOptions()));
            var instance = new RenderInstance(new EntityId(1, 1), RenderTransform.Identity,
                new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default,
                BuildingFeature: new(building, BuildingPresentationState.Critical));
            var snapshots = new PresentationSnapshotBuffer();
            snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1, [instance]));
            var world = new RenderWorld();
            Assert.True(world.Update(snapshots));
            var device = new FakeDevice();
            using var renderer = new SimpleInstanceRenderer(device, RuntimeAssetCatalog.Load(root));
            var context = new FakeContext();
            renderer.Render(context, new RtsCamera(), world, 1f);
            Assert.Equal(GraphicsCullMode.Clockwise, context.Pipeline!.Description.CullMode);
            Assert.Equal(1, renderer.LastDiagnostics.TexturedRuntimeMeshInstances);
            Assert.NotNull(context.InstanceBuffer);
            ReadOnlySpan<float> uploaded = MemoryMarshal.Cast<byte, float>(context.InstanceBuffer!.Upload);
            Assert.Equal(0.35f, uploaded[24]);
            Assert.Equal(0.018f, uploaded[25]);
            Assert.Equal(0.518f, uploaded[26]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MateriallessDecalCarrierSupportsItsPlacementMaterialAndAtlasOffset()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-decal-material-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0,
                [new(0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0),
                 new(1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0),
                 new(0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0)],
                [0, 2, 1], [], [new(0, 3, -1)]);
            RuntimeAssetRecord[] records =
            [
                Write(root, "mesh.test.decal", RuntimeAssetType.Mesh, mesh.ToPayload()),
                Write(root, "material.test.decal", RuntimeAssetType.Material,
                    """{"uvScale":[0.214,0.464],"uvOffset":[0.268,0.518]}"""u8.ToArray())
            ];
            File.WriteAllText(Path.Combine(root, RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.Serialize(new RuntimeAssetManifest { CompilerVersion = "test", Assets = records },
                    RuntimeAssetCatalog.CreateJsonOptions()));
            using var resources = new RuntimeWorldAssetResources(new FakeDevice(), RuntimeAssetCatalog.Load(root));
            Assert.True(resources.TryGetMesh("mesh.test.decal", out RuntimeMeshBuffers buffers));
            Assert.False(buffers.HasMaterial);
            Assert.True(buffers.SupportsTexturedMaterial);
            Assert.False(buffers.UsesDevelopmentFallback);
            RuntimeMaterialResources material = resources.ResolveMaterial("material.test.decal");
            Assert.Equal(new Vector2(0.268f, 0.518f), material.UvOffset);
            Assert.Equal(new Vector2(0.214f, 0.464f), material.UvScale);
            Assert.Equal(0, resources.MaterialDiagnostics.BindingFailureCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimeAssetRecord Write(string root, string id, RuntimeAssetType type, byte[] payload)
    {
        string path = id + ".flasset";
        RuntimeAssetFile.Write(Path.Combine(root, path), type, [], payload);
        return new RuntimeAssetRecord
        {
            Id = id, Type = type, SourcePath = path, RuntimePath = path,
            SourceHash = "test", BuildHash = "test", RuntimeVersion = 1, CompilerVersion = "test"
        };
    }

    private sealed class FakeDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new FakePipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => new FakeBuffer(description);
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
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged => Upload = MemoryMarshal.AsBytes(data).ToArray();
        public void Dispose() { }
    }

    private sealed class FakePipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    {
        public GraphicsPipelineDescription Description => description;
        public void Dispose() { }
    }

    private sealed class FakeContext : IGraphicsCommandContext
    {
        public int Width => 800;
        public int Height => 600;
        public int FrameIndex => 0;
        public FakeBuffer? InstanceBuffer { get; private set; }
        public IGraphicsPipeline? Pipeline { get; private set; }
        public void SetViewport(float x, float y, float width, float height) { }
        public void SetScissor(int left, int top, int right, int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) => Pipeline = pipeline;
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0)
        {
            if (inputSlot == 1) InstanceBuffer = (FakeBuffer)buffer;
        }
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void SetPixelTexture(int slot, IGraphicsTexture texture) { }
        public void Draw(int vertexCount, int startVertex = 0) { }
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) { }
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) { }
    }

    private sealed class FakeTexture(GraphicsTextureDescription description) : IGraphicsTexture
    {
        public GraphicsTextureDescription Description => description;
        public void Dispose() { }
    }
}
