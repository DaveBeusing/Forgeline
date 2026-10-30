using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

public sealed class CompletedGpuTelemetryTests
{
    private static GraphicsRawGpuTiming Sample(ulong fence = 7) => new(1, 1, fence, 1000, 1000, 10, 50, 30, 40, 12, 20, 22, 28);
    [Fact]
    public void OutOfOrderCompletionsRemainDistinctAndDuplicatesAreRejectedAfterDrain()
    {
        var queue = new CompletedGpuTimingQueue(4);
        queue.Add(Sample(8)); queue.Add(Sample(7));
        Assert.True(queue.TryRead(out var first)); Assert.Equal(8UL, first.SubmittedFence);
        Assert.True(queue.TryRead(out var second)); Assert.Equal(7UL, second.SubmittedFence);
        Assert.Throws<InvalidOperationException>(() => queue.Add(Sample(8)));
        queue.Add(Sample(8) with { Generation = 2 });
    }
    [Fact]
    public void OverflowIsStickyAndNeverOverwritesAnUnreadCompletion()
    {
        var queue = new CompletedGpuTimingQueue(1);
        queue.Add(Sample()); queue.Add(Sample(8));
        Assert.True(queue.Overflowed); Assert.True(queue.TryRead(out var first));
        Assert.Equal(7UL, first.SubmittedFence); Assert.False(queue.TryRead(out _));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void InvalidRawReadbacksCannotBecomeSamples(int defect)
    {
        var sample = defect switch
        {
            0 => Sample() with { CompletedFence = 6 },
            1 => Sample() with { CompletedFence = ulong.MaxValue },
            2 => Sample() with { ResourceGeneration = 0 },
            3 => Sample() with { EndTick = 10 },
            4 => Sample() with { ShadowEndTick = null },
            5 => Sample() with { ParticleEndTick = 51 },
            _ => Sample() with { ShadowStartTick = 25, ShadowEndTick = 20 }
        };
        Assert.Throws<InvalidOperationException>(() => new CompletedGpuTimingQueue(4).Add(sample));
    }
    [Fact]
    public void WarmQueueRecordingAndDrainingDoesNotAllocate()
    {
        var queue = new CompletedGpuTimingQueue(512);
        for (ulong i = 1; i <= 128; i++) { queue.Add(Sample(i)); queue.TryRead(out _); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong i = 129; i <= 384; i++) { queue.Add(Sample(i)); queue.TryRead(out _); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Fact]
    public void LogicalBytesRemainDisjointThroughDelayedReleaseAndShutdown()
    {
        var resources = new GraphicsResourceRetirement();
        var a = resources.Register(static () => { }, 128);
        var b = resources.Register(static () => { }, 256);
        resources.Use(a); resources.CompleteRecording(7); resources.Retire(a, 6);
        Assert.Equal(384, resources.LiveBytes); Assert.Equal(128, resources.PendingBytes);
        Assert.Equal(256, resources.LiveBytes - resources.PendingBytes);
        resources.Collect(7); Assert.Equal(256, resources.LiveBytes); Assert.Equal(0, resources.PendingBytes);
        resources.Retire(a, 7); resources.Retire(b, 7); resources.ReleaseAll();
        Assert.Equal(0, resources.LiveBytes); Assert.Equal(2, resources.ReleasedCount);
    }
}
