using System.Text.Json;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceCollectorControlTests
{
    [Fact]
    public void OffCreatesNoCollectorAndCommonProbeCompletesWithoutAnyOptionalTelemetryApi()
    {
        var forbidden = new ForbiddenSource();
        Assert.Null(ReferenceCollectorControl.CreateOptionalCollector(ReferenceCollectorMode.CollectorOff, forbidden, 2, null, 1, "runtime", null));
        var probe = new ReferenceCollectorControl(1, 2, ReferenceCollectorMode.CollectorOff);
        Assert.Throws<InvalidDataException>(() => probe.Export());
        for (int i = 0; i < 3; i++) probe.Record(i, i * 10 + 1, i * 10 + 9, new((ulong)i + 1, (ulong)i + 20, 1, 1), default, 1920, 1080);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(probe.Export(), VisualReferenceCapture.JsonOptions));
        Assert.False(json.RootElement.GetProperty("collectorEnabled").GetBoolean());
        Assert.False(json.RootElement.GetProperty("preciseProcessCountersEnabled").GetBoolean());
        Assert.Equal(3, json.RootElement.GetProperty("frames").GetArrayLength());
        Assert.Equal(1, json.RootElement.GetProperty("frames")[2].GetProperty("index").GetInt32());
        Assert.Throws<InvalidDataException>(() => probe.Record(3, 31, 39, new(4, 23, 1, 1), default, 1920, 1080));
    }
    [Fact]
    public void SharedProbeRejectsSkippedOrdinalReusedFenceAndOverlappingService()
    {
        var probe = new ReferenceCollectorControl(0, 2, ReferenceCollectorMode.CollectorOn);
        Assert.Throws<InvalidDataException>(() => probe.Record(1, 1, 9, new(1, 10, 1, 1), default, 1, 1));
        probe.Record(0, 1, 9, new(1, 10, 1, 1), default, 1, 1);
        Assert.Throws<InvalidDataException>(() => probe.Record(1, 10, 19, new(2, 10, 1, 1), default, 1, 1));
        Assert.Throws<InvalidDataException>(() => probe.Record(1, 8, 19, new(2, 11, 1, 1), default, 1, 1));
        probe.Record(1, 10, 19, new(2, 11, 1, 1), default, 1, 1);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(probe.Export(), VisualReferenceCapture.JsonOptions));
        Assert.True(json.RootElement.GetProperty("collectorEnabled").GetBoolean());
    }
    private sealed class ForbiddenSource : IGraphicsRenderTelemetry
    {
        public void EnableRenderTelemetry(int capacity = 8192) => throw new InvalidOperationException("OFF may not enable collector");
        public void StopRenderTelemetry() => throw new InvalidOperationException("OFF may not stop collector");
        public bool TryReadGpuCompletion(out GraphicsRawGpuTiming sample) => throw new InvalidOperationException("OFF may not drain collector");
        public bool TelemetryOverflowed => throw new InvalidOperationException("OFF may not read collector");
        public GraphicsCpuFrameIntervals CpuIntervals => throw new InvalidOperationException("OFF must use actual LastSubmission");
        public GraphicsResourceMemoryTelemetry ResourceMemory => throw new InvalidOperationException("OFF may not measure memory");
    }
}
