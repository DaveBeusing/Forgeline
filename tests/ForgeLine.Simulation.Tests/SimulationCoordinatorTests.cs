using System.Diagnostics;
using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class SimulationCoordinatorTests
{
    [Fact]
    public void DefaultClockUsesTwentyHertzFixedTicks()
    {
        var coordinator = new SimulationCoordinator();

        Assert.Equal(20, coordinator.Clock.TicksPerSecond);
        Assert.Equal(TimeSpan.FromMilliseconds(50), coordinator.Clock.TickDuration);
        Assert.Equal(SimulationTick.Zero, coordinator.CurrentTick);
    }

    [Fact]
    public void RunTicksExecutesExactRequestedCount()
    {
        var coordinator = new SimulationCoordinator();

        ulong executed = coordinator.RunTicks(128);

        Assert.Equal(128UL, executed);
        Assert.Equal(new SimulationTick(128), coordinator.CurrentTick);
        Assert.Equal(128UL, coordinator.Metrics.CompletedTicks);
    }

    [Fact]
    public void SystemsExecuteInDeclaredPhaseOrder()
    {
        var executedPhases = new List<SimulationPhase>();
        var coordinator = new SimulationCoordinator();
        ReadOnlySpan<SimulationPhase> phases = SimulationPhaseOrder.All;

        for (int index = phases.Length - 1; index >= 0; index--)
        {
            coordinator.RegisterSystem(new RecordingSystem(phases[index], executedPhases));
        }

        coordinator.AdvanceOneTick();

        Assert.Equal(SimulationPhaseOrder.All.ToArray(), executedPhases);
    }

    [Fact]
    public void SamePhaseSystemsPreserveRegistrationOrder()
    {
        var executionOrder = new List<int>();
        var coordinator = new SimulationCoordinator();

        coordinator.RegisterSystem(new OrderedSystem(1, executionOrder));
        coordinator.RegisterSystem(new OrderedSystem(2, executionOrder));
        coordinator.RegisterSystem(new OrderedSystem(3, executionOrder));

        coordinator.AdvanceOneTick();

        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
    }

    [Fact]
    public void CommandsExecuteOnlyOnTheirTargetTick()
    {
        var recorder = new CommandRecorder();
        var coordinator = new SimulationCoordinator();
        coordinator.SubmitCommand(
            new RecordCommand(recorder, 42),
            new SimulationTick(4),
            new SimulationCommandSource(7));

        coordinator.RunTicks(3);

        Assert.Empty(recorder.Values);
        Assert.Equal(1, coordinator.PendingCommandCount);

        coordinator.AdvanceOneTick();

        Assert.Equal(new[] { 42 }, recorder.Values);
        Assert.Equal(new SimulationTick(4), recorder.ExecutionTicks[0]);
        Assert.Equal(0, coordinator.PendingCommandCount);
    }

    [Fact]
    public void SameTickCommandsUseStableSubmissionOrder()
    {
        var recorder = new CommandRecorder();
        var coordinator = new SimulationCoordinator();

        SimulationCommandEnvelope first = coordinator.SubmitCommand(
            new RecordCommand(recorder, 10),
            new SimulationTick(2));
        SimulationCommandEnvelope second = coordinator.SubmitCommand(
            new RecordCommand(recorder, 20),
            new SimulationTick(2));
        SimulationCommandEnvelope third = coordinator.SubmitCommand(
            new RecordCommand(recorder, 30),
            new SimulationTick(2));

        coordinator.RunTicks(2);

        Assert.True(first.Sequence < second.Sequence);
        Assert.True(second.Sequence < third.Sequence);
        Assert.Equal(new[] { 10, 20, 30 }, recorder.Values);
        Assert.Equal(3UL, coordinator.Metrics.CommandsProcessed);
    }

    [Fact]
    public void CommandsCannotTargetAlreadyExecutedTick()
    {
        var coordinator = new SimulationCoordinator();
        coordinator.AdvanceOneTick();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => coordinator.SubmitCommand(
                new RecordCommand(new CommandRecorder(), 1),
                new SimulationTick(1)));
    }

    [Fact]
    public void SameSeedAndCommandSequenceProduceSameState()
    {
        const ulong seed = 0xD15EA5EUL;

        var first = new SimulationCoordinator(seed: seed);
        var second = new SimulationCoordinator(seed: seed);

        EntityId firstEntity = first.Entities.CreateEntity();
        EntityId secondEntity = second.Entities.CreateEntity();
        first.Entities.AddComponent(firstEntity, new Accumulator(0));
        second.Entities.AddComponent(secondEntity, new Accumulator(0));

        for (ulong tick = 1; tick <= 32; tick++)
        {
            first.SubmitCommand(
                new RandomAccumulateCommand(firstEntity),
                new SimulationTick(tick));
            second.SubmitCommand(
                new RandomAccumulateCommand(secondEntity),
                new SimulationTick(tick));
        }

        first.RunTicks(32);
        second.RunTicks(32);

        Assert.True(first.Entities.TryGetComponent(firstEntity, out Accumulator firstState));
        Assert.True(second.Entities.TryGetComponent(secondEntity, out Accumulator secondState));
        Assert.Equal(firstState, secondState);
        Assert.Equal(first.Random.State, second.Random.State);
    }

    [Fact]
    public void WarmEmptyTickLoopDoesNotAllocate()
    {
        var coordinator = new SimulationCoordinator();
        coordinator.RunTicks(8);

        long before = GC.GetAllocatedBytesForCurrentThread();
        coordinator.RunTicks(1_024);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocatedBytes);
    }

    [Fact]
    public void HeadlessStyleExecutionCanRunFasterThanLogicalTime()
    {
        var coordinator = new SimulationCoordinator();
        var stopwatch = Stopwatch.StartNew();

        coordinator.RunTicks(2_000);

        stopwatch.Stop();
        TimeSpan logicalDuration = TimeSpan.FromTicks(
            coordinator.Clock.TickDuration.Ticks * 2_000L);

        Assert.True(stopwatch.Elapsed < logicalDuration);
    }

    private sealed class RecordingSystem : ISimulationSystem
    {
        private readonly List<SimulationPhase> _executedPhases;

        public RecordingSystem(
            SimulationPhase phase,
            List<SimulationPhase> executedPhases)
        {
            Phase = phase;
            _executedPhases = executedPhases;
        }

        public SimulationPhase Phase { get; }

        public void Execute(SimulationContext context)
        {
            _executedPhases.Add(context.Phase);
        }
    }

    private sealed class OrderedSystem : ISimulationSystem
    {
        private readonly int _value;
        private readonly List<int> _executionOrder;

        public OrderedSystem(int value, List<int> executionOrder)
        {
            _value = value;
            _executionOrder = executionOrder;
        }

        public SimulationPhase Phase => SimulationPhase.Movement;

        public void Execute(SimulationContext context)
        {
            _executionOrder.Add(_value);
        }
    }

    private sealed class CommandRecorder
    {
        public List<int> Values { get; } = new();

        public List<SimulationTick> ExecutionTicks { get; } = new();
    }

    private sealed class RecordCommand : ISimulationCommand
    {
        private readonly CommandRecorder _recorder;
        private readonly int _value;

        public RecordCommand(CommandRecorder recorder, int value)
        {
            _recorder = recorder;
            _value = value;
        }

        public void Execute(SimulationContext context)
        {
            _recorder.Values.Add(_value);
            _recorder.ExecutionTicks.Add(context.Tick);
        }
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
            int randomValue = context.Random.NextInt32(10_000);
            context.Entities.SetComponent(
                _entity,
                new Accumulator(accumulator.Value + randomValue));
        }
    }

    private readonly record struct Accumulator(int Value);
}
