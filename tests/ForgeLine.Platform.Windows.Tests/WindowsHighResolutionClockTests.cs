using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowsHighResolutionClockTests
{
    [Fact]
    public void ClockProvidesMonotonicElapsedTime()
    {
        var clock = new WindowsHighResolutionClock();

        long start = clock.GetTimestamp();
        Thread.SpinWait(10_000);
        long end = clock.GetTimestamp();

        Assert.True(clock.Frequency > 0);
        Assert.True(end >= start);
        Assert.True(clock.GetElapsedTime(start, end) >= TimeSpan.Zero);
    }
}
