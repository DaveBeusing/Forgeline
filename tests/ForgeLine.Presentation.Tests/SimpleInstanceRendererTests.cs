using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SimpleInstanceRendererTests : IDisposable
{
    private readonly FakeGraphicsDevice _graphics = new();

    [Fact]
    public void CloseWreckReportsTheReducedLodUsedByItsMesh()
    {
        var camera = new RtsCamera(new RtsCameraSettings { InitialDistance = 12 });
        var instance = Instance(new EntityId(1, 1), camera.Target, new Vector3(4)) with
        {
            UnitFeature = new UnitFeaturePresentationMetadata(UnitIds.MainBattleTank, UnitPresentationDamageState.Wreck)
        };
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1, [instance]));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        using var renderer = new SimpleInstanceRenderer(_graphics);
        renderer.Render(new FakeGraphicsCommandContext(), camera, world, 1);
        Assert.Equal(1, renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(0, renderer.LastDiagnostics.HighLodInstances);
        Assert.Equal(1, renderer.LastDiagnostics.ReducedLodInstances);
        Assert.Equal(1, renderer.LastDiagnostics.Lod2Instances);
    }

    [Fact]
    public void OversizedScratchIsReleasedBeforeNextSmallFrame()
    {
        var camera = new RtsCamera();
        using var renderer = new SimpleInstanceRenderer(_graphics);
        var context = new FakeGraphicsCommandContext();
        var buffer = new PresentationSnapshotBuffer();
        var large = new RenderInstance[65_537];
        for (int i = 0; i < large.Length; i++)
            large[i] = Instance(new EntityId((uint)i + 1, 1), camera.Target, Vector3.One);
        buffer.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), large.Length, large));
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        renderer.Render(context, camera, world, 1.0f);
        Assert.Equal(large.Length, renderer.SubmissionMetrics.SubmittedInstances);
        buffer.Publish(new PresentationSnapshot(new SimulationTick(2), TimeSpan.FromMilliseconds(50), 1, large.AsSpan(0, 1)));
        Assert.True(world.Update(buffer));
        renderer.Render(context, camera, world, 1.0f);
        Assert.InRange(renderer.SubmissionMetrics.StagingCapacity, 1, 64);
        Assert.InRange(renderer.SubmissionMetrics.BatchInstanceCapacity, 1L, 64L);
        Assert.Equal(1, context.LastInstanceCount);
    }

    [Fact]
    public void FailedUploadGrowthRetainsPreviousBufferAndRecovers()
    {
        var camera = new RtsCamera();
        using var renderer = new SimpleInstanceRenderer(_graphics);
        var context = new FakeGraphicsCommandContext();
        var buffer = new PresentationSnapshotBuffer();
        var instance = Instance(new EntityId(1, 1), camera.Target, Vector3.One);
        buffer.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1, new[] { instance }));
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        renderer.Render(context, camera, world, 1.0f);
        var previous = _graphics.Buffers[^1];
        _graphics.FailBufferCreation = 4;
        buffer.Publish(new PresentationSnapshot(new SimulationTick(2), TimeSpan.FromMilliseconds(50), 128,
            Enumerable.Repeat(instance, 128).ToArray()));
        Assert.True(world.Update(buffer));
        Assert.Throws<InvalidOperationException>(() => renderer.Render(context, camera, world, 1.0f));
        Assert.Equal(0, previous.DisposeCount);
        _graphics.FailBufferCreation = 0;
        buffer.Publish(new PresentationSnapshot(new SimulationTick(3), TimeSpan.FromMilliseconds(50), 1, new[] { instance }));
        Assert.True(world.Update(buffer));
        renderer.Render(context, camera, world, 1.0f);
        Assert.Equal(1, context.LastInstanceCount);
        Assert.Equal(112, previous.LastWriteBytes);
    }

    [Fact]
    public void GrowthEmptyFramesAndRecoveryPreserveExactSubmission()
    {
        _graphics.CaptureUploads = true;
        var camera = new RtsCamera();
        using var renderer = new SimpleInstanceRenderer(_graphics);
        var context = new FakeGraphicsCommandContext();
        foreach (int count in new[] { 128, 1, 0, 257, 3 })
        {
            var instances = new RenderInstance[count];
            for (int i = 0; i < count; i++)
                instances[i] = Instance(new EntityId((uint)i + 1, 1), camera.Target, Vector3.One);
            var buffer = new PresentationSnapshotBuffer();
            buffer.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), count, instances));
            var world = new RenderWorld();
            Assert.True(world.Update(buffer));
            renderer.Render(context, camera, world, 1.0f);
            Assert.Equal(count, renderer.LastDiagnostics.VisibleInstances);
            Assert.Equal(count == 0 ? 0 : 1, renderer.LastDiagnostics.DrawCalls);
            if (count > 0)
            {
                Assert.Equal(count, context.LastInstanceCount);
                Assert.Equal(count * 112, _graphics.Buffers[^1].LastWriteBytes);
                using var referenceGraphics = new FakeGraphicsDevice { CaptureUploads = true };
                using var reference = new SimpleInstanceRenderer(referenceGraphics);
                reference.Render(new FakeGraphicsCommandContext(), camera, world, 1.0f);
                Assert.Equal(referenceGraphics.Buffers[^1].LastUpload, _graphics.Buffers[^1].LastUpload);
            }
        }
    }

    [Fact]
    public void WarmSubmissionDoesNotAllocatePerFrameScratch()
    {
        var camera = new RtsCamera();
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1,
            new[] { Instance(new EntityId(1, 1), camera.Target, Vector3.One) }));
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        using var renderer = new SimpleInstanceRenderer(_graphics);
        var context = new FakeGraphicsCommandContext();
        for (int i = 0; i < 32; i++) renderer.Render(context, camera, world, 1.0f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++) renderer.Render(context, camera, world, 1.0f);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void PartialBufferInitializationReleasesPreviouslyCreatedResources()
    {
        _graphics.FailBufferCreation = 2;
        Assert.Throws<InvalidOperationException>(() => new SimpleInstanceRenderer(_graphics));
        Assert.Single(_graphics.Buffers);
        Assert.Equal(1, _graphics.Buffers[0].DisposeCount);
        Assert.Single(_graphics.Pipelines);
        Assert.Equal(1, _graphics.Pipelines[0].DisposeCount);
    }

    public void Dispose()
    {
        _graphics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RenderDiagnosticsMatchVisibleFixtureCounts()
    {
        var camera = new RtsCamera();
        Vector3 awayFromTarget =
            Vector3.Normalize(camera.Position - camera.Target);
        Vector3 behindCamera =
            camera.Position + awayFromTarget * 500.0f;

        RenderInstance[] instances =
        [
            Instance(
                new EntityId(1, 1),
                camera.Target,
                new Vector3(4.0f)),
            Instance(
                new EntityId(2, 1),
                behindCamera,
                new Vector3(4.0f))
        ];

        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                instances.Length,
                instances));

        var world = new RenderWorld();
        Assert.True(world.Update(buffer));

        using var renderer =
            new SimpleInstanceRenderer(_graphics);
        var context = new FakeGraphicsCommandContext();

        renderer.Render(context, camera, world, 1.0f);

        Assert.Equal(2, renderer.LastDiagnostics.TotalInstances);
        Assert.Equal(1, renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(1, renderer.LastDiagnostics.CulledInstances);
        Assert.Equal(1, renderer.LastDiagnostics.DrawCalls);
        Assert.Equal(1, context.IndexedDrawCalls);
    }

    [Fact]
    public void RepeatedVisibleInstancesShareOneInstancedDraw()
    {
        var camera =
            new RtsCamera();
        RenderInstance[] instances =
        [
            Instance(
                new EntityId(1, 1),
                camera.Target,
                new Vector3(4.0f)),
            Instance(
                new EntityId(2, 1),
                camera.Target +
                new Vector3(8.0f, 0.0f, 0.0f),
                new Vector3(4.0f))
        ];

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                instances.Length,
                instances));

        var world =
            new RenderWorld();
        Assert.True(
            world.Update(
                buffer));

        using var renderer =
            new SimpleInstanceRenderer(
                _graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            camera,
            world,
            1.0f);

        Assert.Equal(
            2,
            renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(
            1,
            renderer.LastDiagnostics.DrawCalls);
        Assert.Equal(
            1,
            context.IndexedDrawCalls);
        Assert.Equal(
            2,
            context.LastInstanceCount);
    }

    [Fact]
    public void RepeatedDirectorateUnitsRemainSingleBatchAtRepresentativeCount()
    {
        const int Count = 256;

        var camera =
            new RtsCamera();
        var instances =
            new RenderInstance[Count];

        for (int index = 0; index < Count; index++)
        {
            int x =
                index % 16;
            int z =
                index / 16;
            Vector3 position =
                camera.Target +
                new Vector3(
                    (x - 7.5f) * 2.0f,
                    0.0f,
                    (z - 7.5f) * 2.0f);

            instances[index] =
                Instance(
                    new EntityId(
                        checked((uint)index + 1U),
                        1),
                    position,
                    new Vector3(
                        4.0f,
                        3.0f,
                        7.2f)) with
                {
                    UnitFeature =
                        new UnitFeaturePresentationMetadata(
                            UnitIds.MainBattleTank,
                            UnitPresentationDamageState.Intact)
                };
        }

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                instances.Length,
                instances));

        var world =
            new RenderWorld();
        Assert.True(
            world.Update(
                buffer));

        using var renderer =
            new SimpleInstanceRenderer(
                _graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            camera,
            world,
            1.0f);

        Assert.Equal(
            Count,
            renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(
            1,
            renderer.LastDiagnostics.DrawCalls);
        Assert.Equal(
            1,
            context.IndexedDrawCalls);
        Assert.Equal(
            Count,
            context.LastInstanceCount);
    }

    [Fact]
    public void RepeatedDirectorateBuildingsUseSharedInstancingPath()
    {
        const int Count = 256;

        var camera =
            new RtsCamera();
        var instances =
            new RenderInstance[Count];

        for (int index = 0;
             index < Count;
             index++)
        {
            int x =
                index % 16;
            int z =
                index / 16;

            instances[index] =
                Instance(
                    new EntityId(
                        checked((uint)index + 1U),
                        1),
                    camera.Target +
                    new Vector3(
                        (x - 7.5f) * 3.0f,
                        0.0f,
                        (z - 7.5f) * 3.0f),
                    new Vector3(
                        20.0f,
                        12.0f,
                        20.0f)) with
                {
                    BuildingFeature =
                        new BuildingFeaturePresentationMetadata(
                            BuildingIds.CommandCore,
                            BuildingPresentationState.Operational)
                };
        }

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(
                    1),
                TimeSpan.FromMilliseconds(
                    50),
                Count,
                instances));

        var world =
            new RenderWorld();
        Assert.True(
            world.Update(
                buffer));

        using var renderer =
            new SimpleInstanceRenderer(
                _graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            camera,
            world,
            1.0f);

        Assert.Equal(
            Count,
            renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(
            1,
            renderer.LastDiagnostics.DrawCalls);
        Assert.Equal(
            1,
            context.IndexedDrawCalls);
        Assert.Equal(
            Count,
            context.LastInstanceCount);
    }

    [Fact]
    public void RepeatedVfxWithoutRuntimeCatalogUsesSharedFallbackBatch()
    {
        const int Count = 512;

        var camera =
            new RtsCamera();
        var instances =
            new RenderInstance[Count];

        for (int index = 0;
             index < Count;
             index++)
        {
            int x =
                index % 32;
            int z =
                index / 32;

            instances[index] =
                Instance(
                    new EntityId(
                        checked((uint)index + 1U),
                        1),
                    camera.Target +
                    new Vector3(
                        (x - 15.5f) * 0.6f,
                        0.0f,
                        (z - 7.5f) * 0.6f),
                    new Vector3(
                        1.0f)) with
                {
                    VfxFeature =
                        new VfxFeaturePresentationMetadata(
                            VfxEffectKind.ExplosionSmall)
                };
        }

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(
                    1),
                TimeSpan.FromMilliseconds(
                    50),
                Count,
                instances));

        var world =
            new RenderWorld();
        Assert.True(
            world.Update(
                buffer));

        using var renderer =
            new SimpleInstanceRenderer(
                _graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            camera,
            world,
            1.0f);

        Assert.Equal(
            Count,
            renderer.LastDiagnostics.VisibleInstances);
        Assert.Equal(
            1,
            renderer.LastDiagnostics.DrawCalls);
        Assert.Equal(
            1,
            context.IndexedDrawCalls);
        Assert.Equal(
            Count,
            context.LastInstanceCount);
    }

    private static RenderInstance Instance(
        EntityId entity,
        Vector3 position,
        Vector3 scale) =>
        new(
            entity,
            new RenderTransform(
                position,
                Quaternion.Identity,
                scale),
            new RenderMeshHandle(1),
            RenderMaterialHandle.Default,
            RenderVisibilityMask.World,
            entity.Index);

    private sealed class FakeGraphicsDevice : IGraphicsDevice
    {
        public bool CaptureUploads { get; set; }
        public int FailBufferCreation { get; set; }
        public List<FakeGraphicsBuffer> Buffers { get; } = [];
        public List<FakeGraphicsPipeline> Pipelines { get; } = [];
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            var pipeline = new FakeGraphicsPipeline(description);
            Pipelines.Add(pipeline);
            return pipeline;
        }

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description)
        {
            if (FailBufferCreation == Buffers.Count + 1)
            {
                throw new InvalidOperationException("controlled buffer allocation failure");
            }
            var buffer = new FakeGraphicsBuffer(description) { CaptureUploads = CaptureUploads };
            Buffers.Add(buffer);
            return buffer;
        }

        public void RenderFrame(
            GraphicsColor clearColor,
            Action<IGraphicsCommandContext>? recordCommands = null)
        {
            recordCommands?.Invoke(
                new FakeGraphicsCommandContext());
        }

        public void Resize(int width, int height)
        {
        }

        public void WaitForIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsPipeline : IGraphicsPipeline
    {
        public int DisposeCount { get; private set; }
        public FakeGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description = description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class FakeGraphicsBuffer : IGraphicsBuffer
    {
        public bool CaptureUploads { get; init; }
        public byte[] LastUpload { get; private set; } = [];
        public int LastWriteBytes { get; private set; }
        public int DisposeCount { get; private set; }
        public FakeGraphicsBuffer(
            GraphicsBufferDescription description)
        {
            Description = description;
        }

        public GraphicsBufferDescription Description { get; }

        public void SetData<T>(
            ReadOnlySpan<T> data,
            int offsetInBytes = 0)
            where T : unmanaged
        {
            LastWriteBytes = data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            if (CaptureUploads) LastUpload = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data).ToArray();
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class FakeGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

        public int IndexedDrawCalls { get; private set; }

        public int LastInstanceCount { get; private set; }

        public void SetViewport(
            float x,
            float y,
            float width,
            float height)
        {
        }

        public void SetScissor(
            int left,
            int top,
            int right,
            int bottom)
        {
        }

        public void SetPipeline(IGraphicsPipeline pipeline)
        {
        }

        public void SetVertexBuffer(
            IGraphicsBuffer buffer,
            int strideInBytes,
            int offsetInBytes = 0,
            int inputSlot = 0)
        {
        }

        public void SetIndexBuffer(
            IGraphicsBuffer buffer,
            GraphicsIndexFormat format,
            int offsetInBytes = 0)
        {
        }

        public void SetVertexConstants(
            ReadOnlySpan<float> values)
        {
        }

        public void Draw(
            int vertexCount,
            int startVertex = 0)
        {
        }

        public void DrawIndexed(
            int indexCount,
            int startIndex = 0,
            int baseVertex = 0)
        {
            IndexedDrawCalls++;
        }

        public void DrawIndexedInstanced(
            int indexCount,
            int instanceCount,
            int startIndex = 0,
            int baseVertex = 0,
            int startInstance = 0)
        {
            IndexedDrawCalls++;
            LastInstanceCount =
                instanceCount;
        }
    }
}
