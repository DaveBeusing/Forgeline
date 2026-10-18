using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderParallelization;

public sealed class InitializedStaticBufferTests
{
    [Fact]
    public void InputIsFullyCopiedBeforeReturnAndBindingsKeepUploadCompatibility()
    {
        using var device = new Device(); IGraphicsDevice graphics = device;
        byte[] input = [1, 3, 5, 7];
        using IGraphicsBuffer buffer = graphics.CreateInitializedStaticBuffer(new(4, StaticGraphicsBufferUsage.Vertex), input);
        input.AsSpan().Clear();
        Assert.Equal(new byte[] { 1, 3, 5, 7 }, device.Buffers[0].Bytes);
        Assert.Equal(GraphicsBufferMemory.Upload, buffer.Description.Memory);
        Assert.Equal(4UL, buffer.Description.SizeInBytes);
    }
    [Fact]
    public void InvalidInputFailsBeforeAllocationAndFailedReplacementKeepsExistingResource()
    {
        using var device = new Device(); IGraphicsDevice graphics = device;
        Assert.Throws<ArgumentException>(() => graphics.CreateInitializedStaticBuffer(new(8, StaticGraphicsBufferUsage.Index), new byte[4]));
        Assert.Throws<ArgumentException>(() => graphics.CreateInitializedStaticBuffer(new(0, StaticGraphicsBufferUsage.Vertex), []));
        Assert.Throws<ArgumentException>(() => graphics.CreateInitializedStaticBuffer(new(1, (StaticGraphicsBufferUsage)99), new byte[1]));
        Assert.Empty(device.Buffers);
        using var original = graphics.CreateInitializedStaticBuffer(new(4, StaticGraphicsBufferUsage.Index), new byte[] { 1, 2, 3, 4 });
        device.FailUpload = true;
        Assert.Throws<InvalidOperationException>(() => graphics.CreateInitializedStaticBuffer(new(4, StaticGraphicsBufferUsage.Index), new byte[4]));
        Assert.False(device.Buffers[0].Disposed);
        Assert.True(device.Buffers[1].Disposed);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, device.Buffers[0].Bytes);
    }
    private sealed class Device : IGraphicsDevice
    {
        internal bool FailUpload;
        internal readonly List<Buffer> Buffers = [];
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => throw new NotSupportedException();
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description)
        { var buffer = new Buffer(description, FailUpload); Buffers.Add(buffer); return buffer; }
        public void RenderFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands = null) => throw new NotSupportedException();
        public void Resize(int width, int height) => throw new NotSupportedException();
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Buffer(GraphicsBufferDescription description, bool fail) : IGraphicsBuffer
    {
        internal byte[] Bytes = []; internal bool Disposed;
        public GraphicsBufferDescription Description => description;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged
        { if (fail) throw new InvalidOperationException("Controlled upload failure."); Bytes = MemoryMarshal.AsBytes(data).ToArray(); }
        public void Dispose() => Disposed = true;
    }
}
