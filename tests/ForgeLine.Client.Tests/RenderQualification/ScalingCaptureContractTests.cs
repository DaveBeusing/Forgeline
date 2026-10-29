using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests.RenderQualification;

public sealed class ScalingCaptureContractTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.Scaling1000High)]
    [InlineData(VisualReferenceSceneKind.Scaling1000Low)]
    [InlineData(VisualReferenceSceneKind.Scaling10000High)]
    [InlineData(VisualReferenceSceneKind.Scaling10000Low)]
    [InlineData(VisualReferenceSceneKind.Scaling100000High)]
    [InlineData(VisualReferenceSceneKind.Scaling100000Low)]
    [InlineData(VisualReferenceSceneKind.ScalingTerrainOnly)]
    public void ExistingRequestSchemaAdmitsAppendedFixtures(VisualReferenceSceneKind kind)
    {
        var request = new VisualReferenceCaptureRequest
        { OutputRoot = "unused", RuntimeRoot = "unused", Commit = new('a', 40), Scenes = [kind], Warmups = 128, Samples = 512 };
        request.Validate();
        Assert.Equal(ReferenceCameraPath.Fixed, request.CameraPath);
        Assert.True(RenderScalingReferenceScene.Contains(kind));
        Assert.Throws<ArgumentOutOfRangeException>(() => (request with { Samples = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Scenes = [(VisualReferenceSceneKind)int.MaxValue] }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public void CollectorMemoryIsBoundedBeforeSampling(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferencePerformanceSamples(capacity, 0));

    [Fact]
    public void CompletionGapsAndUnavailableTimingRemainExplicit()
    {
        var samples = new ReferencePerformanceSamples(8, 10);
        samples.AddGpu(Diagnostics(10, 1));
        samples.AddGpu(Diagnostics(11, null));
        samples.AddGpu(Diagnostics(12, 0));
        samples.AddGpu(Diagnostics(13, 2) with { TimestampFrequencyHz = 0 });
        samples.AddGpu(Diagnostics(14, 2));
        samples.AddGpu(Diagnostics(14, 2));
        samples.AddGpu(Diagnostics(16, 4));
        using var report = Report(samples);
        var root = report.RootElement;
        Assert.Equal(2, root.GetProperty("collectorSchemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("gpu").GetProperty("samples").GetInt32());
        var timing = root.GetProperty("gpuTiming");
        Assert.Equal(4UL, timing.GetProperty("unobservedFenceValues").GetUInt64());
        Assert.Equal(1, timing.GetProperty("missingDurationObservations").GetInt64());
        Assert.Equal(1, timing.GetProperty("zeroDurationObservations").GetInt64());
        Assert.Equal(1, timing.GetProperty("missingFrequencyObservations").GetInt64());
        Assert.Equal(1, timing.GetProperty("duplicateOrOlderObservations").GetInt64());
        Assert.Equal(0, root.GetProperty("cpuPasses").GetProperty("preparation").GetProperty("samples").GetInt32());
        Assert.Equal(1000000UL, root.GetProperty("gpuFrames")[0].GetProperty("timestampFrequencyHz").GetUInt64());
    }

    [Fact]
    public void MissingZeroOrUnknownFrequencyNeverBecomesAZeroCostPass()
    {
        var samples = new ReferencePerformanceSamples(8, 0);
        samples.AddGpu(Diagnostics(1, 0));
        samples.AddGpu(Diagnostics(2, 1) with { TimestampFrequencyHz = 0 });
        using var report = Report(samples);
        Assert.Equal(0, report.RootElement.GetProperty("gpu").GetProperty("samples").GetInt32());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("gpu").GetProperty("mean").ValueKind);
        Assert.False(report.RootElement.GetProperty("gpuTiming").GetProperty("available").GetBoolean());
    }

    [Fact]
    public void CpuChronologyAndQueueFrequencyCannotSilentlyChange()
    {
        var samples = new ReferencePerformanceSamples(2, 0);
        samples.AddCpu(0, 1, 0, Diagnostics(1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.AddCpu(0, 1, 0, Diagnostics(1, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.AddCpu(2, 1, 0, Diagnostics(1, 2)));
        Assert.Throws<InvalidOperationException>(() => samples.AddGpu(Diagnostics(2, 2) with { TimestampFrequencyHz = 7 }));
        Assert.Throws<InvalidOperationException>(() => samples.AddGpu(Diagnostics(2, double.NaN)));
        samples.AddCpu(1, 1, 0, Diagnostics(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.AddCpu(2, 1, 0, Diagnostics(2, 2)));
        using var report = Report(samples);
        Assert.Equal(7UL, report.RootElement.GetProperty("cpuFrames")[0].GetProperty("cpuSubmission").GetUInt64());
        Assert.Equal(.1, report.RootElement.GetProperty("cpuPasses").GetProperty("present").GetProperty("mean").GetDouble());
    }

    private static JsonDocument Report(ReferencePerformanceSamples samples) =>
        JsonDocument.Parse(JsonSerializer.Serialize(samples.Report(Diagnostics(16, 4)), VisualReferenceCapture.JsonOptions));

    [Fact]
    public async Task ComparatorRetainsBaselineAndFailsChangedPayloadOrZeroTiming()
    {
        string? repository = AppContext.BaseDirectory;
        while (repository is not null && !File.Exists(Path.Combine(repository, "build", "Compare-ReferencePerformance.ps1")))
            repository = Path.GetDirectoryName(repository.TrimEnd(Path.DirectorySeparatorChar));
        Assert.NotNull(repository);
        string temporary = Path.Combine(Path.GetTempPath(), "forgeline-scaling-" + Guid.NewGuid().ToString("N"));
        string baseline = Path.Combine(temporary, "baseline");
        string current = Path.Combine(temporary, "current");
        Directory.CreateDirectory(baseline); Directory.CreateDirectory(current);
        try
        {
            var manifest = JsonNode.Parse("""
                {"schemaVersion":1,"status":"structural-pass","commit":"contract","software":false,"samples":8,
                 "runtimeFingerprint":"same","environment":{"cpu":"contract"},"driver":"contract",
                 "captures":[{"key":"synthetic-contract","fingerprint":"same","repeatPixelsEqual":true,
                 "instances":{"fallbackMeshInstances":0},"materials":{"bindingFailureCount":0},
                 "graphics":{"resources":{"textureBindingFailureCount":0},"health":{"submissionFaultCount":0},"device":{"adapterName":"contract"}},
                 "actualSurface":{"width":10,"height":10},"requestedDisplay":{"width":10,"height":10},
                 "camera":{"distance":420},"profile":{"quality":"Medium"},"lighting":{},"rasterDpi":96,"uiScale":1,
                 "run":{"runProfile":"Warm","cameraPath":"Fixed","cameraPathVersion":1,"warmupFrames":128},
                 "cpu":{"samples":8,"mean":1,"p50":1,"p95":1,"p99":1},
                 "gpu":{"samples":8,"mean":1,"p50":1,"p95":1,"p99":1}}]}
                """)!;
            string unchanged = manifest.ToJsonString();
            await File.WriteAllTextAsync(Path.Combine(baseline, "manifest.json"), unchanged, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(current, "manifest.json"), unchanged, TestContext.Current.CancellationToken);
            Assert.Equal(0, await Compare("matched.json"));
            manifest["runtimeFingerprint"] = "changed";
            await File.WriteAllTextAsync(Path.Combine(current, "manifest.json"), manifest.ToJsonString(), TestContext.Current.CancellationToken);
            Assert.NotEqual(0, await Compare("payload.json"));
            manifest["runtimeFingerprint"] = "same";
            foreach (string statistic in new[] { "mean", "p50", "p95", "p99" }) manifest["captures"]![0]!["gpu"]![statistic] = 0;
            await File.WriteAllTextAsync(Path.Combine(current, "manifest.json"), manifest.ToJsonString(), TestContext.Current.CancellationToken);
            Assert.NotEqual(0, await Compare("zero.json"));
            Assert.Equal(unchanged, await File.ReadAllTextAsync(Path.Combine(baseline, "manifest.json"), TestContext.Current.CancellationToken));

            async Task<int> Compare(string report)
            {
                var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(repository, "build", "Compare-ReferencePerformance.ps1"),
                    "-CurrentRoot", current, "-BaselineRoot", baseline, "-ReportPath", Path.Combine(current, report), "-GatePerformance" })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                Task<string> errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                await Task.WhenAll(output, errors);
                return process.ExitCode;
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static GraphicsDiagnostics Diagnostics(ulong fence, double? milliseconds) =>
        new(new("contract", 0, false, "test", false), new(1, 1, 3, 0, false, "Immediate", false, false, 0, 0))
        {
            GpuFrameMilliseconds = milliseconds, TimestampFrequencyHz = 1000000,
            Frame = new(new(1, 1, 3, false, 0, false), 7, 1, null, fence, milliseconds, null)
            { PresentMilliseconds = .1, FrameWaitMilliseconds = .2 }
        };
}
