namespace ForgeLine.Simulation;

public sealed class SimulationSystemPipeline
{
    private readonly List<Registration> _systems = new();
    private ulong _nextRegistrationOrder;
    private bool _sealed;

    public int SystemCount => _systems.Count;

    public void Register(ISimulationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (_sealed)
        {
            throw new InvalidOperationException(
                "Simulation systems cannot be registered after ticking has started.");
        }

        int phaseOrder = SimulationPhaseOrder.GetIndex(system.Phase);
        _systems.Add(new Registration(system, phaseOrder, _nextRegistrationOrder++));
        _systems.Sort(static (left, right) =>
        {
            int phaseComparison = left.PhaseOrder.CompareTo(right.PhaseOrder);
            return phaseComparison != 0
                ? phaseComparison
                : left.RegistrationOrder.CompareTo(right.RegistrationOrder);
        });
    }

    internal void Seal()
    {
        _sealed = true;
    }

    internal int ExecutePhase(SimulationPhase phase, SimulationContext context)
    {
        int executionCount = 0;

        for (int index = 0; index < _systems.Count; index++)
        {
            Registration registration = _systems[index];

            if (registration.System.Phase == phase)
            {
                registration.System.Execute(context);
                executionCount++;
            }
        }

        return executionCount;
    }

    private readonly record struct Registration(
        ISimulationSystem System,
        int PhaseOrder,
        ulong RegistrationOrder);
}
