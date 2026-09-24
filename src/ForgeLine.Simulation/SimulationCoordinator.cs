using ForgeLine.Ecs;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation;

public sealed class SimulationCoordinator
{
    private readonly SimulationContext _context;
    private readonly SimulationCommandSchedule _commands = new();
    private readonly SimulationSystemPipeline _systems = new();
    private readonly List<ISimulationTickObserver> _tickObservers = new();
    private ulong _commandsProcessed;
    private ulong _systemInvocations;
    private int _peakPendingCommands;

    public SimulationCoordinator(
        int ticksPerSecond = FixedTickClock.DefaultTicksPerSecond,
        ulong seed = 1,
        int initialEntityCapacity = 256,
        JobScheduler? jobScheduler = null,
        SimulationDiagnosticsOptions? diagnosticsOptions = null)
    {
        Clock = new FixedTickClock(ticksPerSecond);
        var entities = new EntityRegistry(initialEntityCapacity);
        Random = new SimulationRandom(seed);
        _context = new SimulationContext(entities, Random, jobScheduler);
        Diagnostics = new SimulationDiagnostics(diagnosticsOptions);
    }

    public FixedTickClock Clock { get; }

    public SimulationRandom Random { get; }

    public EntityRegistry Entities => _context.Entities;

    public SimulationJobs Jobs => _context.Jobs;

    public SimulationDiagnostics Diagnostics { get; }

    public SimulationTick CurrentTick => Clock.CurrentTick;

    public int PendingCommandCount => _commands.PendingCount;

    public int RegisteredSystemCount => _systems.SystemCount;

    public int RegisteredTickObserverCount => _tickObservers.Count;

    public SimulationLoopMetrics Metrics =>
        new(
            CurrentTick.Value,
            _commandsProcessed,
            _systemInvocations,
            _commands.PendingCount,
            _peakPendingCommands);

    public void RegisterSystem(ISimulationSystem system)
    {
        _systems.Register(system);
    }

    public void RegisterTickObserver(ISimulationTickObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        if (CurrentTick != SimulationTick.Zero)
        {
            throw new InvalidOperationException(
                "Simulation tick observers must be registered before ticking starts.");
        }

        _tickObservers.Add(observer);
    }

    public SimulationCommandEnvelope SubmitCommand(
        ISimulationCommand command,
        SimulationTick targetTick,
        SimulationCommandSource source = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (targetTick.Value <= CurrentTick.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetTick),
                targetTick,
                "Commands must target a simulation tick that has not executed yet.");
        }

        SimulationCommandEnvelope envelope = _commands.Enqueue(targetTick, source, command);
        _peakPendingCommands = Math.Max(_peakPendingCommands, _commands.PendingCount);
        return envelope;
    }

    public void AdvanceOneTick()
    {
        _systems.Seal();

        SimulationDiagnostics.TickMeasurement measurement = Diagnostics.BeginTick();

        try
        {
            SimulationTick tick = Clock.Advance();
            _context.Tick = tick;

            ReadOnlySpan<SimulationPhase> phases = SimulationPhaseOrder.All;
            for (int index = 0; index < phases.Length; index++)
            {
                SimulationPhase phase = phases[index];
                _context.Phase = phase;

                if (phase == SimulationPhase.InputCommands)
                {
                    _commandsProcessed += (ulong)_commands.ExecuteForTick(tick, _context);
                }

                _systemInvocations += (ulong)_systems.ExecutePhase(phase, _context);
            }

            for (int index = 0; index < _tickObservers.Count; index++)
            {
                _tickObservers[index].OnTickCompleted(_context);
            }
        }
        finally
        {
            Diagnostics.EndTick(measurement);
        }
    }

    public ulong RunTicks(ulong tickCount)
    {
        return RunTicks(tickCount, CancellationToken.None);
    }

    public ulong RunTicks(ulong tickCount, CancellationToken cancellationToken)
    {
        ulong executed = 0;

        while (executed < tickCount && !cancellationToken.IsCancellationRequested)
        {
            AdvanceOneTick();
            executed++;
        }

        return executed;
    }
}
