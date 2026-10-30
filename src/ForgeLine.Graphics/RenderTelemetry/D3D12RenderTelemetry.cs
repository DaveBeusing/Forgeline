using System.Diagnostics;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice : IGraphicsRenderTelemetry
{
    private static long s_telemetryGeneration;
    private readonly string _telemetryIdentitySession = Guid.NewGuid().ToString("N");
    private readonly ulong _telemetryGeneration = checked((ulong)Interlocked.Increment(ref s_telemetryGeneration));
    private CompletedGpuTimingQueue? _completedTelemetry;
    private GraphicsCpuFrameIntervals _cpuIntervals;
    private bool _telemetryActive;
    private ulong _telemetryLastFence = ulong.MaxValue;
    public bool TelemetryOverflowed { get { VerifyOwner(); return _completedTelemetry?.Overflowed ?? false; } }
    public GraphicsCpuFrameIntervals CpuIntervals { get { VerifyOwner(); return _cpuIntervals; } }
    public GraphicsFrameSubmission? LastSubmission
    {
        get { ThrowIfDisposed(); return new(_submittedFrameCount, _lastSubmittedFrameFence, _telemetryGeneration, checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1)); }
    }
    public GraphicsDeviceTelemetryIdentity? DeviceIdentity
    {
        get
        {
            ThrowIfDisposed();
            using var process = Process.GetCurrentProcess();
            uint rawMask = _commandQueue.GetDescription().NodeMask;
            return new(_device.AdapterLuid.ToString("x16", System.Globalization.CultureInfo.InvariantCulture), _device.NodeCount, rawMask,
                rawMask == 0 && _device.NodeCount == 1 ? 1u : rawMask, _telemetryGeneration, Environment.ProcessId, process.StartTime.ToUniversalTime().ToFileTimeUtc(), _telemetryIdentitySession);
        }
    }
    public void EnableRenderTelemetry(int capacity = 8192)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        _completedTelemetry = new CompletedGpuTimingQueue(capacity);
        _telemetryActive = true; _telemetryLastFence = ulong.MaxValue;
    }
    public void StopRenderTelemetry()
    {
        ThrowIfDisposed(); _telemetryActive = false; _telemetryLastFence = _lastSubmittedFrameFence;
    }
    public bool TryReadGpuCompletion(out GraphicsRawGpuTiming sample)
    {
        ThrowIfDisposed();
        if (_completedTelemetry is null) { sample = default; return false; }
        return _completedTelemetry.TryRead(out sample);
    }
    private void RecordRawGpuTiming(int slot, ulong start, ulong end, ulong worldEnd, ulong overlayStart)
    {
        if (_completedTelemetry is null) return;
        ulong fence = _frameFenceValues[slot], completed = GetCompletedFence();
        if (completed == ulong.MaxValue || fence == 0 || fence > completed || fence > _telemetryLastFence) return;
        int offset = checked(slot * 16);
        bool shadow = _shadowReadback is not null && _shadowSamples?[slot] == true;
        bool particles = _particleReadback is not null && _particleSamples?[slot] == true;
        _completedTelemetry.Add(new(_telemetryGeneration, checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1),
            fence, completed, _timestampFrequency, start, end, worldEnd, overlayStart,
            shadow ? _shadowReadback!.GetData<ulong>(offset) : null,
            shadow ? _shadowReadback!.GetData<ulong>(offset + 8) : null,
            particles ? _particleReadback!.GetData<ulong>(offset) : null,
            particles ? _particleReadback!.GetData<ulong>(offset + 8) : null));
    }
    public GraphicsResourceMemoryTelemetry ResourceMemory
    {
        get
        {
            ThrowIfDisposed();
            long internalBytes = 0, internalCount = 0;
            Add(_renderTargets); AddOne(_depthTarget); Add(_sceneTargets); AddOne(_overlayDepthTarget);
            Add(_shadowTargets); Add(_shadowConstants);
            AddOne(_timestampReadback); AddOne(_shadowReadback); AddOne(_particleReadback);
            ulong pending = 0, completed = GetCompletedFence();
            foreach (UploadBatch? batch in _uploadBatches)
                if (batch is not null && (batch.Destinations.Count > 0 || batch.Fence == ulong.MaxValue ||
                    (completed != ulong.MaxValue && batch.Fence > completed))) pending = checked(pending + batch.Used);
            return new(_resources.LiveCount - _resources.PendingCount + internalCount,
                checked(_resources.LiveBytes - _resources.PendingBytes + internalBytes),
                _resources.PendingCount, _resources.PendingBytes, _residentTextureBytes,
                _uploadResidentBytes, _shaderResourceDescriptors.UsedCount, pending);
            void Add(ID3D12Resource[]? values) { if (values is not null) foreach (var value in values) AddOne(value); }
            void AddOne(ID3D12Resource? value)
            {
                if (value is null) return;
                ResourceDescription d = value.Description;
                long bytes;
                if (d.Dimension == ResourceDimension.Buffer) bytes = checked((long)d.Width);
                else
                {
                    int bpp = d.Format switch { Format.R16G16B16A16_Float => 8,
                        Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb or Format.D32_Float or
                        Format.R32_Float or Format.R32_Typeless => 4,
                        _ => throw new InvalidOperationException("Unclassified internal resource format; memory accounting is unavailable.") };
                    bytes = checked((long)d.Width * d.Height * d.DepthOrArraySize * bpp);
                }
                internalBytes = checked(internalBytes + bytes); internalCount++;
            }
        }
    }
}
