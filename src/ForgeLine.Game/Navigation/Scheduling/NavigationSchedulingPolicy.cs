using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>
/// Bounded, single-coordinator scheduling policy. It does not run searches,
/// reserve active-session memory, create jobs, authorize results or mutate ECS.
/// The adapter must validate current ownership before every submission/publication;
/// removed requests deliberately leave no unbounded historical tombstone table.
/// </summary>
internal sealed class NavigationSchedulingPolicy
{
    private readonly NavigationSchedulingPolicyLimits _limits;
    private readonly NavigationPolicyEntryState?[] _entries;
    private readonly int[] _candidates;
    private readonly NavigationPolicyGrant[] _grants;
    private readonly IComparer<int> _comparer;
    private NavigationPolicyLedgerState _ledger = new(false, default, 0, 0, 0, 0, false, 1, 1);
    private NavigationPolicyCounters _counters;
    private int _entryCount;
    private int _grantCount;
    private int _nextSettlement;

    internal NavigationSchedulingPolicy(NavigationSchedulingPolicyLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        _limits = limits;
        _entries = new NavigationPolicyEntryState?[limits.MaximumEntries];
        _candidates = new int[limits.MaximumEntries];
        _grants = new NavigationPolicyGrant[limits.MaximumSlicesPerTick];
        _comparer = Comparer<int>.Create(CompareEntries);
    }

    internal NavigationPolicyDiagnostics Diagnostics
    {
        get
        {
            int limited = 0;
            int admissionDeferred = 0;
            ulong maximumWait = 0;
            foreach (NavigationPolicyEntryState? slot in _entries)
            {
                if (slot is not NavigationPolicyEntryState entry) continue;
                if (entry.Status == NavigationPolicyEntryStatus.BudgetLimited) limited++;
                if (entry.Status == NavigationPolicyEntryStatus.AdmissionDeferred) admissionDeferred++;
                maximumWait = Math.Max(maximumWait, _ledger.Tick.Value - entry.QueuedAt.Value);
            }
            return new NavigationPolicyDiagnostics(_entryCount, limited, admissionDeferred, _grantCount - _nextSettlement,
                maximumWait, _ledger.TickAllowance, _ledger.ReservedWork, _ledger.ConsumedWork,
                _ledger.TickAllowance - _ledger.ReservedWork,
                _limits.MaximumAdmissionsPerTick - _ledger.Admissions, _counters);
        }
    }

    /// <summary>Once per strictly increasing tick; a second call cannot refill an allowance.</summary>
    internal void BeginTick(SimulationTick tick, int workAllowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        EnsureSettled();
        if (_ledger.HasTick && tick <= _ledger.Tick)
            throw new InvalidOperationException("A scheduling tick must advance monotonically.");
        _ledger = _ledger with
        {
            HasTick = true,
            Tick = tick,
            TickAllowance = Math.Min(workAllowance, _limits.WorkPerTick),
            ReservedWork = 0,
            ConsumedWork = 0,
            Admissions = 0,
            Planned = false
        };
        Array.Clear(_grants);
        _grantCount = 0;
        _nextSettlement = 0;
    }

    /// <summary>
    /// Replacements invalidate earlier policy ownership even when admission is
    /// exhausted. A bounded deferred replacement retains waiting age in the old
    /// slot but cannot be planned until a later Submit passes admission. The
    /// caller keeps the actual gameplay order and explicitly retries submission.
    /// </summary>
    internal NavigationPolicySubmission Submit(in NavigationPolicyRequest request, NavigationServiceClass serviceClass)
    {
        EnsureTick();
        if (!request.IsValid || !Enum.IsDefined(serviceClass) ||
            request.Pending.OriginalOrder.AcceptedAtTick > _ledger.Tick)
            return NavigationPolicySubmission.InvalidInput;

        int slot = Find(request.Entity);
        NavigationPolicyEntryState? previous = slot >= 0 ? _entries[slot] : null;
        bool sameRequest = previous is NavigationPolicyEntryState existing && existing.Request == request;
        if (previous is NavigationPolicyEntryState old)
        {
            if (sameRequest && old.Status != NavigationPolicyEntryStatus.AdmissionDeferred)
                return NavigationPolicySubmission.AlreadyPending;
            if (!sameRequest && (request.CommandGeneration < old.Request.CommandGeneration ||
                (request.CommandGeneration == old.Request.CommandGeneration &&
                 (request.Pending.OriginalOrder != old.Request.Pending.OriginalOrder ||
                  request.Pending.Request.RequestId <= old.Request.Pending.Request.RequestId))))
                return NavigationPolicySubmission.Stale;
        }

        bool sameCommand = previous is NavigationPolicyEntryState prior &&
            prior.Request.CommandGeneration == request.CommandGeneration;
        SimulationTick queuedAt = sameCommand ? previous!.Value.QueuedAt : _ledger.Tick;
        SimulationTick readySince = sameCommand ? previous!.Value.ReadySince : _ledger.Tick;
        SimulationTick retryAt = sameCommand ? previous!.Value.RetryAt : _ledger.Tick;
        if (_ledger.Admissions == _limits.MaximumAdmissionsPerTick)
        {
            if (previous is NavigationPolicyEntryState deferred)
            {
                // The changed full request rejects every old grant even though
                // this placeholder retains the former slot's sequence until admission.
                _entries[slot] = new NavigationPolicyEntryState(request, deferred.Sequence, serviceClass,
                    queuedAt, readySince, retryAt, 0, NavigationPolicyEntryStatus.AdmissionDeferred);
                if (!sameRequest) RecordSupersession();
            }
            return NavigationPolicySubmission.AdmissionExhausted;
        }
        if (slot < 0 && _entryCount == _entries.Length)
            return NavigationPolicySubmission.CapacityExhausted;

        // Reject exhausted sequence space before changing retained ownership.
        ulong sequence = _ledger.NextRequestSequence;
        ulong nextSequence = checked(sequence + 1);
        if (slot < 0)
        {
            slot = FindEmpty();
            _entryCount++;
        }
        else if (!sameRequest)
        {
            RecordSupersession();
        }
        _entries[slot] = new NavigationPolicyEntryState(request, sequence, serviceClass, queuedAt,
            readySince, retryAt, 0, NavigationPolicyEntryStatus.Ready);
        _ledger = _ledger with { Admissions = _ledger.Admissions + 1, NextRequestSequence = nextSequence };
        _counters = _counters with { Accepted = Add(_counters.Accepted, 1) };
        return NavigationPolicySubmission.Accepted;
    }

    internal bool Cancel(in NavigationPolicyRequest request)
    {
        int slot = Find(request.Entity);
        if (slot < 0 || _entries[slot]!.Value.Request != request) return false;
        Remove(slot);
        _counters = _counters with { Canceled = Add(_counters.Canceled, 1) };
        return true;
    }

    /// <summary>Invalidates queued ownership; outstanding grants still require settlement.</summary>
    internal void Clear()
    {
        _counters = _counters with { Canceled = Add(_counters.Canceled, (ulong)_entryCount) };
        Array.Clear(_entries);
        _entryCount = 0;
    }

    internal bool TryGetEntry(EntityId entity, out NavigationPolicyEntryState entry)
    {
        int slot = Find(entity);
        entry = slot >= 0 ? _entries[slot]!.Value : default;
        return slot >= 0;
    }

    /// <summary>
    /// Reserves one batch before any worker starts. Entries get at most one slice
    /// per tick; unused reservations are not redistributed by completion timing.
    /// </summary>
    internal int Plan(Span<NavigationPolicyGrant> destination)
    {
        EnsureTick();
        if (_ledger.Planned)
            throw new InvalidOperationException("This tick already has a planned batch.");
        if (destination.Length < _grants.Length)
            throw new ArgumentException("The grant buffer must fit the configured slice limit.", nameof(destination));

        int candidateCount = 0;
        for (int slot = 0; slot < _entries.Length; slot++)
        {
            if (_entries[slot] is NavigationPolicyEntryState entry &&
                entry.Status == NavigationPolicyEntryStatus.Ready && entry.RetryAt <= _ledger.Tick)
                _candidates[candidateCount++] = slot;
        }
        Array.Sort(_candidates, 0, candidateCount, _comparer);

        int availableWork = _ledger.TickAllowance;
        int count = 0;
        ulong nextGrantSequence = _ledger.NextGrantSequence;
        for (int index = 0; index < candidateCount && count < _grants.Length && availableWork > 0; index++)
        {
            NavigationPolicyEntryState entry = _entries[_candidates[index]]!.Value;
            int allowance = Math.Min(Math.Min(_limits.WorkPerSlice, availableWork),
                _limits.WorkPerRequest - entry.WorkConsumed);
            if (allowance <= 0) continue;
            ulong sequence = nextGrantSequence;
            nextGrantSequence = checked(nextGrantSequence + 1);
            _grants[count++] = new NavigationPolicyGrant(sequence, entry.Sequence, _ledger.Tick, entry.Request, allowance);
            availableWork -= allowance;
        }

        _grantCount = count;
        _nextSettlement = 0;
        _ledger = _ledger with
        {
            Planned = true,
            ReservedWork = _ledger.TickAllowance - availableWork,
            NextGrantSequence = nextGrantSequence
        };
        _counters = _counters with { GrantedSlices = Add(_counters.GrantedSlices, (ulong)count) };
        _grants.AsSpan(0, count).CopyTo(destination);
        return count;
    }

    /// <summary>
    /// Called after the worker barrier, strictly in grant order. Charges canceled
    /// and obsolete work too. True means policy ownership still matches; the
    /// adapter must separately revalidate authoritative command/world ownership.
    /// </summary>
    internal bool Settle(in NavigationPolicyGrant grant, int workConsumed, NavigationPolicyContinuation continuation)
    {
        if (_nextSettlement >= _grantCount || _grants[_nextSettlement] != grant)
            throw new InvalidOperationException("Grants must be settled exactly once in their planned order.");
        if (workConsumed < 0 || workConsumed > grant.WorkAllowance)
            throw new ArgumentOutOfRangeException(nameof(workConsumed));
        if (!Enum.IsDefined(continuation)) throw new ArgumentOutOfRangeException(nameof(continuation));

        _nextSettlement++;
        _ledger = _ledger with { ConsumedWork = checked(_ledger.ConsumedWork + workConsumed) };
        _counters = _counters with { WorkConsumed = Add(_counters.WorkConsumed, (ulong)workConsumed) };
        int slot = Find(grant.Request.Entity);
        if (slot < 0 || _entries[slot]!.Value.Sequence != grant.RequestSequence ||
            _entries[slot]!.Value.Request != grant.Request)
        {
            _counters = _counters with { StaleSettlements = Add(_counters.StaleSettlements, 1) };
            return false;
        }

        NavigationPolicyEntryState entry = _entries[slot]!.Value;
        if (continuation == NavigationPolicyContinuation.Finished)
        {
            Remove(slot);
            _counters = _counters with { Completed = Add(_counters.Completed, 1) };
            return true;
        }

        int cumulativeWork = checked(entry.WorkConsumed + workConsumed);
        bool limited = continuation == NavigationPolicyContinuation.BudgetLimited || cumulativeWork == _limits.WorkPerRequest;
        bool retry = limited || continuation == NavigationPolicyContinuation.Retry || workConsumed == 0;
        _entries[slot] = entry with
        {
            WorkConsumed = cumulativeWork,
            // Even zero-progress service used a slice. Rotate it behind waiting
            // work; original queue age and retry timing remain independent.
            ReadySince = _ledger.Tick,
            RetryAt = retry ? RetryTick() : _ledger.Tick,
            Status = limited ? NavigationPolicyEntryStatus.BudgetLimited : NavigationPolicyEntryStatus.Ready
        };
        return true;
    }

    internal NavigationSchedulingPolicyCheckpoint CaptureState()
    {
        EnsureSettled();
        var entries = new NavigationPolicyEntryState[_entryCount];
        int index = 0;
        foreach (NavigationPolicyEntryState? slot in _entries)
            if (slot is NavigationPolicyEntryState entry) entries[index++] = entry;
        Array.Sort(entries, static (left, right) => left.Sequence.CompareTo(right.Sequence));
        return new NavigationSchedulingPolicyCheckpoint(NavigationSchedulingPolicyCheckpoint.CurrentRevision,
            _limits, _ledger, _counters, entries);
    }

    internal void RestoreState(NavigationSchedulingPolicyCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        EnsureSettled();
        if (checkpoint.Revision != NavigationSchedulingPolicyCheckpoint.CurrentRevision || checkpoint.Limits != _limits)
            throw new ArgumentException("Policy checkpoint revision or configuration does not match.", nameof(checkpoint));
        NavigationPolicyLedgerState ledger = checkpoint.Ledger;
        if (ledger.NextRequestSequence == 0 || ledger.NextGrantSequence == 0 ||
            ledger.TickAllowance < 0 || ledger.TickAllowance > _limits.WorkPerTick ||
            ledger.ReservedWork < 0 || ledger.ReservedWork > ledger.TickAllowance ||
            ledger.ConsumedWork < 0 || ledger.ConsumedWork > ledger.ReservedWork ||
            ledger.Admissions < 0 || ledger.Admissions > _limits.MaximumAdmissionsPerTick ||
            (!ledger.Planned && (ledger.ReservedWork != 0 || ledger.ConsumedWork != 0)) ||
            (!ledger.HasTick && (ledger.Tick != SimulationTick.Zero || ledger.TickAllowance != 0 ||
                ledger.Admissions != 0 || ledger.Planned || checkpoint.Entries.Count != 0)) ||
            checkpoint.Entries.Count > _entries.Length)
            throw new ArgumentException("Invalid policy checkpoint ledger.", nameof(checkpoint));

        var entities = new HashSet<EntityId>();
        var sequences = new HashSet<ulong>();
        foreach (NavigationPolicyEntryState entry in checkpoint.Entries)
        {
            if (!entry.Request.IsValid || entry.Request.Pending.OriginalOrder.AcceptedAtTick > entry.QueuedAt ||
                !Enum.IsDefined(entry.ServiceClass) || !Enum.IsDefined(entry.Status) ||
                entry.Sequence == 0 || entry.Sequence >= ledger.NextRequestSequence ||
                entry.QueuedAt > entry.ReadySince || entry.ReadySince > ledger.Tick ||
                entry.RetryAt < entry.ReadySince ||
                entry.RetryAt.Value > Add(ledger.Tick.Value, (ulong)_limits.RetryIntervalTicks) ||
                entry.WorkConsumed < 0 || entry.WorkConsumed > _limits.WorkPerRequest ||
                (entry.Status == NavigationPolicyEntryStatus.Ready && entry.WorkConsumed == _limits.WorkPerRequest) ||
                (entry.Status == NavigationPolicyEntryStatus.AdmissionDeferred && entry.WorkConsumed != 0) ||
                !entities.Add(entry.Request.Entity) || !sequences.Add(entry.Sequence))
                throw new ArgumentException("Invalid or duplicate policy checkpoint entry.", nameof(checkpoint));
        }

        Array.Clear(_entries);
        Array.Clear(_grants);
        _entryCount = checkpoint.Entries.Count;
        for (int index = 0; index < _entryCount; index++) _entries[index] = checkpoint.Entries[index];
        _ledger = ledger;
        _counters = checkpoint.Counters;
        _grantCount = 0;
        _nextSettlement = 0;
    }

    private int CompareEntries(int leftIndex, int rightIndex)
    {
        NavigationPolicyEntryState left = _entries[leftIndex]!.Value;
        NavigationPolicyEntryState right = _entries[rightIndex]!.Value;
        int comparison = EffectiveClass(left).CompareTo(EffectiveClass(right));
        if (comparison == 0) comparison = left.ReadySince.CompareTo(right.ReadySince);
        if (comparison == 0) comparison = left.Sequence.CompareTo(right.Sequence);
        if (comparison == 0) comparison = left.Request.Entity.CompareTo(right.Request.Entity);
        return comparison;
    }

    private int EffectiveClass(in NavigationPolicyEntryState entry)
    {
        ulong age = _ledger.Tick.Value - entry.ReadySince.Value;
        ulong promotion = Math.Min((ulong)entry.ServiceClass, age / (ulong)_limits.AgingIntervalTicks);
        return (int)entry.ServiceClass - (int)promotion;
    }

    private int Find(EntityId entity)
    {
        for (int slot = 0; slot < _entries.Length; slot++)
            if (_entries[slot] is NavigationPolicyEntryState entry && entry.Request.Entity == entity) return slot;
        return -1;
    }

    private int FindEmpty()
    {
        for (int slot = 0; slot < _entries.Length; slot++)
            if (!_entries[slot].HasValue) return slot;
        throw new InvalidOperationException("The policy entry count does not match its storage.");
    }

    private void Remove(int slot)
    {
        _entries[slot] = null;
        _entryCount--;
    }

    private void RecordSupersession()
    {
        _counters = _counters with
        {
            Canceled = Add(_counters.Canceled, 1),
            Superseded = Add(_counters.Superseded, 1)
        };
    }

    private SimulationTick RetryTick() => new(Add(_ledger.Tick.Value, (ulong)_limits.RetryIntervalTicks));

    private void EnsureTick()
    {
        if (!_ledger.HasTick) throw new InvalidOperationException("Begin a simulation tick before scheduling.");
    }

    private void EnsureSettled()
    {
        if (_nextSettlement != _grantCount)
            throw new InvalidOperationException("Complete the worker boundary and settle every grant first.");
    }

    private static ulong Add(ulong value, ulong increment) =>
        increment > ulong.MaxValue - value ? ulong.MaxValue : value + increment;
}
