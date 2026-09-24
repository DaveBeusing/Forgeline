using System.Runtime.InteropServices;
using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsBuffer : IGraphicsBuffer
{
    private ID3D12Resource? _resource;

    internal D3D12GraphicsBuffer(
        D3D12GraphicsDevice owner,
        GraphicsBufferDescription description,
        ID3D12Resource resource)
    {
        Owner = owner;
        Description = description;
        _resource = resource;
    }

    public GraphicsBufferDescription Description { get; }

    internal D3D12GraphicsDevice Owner { get; }

    internal ID3D12Resource Resource =>
        _resource ?? throw new ObjectDisposedException(nameof(D3D12GraphicsBuffer));

    public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0)
        where T : unmanaged
    {
        if (Description.Memory != GraphicsBufferMemory.Upload)
        {
            throw new InvalidOperationException(
                "Only CPU-visible upload buffers can be written directly.");
        }

        if (offsetInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
        }

        ulong byteCount = checked((ulong)MemoryMarshal.AsBytes(data).Length);
        ulong end = checked((ulong)offsetInBytes + byteCount);
        if (end > Description.SizeInBytes)
        {
            throw new ArgumentException(
                "The source data exceeds the graphics buffer bounds.",
                nameof(data));
        }

        if (data.IsEmpty)
        {
            return;
        }

        Resource.SetData(data, offsetInBytes);
    }

    public void Dispose()
    {
        _resource?.Dispose();
        _resource = null;
    }
}
