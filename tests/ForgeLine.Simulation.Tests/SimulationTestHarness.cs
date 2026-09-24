using Xunit;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation.Tests;

internal sealed class SimulationTestHarness : IDisposable
{
    private readonly JobScheduler? _scheduler;

    public SimulationTestHarness(
        ulong seed = 1,
        int ticksPerSecond = FixedTickClock.DefaultTicksPerSecond,
        int initialEntityCapacity = 256,
        int workerCount = 0,
        bool enableDiagnostics = false)
    {
        if (workerCount > 0)
        {
            _scheduler = new JobScheduler(
                new JobSchedulerOptions
                {
                    WorkerCount = workerCount,
                    WorkerNamePrefix = "ForgeLine Test Harness Worker"
                });
        }

        Simulation = new SimulationCoordinator(
            ticksPerSecond,
            seed,
            initialEntityCapacity,
            _scheduler,
            new SimulationDiagnosticsOptions
            {
                Enabled = enableDiagnostics
            });
    }

    public SimulationCoordinator Simulation { get; }

    public EntityRegistry Entities => Simulation.Entities;

    public EntityId CreateEntity()
    {
        return Entities.CreateEntity();
    }

    public SimulationCommandEnvelope Submit(
        ISimulationCommand command,
        ulong targetTick,
        SimulationCommandSource source = default)
    {
        return Simulation.SubmitCommand(
            command,
            new SimulationTick(targetTick),
            source);
    }

    public ulong Run(ulong tickCount)
    {
        return Simulation.RunTicks(
            tickCount,
            TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _scheduler?.Dispose();
    }
}
