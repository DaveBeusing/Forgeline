using ForgeLine.Client.RenderQualification;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests.RenderParallelization;

public sealed class SmokeRuntimeRateObserverTests
{
    [Fact]
    public void SlowNativeFramesUseRealProgressWithoutRelaxingTheStaleWindowPolicy()
    {
        var original = new RuntimeMetricsSampler();
        original.Sample(TimeSpan.Zero, 0, 0, new(1), RuntimeSimulationState.Running);
        Assert.Null(original.Sample(TimeSpan.FromSeconds(8), 1, 160, new(1), RuntimeSimulationState.Running).FramesPerSecond);
        var observer = new SmokeRuntimeRateObserver();
        for (int i = 0; i <= 40; i++)
            observer.Sample(TimeSpan.FromSeconds(i * .25), i >= 32 ? 1UL : 0,
                new(new(1), (ulong)(i * 5), RuntimeSimulationState.Running), true);
        Assert.NotNull(observer.LastRunningRates);
        Assert.Equal(4.0 / 3, observer.LastRunningRates.Value.FramesPerSecond!.Value, 8);
        Assert.Equal(20, observer.LastRunningRates.Value.TicksPerSecond);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MissingFrameOrTickProgressNeverProducesQualifiedRunningRates(bool noFrames)
    {
        var observer = new SmokeRuntimeRateObserver();
        for (int i = 0; i <= 20; i++)
            observer.Sample(TimeSpan.FromSeconds(i * .25), noFrames ? 0 : (ulong)i,
                new(new(1), noFrames ? (ulong)(i * 5) : 0, RuntimeSimulationState.Running), true);
        Assert.Null(observer.LastRunningRates);
    }

    [Fact]
    public void InactiveSessionChangeAndRollbackInvalidatePriorQualification()
    {
        var observer = new SmokeRuntimeRateObserver();
        observer.Sample(TimeSpan.Zero, 0, new(new(1), 0, RuntimeSimulationState.Running), true);
        observer.Sample(TimeSpan.FromSeconds(1), 60, new(new(1), 20, RuntimeSimulationState.Running), true);
        Assert.NotNull(observer.LastRunningRates);
        observer.Sample(TimeSpan.FromSeconds(1.1), 60, new(new(2), 0, RuntimeSimulationState.Running), true);
        Assert.Null(observer.LastRunningRates);
        observer.Sample(TimeSpan.FromSeconds(2.1), 120, new(new(2), 20, RuntimeSimulationState.Running), true);
        Assert.NotNull(observer.LastRunningRates);
        observer.Sample(TimeSpan.FromSeconds(2.2), 0, new(new(2), 0, RuntimeSimulationState.Running), true);
        Assert.Null(observer.LastRunningRates);
        observer.Sample(TimeSpan.FromSeconds(3.2), 60, new(new(2), 20, RuntimeSimulationState.Running), true);
        Assert.NotNull(observer.LastRunningRates);
        observer.Sample(TimeSpan.FromSeconds(3.3), 60, new(new(2), 20, RuntimeSimulationState.Running), false);
        Assert.Null(observer.LastRunningRates);
    }
}
