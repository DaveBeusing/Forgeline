using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsCommandContext : IGraphicsCommandContext
{
    private readonly ID3D12GraphicsCommandList _commandList;

    internal D3D12GraphicsCommandContext(
        ID3D12GraphicsCommandList commandList,
        int width,
        int height,
        int frameIndex)
    {
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
}
