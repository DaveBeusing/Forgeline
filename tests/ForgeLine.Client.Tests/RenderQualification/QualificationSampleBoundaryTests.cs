using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests.RenderQualification;

public sealed class QualificationSampleBoundaryTests
{
    [Fact]
    public void EnclosingIntervalsRemainIndependentOfOverlappingPasses()
    {
        var diagnostics = Diagnostics(7, 20) with
        {
            Frame = new(default, 1, 16, 14, 7, 14, 12)
            {
                CompositeCpuMilliseconds = 6,
                CompositeGpuMilliseconds = 8,
                FrameWaitMilliseconds = 3,
                PresentMilliseconds = 2
            }
        };
        var samples = new ReferencePerformanceSamples(1, 0);
        samples.AddCpu(0, 30, 4096, diagnostics);
        using var report = Report(samples, diagnostics);
        var cpu = report.RootElement.GetProperty("cpuFrames")[0];
        var gpu = report.RootElement.GetProperty("gpuFrames")[0];

        Assert.Equal(30, cpu.GetProperty("milliseconds").GetDouble());
        Assert.Equal(4096, cpu.GetProperty("allocatedBytes").GetInt64());
        Assert.Equal(16, cpu.GetProperty("worldMilliseconds").GetDouble());
        Assert.Equal(14, cpu.GetProperty("overlayMilliseconds").GetDouble());
        Assert.Equal(6, cpu.GetProperty("compositeMilliseconds").GetDouble());
        Assert.Equal(20, gpu.GetProperty("frameMilliseconds").GetDouble());
        Assert.Equal(14, gpu.GetProperty("worldMilliseconds").GetDouble());
        Assert.Equal(12, gpu.GetProperty("overlayMilliseconds").GetDouble());
        Assert.Equal(8, gpu.GetProperty("compositeMilliseconds").GetDouble());
        Assert.Equal(20, report.RootElement.GetProperty("gpu").GetProperty("mean").GetDouble());
    }

    [Fact]
    public void DelayedParticleObservationCannotAttachToAnotherSubmission()
    {
        var samples = new ReferencePerformanceSamples(3, 9);
        var delayed = Diagnostics(10, 4) with { Particles = new(true, 9, 1, 2) };
        var matched = Diagnostics(11, 5) with { Particles = new(true, 11, 2, 2) };
        samples.AddGpu(delayed);
        samples.AddGpu(matched);
        samples.AddGpu(matched with { GpuFrameMilliseconds = 99 });
        using var report = Report(samples, matched);
        var frames = report.RootElement.GetProperty("gpuFrames");

        Assert.Equal(2, frames.GetArrayLength());
        Assert.Equal(10UL, frames[0].GetProperty("fence").GetUInt64());
        Assert.Equal(JsonValueKind.Null, frames[0].GetProperty("particleMilliseconds").ValueKind);
        Assert.Equal(11UL, frames[1].GetProperty("fence").GetUInt64());
        Assert.Equal(5, frames[1].GetProperty("frameMilliseconds").GetDouble());
        Assert.Equal(2, frames[1].GetProperty("particleMilliseconds").GetDouble());
        Assert.Equal(1, report.RootElement.GetProperty("gpuPasses").GetProperty("particles").GetProperty("samples").GetInt32());
    }

    [Fact]
    public void FreshRendererCollectorsHaveIndependentFenceNamespaces()
    {
        var first = new ReferencePerformanceSamples(1, 0);
        var second = new ReferencePerformanceSamples(1, 0);
        first.AddGpu(Diagnostics(1, 2));
        second.AddGpu(Diagnostics(1, 3));
        using var firstReport = Report(first, Diagnostics(1, 2));
        using var secondReport = Report(second, Diagnostics(1, 3));

        Assert.Equal(2, firstReport.RootElement.GetProperty("gpuFrames")[0].GetProperty("frameMilliseconds").GetDouble());
        Assert.Equal(3, secondReport.RootElement.GetProperty("gpuFrames")[0].GetProperty("frameMilliseconds").GetDouble());
    }

    [Fact]
    public void CpuSuccessDoesNotCreateMissingGpuOrResourceObservations()
    {
        var diagnostics = Diagnostics(0, null) with
        {
            TimestampFrequencyHz = 0,
            Memory = null,
            Frame = null
        };
        var samples = new ReferencePerformanceSamples(1, 0);
        samples.AddCpu(0, 12.5, 1024, diagnostics);
        using var report = Report(samples, diagnostics);

        Assert.Equal(1, report.RootElement.GetProperty("cpuFrames").GetArrayLength());
        Assert.Equal(0, report.RootElement.GetProperty("gpuFrames").GetArrayLength());
        Assert.False(report.RootElement.GetProperty("gpuTiming").GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("gpu").GetProperty("p99").ValueKind);
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("cpuFrames")[0].GetProperty("memory").ValueKind);
    }

    [Theory]
    [InlineData(RtsReferenceZoom.CloseTactical, (int)ReferenceCameraPath.Sweep)]
    [InlineData(RtsReferenceZoom.NormalGameplay, (int)ReferenceCameraPath.Sweep)]
    [InlineData(RtsReferenceZoom.Strategic, (int)ReferenceCameraPath.Sweep)]
    [InlineData(RtsReferenceZoom.CloseTactical, (int)ReferenceCameraPath.Traverse)]
    [InlineData(RtsReferenceZoom.NormalGameplay, (int)ReferenceCameraPath.Traverse)]
    [InlineData(RtsReferenceZoom.Strategic, (int)ReferenceCameraPath.Traverse)]
    public void QualificationMotionRepeatsByFrameAndPreservesTheSourceBookmark(
        RtsReferenceZoom zoom, int pathValue)
    {
        var path = (ReferenceCameraPath)pathValue;
        var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(new(650, 4, 520), zoom));
        var start = camera.CaptureState();
        for (int frame = 0; frame < 512; frame++)
        {
            var pose = ReferencePerformanceSamples.CameraPose(start, path, frame, 512);
            Assert.Equal(pose, ReferencePerformanceSamples.CameraPose(start, path, frame + 512, 512));
            Assert.Equal(start.Target.Y, pose.Target.Y);
            Assert.Equal(start.PitchRadians, pose.PitchRadians);
            Assert.True(float.IsFinite(pose.Distance));
            Assert.True(pose.Distance >= 120);
        }

        Assert.NotEqual(start.Target, ReferencePerformanceSamples.CameraPose(start, path, 128, 512).Target);
        Assert.Equal(start, ReferencePerformanceSamples.CameraPose(start, path, 512, 512));
        Assert.Equal(start, camera.CaptureState());
    }

    private static JsonDocument Report(ReferencePerformanceSamples samples, GraphicsDiagnostics diagnostics) =>
        JsonDocument.Parse(JsonSerializer.Serialize(samples.Report(diagnostics), VisualReferenceCapture.JsonOptions));

    private static GraphicsDiagnostics Diagnostics(ulong fence, double? milliseconds) => new(
        new("qualification fixture", 0, false, "contract", false),
        new(1, 1, 3, 0, false, "Immediate", false, false, 0, 0))
    {
        TimestampFrequencyHz = 1_000_000,
        GpuFrameMilliseconds = milliseconds,
        Frame = new(default, 1, 1, null, fence, milliseconds, null)
    };
}
