using System.Text.Json;
using System.Diagnostics;
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
    [Fact]
    public void AggregateCompletionBudgetAcceptsItsExactBoundaryAndRejectsOneMoreSample()
    {
        Assert.Equal(65536, LiveRenderTelemetry.CompletionCapacityFor(57336, 4096));
        Assert.Equal(65536, LiveRenderTelemetry.CompletionCapacityFor(61368, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRenderTelemetry.CompletionCapacityFor(57337, 4096));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRenderTelemetry.CompletionCapacityFor(65536, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveRenderTelemetry(new Source(), 65536));
    }
    [Fact]
    public void MaximumWarmupsRetainStartupWithoutOverflowingTheCombinedPrefix()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 1, warmups: 4096);
        collector.Complete(collector.Begin(), "loading");
        for (int i = 0; i < 4096; i++) collector.Complete(collector.Begin(), "gameplay");
        collector.Complete(collector.Begin(), "gameplay");
        Assert.True(collector.WindowCompleted); Assert.False(collector.Overflowed);
        using var report = JsonDocument.Parse(collector.Export().ToJsonString());
        Assert.Equal(4097, report.RootElement.GetProperty("beforeWindow").GetArrayLength());
        Assert.Equal(1, report.RootElement.GetProperty("frames").GetArrayLength());
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentOrClearedOptionalContextDoesNotOpenAFile(string? value)
    {
        const string name = "FORGELINE_RENDER_TELEMETRY_CONTEXT";
        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            Assert.Null(LiveRenderTelemetry.ReadOptionalContextFromEnvironment(new string('a', 40), null));
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }
    [Fact]
    public void ProvidedContextStillRequiresItsExactExecutingSourceAndRuntime()
    {
        const string name = "FORGELINE_RENDER_TELEMETRY_CONTEXT";
        string? previous = Environment.GetEnvironmentVariable(name);
        string path = Path.GetTempFileName();
        string source = new('a', 40), runtime = new('b', 64);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { sourceSha = source, runtimeFingerprint = runtime }));
            Environment.SetEnvironmentVariable(name, path);
            Assert.NotNull(LiveRenderTelemetry.ReadOptionalContextFromEnvironment(source, runtime));
            Assert.Throws<InvalidDataException>(() => LiveRenderTelemetry.ReadOptionalContextFromEnvironment(new string('c', 40), runtime));
            Assert.Throws<InvalidDataException>(() => LiveRenderTelemetry.ReadOptionalContextFromEnvironment(source, new string('c', 64)));
        }
        finally { Environment.SetEnvironmentVariable(name, previous); File.Delete(path); }
    }
    [Fact]
    public void ObserverServiceIncludesBothPreciseCounterCallsAndThePostPayloadWork()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 1);
        collector.Complete(collector.Begin(), "gameplay");
        using var report = JsonDocument.Parse(collector.Export().ToJsonString());
        var frame = report.RootElement.GetProperty("frames")[0];
        long start = frame.GetProperty("startTick").GetInt64(), end = frame.GetProperty("endTick").GetInt64();
        long serviceStart = frame.GetProperty("serviceStartTick").GetInt64(), serviceEnd = frame.GetProperty("serviceEndTick").GetInt64();
        var observer = frame.GetProperty("observer");
        Assert.True(serviceStart <= observer.GetProperty("beginProcessCounter").GetProperty("startTick").GetInt64());
        Assert.True(observer.GetProperty("beginProcessCounter").GetProperty("endTick").GetInt64() <= start);
        Assert.True(end <= observer.GetProperty("endProcessCounter").GetProperty("startTick").GetInt64());
        Assert.True(observer.GetProperty("endProcessCounter").GetProperty("endTick").GetInt64() <= serviceEnd);
        Assert.True(observer.GetProperty("gpuDrain").GetProperty("endTick").GetInt64() <= serviceEnd);
        Assert.True(serviceStart <= start && end <= serviceEnd);
    }
    [Fact]
    public void FailedFixtureAttemptDoesNotConsumeWarmupsOrSamplesAndCannotResume()
    {
        using var collector = new LiveRenderTelemetry(new Source(), 2, warmups: 2);
        collector.RecordFixtureFailure(collector.Begin(), "fixture-not-presented");
        Assert.Equal(0, collector.Count); Assert.False(collector.WindowCompleted);
        using var report = JsonDocument.Parse(collector.Export().ToJsonString());
        Assert.Equal(0, report.RootElement.GetProperty("beforeWindow").GetArrayLength());
        Assert.Equal(2, report.RootElement.GetProperty("failedFixtureAttempt").GetProperty("warmupsRemaining").GetInt32());
        Assert.Throws<InvalidOperationException>(() => collector.Begin());
        Assert.Throws<InvalidOperationException>(() => collector.RecordFixtureFailure(default, "replacement"));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ActualCpuAckAndCompletedGpuTuplesMustJoinExactly(int defect)
    {
        var source = new SubmittedSource { Remaining = defect == 5 ? 0 : 1 };
        using var collector = new LiveRenderTelemetry(source, 1);
        var start = collector.Begin();
        while (Stopwatch.GetTimestamp() <= start.Tick + 3) Thread.SpinWait(1);
        source.CpuIntervals = new(1, new(start.Tick + 1, start.Tick + 2), new(start.Tick + 1, start.Tick + 2), new(start.Tick + 1, start.Tick + 1), true, 999, 7, 3);
        collector.Complete(start, "gameplay");
        var ack = new ReferenceFixtureFrameObservation(0, 0, default, 1920, 1080, 999, 7, 3, default, default, 3, 0, 0, 0);
        ack = defect switch { 1 => ack with { Index = 1 }, 2 => ack with { Ordinal = 1 }, 3 => ack with { SubmittedFence = 1 },
            4 => ack with { Generation = 8 }, 6 => ack with { ResourceGeneration = 4 }, _ => ack };
        if (defect == 0) collector.ValidateFixtureCorrespondence([ack]);
        else Assert.Throws<InvalidDataException>(() => collector.ValidateFixtureCorrespondence([ack]));
    }
    [Fact]
    public void DuplicateCpuAndAckTupleCannotHideAnUnreferencedDistinctGpuCompletion()
    {
        var source = new SubmittedSource { Remaining = 2, IncrementFence = true };
        using var collector = new LiveRenderTelemetry(source, 2);
        for (int i = 0; i < 2; i++)
        {
            var start = collector.Begin();
            while (Stopwatch.GetTimestamp() <= start.Tick + 3) Thread.SpinWait(1);
            source.CpuIntervals = new((ulong)i + 1, new(start.Tick + 1, start.Tick + 2), new(start.Tick + 1, start.Tick + 2),
                new(start.Tick + 1, start.Tick + 1), true, 999, 7, 3);
            collector.Complete(start, "gameplay");
        }
        var first = new ReferenceFixtureFrameObservation(0, 0, default, 1920, 1080, 999, 7, 3, default, default, 3, 0, 0, 0);
        Assert.Throws<InvalidDataException>(() => collector.ValidateFixtureCorrespondence([first, first with { Index = 1, Ordinal = 1 }]));
    }
    private sealed class SubmittedSource : IGraphicsRenderTelemetry
    {
        internal int Remaining { get; init; } = 1;
        internal bool IncrementFence { get; init; }
        private int _read;
        public bool TelemetryOverflowed => false;
        public GraphicsCpuFrameIntervals CpuIntervals { get; set; }
        public GraphicsResourceMemoryTelemetry ResourceMemory => default;
        public void EnableRenderTelemetry(int capacity = 8192) { }
        public void StopRenderTelemetry() { }
        public bool TryReadGpuCompletion(out GraphicsRawGpuTiming sample)
        {
            ulong fence = 999UL + (IncrementFence ? (ulong)_read : 0);
            sample = new(7, 3, fence, fence, 1000, 10, 20, 15, 18, null, null, null, null);
            return _read++ < Remaining;
        }
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
