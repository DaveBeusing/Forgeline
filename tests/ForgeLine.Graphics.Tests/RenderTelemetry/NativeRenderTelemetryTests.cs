using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Graphics.RenderContracts;
using Xunit;
using Fixture = ForgeLine.Graphics.Tests.ResourceScheduling.NativeUploadSchedulingTests.NativeFixture;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

public sealed class NativeRenderTelemetryTests
{
    [Fact]
    public void UploadFencesRawTicksAndMemoryRetainTheirActualMeaning()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        device.EnableRenderTelemetry(64);
        var before = device.ResourceMemory;
        using var vertices = device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        var pending = device.ResourceMemory;
        Assert.Equal(36UL, pending.PendingUploadBytes);
        Assert.True(pending.StagingBytes >= 36);
        Assert.Equal(before.LiveResourceBytes + 36, pending.LiveResourceBytes);
        byte[] observed = fixture.Capture(vertices);
        var intervals = device.CpuIntervals;
        Assert.True(intervals.SubmittedFence > intervals.Submission); // Upload used an earlier queue fence.
        Assert.True(intervals.RenderFrame.StartTick <= intervals.FenceWait.StartTick);
        Assert.True(intervals.Present.EndTick <= intervals.RenderFrame.EndTick);
        Assert.True(device.TryReadGpuCompletion(out var sample));
        Assert.Equal(intervals.SubmittedFence, sample.SubmittedFence);
        Assert.Equal(intervals.Generation, sample.Generation);
        Assert.True(sample.SubmittedFence <= sample.CompletedFence && sample.StartTick < sample.EndTick);
        Assert.Equal(0UL, device.ResourceMemory.PendingUploadBytes);
        device.StopRenderTelemetry();
        for (int i = 0; i < 128; i++) device.RenderFrame(GraphicsColor.ForgeLineClear);
        _ = device.Diagnostics;
        Assert.False(device.TelemetryOverflowed);
        Assert.Equal(observed, fixture.Capture(vertices));
        Assert.Null(device.ResourceMemory.PhysicalAllocationBytes);
    }
    [Fact]
    public void SameBinaryObserverOnOffProtocolRetainsAllFourCohorts()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        var cohorts = new List<object>();
        foreach (bool enabled in new[] { false, true, true, false })
        {
            device.WaitForIdle();
            if (enabled) device.EnableRenderTelemetry(1024); else device.StopRenderTelemetry();
            for (int i = 0; i < 64; i++) device.RenderFrame(GraphicsColor.ForgeLineClear);
            var ticks = new long[256]; var allocations = new long[256];
            for (int i = 0; i < ticks.Length; i++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                device.RenderFrame(GraphicsColor.ForgeLineClear);
                ticks[i] = Stopwatch.GetTimestamp() - start;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            device.WaitForIdle();
            _ = device.Diagnostics;
            while (device.TryReadGpuCompletion(out _)) { }
            Assert.False(device.TelemetryOverflowed);
            cohorts.Add(new { enabled, ticks, threadAllocatedBytes = allocations });
        }
        string directory = Path.Combine("artifacts", "render-telemetry-overhead");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new
        {
            kind = "same-binary-observer-overhead", protocol = "off-on-on-off", warmups = 64, samples = 256,
            frequencyHz = Stopwatch.Frequency, device = device.Diagnostics.Device, cohorts,
            limitation = "Synthetic clear-frame observer overhead only; no controlled clocks/thermals or game speedup claim."
        }));
    }

}
