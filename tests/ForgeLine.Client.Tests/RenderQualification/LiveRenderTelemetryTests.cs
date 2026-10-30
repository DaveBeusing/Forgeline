using System.Text.Json;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Client.Tests.RenderQualification;

public sealed class LiveRenderTelemetryTests
{
    [Fact]
    public void LiveStreamIncludesHostTailAllocationsAndRetainsUnavailableGpuAndState()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 4);
        var start = collector.Begin();
        byte[] tail = new byte[4096]; GC.KeepAlive(tail);
        collector.Complete(start, "gameplay");
        using var json = JsonDocument.Parse(collector.Export().ToJsonString());
        var frame = json.RootElement.GetProperty("frames")[0];
        Assert.Equal("gameplay", frame.GetProperty("state").GetString());
        Assert.True(frame.GetProperty("threadAllocatedBytes").GetInt64() >= 4096);
        Assert.True(frame.GetProperty("processAllocatedBytes").GetInt64() >= frame.GetProperty("threadAllocatedBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, frame.GetProperty("gpu").ValueKind);
        Assert.Equal("independent-live-client", json.RootElement.GetProperty("gpuStreamScope").GetString());
    }
    [Fact]
    public void CompletedWindowStopsWithoutReplacingItsMeasuredFrames()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 1);
        collector.Complete(collector.Begin(), "gameplay");
        collector.Complete(collector.Begin(), "suspended");
        for (int i = 0; i < 10000; i++) collector.Complete(collector.Begin(), "gameplay");
        Assert.Equal(1, collector.Count); Assert.False(collector.Overflowed);
        Assert.True(collector.WindowCompleted);
    }
    [Fact]
    public void WarmObservationAddsNoThreadAllocation()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 512);
        for (int i = 0; i < 128; i++) collector.Complete(collector.Begin(), "gameplay");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) collector.Complete(collector.Begin(), "gameplay");
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Fact]
    public void OnlyTheRenderOwnerMayObserveOrExport()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 4);
        var thread = new Thread(() => Assert.Throws<InvalidOperationException>(() => collector.Begin()));
        thread.Start(); thread.Join();
    }
    [Fact]
    public void FixedWindowKeepsStartupAndDoesNotSkipANonGameplayMeasuredIteration()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 2, warmups: 2);
        collector.Complete(collector.Begin(), "loading");
        collector.Complete(collector.Begin(), "gameplay");
        collector.Complete(collector.Begin(), "suspended");
        collector.Complete(collector.Begin(), "gameplay");
        collector.Complete(collector.Begin(), "terrain-preparing");
        using var report = JsonDocument.Parse(collector.Export().ToJsonString());
        Assert.Equal(3, report.RootElement.GetProperty("beforeWindow").GetArrayLength());
        var measured = report.RootElement.GetProperty("frames");
        Assert.Equal(2, measured.GetArrayLength());
        Assert.Equal("terrain-preparing", measured[1].GetProperty("state").GetString());
        Assert.False(report.RootElement.GetProperty("nativeFixtureBindingAvailable").GetBoolean());
    }
    private sealed class Source : IGraphicsRenderTelemetry
    {
        public bool TelemetryOverflowed => false;
        public GraphicsCpuFrameIntervals CpuIntervals => default;
        public GraphicsResourceMemoryTelemetry ResourceMemory => default;
        public void EnableRenderTelemetry(int capacity = 8192) { }
        public void StopRenderTelemetry() { }
        public bool TryReadGpuCompletion(out GraphicsRawGpuTiming sample) { sample = default; return false; }
    }
}
