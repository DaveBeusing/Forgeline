using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class AdmissionProducerShapeTests
{
    [Fact]
    public void ActualProductionSerializersRetainNativeOwnerAndCommonProbeContracts()
    {
        var source = new SyntheticSource();
        var samples = new ReferencePerformanceSamples(2, 0);
        using var owner = new LiveRenderTelemetry(source, 2, actualDevice: new("SYNTHETIC CONTRACT GPU", 1024, false, "Level_12_1", false));
        owner.BindSessionOrigin(LiveSessionOriginTests.Origin());
        var camera = new RtsCamera(new()).CaptureState();
        var acks = new ReferenceFixtureFrameObservation[2];
        var raw = new GraphicsRawGpuTiming[2];
        for (int i = 0; i < 2; i++)
        {
            var start = owner.Begin();
            while (Stopwatch.GetTimestamp() <= start.Tick + 20) Thread.SpinWait(1);
            long end = Stopwatch.GetTimestamp();
            source.CpuIntervals = new((ulong)i + 1, new(start.Tick + 1, end - 1), new(end - 2, end - 1), new(start.Tick + 1, start.Tick + 2), true, (ulong)i + 10, 2, 1);
            raw[i] = new(2, 1, (ulong)i + 10, (ulong)i + 10, 1000000, (ulong)i * 10 + 100, (ulong)i * 10 + 108, (ulong)i * 10 + 106, (ulong)i * 10 + 107, null, null, null, null);
            source.Next = raw[i];
            samples.AddCpu(i, Stopwatch.GetElapsedTime(start.Tick, end).TotalMilliseconds, 0, new(source.Device, new(16, 16, 3, 0, false, "Immediate", false, false, 0, 0)),
                actualSubmission: source.CpuIntervals, measuredInterval: new(start.Tick, end), actualCamera: camera);
            owner.Complete(start, "gameplay", simulation: new(7, 7, (ulong)i + 100, 7, (ulong)i + 100, true));
            acks[i] = new(i, i, camera, 16, 16, (ulong)i + 10, 2, 1, default, default, 3, 0, 0, 0);
        }
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(samples.Report(new(source.Device, new(16, 16, 3, 0, false, "Immediate", false, false, 0, 0))), VisualReferenceCapture.JsonOptions));
        Assert.Equal(2, native.RootElement.GetProperty("cpuFrames").GetArrayLength());
        Assert.True(native.RootElement.GetProperty("cpuFrames")[0].GetProperty("actualSubmission").GetProperty("submitted").GetBoolean());
        Assert.Equal(JsonValueKind.Object, native.RootElement.GetProperty("cpuFrames")[0].GetProperty("actualCamera").ValueKind);
        var payload = new { kind = "SYNTHETIC-CONTRACT-ONLY-NOT-PHYSICAL-EVIDENCE", owner = owner.Export(), native = native.RootElement,
            rawGpuTiming = new { overflowed = false, scope = "native-reference-direct-queue", worldIntervalIncludesShadow = true, completions = raw }, acknowledgements = acks };
        if (Environment.GetEnvironmentVariable("FORGELINE_ADMISSION_SHAPE_OUTPUT") is { Length: > 0 } path)
        {
            using var file = new FileStream(path, FileMode.CreateNew);
            JsonSerializer.Serialize(file, payload, VisualReferenceCapture.JsonOptions);
        }
    }
    private sealed class SyntheticSource : IGraphicsRenderTelemetry
    {
        internal GraphicsDeviceInfo Device { get; } = new("SYNTHETIC CONTRACT GPU", 1024, false, "Level_12_1", false);
        internal GraphicsRawGpuTiming? Next { get; set; }
        public GraphicsCpuFrameIntervals CpuIntervals { get; set; }
        public GraphicsResourceMemoryTelemetry ResourceMemory => default;
        public bool TelemetryOverflowed => false;
        public void EnableRenderTelemetry(int capacity = 8192) { }
        public void StopRenderTelemetry() { }
        public bool TryReadGpuCompletion(out GraphicsRawGpuTiming sample)
        { sample = Next ?? default; bool found = Next.HasValue; Next = null; return found; }
    }
}
