using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsTexture : IGraphicsTexture
{
    private readonly ID3D12Resource _resource;
    internal GraphicsResourceRetirement.Resource Lifetime { get; }
    internal ID3D12Resource Resource { get { Lifetime.ThrowIfDisposed(); return _resource; } }

    internal D3D12GraphicsTexture(
        D3D12GraphicsDevice owner,
        GraphicsTextureDescription description,
        ID3D12Resource resource,
        int descriptorIndex,
        GpuDescriptorHandle gpuDescriptorHandle,
        long residentByteCount,
        GraphicsResourceRetirement.Resource lifetime)
    {
        _resource = resource;
        Owner = owner;
        Description = description;
        DescriptorIndex = descriptorIndex;
        GpuDescriptorHandle = gpuDescriptorHandle;
        ResidentByteCount = residentByteCount;
        Lifetime = lifetime;
    }

    public GraphicsTextureDescription Description { get; }

    internal D3D12GraphicsDevice Owner { get; }

    internal int DescriptorIndex { get; }

    internal GpuDescriptorHandle GpuDescriptorHandle { get; }

    internal long ResidentByteCount { get; }

    internal bool IsDisposed =>
        Lifetime.IsDisposed;

    public void Dispose()
    {
        Owner.RetireResource(Lifetime);
    }
}
