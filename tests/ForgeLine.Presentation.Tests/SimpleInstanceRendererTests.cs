using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SimpleInstanceRendererTests : IDisposable
{
    private readonly FakeGraphicsDevice _graphics = new();

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
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description) =>
            new FakeGraphicsPipeline(description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            new FakeGraphicsBuffer(description);

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
        public FakeGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description = description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsBuffer : IGraphicsBuffer
    {
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
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

        public int IndexedDrawCalls { get; private set; }

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
            int offsetInBytes = 0)
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
    }
}
