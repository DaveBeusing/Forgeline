using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationDiagnosticDeliveryTests
{
    [Fact]
    public void DeliveryWindowCountsActualCompletedOrdersForEachOwnerAndResetsWhenDisabled()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026),
            TestContext.Current.CancellationToken);
        runtime.Progress.SetNavigationDiagnosticWindow(new(1), true, SimulationTick.Zero);
        runtime.Progress.SetNavigationDiagnosticWindow(new(2), true, SimulationTick.Zero);
        runtime.Simulation.RunTicks(5000, TestContext.Current.CancellationToken);
        var first = Assert.IsType<NavigationDeliveryWindow>(runtime.Progress.CaptureNavigationDeliveryWindow(new(1), runtime.Simulation.CurrentTick));
        var second = Assert.IsType<NavigationDeliveryWindow>(runtime.Progress.CaptureNavigationDeliveryWindow(new(2), runtime.Simulation.CurrentTick));
        Assert.Equal(runtime.Simulation.SessionId, first.Session);
        Assert.Equal(SimulationTick.Zero, first.StartExclusive);
        Assert.Equal(runtime.Simulation.CurrentTick, first.EndInclusive);
        Assert.True(first.CompletedDeliveries > 0);
        Assert.True(second.CompletedDeliveries > 0);
        Assert.Equal((ulong)runtime.CargoTransport.Metrics.CompletedOrderCount, first.CompletedDeliveries + second.CompletedDeliveries);
        Assert.Null(runtime.Progress.CaptureNavigationDeliveryWindow(new(3), runtime.Simulation.CurrentTick));
        runtime.Progress.SetNavigationDiagnosticWindow(new(1), false, runtime.Simulation.CurrentTick);
        Assert.Null(runtime.Progress.CaptureNavigationDeliveryWindow(new(1), runtime.Simulation.CurrentTick));
        runtime.Progress.SetNavigationDiagnosticWindow(new(1), true, runtime.Simulation.CurrentTick);
        var reset = Assert.IsType<NavigationDeliveryWindow>(runtime.Progress.CaptureNavigationDeliveryWindow(new(1), runtime.Simulation.CurrentTick));
        Assert.Equal(0UL, reset.CompletedDeliveries);
        Assert.Equal(runtime.Simulation.CurrentTick, reset.StartExclusive);
    }
}
