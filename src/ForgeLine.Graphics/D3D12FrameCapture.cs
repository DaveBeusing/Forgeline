using Vortice.Direct3D12;
using System.Security.Cryptography;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private ID3D12Resource? _captureTarget;
    private GraphicsResourceRetirement.Resource? _captureLifetime;
    private PlacedSubresourceFootPrint _captureFootprint;
    private IncrementalHash? _captureInputHash;
    private ID3D12Resource? _captureShadowTarget;
    private GraphicsResourceRetirement.Resource? _captureShadowLifetime;
    private PlacedSubresourceFootPrint _captureShadowFootprint;
    private bool _captureShadowRecorded;
    private bool _captureFrameRecorded;
    public string? CaptureInputFingerprint { get; private set; }
    public ReadOnlyMemory<byte> CaptureShadowDepth { get; private set; }
    internal void RecordCaptureInput(ReadOnlySpan<byte> values) => _captureInputHash?.AppendData(values);

    // Qualification only: a requested capture adds one copy and waits for completion.
    // Normal RenderFrame never allocates a readback resource or copies the output.
    public byte[] CaptureFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext> commands, bool captureShadowDepth = false)
    {
        ArgumentNullException.ThrowIfNull(commands);
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
        CaptureInputFingerprint = null;
        CaptureShadowDepth = ReadOnlyMemory<byte>.Empty;
        _captureShadowRecorded = false;
        _captureFrameRecorded = false;
        try
        {
            _captureInputHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            ulong shadowBytes = 0;
            if (captureShadowDepth && _directionalShadows.Enabled)
            {
                _device.GetCopyableFootprints(_shadowTargets![_frameIndex].Description, 0, 1, 0,
                    footprints, new uint[1], new ulong[1], out shadowBytes);
                _captureShadowFootprint = footprints[0];
                ID3D12Resource shadowReadback = _device.CreateCommittedResource(HeapType.Readback,
                    ResourceDescription.Buffer(shadowBytes), ResourceStates.CopyDest);
                try { _captureShadowLifetime = RegisterResource(shadowReadback.Dispose); }
                catch { shadowReadback.Dispose(); throw; }
                _captureShadowTarget = shadowReadback;
            }
            RenderFrame(clearColor, commands);
            if (!_captureFrameRecorded) throw new InvalidOperationException("Native frame capture did not submit a backbuffer copy.");
            CaptureInputFingerprint = Convert.ToHexString(_captureInputHash.GetHashAndReset());
            WaitForIdle();
            if (_captureShadowRecorded)
            {
                byte[] shadow = new byte[checked((int)shadowBytes)];
                _captureShadowTarget!.GetData<byte>(shadow.AsSpan());
                CaptureShadowDepth = shadow;
            }
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
            _captureInputHash?.Dispose();
            _captureInputHash = null;
            _captureShadowTarget = null;
            if (_captureShadowLifetime is { } shadowLifetime) RetireResource(shadowLifetime);
            _captureShadowLifetime = null;
            RetireResource(lifetime);
        }
    }

    private void RecordShadowCapture()
    {
        if (_captureShadowTarget is null) return;
        UseResource(_captureShadowLifetime!);
        ID3D12Resource depth = _shadowTargets![_frameIndex];
        _commandList.CopyTextureRegion(new TextureCopyLocation(_captureShadowTarget, _captureShadowFootprint), 0, 0, 0,
            new TextureCopyLocation(depth, 0));
        _captureShadowRecorded = true;
    }

    private void RecordFrameCapture(ID3D12Resource backBuffer)
    {
        if (_captureTarget is null)
            return;
        UseResource(_captureLifetime!);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.RenderTarget, ResourceStates.CopySource);
        _commandList.CopyTextureRegion(new TextureCopyLocation(_captureTarget, _captureFootprint), 0, 0, 0,
            new TextureCopyLocation(backBuffer, 0));
        _captureFrameRecorded = true;
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopySource, ResourceStates.RenderTarget);
    }
}
