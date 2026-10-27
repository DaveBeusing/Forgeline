namespace ForgeLine.Navigation;

public static class NavigationIntegrationContract
{
    public const int Revision = 4;
}

public enum NavigationModuleAvailability : byte
{
    NotConnected = 0,
    Available = 1,
    Unavailable = 2
}

/// <summary>Explicit proposed limits; no measured defaults or runtime registration.</summary>
public sealed record NavigationIntegrationLimits(
    int WorkPerTick,
    int WorkPerSearch,
    int MaximumActiveSearches,
    long MaximumSearchBytes,
    long MaximumCacheBytes,
    int MaximumEndpointCandidates,
    int RetryIntervalTicks,
    float PlanningHorizonSeconds,
    int MaximumNeighbors,
    int MaximumReservations,
    int MaximumDiagnosticEvents)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WorkPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WorkPerSearch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumActiveSearches);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSearchBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCacheBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEndpointCandidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RetryIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumNeighbors);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumReservations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDiagnosticEvents);
        if (!float.IsFinite(PlanningHorizonSeconds) || PlanningHorizonSeconds <= 0)
            throw new InvalidOperationException("Planning horizon must be finite and positive.");
    }
}

/// <summary>Concrete checkpoint types must be immutable, complete, versioned data only.</summary>
public interface INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    TCheckpoint CaptureState();
    void RestoreState(TCheckpoint checkpoint);
}
