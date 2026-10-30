namespace ForgeLine.Graphics;

// Optional owner-thread observation; no graphics policy or synchronization is controlled here.
public interface IGraphicsRenderTelemetry
{
    void EnableRenderTelemetry(int capacity = 8192);
    void StopRenderTelemetry();
    bool TryReadGpuCompletion(out GraphicsRawGpuTiming sample);
    bool TelemetryOverflowed { get; }
    GraphicsCpuFrameIntervals CpuIntervals { get; }
    GraphicsResourceMemoryTelemetry ResourceMemory { get; }
    GraphicsDeviceTelemetryIdentity? DeviceIdentity => null;
    GraphicsTextureReadbackLayout? CaptureShadowLayout => null;
    GraphicsFrameSubmission? LastSubmission => null;
}

public readonly record struct GraphicsFrameSubmission(ulong Submission, ulong SubmittedFence, ulong Generation, ulong ResourceGeneration);

public readonly record struct GraphicsTextureReadbackLayout(int Width, int Height, int RowPitch, ulong Offset, string Format);

// Additive physical-device identity; intentionally outside the historical GraphicsDeviceInfo digest.
public readonly record struct GraphicsDeviceTelemetryIdentity(string AdapterLuidHex, uint NodeCount,
    uint QueueNodeMask, uint EffectiveNodeMask, ulong Generation, int ProcessId, long ProcessStartedFileTimeUtc, string SessionId)
{
    public int SchemaVersion { get; init; } = 1;
    public string Source { get; init; } = "ID3D12Device.AdapterLuid/NodeCount + ID3D12CommandQueue.GetDescription().NodeMask";
}

public readonly record struct GraphicsTimestampInterval(long StartTick, long EndTick);
public readonly record struct GraphicsCpuFrameIntervals(ulong Submission, GraphicsTimestampInterval RenderFrame,
    GraphicsTimestampInterval Present, GraphicsTimestampInterval FenceWait, bool Submitted,
    ulong SubmittedFence = 0, ulong Generation = 0, ulong ResourceGeneration = 0);
public readonly record struct GraphicsRawGpuTiming(ulong Generation, ulong ResourceGeneration,
    ulong SubmittedFence, ulong CompletedFence, ulong FrequencyHz, ulong StartTick, ulong EndTick,
    ulong WorldEndTick, ulong OverlayStartTick, ulong? ShadowStartTick, ulong? ShadowEndTick,
    ulong? ParticleStartTick, ulong? ParticleEndTick)
{
    public string Queue { get; init; } = "direct";
}

// Logical resource data bytes, not heap allocations, DXGI process usage, or total VRAM.
public readonly record struct GraphicsResourceMemoryTelemetry(long LiveResourceCount, long LiveResourceBytes,
    long RetiringResourceCount, long RetiringResourceBytes, long TextureMipBytes, ulong StagingBytes,
    int Descriptors, ulong PendingUploadBytes)
{
    public ulong? PhysicalAllocationBytes { get; init; }
    public string PhysicalAllocationUnavailableReason { get; init; } = "Native heap/driver/command/query/descriptor allocation sizes are not exposed by this logical data-resource accounting.";
}

internal sealed class CompletedGpuTimingQueue(int capacity)
{
    private readonly GraphicsRawGpuTiming[] _samples = new GraphicsRawGpuTiming[Validate(capacity)];
    private int _head, _count;
    private readonly HashSet<(ulong Generation, ulong Fence)> _seen = new(capacity);
    internal bool Overflowed { get; private set; }
    internal void Add(in GraphicsRawGpuTiming sample)
    {
        if (sample.Generation == 0 || sample.ResourceGeneration == 0 || sample.FrequencyHz == 0 || sample.SubmittedFence == 0 ||
            sample.CompletedFence == ulong.MaxValue || sample.SubmittedFence > sample.CompletedFence ||
            sample.StartTick >= sample.EndTick || sample.WorldEndTick < sample.StartTick ||
            sample.OverlayStartTick < sample.WorldEndTick || sample.OverlayStartTick > sample.EndTick ||
            !ValidPass(sample.ShadowStartTick, sample.ShadowEndTick, sample.StartTick, sample.EndTick) ||
            !ValidPass(sample.ParticleStartTick, sample.ParticleEndTick, sample.StartTick, sample.EndTick))
            throw new InvalidOperationException("Invalid raw completed GPU observation.");
        if (_seen.Contains((sample.Generation, sample.SubmittedFence)))
            throw new InvalidOperationException("Duplicate raw GPU submission in one device generation.");
        if (_count == _samples.Length || _seen.Count == _samples.Length) { Overflowed = true; return; }
        _seen.Add((sample.Generation, sample.SubmittedFence));
        _samples[(_head + _count++) % _samples.Length] = sample;
    }
    internal bool TryRead(out GraphicsRawGpuTiming sample)
    {
        if (_count == 0) { sample = default; return false; }
        sample = _samples[_head]; _head = (_head + 1) % _samples.Length; _count--; return true;
    }
    private static bool ValidPass(ulong? start, ulong? end, ulong frameStart, ulong frameEnd) =>
        (!start.HasValue && !end.HasValue) || (start.HasValue && end.HasValue &&
        frameStart <= start && start <= end && end <= frameEnd);
    private static int Validate(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 65536);
        return capacity;
    }
}
