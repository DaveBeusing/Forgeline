using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsCommandContext : IGraphicsCommandContext
{
    private readonly D3D12GraphicsDevice _owner;
    private readonly ID3D12GraphicsCommandList _commandList;

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

        _commandList.SetGraphicsRootSignature(d3d12Pipeline.RootSignature);
        _commandList.SetPipelineState(d3d12Pipeline.PipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
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
}
