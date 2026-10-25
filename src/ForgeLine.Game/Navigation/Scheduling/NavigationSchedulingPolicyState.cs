using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal enum NavigationServiceClass : byte { PlayerOrder, GeometryChange, Recovery }
internal enum NavigationPolicySubmission : byte
{
    Accepted, AlreadyPending, CapacityExhausted, AdmissionExhausted, InvalidInput, Stale
}
internal enum NavigationPolicyEntryStatus : byte { Ready, BudgetLimited, AdmissionDeferred }
internal enum NavigationPolicyContinuation : byte { Continue, Retry, Finished, BudgetLimited }

/// <summary>Explicit policy tuning, with no runtime registration or measured defaults.</summary>
internal sealed record NavigationSchedulingPolicyLimits(
    int MaximumEntries,
    int MaximumAdmissionsPerTick,
    int MaximumSlicesPerTick,
    int WorkPerTick,
    int WorkPerRequest,
    int WorkPerSlice,
    int AgingIntervalTicks,
    int RetryIntervalTicks)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumAdmissionsPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSlicesPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WorkPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WorkPerRequest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WorkPerSlice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(AgingIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RetryIntervalTicks);
        if (MaximumSlicesPerTick > MaximumEntries)
            throw new ArgumentOutOfRangeException(nameof(MaximumSlicesPerTick));
    }
}

/// <summary>
/// Internal policy metadata only. The caller supplies the authoritative command
/// generation; a tick or search request ID is not a substitute for that generation.
/// </summary>
internal readonly record struct NavigationPolicyRequest(
    NavigationPendingPath Pending,
    ulong CommandGeneration,
    ulong DockGeneration,
    ulong QueueGeneration)
{
    internal EntityId Entity => Pending.Request.Requester;

    internal bool IsValid => Entity.IsValid && Pending.Request.RequestId != 0 &&
        Pending.Request.NavigationVersion.IsValid && CommandGeneration != 0 &&
        Pending.OriginalOrder.Issuer.IsSpecified &&
        Pending.OriginalOrder.SubmittedAtTick <= Pending.OriginalOrder.AcceptedAtTick &&
        Pending.OriginalOrder.WorldTarget == Pending.Request.Destination &&
        Enum.IsDefined(Pending.OriginalOrder.Kind) &&
        (!Pending.OriginalOrder.TargetHeadingRadians.HasValue ||
         float.IsFinite(Pending.OriginalOrder.TargetHeadingRadians.Value));
}

internal readonly record struct NavigationPolicyGrant(
    ulong Sequence,
    ulong RequestSequence,
    SimulationTick Tick,
    NavigationPolicyRequest Request,
    int WorkAllowance);

internal readonly record struct NavigationPolicyEntryState(
    NavigationPolicyRequest Request,
    ulong Sequence,
    NavigationServiceClass ServiceClass,
    SimulationTick QueuedAt,
    SimulationTick ReadySince,
    SimulationTick RetryAt,
    int WorkConsumed,
    NavigationPolicyEntryStatus Status);

internal readonly record struct NavigationPolicyLedgerState(
    bool HasTick,
    SimulationTick Tick,
    int TickAllowance,
    int ReservedWork,
    int ConsumedWork,
    int Admissions,
    bool Planned,
    ulong NextRequestSequence,
    ulong NextGrantSequence);

internal readonly record struct NavigationPolicyCounters(
    ulong Accepted,
    ulong Canceled,
    ulong Superseded,
    ulong StaleSettlements,
    ulong Completed,
    ulong GrantedSlices,
    ulong WorkConsumed);

internal readonly record struct NavigationPolicyDiagnostics(
    int Entries,
    int BudgetLimitedEntries,
    int AdmissionDeferredEntries,
    int UnsettledGrants,
    ulong MaximumWaitingTicks,
    int TickAllowance,
    int ReservedWork,
    int ConsumedWork,
    int RemainingUnreservedWork,
    int RemainingAdmissions,
    NavigationPolicyCounters Counters);

/// <summary>
/// Detached policy state at a completed grant boundary. Search frontiers,
/// snapshots, command ownership and memory leases are not contained here.
/// </summary>
internal sealed class NavigationSchedulingPolicyCheckpoint
{
    internal const int CurrentRevision = 1;

    internal NavigationSchedulingPolicyCheckpoint(
        int revision,
        NavigationSchedulingPolicyLimits limits,
        NavigationPolicyLedgerState ledger,
        NavigationPolicyCounters counters,
        ReadOnlySpan<NavigationPolicyEntryState> entries)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (entries.Length > limits.MaximumEntries)
            throw new ArgumentException("Policy entries exceed the configured capacity.", nameof(entries));
        Revision = revision;
        Limits = limits;
        Ledger = ledger;
        Counters = counters;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    internal int Revision { get; }
    internal NavigationSchedulingPolicyLimits Limits { get; }
    internal NavigationPolicyLedgerState Ledger { get; }
    internal NavigationPolicyCounters Counters { get; }
    internal IReadOnlyList<NavigationPolicyEntryState> Entries { get; }
}
