using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private ID3D12Resource? _captureTarget;
    private GraphicsResourceRetirement.Resource? _captureLifetime;
    private PlacedSubresourceFootPrint _captureFootprint;

    // Qualification only: a requested capture adds one copy and waits for completion.
    // Normal RenderFrame never allocates a readback resource or copies the output.
    internal byte[] CaptureFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext> commands)
    {
        ThrowIfDisposed();
        ThrowIfRecording();
        if (_surfaceLifecycle.IsSuspended || _surfaceLifecycle.HasPendingResize || _surfaceLifecycle.IsOccluded)
            throw new InvalidOperationException("Frame capture requires an active, settled surface.");
        PlacedSubresourceFootPrint[] footprints = new PlacedSubresourceFootPrint[1];
        _device.GetCopyableFootprints(_renderTargets[_frameIndex].Description, 0, 1, 0,
            footprints, new uint[1], new ulong[1], out ulong bytes);
        _captureFootprint = footprints[0];
        ID3D12Resource readback = _device.CreateCommittedResource(HeapType.Readback,
            ResourceDescription.Buffer(bytes), ResourceStates.CopyDest);
        GraphicsResourceRetirement.Resource lifetime;
        try { lifetime = RegisterResource(readback.Dispose); }
        catch { readback.Dispose(); throw; }
        _captureTarget = readback;
        _captureLifetime = lifetime;
        try
        {
            RenderFrame(clearColor, commands);
            WaitForIdle();
            byte[] padded = new byte[checked((int)bytes)];
            readback.GetData<byte>(padded.AsSpan());
            int rowBytes = checked(_surfaceLifecycle.Width * 4);
            byte[] output = new byte[checked(rowBytes * _surfaceLifecycle.Height)];
            for (int row = 0; row < _surfaceLifecycle.Height; row++)
                padded.AsSpan(checked((int)_captureFootprint.Offset + row * (int)_captureFootprint.Footprint.RowPitch), rowBytes)
                    .CopyTo(output.AsSpan(row * rowBytes, rowBytes));
            return output;
        }
        finally
        {
            _captureTarget = null;
            _captureLifetime = null;
            RetireResource(lifetime);
        }
    }

    private void RecordFrameCapture(ID3D12Resource backBuffer)
    {
        if (_captureTarget is null)
            return;
        UseResource(_captureLifetime!);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.RenderTarget, ResourceStates.CopySource);
        _commandList.CopyTextureRegion(new TextureCopyLocation(_captureTarget, _captureFootprint), 0, 0, 0,
            new TextureCopyLocation(backBuffer, 0));
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopySource, ResourceStates.RenderTarget);
    }
}
