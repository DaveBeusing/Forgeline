using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferencePerformanceSamplesTests
{
    [Fact]
    public void WarmupDuplicateMissingAndMismatchedPassFencesDoNotBecomeSamples()
    {
        var samples = new ReferencePerformanceSamples(8, 12);
        samples.AddGpu(Diagnostics(12, 9));
        samples.AddGpu(Diagnostics(13, null));
        samples.AddGpu(Diagnostics(14, 2));
        samples.AddGpu(Diagnostics(14, 8));
        samples.AddGpu(Diagnostics(13, 8));
        samples.AddGpu(Diagnostics(ulong.MaxValue, 8));
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(samples.Report(Diagnostics(14, 2)), VisualReferenceCapture.JsonOptions));
        Assert.Equal(1, result.RootElement.GetProperty("gpu").GetProperty("samples").GetInt32());
        Assert.Equal(2, result.RootElement.GetProperty("gpu").GetProperty("mean").GetDouble());
        Assert.Equal(0, result.RootElement.GetProperty("gpuPasses").GetProperty("shadows").GetProperty("samples").GetInt32());
        Assert.Equal(1000000UL, result.RootElement.GetProperty("gpuTiming").GetProperty("timestampFrequencyHz").GetUInt64());
    }

    [Fact]
    public void MissingGpuCostIsNullAndSmallWindowsKeepTailSpikes()
    {
        var samples = new ReferencePerformanceSamples(8, 0);
        using var missing = JsonDocument.Parse(JsonSerializer.Serialize(samples.Report(Diagnostics(0, null)), VisualReferenceCapture.JsonOptions));
        Assert.Equal(JsonValueKind.Null, missing.RootElement.GetProperty("gpu").GetProperty("mean").ValueKind);
        Assert.False(missing.RootElement.GetProperty("gpuTiming").GetProperty("available").GetBoolean());
        using var distribution = JsonDocument.Parse(JsonSerializer.Serialize(VisualReferenceCapture.Distribution([1,1,1,1,1,1,1,80])));
        Assert.Equal(80, distribution.RootElement.GetProperty("p99").GetDouble());
        Assert.Equal(80, distribution.RootElement.GetProperty("p95").GetDouble());
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.AddCpu(0, double.NaN, 0, Diagnostics(0, null)));
    }

    [Fact]
    public void CameraSweepIsFrameIndexedPeriodicAndPreservesHeightDistanceAndPitch()
    {
        var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(new(650, 4, 520), RtsReferenceZoom.Strategic));
        var original = camera.CaptureState();
        var pose = ReferencePerformanceSamples.CameraPose(original, ReferenceCameraPath.Sweep, 16, 64);
        Assert.NotEqual(original.Target, pose.Target);
        Assert.Equal(original.Distance, pose.Distance);
        Assert.Equal(original.PitchRadians, pose.PitchRadians);
        Assert.Equal(original.Target.Y, pose.Target.Y);
        Assert.Equal(pose, ReferencePerformanceSamples.CameraPose(original, ReferenceCameraPath.Sweep, 80, 64));
        Assert.Equal(original, ReferencePerformanceSamples.CameraPose(original, ReferenceCameraPath.Fixed, 16, 64));
        var request = new VisualReferenceCaptureRequest { OutputRoot = "unused", RuntimeRoot = "unused", Commit = new('a',40) };
        Assert.Throws<ArgumentException>(() => (request with { RunProfile = (ReferenceRunProfile)99 }).Validate());
    }

    private static GraphicsDiagnostics Diagnostics(ulong fence, double? milliseconds) => new(new("contract", 0, false, "test", false), new(1,1,3,0,false,"Immediate",false,false,0,0))
    {
        GpuFrameMilliseconds = milliseconds, TimestampFrequencyHz = 1000000,
        Frame = new(new(1,1,3,false,0,false), 1, 1, null, fence, milliseconds, null),
        DirectionalShadows = new(DirectionalShadowQuality.Disabled,0,0,0,0,0,0,null,1,4)
    };
}
