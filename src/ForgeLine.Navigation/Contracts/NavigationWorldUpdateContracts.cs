using ForgeLine.World;

namespace ForgeLine.Navigation;

public readonly record struct NavigationObstacleChange(
    NavigationObstacleIdentity Identity, NavigationVersion SourceRevision,
    AxisAlignedBounds? PreviousBounds, AxisAlignedBounds? CurrentBounds);
public enum NavigationWorldUpdateStatus : byte
{
    NotConnected = 0, Pending = 1, Published = 2, BudgetExhausted = 3, InvalidInput = 4, Stale = 5
}
public readonly record struct NavigationWorldUpdateAdvance(
    NavigationWorldUpdateStatus Status, int WorkConsumed, NavigationWorldSnapshot? Snapshot);

/// <summary>Single-owner derived rebuilding; publish atomically only at the integration boundary.</summary>
public interface INavigationWorldUpdateSession<TCheckpoint> : INavigationStateTransfer<TCheckpoint>
    where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    NavigationWorldUpdateStatus QueueChanges(ReadOnlySpan<NavigationObstacleChange> changes);
    NavigationWorldUpdateAdvance Advance(int workAllowance);
}
