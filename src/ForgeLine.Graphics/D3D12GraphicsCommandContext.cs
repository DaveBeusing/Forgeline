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

    public void SetViewport(float x, float y, float width, float height)
    {
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
        ArgumentNullException.ThrowIfNull(pipeline);

        if (pipeline is not D3D12GraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Owner, _owner))
        {
            throw new ArgumentException(
                "The graphics pipeline was not created by this graphics device.",
                nameof(pipeline));
        }

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
                    nameof(d3d12Pipeline.Description.PrimitiveTopology))
            });
    }

    public void SetVertexBuffer(
        IGraphicsBuffer buffer,
        int strideInBytes,
        int offsetInBytes = 0)
    {
        D3D12GraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strideInBytes);

        ulong remaining = ValidateBufferOffset(d3d12Buffer, offsetInBytes);
        var view = new VertexBufferView(
            d3d12Buffer.Resource.GPUVirtualAddress + (ulong)offsetInBytes,
            checked((uint)remaining),
            checked((uint)strideInBytes));

        _commandList.IASetVertexBuffers(0, view);
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

    public void Draw(int vertexCount, int startVertex = 0)
    {
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
        int baseVertex = 0)
    {
        if (indexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indexCount),
                indexCount,
                "Indexed draw calls must contain at least one index.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);

        _commandList.DrawIndexedInstanced(
            checked((uint)indexCount),
            1,
            checked((uint)startIndex),
            baseVertex,
            0);
    }

    private D3D12GraphicsBuffer ValidateBuffer(IGraphicsBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer is not D3D12GraphicsBuffer d3d12Buffer ||
            !ReferenceEquals(d3d12Buffer.Owner, _owner))
        {
            throw new ArgumentException(
                "The graphics buffer was not created by this graphics device.",
                nameof(buffer));
        }

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
