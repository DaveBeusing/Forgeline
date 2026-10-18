using Xunit;

namespace ForgeLine.Client.Tests;

[Collection("Exclusive native and timing qualification")]
public sealed class StudioVideoDecoderTests
{
    [Fact]
    public void PackagedMasterDecodesAndCancelsWithoutWaitingForTheConsumer()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "runtime", "branding", "studio-intro.mp4");
        Assert.True(File.Exists(path));
        using var decoder = new StudioVideoDecoder(path);
        StudioVideoFrame? frame = null;
        Assert.True(SpinWait.SpinUntil(() => decoder.TryRead(TimeSpan.TicksPerSecond, out frame) || decoder.Failure is not null,
            TimeSpan.FromSeconds(15)));
        Assert.Null(decoder.Failure);
        Assert.NotNull(frame);
        Assert.InRange(frame.Timestamp, 0, TimeSpan.TicksPerSecond);
        Assert.Equal(1672, frame.Width);
        Assert.Equal(940, frame.Height);
        Assert.Equal(frame.Width * frame.Height * 4, frame.Pixels.Length);
        Assert.All(Enumerable.Range(0, 100), i => Assert.Equal(255, frame.Pixels[i * 4 + 3]));
        decoder.Dispose();
        Assert.True(decoder.WorkerCompleted);
    }

    [Fact]
    public void MissingVideoReportsFailureWithoutCrashingOrBlockingTheOwner()
    {
        using var decoder = new StudioVideoDecoder(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4"));
        Assert.True(SpinWait.SpinUntil(() => decoder.WorkerCompleted, TimeSpan.FromSeconds(15)));
        Assert.NotNull(decoder.Failure);
        Assert.False(decoder.TryRead(TimeSpan.TicksPerSecond, out _));
    }
}
