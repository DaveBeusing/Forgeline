using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RuntimeMetricsSamplerTests
{
    [Theory]
    [InlineData(144, 20)]
    [InlineData(37, 13)]
    [InlineData(240, 30)]
    public void CompletedWorkUsesExactDurationAndIndependentRates(int frames, int ticks)
    {
        var sampler = new RuntimeMetricsSampler();
        Assert.Null(Sample(sampler, 0, 900, 200).FramesPerSecond);
        Assert.Null(Sample(sampler, 0.25, 900, 200).TicksPerSecond);
        RuntimeMetricsView view = Sample(sampler, 1, (ulong)(900 + frames), (ulong)(200 + ticks));
        Assert.Equal(frames, view.FramesPerSecond);
        Assert.Equal(ticks, view.TicksPerSecond);
        Assert.Equal(view, Sample(sampler, 1.1, (ulong)(901 + frames), (ulong)(200 + ticks)));
    }

    [Fact]
    public void PauseResumeAndUnavailableDoNotKeepRunningTickRates()
    {
        var sampler = new RuntimeMetricsSampler();
        Sample(sampler, 0, 0, 0);
        Assert.Equal(20, Sample(sampler, 1, 144, 20).TicksPerSecond);
        RuntimeMetricsView paused = Sample(sampler, 1.1, 150, 20, RuntimeSimulationState.Paused);
        Assert.Null(paused.TicksPerSecond);
        Assert.Equal(144, paused.FramesPerSecond);
        Assert.Null(Sample(sampler, 1.2, 160, 20).TicksPerSecond);
        Assert.Equal(10, Sample(sampler, 2.2, 260, 30).TicksPerSecond);
        Assert.Null(Sample(sampler, 2.3, 270, 30, RuntimeSimulationState.Unavailable).TicksPerSecond);
        Assert.Null(Sample(sampler, 2.4, 280, 30, RuntimeSimulationState.Stopped).TicksPerSecond);
    }

    [Fact]
    public void RestartBackgroundLongGapAndCounterRollbackDiscardStaleValues()
    {
        var sampler = new RuntimeMetricsSampler();
        Sample(sampler, 0, 0, 0);
        Sample(sampler, 1, 144, 20);
        RuntimeMetricsView restarted = sampler.Sample(TimeSpan.FromSeconds(1.1), 145, 0,
            new SimulationSessionId(2), RuntimeSimulationState.Running);
        Assert.Null(restarted.FramesPerSecond);
        Assert.Null(restarted.TicksPerSecond);
        Sample(sampler, 2, 200, 10);
        Assert.Equal(default, sampler.Sample(TimeSpan.FromSeconds(2.1), 210, 11,
            new SimulationSessionId(1), RuntimeSimulationState.Running, active: false));
        Assert.Null(Sample(sampler, 2.2, 220, 12).FramesPerSecond);
        Assert.Null(Sample(sampler, 10, 1000, 100).TicksPerSecond);
        Sample(sampler, 11, 1100, 110);
        Assert.Null(Sample(sampler, 11.1, 0, 0).FramesPerSecond);
        Assert.Null(Sample(sampler, 5, 1, 1).TicksPerSecond);
    }

    [Fact]
    public void StalledSimulationMeasuresZeroRatherThanConfiguredFrequency()
    {
        var sampler = new RuntimeMetricsSampler();
        Sample(sampler, 0, 0, 42);
        RuntimeMetricsView view = Sample(sampler, 0.8, 80, 42);
        Assert.Equal(100, view.FramesPerSecond);
        Assert.Equal(0, view.TicksPerSecond);
    }

    private static RuntimeMetricsView Sample(RuntimeMetricsSampler sampler, double seconds,
        ulong frames, ulong ticks, RuntimeSimulationState state = RuntimeSimulationState.Running) =>
        sampler.Sample(TimeSpan.FromSeconds(seconds), frames, ticks, new SimulationSessionId(1), state);
}
