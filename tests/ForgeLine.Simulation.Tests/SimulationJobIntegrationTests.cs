using ForgeLine.Jobs;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class SimulationJobIntegrationTests
{
    [Fact]
    public void ParallelSystemWorkCompletesBeforeNextRegisteredSystem()
    {
        using var scheduler = new JobScheduler(
            new JobSchedulerOptions { WorkerCount = 4 });
        var visits = new int[4_096];
        var coordinator = new SimulationCoordinator(jobScheduler: scheduler);
        var verifier = new VerificationSystem(visits);

        coordinator.RegisterSystem(new ParallelRangeSystem(visits));
        coordinator.RegisterSystem(verifier);

        coordinator.AdvanceOneTick();

        Assert.True(verifier.ObservedCompleteRange);
    }

    [Fact]
    public void ScheduledJobFailureSurfacesFromSimulationCoordinator()
    {
        using var scheduler = new JobScheduler(
            new JobSchedulerOptions { WorkerCount = 2 });
        var coordinator = new SimulationCoordinator(jobScheduler: scheduler);
        coordinator.RegisterSystem(new FailingParallelSystem());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => coordinator.AdvanceOneTick());

        Assert.Equal("parallel-system-failure", exception.Message);
    }

    [Fact]
    public void CoordinatorRemainsSingleThreadedWhenNoSchedulerIsProvided()
    {
        var coordinator = new SimulationCoordinator();
        var system = new AvailabilitySystem();
        coordinator.RegisterSystem(system);

        coordinator.AdvanceOneTick();

        Assert.False(system.JobsAvailable);
    }

    private sealed class ParallelRangeSystem : ISimulationSystem
    {
        private readonly int[] _visits;

        public ParallelRangeSystem(int[] visits)
        {
            _visits = visits;
        }

        public SimulationPhase Phase => SimulationPhase.Movement;

        public void Execute(SimulationContext context)
        {
            context.Jobs.ParallelFor(
                0,
                _visits.Length,
                128,
                (start, end, _) =>
                {
                    for (int index = start; index < end; index++)
                    {
                        _visits[index] = 1;
                    }
                });
        }
    }

    private sealed class VerificationSystem : ISimulationSystem
    {
        private readonly int[] _visits;

        public VerificationSystem(int[] visits)
        {
            _visits = visits;
        }

        public SimulationPhase Phase => SimulationPhase.Movement;

        public bool ObservedCompleteRange { get; private set; }

        public void Execute(SimulationContext context)
        {
            ObservedCompleteRange = true;

            for (int index = 0; index < _visits.Length; index++)
            {
                if (_visits[index] != 1)
                {
                    ObservedCompleteRange = false;
                    return;
                }
            }
        }
    }

    private sealed class FailingParallelSystem : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.Sensors;

        public void Execute(SimulationContext context)
        {
            context.Jobs.Schedule(
                static _ => throw new InvalidOperationException(
                    "parallel-system-failure"));
        }
    }

    private sealed class AvailabilitySystem : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.Movement;

        public bool JobsAvailable { get; private set; }

        public void Execute(SimulationContext context)
        {
            JobsAvailable = context.Jobs.IsAvailable;
        }
    }
}
