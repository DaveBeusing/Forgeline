using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsBuffer : IGraphicsBuffer
{
    private ID3D12Resource? _resource;

    internal D3D12GraphicsBuffer(
        GraphicsBufferDescription description,
        ID3D12Resource resource)
    {
        Description = description;
        _resource = resource;
    }

    public GraphicsBufferDescription Description { get; }

    internal ID3D12Resource Resource =>
        _resource ?? throw new ObjectDisposedException(nameof(D3D12GraphicsBuffer));

    public void Dispose()
    {
        _resource?.Dispose();
        _resource = null;
    }
}
