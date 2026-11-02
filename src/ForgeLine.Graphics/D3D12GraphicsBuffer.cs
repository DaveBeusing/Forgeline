using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsBuffer : IGraphicsBuffer
{
    private ID3D12Resource? _resource;
    internal GraphicsResourceRetirement.Resource Lifetime { get; }

    internal D3D12GraphicsBuffer(
        D3D12GraphicsDevice owner,
        GraphicsBufferDescription description,
        ID3D12Resource resource)
    {
        Owner = owner;
        Description = description;
        _resource = resource;
        try { Lifetime = owner.RegisterResource(resource.Dispose, checked((long)description.SizeInBytes)); }
        catch { resource.Dispose(); throw; }
        try { owner.RegisterBoundBuffer(Lifetime, description); }
        catch
        {
            try { owner.RetireResource(Lifetime); } catch { }
            _resource = null;
            throw;
        }
    }

    public GraphicsBufferDescription Description { get; }

    internal D3D12GraphicsDevice Owner { get; }

    internal ID3D12Resource Resource =>
        _resource ?? throw new ObjectDisposedException(nameof(D3D12GraphicsBuffer));

    public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0)
        where T : unmanaged
    {
        Owner.ValidateWrite(Lifetime);
        if (Description.Memory != GraphicsBufferMemory.Upload)
        {
            throw new InvalidOperationException(
                "Only CPU-visible upload buffers can be written directly.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);

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

        if (Owner.BoundInputTracker is not null)
        {
            using var frozen = Owner.FreezeBoundSource(MemoryMarshal.AsBytes(data));
            var consumed = MemoryMarshal.Cast<byte, T>(frozen.Bytes);
            Resource.SetData(consumed, offsetInBytes);
            Owner.RecordCaptureInput(frozen.Bytes);
            Owner.RecordBoundWrite(Lifetime, offsetInBytes, frozen.Bytes, Unsafe.SizeOf<T>());
        }
        else
        {
            Resource.SetData(data, offsetInBytes);
            Owner.RecordCaptureInput(MemoryMarshal.AsBytes(data));
        }
    }

    public void Dispose()
    {
        Owner.RetireResource(Lifetime);
        _resource = null;
    }
}
