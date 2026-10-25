using ForgeLine.Graphics;
namespace ForgeLine.Presentation.Tests.RenderSubmission;
internal static class SubmissionGraphics
{
    internal sealed class FakeGraphicsDevice : IGraphicsDevice
    {
        public GraphicsDirectionalShadowSettings DirectionalShadows { get; set; }
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

    internal sealed class FakeGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

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
