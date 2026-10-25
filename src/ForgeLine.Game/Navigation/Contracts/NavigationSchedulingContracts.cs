using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum NavigationSubmissionStatus : byte
{
    NotConnected = 0, Accepted = 1, AlreadyPending = 2, CapacityExhausted = 3, InvalidInput = 4, Stale = 5
}
public readonly record struct NavigationScheduledResult(NavigationOrderBinding Binding, NavigationSearchAdvance Search);

public sealed class NavigationScheduleAdvance
{
    public NavigationScheduleAdvance(SimulationTick tick, int workConsumed,
        ReadOnlySpan<NavigationScheduledResult> results, int maximumResults)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workConsumed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
        if (results.Length > maximumResults)
            throw new ArgumentException("Result publication exceeds its configured bound.", nameof(results));
        Tick = tick;
        WorkConsumed = workConsumed;
        Results = Array.AsReadOnly(results.ToArray());
    }

    public SimulationTick Tick { get; }
    public int WorkConsumed { get; }
    public IReadOnlyList<NavigationScheduledResult> Results { get; }
}

public interface INavigationSearchScheduler<TCheckpoint> : INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    NavigationSubmissionStatus Submit(NavigationBoundSearch request, NavigationWorldSnapshot snapshot);
    bool Cancel(in NavigationOrderBinding binding);
    void UpdateSnapshot(NavigationWorldSnapshot snapshot);
    NavigationScheduleAdvance Advance(SimulationTick tick, int workAllowance);
}
