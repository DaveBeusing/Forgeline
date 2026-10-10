using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsCommandContext : IGraphicsCommandContext
{
    private readonly D3D12GraphicsDevice _owner;
    private readonly ID3D12GraphicsCommandList _commandList;

    private D3D12GraphicsPipeline? _pipeline;
    private bool _closed;
    internal void Close() => _closed = true;
    private void ValidateRecording()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _owner.ValidateRecording();
    }

    internal D3D12GraphicsCommandContext(
        D3D12GraphicsDevice owner,
        ID3D12GraphicsCommandList commandList,
        int width,
        int height,
        int frameIndex)
    {
        _owner = owner;
        _commandList = commandList;
        Width = width;
        Height = height;
        FrameIndex = frameIndex;
    }

    public int Width { get; }

    public int Height { get; }

    public int FrameIndex { get; }

    public void BeginPass(GraphicsFramePass pass)
    {
        ValidateRecording();
        _owner.BeginPass(pass);
    }

    public void SetViewport(float x, float y, float width, float height)
    {
        ValidateRecording();
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Viewport dimensions must be positive.");
        }

        _commandList.RSSetViewport(new Viewport(x, y, width, height));
    }

    public void SetScissor(int left, int top, int right, int bottom)
    {
        ValidateRecording();
        if (right <= left || bottom <= top)
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "Scissor bounds must describe a positive area.");
        }

        RectI rectangle = RectI.FromLTRB(left, top, right, bottom);
        _commandList.RSSetScissorRect(rectangle);
    }

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        ValidateRecording();
        ArgumentNullException.ThrowIfNull(pipeline);

        if (pipeline is not D3D12GraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Owner, _owner))
        {
            throw new ArgumentException(
                "The graphics pipeline was not created by this graphics device.",
                nameof(pipeline));
        }

        _owner.UseResource(d3d12Pipeline.Lifetime);
        _pipeline = d3d12Pipeline;
        _commandList.SetGraphicsRootSignature(d3d12Pipeline.RootSignature);
        _commandList.SetPipelineState(d3d12Pipeline.PipelineState);
        _commandList.IASetPrimitiveTopology(
            d3d12Pipeline.Description.PrimitiveTopology switch
            {
                GraphicsPrimitiveTopology.TriangleList =>
                    PrimitiveTopology.TriangleList,
                GraphicsPrimitiveTopology.LineList =>
                    PrimitiveTopology.LineList,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pipeline),
                    d3d12Pipeline.Description.PrimitiveTopology,
                    "Unsupported graphics primitive topology.")
            });
    }

    public void SetVertexBuffer(
        IGraphicsBuffer buffer,
        int strideInBytes,
        int offsetInBytes = 0,
        int inputSlot = 0)
    {
        D3D12GraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strideInBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(inputSlot);

        ulong remaining = ValidateBufferOffset(d3d12Buffer, offsetInBytes);
        var view = new VertexBufferView(
            d3d12Buffer.Resource.GPUVirtualAddress + (ulong)offsetInBytes,
            checked((uint)remaining),
            checked((uint)strideInBytes));

        _commandList.IASetVertexBuffers(
            checked((uint)inputSlot),
            view);
    }

    public void SetIndexBuffer(
        IGraphicsBuffer buffer,
        GraphicsIndexFormat format,
        int offsetInBytes = 0)
    {
        D3D12GraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        ulong remaining = ValidateBufferOffset(d3d12Buffer, offsetInBytes);

        Format nativeFormat = format switch
        {
            GraphicsIndexFormat.SixteenBit => Format.R16_UInt,
            GraphicsIndexFormat.ThirtyTwoBit => Format.R32_UInt,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        _commandList.IASetIndexBuffer(
            d3d12Buffer.Resource.GPUVirtualAddress + (ulong)offsetInBytes,
            checked((uint)remaining),
            nativeFormat);
    }

    public void SetVertexConstants(ReadOnlySpan<float> values)
    {
        ValidateRecording();
        if (_pipeline is null)
        {
            throw new InvalidOperationException(
                "A graphics pipeline must be bound before setting root constants.");
        }

        int expectedCount = _pipeline.Description.VertexRootConstantCount;
        if (expectedCount == 0)
        {
            throw new InvalidOperationException(
                "The current graphics pipeline does not declare vertex root constants.");
        }

        if (values.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The current graphics pipeline expects {expectedCount} vertex constants.",
                nameof(values));
        }

        _commandList.SetGraphicsRoot32BitConstants(0, values);
    }

    public void SetPixelTexture(
        int slot,
        IGraphicsTexture texture)
    {
        ValidateRecording();
        if (_pipeline is null)
        {
            _owner.RecordTextureBindingFailure();
            throw new InvalidOperationException(
                "A graphics pipeline must be bound before setting pixel textures.");
        }

        if (slot < 0 ||
            slot >= _pipeline.Description.PixelTextureCount)
        {
            _owner.RecordTextureBindingFailure();
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"The current graphics pipeline declares {_pipeline.Description.PixelTextureCount} pixel textures.");
        }

        ArgumentNullException.ThrowIfNull(texture);

        if (texture is not D3D12GraphicsTexture d3d12Texture ||
            !ReferenceEquals(d3d12Texture.Owner, _owner))
        {
            _owner.RecordTextureBindingFailure();
            throw new ArgumentException(
                "The graphics texture was not created by this graphics device.",
                nameof(texture));
        }

        if (d3d12Texture.IsDisposed)
        {
            _owner.RecordTextureBindingFailure();
            throw new ObjectDisposedException(
                nameof(texture),
                "Disposed graphics textures cannot be rebound.");
        }

        _owner.UseResource(d3d12Texture.Lifetime);
        _commandList.SetDescriptorHeaps(_owner.ShaderResourceHeap);

        int rootParameterIndex =
            (_pipeline.Description.VertexRootConstantCount > 0 ? 1 : 0) +
            slot;

        _commandList.SetGraphicsRootDescriptorTable(
            checked((uint)rootParameterIndex),
            d3d12Texture.GpuDescriptorHandle);
    }

    public void Draw(int vertexCount, int startVertex = 0)
    {
        ValidateRecording();
        if (vertexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount),
                vertexCount,
                "Draw calls must contain at least one vertex.");
        }

        if (startVertex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startVertex),
                startVertex,
                "The start vertex cannot be negative.");
        }

        _commandList.DrawInstanced(
            checked((uint)vertexCount),
            1,
            checked((uint)startVertex),
            0);
    }

    public void DrawIndexed(
        int indexCount,
        int startIndex = 0,
        int baseVertex = 0) =>
        DrawIndexedInstanced(
            indexCount,
            1,
            startIndex,
            baseVertex);

    public void DrawIndexedInstanced(
        int indexCount,
        int instanceCount,
        int startIndex = 0,
        int baseVertex = 0,
        int startInstance = 0)
    {
        ValidateRecording();
        if (indexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indexCount),
                indexCount,
                "Indexed draw calls must contain at least one index.");
        }

        if (instanceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceCount),
                instanceCount,
                "Instanced draw calls must contain at least one instance.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(startInstance);

        _commandList.DrawIndexedInstanced(
            checked((uint)indexCount),
            checked((uint)instanceCount),
            checked((uint)startIndex),
            baseVertex,
            checked((uint)startInstance));
    }

    private D3D12GraphicsBuffer ValidateBuffer(IGraphicsBuffer buffer)
    {
        ValidateRecording();
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer is not D3D12GraphicsBuffer d3d12Buffer ||
            !ReferenceEquals(d3d12Buffer.Owner, _owner))
        {
            throw new ArgumentException(
                "The graphics buffer was not created by this graphics device.",
                nameof(buffer));
        }

        _owner.UseResource(d3d12Buffer.Lifetime);
        return d3d12Buffer;
    }

    private static ulong ValidateBufferOffset(
        D3D12GraphicsBuffer buffer,
        int offsetInBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);

        ulong offset = (ulong)offsetInBytes;
        if (offset >= buffer.Description.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
        }

        return buffer.Description.SizeInBytes - offset;
    }
}
