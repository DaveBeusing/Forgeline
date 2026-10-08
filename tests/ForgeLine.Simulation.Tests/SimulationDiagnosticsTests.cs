using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class SimulationDiagnosticsTests
{
    [Fact]
    public void PhaseTimingPreservesSeededCommandAndObserverOrder()
    {
        var plain = new SimulationCoordinator(seed: 77);
        var timed = new SimulationCoordinator(seed: 77,
            diagnosticsOptions: new SimulationDiagnosticsOptions { Enabled = true, TrackPhaseTiming = true });
        var plainObserver = new CountingObserver();
        var timedObserver = new CountingObserver();
        plain.RegisterTickObserver(plainObserver);
        timed.RegisterTickObserver(timedObserver);
        EntityId first = plain.Entities.CreateEntity(), second = timed.Entities.CreateEntity();
        plain.Entities.AddComponent(first, new Accumulator(0));
        timed.Entities.AddComponent(second, new Accumulator(0));
        for (ulong tick = 1; tick <= 32; tick++)
        {
            plain.SubmitCommand(new RandomAccumulateCommand(first), new SimulationTick(tick));
            timed.SubmitCommand(new RandomAccumulateCommand(second), new SimulationTick(tick));
        }
        plain.RunTicks(32, TestContext.Current.CancellationToken);
        timed.RunTicks(32, TestContext.Current.CancellationToken);
        Assert.Equal(plain.Entities.GetComponent<Accumulator>(first), timed.Entities.GetComponent<Accumulator>(second));
        Assert.Equal(plain.Random.State, timed.Random.State);
        Assert.Equal(plain.Metrics, timed.Metrics);
        Assert.Equal(plainObserver.Ticks, timedObserver.Ticks);
        Assert.Equal(32UL, timedObserver.Ticks);
        Assert.True(timed.Diagnostics.PhaseTimingEnabled);
        foreach (var phase in SimulationPhaseOrder.All)
            Assert.True(timed.Diagnostics.GetLastPhaseDuration(phase) >= TimeSpan.Zero);
        Assert.True(timed.Diagnostics.LastTickObserversDuration >= TimeSpan.Zero);
    }

    [Fact]
    public void PhaseTimingRequiresEnabledDiagnosticsAndDoesNotAllocatePerTick()
    {
        var disabled = new SimulationCoordinator(diagnosticsOptions:
            new SimulationDiagnosticsOptions { TrackPhaseTiming = true });
        disabled.AdvanceOneTick();
        Assert.False(disabled.Diagnostics.PhaseTimingEnabled);
        Assert.Equal(TimeSpan.Zero, disabled.Diagnostics.GetLastPhaseDuration(SimulationPhase.Movement));
        var timed = new SimulationCoordinator(diagnosticsOptions:
            new SimulationDiagnosticsOptions { Enabled = true, TrackPhaseTiming = true });
        timed.RunTicks(32, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        timed.RunTicks(64, TestContext.Current.CancellationToken);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class CountingObserver : ISimulationTickObserver
    {
        public ulong Ticks { get; private set; }
        public void OnTickCompleted(SimulationContext context)
        {
            Assert.Equal(++Ticks, context.Tick.Value);
        }
    }

    [Fact]
    public void DiagnosticsCaptureTickEntityComponentAndRuntimeState()
    {
        using var harness = new SimulationTestHarness(
            seed: 12345,
            enableDiagnostics: true);

        EntityId entity = harness.CreateEntity();
        harness.Entities.AddComponent(entity, new Position(10, 20));

        harness.Run(16);

        SimulationDiagnosticsSnapshot snapshot =
            harness.Simulation.Diagnostics.Capture(harness.Simulation);

        Assert.True(snapshot.Enabled);
        Assert.Equal(20, snapshot.ConfiguredTicksPerSecond);
        Assert.Equal(16UL, snapshot.CompletedTicks);
        Assert.Equal(1, snapshot.Entities.LiveEntityCount);
        Assert.Equal(1, snapshot.Entities.TotalComponentCount);
        Assert.Single(snapshot.Components);
        Assert.Equal(1, snapshot.Components[0].Count);
        Assert.True(snapshot.Runtime.TotalAllocatedBytes > 0);
        Assert.True(snapshot.LastTickDuration >= TimeSpan.Zero);
        Assert.True(snapshot.AverageTickDuration >= TimeSpan.Zero);
        Assert.True(snapshot.MaxTickDuration >= snapshot.LastTickDuration);
    }

    [Fact]
    public void DisabledDiagnosticsPreserveZeroTimingCounters()
    {
        using var harness = new SimulationTestHarness();

        harness.Run(8);

        SimulationDiagnosticsSnapshot snapshot =
            harness.Simulation.Diagnostics.Capture(harness.Simulation);

        Assert.False(snapshot.Enabled);
        Assert.Equal(TimeSpan.Zero, snapshot.LastTickDuration);
        Assert.Equal(TimeSpan.Zero, snapshot.AverageTickDuration);
        Assert.Equal(TimeSpan.Zero, snapshot.MaxTickDuration);
        Assert.Equal(0, snapshot.ObservedAllocatedBytes);
    }

    [Fact]
    public void HarnessRunsRepeatableSeededCommandScenarios()
    {
        using var first = new SimulationTestHarness(seed: 77);
        using var second = new SimulationTestHarness(seed: 77);

        EntityId firstEntity = first.CreateEntity();
        EntityId secondEntity = second.CreateEntity();
        first.Entities.AddComponent(firstEntity, new Accumulator(0));
        second.Entities.AddComponent(secondEntity, new Accumulator(0));

        for (ulong tick = 1; tick <= 32; tick++)
        {
            first.Submit(new RandomAccumulateCommand(firstEntity), tick);
            second.Submit(new RandomAccumulateCommand(secondEntity), tick);
        }

        first.Run(32);
        second.Run(32);

        Assert.True(first.Entities.TryGetComponent(firstEntity, out Accumulator firstState));
        Assert.True(second.Entities.TryGetComponent(secondEntity, out Accumulator secondState));
        Assert.Equal(firstState, secondState);
        Assert.Equal(first.Simulation.Random.State, second.Simulation.Random.State);
    }

    private sealed class RandomAccumulateCommand : ISimulationCommand
    {
        private readonly EntityId _entity;

        public RandomAccumulateCommand(EntityId entity)
        {
            _entity = entity;
        }

        public void Execute(SimulationContext context)
        {
            Accumulator accumulator = context.Entities.GetComponent<Accumulator>(_entity);
            context.Entities.SetComponent(
                _entity,
                new Accumulator(
                    accumulator.Value + context.Random.NextInt32(1_000)));
        }
    }

    private readonly record struct Position(int X, int Y);

    private readonly record struct Accumulator(int Value);
}
