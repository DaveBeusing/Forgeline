using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation.Tests.RenderParallelization;

internal sealed class ContractGraphicsDevice : IGraphicsDevice
{
    internal readonly List<ContractBuffer> Buffers = [];
    public bool IsRecording { get; set; }
    public GraphicsDiagnostics Diagnostics => new(new("contract", 0, true, "test", false), default);
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new Pipeline(description);
    public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description)
    { var buffer = new ContractBuffer(description); Buffers.Add(buffer); return buffer; }
    public IGraphicsTexture CreateTexture(GraphicsTextureData texture) => new Texture(texture.Description);
    public void RenderFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands = null)
    { IsRecording = true; try { recordCommands?.Invoke(new Context()); } finally { IsRecording = false; } }
    public void Resize(int width, int height) { }
    public void WaitForIdle() { }
    public void Dispose() { }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class Texture(GraphicsTextureDescription description) : IGraphicsTexture
    { public GraphicsTextureDescription Description => description; public void Dispose() { } }
    internal sealed class ContractBuffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        internal byte[] Bytes = [];
        public GraphicsBufferDescription Description => description;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged => Bytes = MemoryMarshal.AsBytes(data).ToArray();
        public void Dispose() { }
    }
    internal sealed class Context : IGraphicsCommandContext
    {
        public int Width => 1600; public int Height => 900; public int FrameIndex => 0;
        public void SetViewport(float x, float y, float width, float height) { }
        public void SetScissor(int left, int top, int right, int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) { }
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0) { }
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void SetPixelTexture(int slot, IGraphicsTexture texture) { }
        public void Draw(int vertexCount, int startVertex = 0) { }
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) { }
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) { }
    }
}
