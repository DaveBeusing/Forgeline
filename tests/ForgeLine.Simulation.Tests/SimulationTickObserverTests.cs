using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class SimulationTickObserverTests
{
    [Fact]
    public void ObserverRunsAfterSnapshotEventsSystems()
    {
        var coordinator = new SimulationCoordinator();
        EntityId entity = coordinator.Entities.CreateEntity();
        coordinator.Entities.AddComponent(entity, new Marker(0));

        coordinator.RegisterSystem(new FinalPhaseSystem(entity));
        var observer = new RecordingObserver(entity);
        coordinator.RegisterTickObserver(observer);

        coordinator.AdvanceOneTick();

        Assert.Equal(new SimulationTick(1), observer.Tick);
        Assert.Equal(42, observer.Value);
        Assert.Equal(1, coordinator.RegisteredTickObserverCount);
    }

    [Fact]
    public void ObserverRegistrationIsClosedAfterTickingStarts()
    {
        var coordinator = new SimulationCoordinator();
        coordinator.AdvanceOneTick();

        Assert.Throws<InvalidOperationException>(
            () => coordinator.RegisterTickObserver(new RecordingObserver(EntityId.Invalid)));
    }

    private readonly record struct Marker(int Value);

    private sealed class FinalPhaseSystem : ISimulationSystem
    {
        private readonly EntityId _entity;

        public FinalPhaseSystem(EntityId entity)
        {
            _entity = entity;
        }

        public SimulationPhase Phase => SimulationPhase.SnapshotEvents;

        public void Execute(SimulationContext context)
        {
            context.Entities.SetComponent(_entity, new Marker(42));
        }
    }

    private sealed class RecordingObserver : ISimulationTickObserver
    {
        private readonly EntityId _entity;

        public RecordingObserver(EntityId entity)
        {
            _entity = entity;
        }

        public SimulationTick Tick { get; private set; }

        public int Value { get; private set; }

        public void OnTickCompleted(SimulationContext context)
        {
            Tick = context.Tick;

            if (_entity.IsValid &&
                context.Entities.TryGetComponent(_entity, out Marker marker))
            {
                Value = marker.Value;
            }
        }
    }
}
