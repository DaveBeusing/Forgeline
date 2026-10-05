using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsTexture : IGraphicsTexture
{
    private ID3D12Resource? _resource;

    internal D3D12GraphicsTexture(
        D3D12GraphicsDevice owner,
        GraphicsTextureDescription description,
        ID3D12Resource resource,
        int descriptorIndex,
        GpuDescriptorHandle gpuDescriptorHandle,
        long residentByteCount)
    {
        Owner = owner;
        Description = description;
        _resource = resource;
        DescriptorIndex = descriptorIndex;
        GpuDescriptorHandle = gpuDescriptorHandle;
        ResidentByteCount = residentByteCount;
    }

    public GraphicsTextureDescription Description { get; }

    internal D3D12GraphicsDevice Owner { get; }

    internal int DescriptorIndex { get; }

    internal GpuDescriptorHandle GpuDescriptorHandle { get; }

    internal long ResidentByteCount { get; }

    internal bool IsDisposed =>
        _resource is null;

    public void Dispose()
    {
        ID3D12Resource? resource =
            Interlocked.Exchange(ref _resource, null);

        if (resource is null)
        {
            return;
        }

        Owner.ReleaseTexture(
            resource,
            DescriptorIndex,
            ResidentByteCount);
    }
}
