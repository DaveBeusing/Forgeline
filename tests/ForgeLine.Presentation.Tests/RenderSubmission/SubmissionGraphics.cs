using ForgeLine.Graphics;
namespace ForgeLine.Presentation.Tests.RenderSubmission;
internal static class SubmissionGraphics
{
    internal sealed class FakeGraphicsDevice : IGraphicsDevice
    {
        public GraphicsDirectionalShadowSettings DirectionalShadows { get; set; }
        public bool CaptureUploads { get; set; }
        public long CompletedFence { get; set; }
        public bool ValidateFences { get; set; }
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

        public IGraphicsTexture CreateTexture(GraphicsTextureData texture) => new FakeGraphicsTexture(texture.Description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description)
        {
            if (FailBufferCreation == Buffers.Count + 1)
            {
                throw new InvalidOperationException("controlled buffer allocation failure");
            }
            var buffer = new FakeGraphicsBuffer(description) { CaptureUploads = CaptureUploads, Owner = this };
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

    private sealed class FakeGraphicsTexture(GraphicsTextureDescription description) : IGraphicsTexture
    {
        public GraphicsTextureDescription Description => description;
        public void Dispose() { }
    }

    internal sealed class FakeGraphicsPipeline : IGraphicsPipeline
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

    internal sealed class FakeGraphicsBuffer : IGraphicsBuffer
    {
        public bool CaptureUploads { get; init; }
        public FakeGraphicsDevice? Owner { get; init; }
        public long LastUseFence { get; set; }
        public int WriteCount { get; private set; }
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
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            if (Owner is { ValidateFences: true } && LastUseFence > Owner.CompletedFence)
                throw new InvalidOperationException("Frame slot is still in flight.");
            WriteCount++;
            LastWriteBytes = data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            if (CaptureUploads) LastUpload = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data).ToArray();
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    internal sealed record DrawRecord(IGraphicsBuffer Geometry, IGraphicsBuffer Indices,
        IGraphicsBuffer Instances, IGraphicsPipeline Pipeline, int IndexCount, int InstanceCount,
        int StartIndex, int InstanceOffset, int InstanceStride,
        IReadOnlyDictionary<int, IGraphicsTexture> Textures);

    internal sealed class FakeGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex { get; init; }
        public long SubmissionFence { get; init; }
        public bool CaptureDraws { get; init; }
        public List<DrawRecord> Draws { get; } = [];
        private IGraphicsBuffer? _instances;
        private IGraphicsBuffer? _geometry;
        private IGraphicsBuffer? _indices;
        private int _instanceOffset;
        private int _instanceStride;
        private IGraphicsPipeline? _pipeline;
        public int TotalInstances { get; private set; }

        public int IndexedDrawCalls { get; private set; }

        public int LastInstanceCount { get; private set; }

        public Dictionary<int, IGraphicsTexture> PixelTextures { get; } = [];

        public void SetPixelTexture(int slot, IGraphicsTexture texture)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            ArgumentNullException.ThrowIfNull(texture);
            PixelTextures[slot] = texture;
        }

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
            _pipeline = pipeline;
        }

        public void SetVertexBuffer(
            IGraphicsBuffer buffer,
            int strideInBytes,
            int offsetInBytes = 0,
            int inputSlot = 0)
        {
            if (inputSlot == 0) _geometry = buffer;
            else { _instances = buffer; _instanceOffset = offsetInBytes; _instanceStride = strideInBytes; }
        }

        public void SetIndexBuffer(
            IGraphicsBuffer buffer,
            GraphicsIndexFormat format,
            int offsetInBytes = 0)
        {
            _indices = buffer;
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
            TotalInstances += instanceCount;
            if (_instances is FakeGraphicsBuffer buffer) buffer.LastUseFence = SubmissionFence;
            if (CaptureDraws) Draws.Add(new(_geometry!, _indices!, _instances!, _pipeline!,
                indexCount, instanceCount, startIndex, _instanceOffset, _instanceStride,
                new Dictionary<int, IGraphicsTexture>(PixelTextures)));
            LastInstanceCount =
                instanceCount;
        }
    }
}
