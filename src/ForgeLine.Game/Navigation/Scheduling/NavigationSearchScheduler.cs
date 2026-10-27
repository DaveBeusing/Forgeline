using ForgeLine.Core;
using ForgeLine.Jobs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal readonly record struct NavigationSchedulerDiagnostics(
    int RetainedRequests, int ActiveSessions, long ReservedWorkspaceBytes, int SnapshotProfiles,
    ulong MaximumWaitingTicks, ulong Started, ulong Continued, ulong Canceled, ulong Stale,
    ulong WorkConsumed, bool IsFaulted, bool HasUnaccountedWork, NavigationPolicyDiagnostics Policy);

/// <summary>
/// Single-coordinator scheduler. Sessions reserve their complete peak workspace
/// before creation and execute only against pinned immutable snapshots. Workers
/// never receive ECS, queue or scheduler mutation access. The host owns Jobs.
/// </summary>
internal sealed partial class NavigationSearchScheduler<TKernelCheckpoint> :
    INavigationSearchScheduler<NavigationSchedulerCheckpoint<TKernelCheckpoint>>, IDisposable
    where TKernelCheckpoint : class
{
    private readonly INavigationSearchKernel<TKernelCheckpoint> _kernel;
    private readonly NavigationIntegrationLimits _limits;
    private readonly NavigationSchedulingPolicyLimits _policyLimits;
    private readonly NavigationSchedulerStorageLimits _storage;
    private readonly NavigationSchedulerIdentity _identity;
    private readonly long _activeWorkspaceBudget;
    private readonly NavigationSchedulerCheckpointCodec<TKernelCheckpoint> _codec;
    private readonly Func<NavigationSchedulerSnapshotKey, NavigationWorldSnapshot?> _resolveSnapshot;
    private readonly Func<NavigationBoundSearch, NavigationServiceClass> _selectServiceClass;
    private readonly JobScheduler? _jobs;
    private readonly bool _preservePinnedCosts;
    private readonly Dictionary<PlayerId, int> _viewerWork = new();
    private readonly NavigationSchedulingPolicy _policy;
    private readonly Entry?[] _entries;
    private readonly NavigationWorldSnapshot?[] _snapshots;
    private readonly int[] _orderedEntries;
    private readonly IComparer<int> _entryComparer;
    private readonly Predicate<NavigationPolicyRequest> _prepareGrant;
    private readonly NavigationPolicyGrant[] _grants;
    private readonly Outcome[] _outcomes;
    private readonly JobHandle[] _handles;
    private readonly NavigationScheduledResult[] _results;
    private NavigationVersion _geometry;
    private NavigationGrid? _geometryGrid;
    private SimulationTick _lastTick;
    private bool _hasGeometry;
    private bool _hasAdvanced;
    private bool _running;
    private bool _holdNewSessions;
    private bool _disposed;
    private bool _unaccountedWork;
    private Exception? _fault;
    private int _entryCount;
    private int _snapshotCount;
    private int _activeCount;
    private long _reservedBytes;
    private ulong _nextSubmissionSequence = 1;
    private ulong _started;
    private ulong _continued;
    private ulong _canceled;
    private ulong _stale;
    private ulong _workConsumed;

    internal NavigationSearchScheduler(
        INavigationSearchKernel<TKernelCheckpoint> kernel,
        NavigationIntegrationLimits limits,
        NavigationSchedulingPolicyLimits policyLimits,
        NavigationSchedulerStorageLimits storage,
        NavigationSchedulerIdentity identity,
        NavigationSchedulerCheckpointCodec<TKernelCheckpoint> codec,
        Func<NavigationSchedulerSnapshotKey, NavigationWorldSnapshot?> resolveSnapshot,
        Func<NavigationBoundSearch, NavigationServiceClass> selectServiceClass,
        JobScheduler? jobs, bool preservePinnedCosts = false)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(policyLimits);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(resolveSnapshot);
        ArgumentNullException.ThrowIfNull(selectServiceClass);
        limits.Validate();
        policyLimits.Validate();
        storage.Validate();
        identity.Validate();
        if (storage.ReservedPoolBytes >= limits.MaximumSearchBytes)
            throw new ArgumentException("Pool retention must leave a positive active-session memory allowance.", nameof(storage));
        if (policyLimits.WorkPerTick != limits.WorkPerTick || policyLimits.WorkPerRequest != limits.WorkPerSearch ||
            policyLimits.RetryIntervalTicks != limits.RetryIntervalTicks ||
            limits.MaximumActiveSearches > policyLimits.MaximumEntries)
            throw new ArgumentException("Scheduler policy and shared limits must describe the same work and retry bounds.");
        _kernel = kernel;
        _limits = limits;
        _policyLimits = policyLimits;
        _storage = storage;
        _identity = identity;
        _activeWorkspaceBudget = limits.MaximumSearchBytes - storage.ReservedPoolBytes;
        _codec = codec;
        _resolveSnapshot = resolveSnapshot;
        _selectServiceClass = selectServiceClass;
        _jobs = jobs; _preservePinnedCosts = preservePinnedCosts;
        _policy = new NavigationSchedulingPolicy(policyLimits);
        _entries = new Entry?[policyLimits.MaximumEntries];
        _snapshots = new NavigationWorldSnapshot?[storage.MaximumSnapshotProfiles];
        _orderedEntries = new int[policyLimits.MaximumEntries];
        _entryComparer = Comparer<int>.Create((left, right) =>
            _entries[left]!.InputSequence.CompareTo(_entries[right]!.InputSequence));
        _prepareGrant = PrepareForGrant;
        _grants = new NavigationPolicyGrant[policyLimits.MaximumSlicesPerTick];
        _outcomes = new Outcome[policyLimits.MaximumSlicesPerTick];
        _handles = new JobHandle[policyLimits.MaximumSlicesPerTick];
        _results = new NavigationScheduledResult[policyLimits.MaximumEntries];
    }

    public NavigationModuleAvailability Availability => _disposed || _fault is not null ||
        !Enum.IsDefined(_kernel.Availability) ? NavigationModuleAvailability.Unavailable : _kernel.Availability;

    internal NavigationSchedulerDiagnostics Diagnostics
    {
        get
        {
            ulong maximumWait = 0;
            foreach (Entry? entry in _entries)
                if (entry is not null && entry.HasQueuedAt && _hasAdvanced)
                    maximumWait = Math.Max(maximumWait, _lastTick.Value - entry.QueuedAt.Value);
            return new NavigationSchedulerDiagnostics(_entryCount, _activeCount, _reservedBytes, _snapshotCount,
                maximumWait, _started, _continued, _canceled, _stale, _workConsumed,
                _fault is not null, _unaccountedWork, _policy.Diagnostics);
        }
    }

    /// <summary>
    /// Accepted means retained in the bounded input queue. Admission occurs at
    /// Advance's real simulation tick; this interface has no submission tick.
    /// </summary>
    public NavigationSubmissionStatus Submit(NavigationBoundSearch request, NavigationWorldSnapshot snapshot)
    {
        EnsureMutable();
        if (Availability != NavigationModuleAvailability.Available) return NavigationSubmissionStatus.NotConnected;
        if (!ValidRequest(request, snapshot)) return NavigationSubmissionStatus.InvalidInput;
        int slot = Find(request.Binding.Requester);
        Entry? old = slot >= 0 ? _entries[slot] : null;
        if (old is not null)
        {
            if (old.Request == request)
                return IsCurrent(old) && ReferenceEquals(snapshot, old.Snapshot)
                    ? NavigationSubmissionStatus.AlreadyPending : NavigationSubmissionStatus.Stale;
            if (request.Binding.CommandGeneration < old.Request.Binding.CommandGeneration ||
                (request.Binding.CommandGeneration == old.Request.Binding.CommandGeneration &&
                 (request.Binding.OriginalOrder != old.Request.Binding.OriginalOrder ||
                  request.Binding.RequestId <= old.Request.Binding.RequestId)))
                return NavigationSubmissionStatus.Stale;
        }
        else if (_entryCount == _entries.Length)
        {
            return NavigationSubmissionStatus.CapacityExhausted;
        }

        NavigationServiceClass serviceClass = _selectServiceClass(request);
        if (!Enum.IsDefined(serviceClass)) return NavigationSubmissionStatus.InvalidInput;
        ulong sequence = _nextSubmissionSequence;
        ulong nextSequence = checked(sequence + 1);
        NavigationSubmissionStatus snapshotStatus = RegisterSnapshot(snapshot, publication: false);
        if (snapshotStatus != NavigationSubmissionStatus.Accepted) return snapshotStatus;
        bool sameCommand = old is not null && old.Request.Binding.CommandGeneration == request.Binding.CommandGeneration;
        if (old is not null)
        {
            ReleaseSession(old, cancel: true);
            _canceled = Add(_canceled, 1);
        }
        else
        {
            slot = FindEmpty();
            _entryCount++;
        }
        _entries[slot] = new Entry(request, snapshot, serviceClass, sequence)
        {
            QueuedAt = sameCommand ? old!.QueuedAt : default,
            HasQueuedAt = sameCommand && old!.HasQueuedAt
        };
        _nextSubmissionSequence = nextSequence;
        return NavigationSubmissionStatus.Accepted;
    }

    public bool Cancel(in NavigationOrderBinding binding)
    {
        EnsureMutable();
        int slot = Find(binding.Requester);
        if (slot < 0 || _entries[slot]!.Request.Binding != binding) return false;
        CancelPolicy(binding.Requester);
        ReleaseSession(_entries[slot]!, cancel: true);
        Remove(slot);
        _canceled = Add(_canceled, 1);
        return true;
    }

    public void UpdateSnapshot(NavigationWorldSnapshot snapshot)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(snapshot);
        NavigationSubmissionStatus result = RegisterSnapshot(snapshot, publication: true);
        if (result != NavigationSubmissionStatus.Accepted)
            throw new ArgumentException($"Snapshot publication was rejected: {result}.", nameof(snapshot));
        // Retain request intent for an ordered Stale publication. Old sessions
        // can release now because this service has no outstanding worker jobs.
        foreach (Entry? entry in _entries)
            if (entry is not null && !IsCurrent(entry)) ReleaseSession(entry, cancel: true);
    }

    public NavigationScheduleAdvance Advance(SimulationTick tick, int workAllowance)
    {
        EnsureMutable();
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        if (_hasAdvanced && tick <= _lastTick)
            throw new InvalidOperationException("A scheduler tick must advance monotonically.");
        _running = true;
        int handleCount = 0;
        bool joined = false;
        var failures = new List<Exception>();
        try
        {
            _viewerWork.Clear();
            _policy.BeginTick(tick, workAllowance);
            _lastTick = tick;
            _hasAdvanced = true;
            int resultCount = Admit(tick);
            _holdNewSessions = false;
            int grantCount = _policy.Plan(_grants, _prepareGrant);
            for (int index = 0; index < grantCount; index++)
            {
                NavigationPolicyGrant grant = _grants[index];
                int slot = Find(grant.Request.Entity);
                Entry? entry = slot >= 0 ? _entries[slot] : null;
                if (entry is null || Token(entry.Request) != grant.Request)
                {
                    _outcomes[index] = Synthetic(NavigationSearchStatus.Stale, NavigationFailureReason.StaleNavigationVersion);
                    continue;
                }
                if (!_policy.TryGetEntry(grant.Request.Entity, out NavigationPolicyEntryState policyEntry))
                    throw new InvalidOperationException("A grant lost its policy entry.");
                int remainingWork = entry.Request.Search.MaximumWork - policyEntry.WorkConsumed;
                if (remainingWork <= 0)
                {
                    _outcomes[index] = Synthetic(NavigationSearchStatus.BudgetExhausted, NavigationFailureReason.SearchBudgetExhausted);
                    continue;
                }
                if (entry.Session is null)
                {
                    _outcomes[index] = Synthetic(NavigationSearchStatus.Pending, NavigationFailureReason.None);
                    continue;
                }
                if (entry.HasAdvanced) _continued = Add(_continued, 1);
                entry.HasAdvanced = true;
                int allowance = Math.Min(grant.WorkAllowance, remainingWork);
                var outcome = new Outcome { Entry = entry, Allowance = allowance, WorkerAllowance = allowance,
                    Executed = true, WorkerRequired = true };
                if (entry.Session is INavigationCoordinatorSearchSession coordinated)
                {
                    var prepared = coordinated.Prepare(allowance);
                    outcome.Advance = prepared;
                    ValidateOutcome(outcome);
                    outcome.CoordinatorWork = prepared.WorkConsumed;
                    outcome.WorkerAllowance = allowance - prepared.WorkConsumed;
                    outcome.WorkerRequired = prepared.Status == NavigationSearchStatus.Pending && outcome.WorkerAllowance > 0;
                }
                _outcomes[index] = outcome;
            }
            // Every session and fixed reservation is now established; workers
            // cannot race later admissions or decide which lease becomes free.
            for (int index = 0; index < grantCount; index++)
            {
                if (!_outcomes[index].WorkerRequired) continue;
                int outcomeIndex = index;
                if (_jobs is null)
                    Execute(outcomeIndex);
                else
                {
                    JobHandle handle = _jobs.Schedule(cancellation =>
                    {
                        if (cancellation.IsCancellationRequested)
                            _outcomes[outcomeIndex].Failure = new OperationCanceledException(cancellation);
                        else Execute(outcomeIndex);
                    });
                    _handles[handleCount++] = handle;
                }
            }
            Join(handleCount, failures);
            joined = true;
            for (int index = 0; index < grantCount; index++)
            {
                if (_outcomes[index].Failure is Exception error)
                {
                    failures.Add(error);
                    _unaccountedWork = true;
                }
            }
            if (failures.Count != 0) throw new AggregateException("Navigation batch failed before complete work accounting.", failures);
            // Cache writes and lease-backed output work run only after all jobs join,
            // in grant order, and can spend only their original grant's remainder.
            for (int index = 0; index < grantCount; index++)
            {
                ref Outcome outcome = ref _outcomes[index];
                if (outcome.WorkerRequired && outcome.Entry?.Session is INavigationCoordinatorSearchSession coordinated)
                {
                    ValidateOutcome(outcome);
                    if (outcome.Advance.WorkConsumed > outcome.WorkerAllowance)
                        throw new InvalidDataException("Worker exceeded its disjoint grant.");
                    int remaining = outcome.Allowance - outcome.CoordinatorWork - outcome.Advance.WorkConsumed;
                    outcome.Advance = coordinated.Complete(outcome.Advance, remaining);
                    outcome.Advance = outcome.Advance with { WorkConsumed = checked(outcome.Advance.WorkConsumed + outcome.CoordinatorWork) };
                }
            }
            // Validate the whole batch before publishing or settling any result.
            for (int index = 0; index < grantCount; index++) ValidateOutcome(_outcomes[index]);
            int consumed = 0;
            for (int index = 0; index < grantCount; index++)
            {
                NavigationPolicyGrant grant = _grants[index];
                Outcome outcome = _outcomes[index];
                NavigationSearchAdvance advance = Normalize(outcome, grant);
                consumed = checked(consumed + advance.WorkConsumed);
                _workConsumed = Add(_workConsumed, (ulong)advance.WorkConsumed);
                PlayerId viewer = grant.Request.Pending.OriginalOrder.Issuer;
                _viewerWork[viewer] = checked(_viewerWork.GetValueOrDefault(viewer) + advance.WorkConsumed);
                NavigationPolicyContinuation continuation = advance.Status switch
                {
                    NavigationSearchStatus.Pending => outcome.Executed ? NavigationPolicyContinuation.Continue : NavigationPolicyContinuation.Retry,
                    NavigationSearchStatus.BudgetExhausted => NavigationPolicyContinuation.BudgetLimited,
                    _ => NavigationPolicyContinuation.Finished
                };
                bool current = _policy.Settle(grant, advance.WorkConsumed, continuation);
                int slot = Find(grant.Request.Entity);
                Entry? entry = slot >= 0 ? _entries[slot] : null;
                if (!current || entry is null || Token(entry.Request) != grant.Request || !IsCurrent(entry))
                {
                    _stale = Add(_stale, 1);
                    continue;
                }
                _results[resultCount++] = new NavigationScheduledResult(entry.Request.Binding, advance);
                if (advance.Status != NavigationSearchStatus.Pending)
                {
                    ReleaseSession(entry, cancel: advance.Status != NavigationSearchStatus.Found);
                    if (advance.Status != NavigationSearchStatus.BudgetExhausted) Remove(slot);
                }
            }
            Array.Sort(_results, 0, resultCount, ResultComparer.Instance);
            return new NavigationScheduleAdvance(tick, consumed, _results.AsSpan(0, resultCount), _results.Length);
        }
        catch (Exception failure)
        {
            if (!joined) Join(handleCount, failures);
            _fault = failure;
            // An invalid producer may have consumed unreported work. Never
            // convert this batch into fabricated zero-work success or retry.
            if (failure is InvalidDataException) _unaccountedWork = true;
            if (_policy.Diagnostics.UnsettledGrants != 0)
                foreach (Outcome outcome in _outcomes)
                    if (outcome.Executed) _unaccountedWork = true;
            ReleaseAllSessions(failures);
            throw;
        }
        finally
        {
            Array.Clear(_outcomes);
            Array.Clear(_handles);
            Array.Clear(_grants);
            Array.Clear(_results);
            _running = false;
        }
    }

    private int Admit(SimulationTick tick)
    {
        int orderedCount = OrderEntries();
        int resultCount = 0;
        for (int index = 0; index < orderedCount; index++)
        {
            int slot = _orderedEntries[index];
            Entry entry = _entries[slot]!;
            if (!entry.HasQueuedAt) { entry.QueuedAt = tick; entry.HasQueuedAt = true; }
            bool invalidTick = entry.Request.Binding.OriginalOrder.AcceptedAtTick > tick;
            if (invalidTick || !IsCurrent(entry))
            {
                CancelPolicy(entry.Request.Binding.Requester);
                ReleaseSession(entry, cancel: true);
                NavigationSearchAdvance result = invalidTick
                    ? new(NavigationSearchStatus.InvalidInput, 0, 0, null, NavigationFailureReason.None)
                    : new(NavigationSearchStatus.Stale, 0, 0, null, NavigationFailureReason.StaleNavigationVersion);
                _results[resultCount++] = new NavigationScheduledResult(entry.Request.Binding, result);
                if (!invalidTick) _stale = Add(_stale, 1);
                Remove(slot);
                continue;
            }
            if (!entry.NeedsAdmission) continue;
            NavigationPolicyRequest token = Token(entry.Request);
            NavigationPolicySubmission resultStatus = _policy.Submit(token, entry.ServiceClass, entry.QueuedAt);
            if (resultStatus is NavigationPolicySubmission.Accepted or NavigationPolicySubmission.AlreadyPending)
                entry.NeedsAdmission = false;
            else if (resultStatus is not (NavigationPolicySubmission.AdmissionExhausted or NavigationPolicySubmission.CapacityExhausted))
                throw new InvalidOperationException("A staged request does not match policy ownership.");
        }
        return resultCount;
    }

    private bool TryCreateSession(Entry entry)
    {
        long reservation = entry.Request.Search.MaximumWorkspaceBytes;
        if (Availability != NavigationModuleAvailability.Available || _activeCount == _limits.MaximumActiveSearches ||
            reservation > _activeWorkspaceBudget - _reservedBytes) return false;
        _reservedBytes += reservation;
        _activeCount++;
        entry.ReservedBytes = reservation;
        try
        {
            // The concrete kernel must reserve from the one shared bounded pool
            // here, before workers; initialization work belongs to Advance.
            entry.Session = _kernel.CreateSession(entry.Snapshot, entry.Request.Search);
            if (entry.Session is null || entry.Session.Request != entry.Request.Search)
                throw new InvalidDataException("The kernel created a session for a different request.");
            _started = Add(_started, 1);
            return true;
        }
        catch
        {
            ReleaseSession(entry, cancel: true);
            throw;
        }
    }

    private bool PrepareForGrant(NavigationPolicyRequest token)
    {
        if (Availability != NavigationModuleAvailability.Available) return false;
        int slot = Find(token.Entity);
        if (slot < 0 || Token(_entries[slot]!.Request) != token) return false;
        Entry entry = _entries[slot]!;
        if (entry.Session is not null) return true;
        if (_holdNewSessions) return false;
        if (TryCreateSession(entry)) return true;
        // An aged large reservation must eventually see enough free capacity.
        // Existing sessions may progress, but lower-ranked new sessions cannot
        // repeatedly consume each small release ahead of this waiting request.
        _holdNewSessions = true;
        return false;
    }

    private void Execute(int index)
    {
        try { _outcomes[index].Advance = _outcomes[index].Entry!.Session!.Advance(_outcomes[index].WorkerAllowance); }
        catch (Exception failure) { _outcomes[index].Failure = failure; }
    }

    private void Join(int count, List<Exception> failures)
    {
        if (_jobs is null || count == 0) return;
        try { _jobs.Wait(_jobs.CreateFence(_handles.AsSpan(0, count))); }
        catch (Exception failure) { failures.Add(failure); }
        // Fence creation/dispatch may fail during shutdown. Independently join
        // every submitted handle before any lease/session is disposed.
        for (int index = 0; index < count; index++)
            try { _jobs.Wait(_handles[index]); }
            catch (Exception failure) { failures.Add(failure); }
    }

    private static Outcome Synthetic(NavigationSearchStatus status, NavigationFailureReason reason) =>
        new() { Advance = new NavigationSearchAdvance(status, 0, 0, null, reason) };

    private static void ValidateOutcome(in Outcome outcome)
    {
        NavigationSearchAdvance value = outcome.Advance;
        if (!Enum.IsDefined(value.Status) || !Enum.IsDefined(value.FailureReason) || value.WorkConsumed < 0 ||
            value.WorkConsumed > outcome.Allowance || value.WorkspaceBytes < 0 ||
            (outcome.Entry is not null && value.WorkspaceBytes > outcome.Entry.ReservedBytes))
            throw new InvalidDataException("The kernel exceeded its granted work or reserved memory contract.");
        if (value.Status == NavigationSearchStatus.Found)
        {
            NavigationSearchSpecification? request = outcome.Entry?.Request.Search;
            if (request is null || value.Route is null || value.Route.Request != request ||
                value.FailureReason != NavigationFailureReason.None || value.Route.Waypoints.Count == 0 ||
                value.Route.Waypoints.Count > request.MaximumWork ||
                512L + value.Route.Waypoints.Count * 12L + value.Route.Regions.Count * 32L > request.MaximumWorkspaceBytes)
                throw new InvalidDataException("A found route does not match its captured request or output bounds.");
        }
        else if (value.Route is not null ||
            (value.Status == NavigationSearchStatus.Pending && value.FailureReason != NavigationFailureReason.None) ||
            (value.Status == NavigationSearchStatus.Unreachable && value.FailureReason is NavigationFailureReason.None or
                NavigationFailureReason.Canceled or NavigationFailureReason.StaleNavigationVersion or NavigationFailureReason.SearchBudgetExhausted) ||
            (value.Status == NavigationSearchStatus.BudgetExhausted && value.FailureReason != NavigationFailureReason.SearchBudgetExhausted) ||
            (value.Status == NavigationSearchStatus.Canceled && value.FailureReason != NavigationFailureReason.Canceled) ||
            (value.Status == NavigationSearchStatus.Stale && value.FailureReason != NavigationFailureReason.StaleNavigationVersion))
            throw new InvalidDataException("The kernel returned an inconsistent search status, route or failure reason.");
    }

    private NavigationSearchAdvance Normalize(in Outcome outcome, in NavigationPolicyGrant grant)
    {
        NavigationSearchAdvance value = outcome.Advance;
        if (outcome.Executed && value.Status == NavigationSearchStatus.Pending &&
            (value.WorkConsumed == 0 || (_policy.TryGetEntry(grant.Request.Entity, out NavigationPolicyEntryState state) &&
                state.WorkConsumed + value.WorkConsumed >= outcome.Entry!.Request.Search.MaximumWork)))
            return value with { Status = NavigationSearchStatus.BudgetExhausted, FailureReason = NavigationFailureReason.SearchBudgetExhausted };
        return value;
    }

    private bool ValidRequest(NavigationBoundSearch? request, NavigationWorldSnapshot? snapshot)
    {
        if (request is null || snapshot is null) return false;
        try { request.Validate(); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        NavigationSearchSpecification search = request.Search;
        return search.Revision == snapshot.Revision && search.Capabilities == snapshot.Capabilities &&
            search.KnowledgeScope == snapshot.KnowledgeScope && search.MaximumWork <= _limits.WorkPerSearch &&
            search.MaximumWorkspaceBytes <= _activeWorkspaceBudget &&
            search.MaximumEndpointCandidates <= _limits.MaximumEndpointCandidates;
    }

    private NavigationSubmissionStatus RegisterSnapshot(NavigationWorldSnapshot snapshot, bool publication)
    {
        if (!snapshot.Revision.IsValid) return NavigationSubmissionStatus.InvalidInput;
        if (_hasGeometry && snapshot.Revision.Geometry != _geometry)
        {
            if (!publication || snapshot.Revision.Geometry < _geometry) return NavigationSubmissionStatus.Stale;
            Array.Clear(_snapshots);
            _snapshotCount = 0;
            _geometryGrid = snapshot.Grid;
            _geometry = snapshot.Revision.Geometry;
        }
        if (!_hasGeometry)
        {
            _hasGeometry = true;
            _geometry = snapshot.Revision.Geometry;
            _geometryGrid = snapshot.Grid;
        }
        if (!ReferenceEquals(snapshot.Grid, _geometryGrid)) return NavigationSubmissionStatus.InvalidInput;
        int profile = FindSnapshot(snapshot.Capabilities, snapshot.KnowledgeScope);
        if (profile >= 0)
        {
            NavigationWorldSnapshot current = _snapshots[profile]!;
            if (snapshot.Revision.Costs != current.Revision.Costs &&
                (!publication || snapshot.Revision.Costs < current.Revision.Costs)) return NavigationSubmissionStatus.Stale;
            if (!ReferenceEquals(snapshot.SectorGraph, current.SectorGraph)) return NavigationSubmissionStatus.InvalidInput;
            if (snapshot.Revision.Costs == current.Revision.Costs && !ReferenceEquals(snapshot, current))
            {
                if (!publication) return NavigationSubmissionStatus.Stale;
                if (snapshot.CostAvailability == current.CostAvailability)
                    return NavigationSubmissionStatus.InvalidInput;
            }
            if (publication) _snapshots[profile] = snapshot;
            return NavigationSubmissionStatus.Accepted;
        }
        if (_snapshotCount == _snapshots.Length) return NavigationSubmissionStatus.CapacityExhausted;
        _snapshots[_snapshotCount++] = snapshot;
        return NavigationSubmissionStatus.Accepted;
    }

    private bool IsCurrent(Entry entry)
    {
        if (!_hasGeometry || entry.Request.Search.Revision.Geometry != _geometry ||
            !ReferenceEquals(entry.Snapshot.Grid, _geometryGrid)) return false;
        if (_preservePinnedCosts)
            return entry.Request.Search.Revision == entry.Snapshot.Revision &&
                entry.Request.Search.Capabilities == entry.Snapshot.Capabilities &&
                entry.Request.Search.KnowledgeScope == entry.Snapshot.KnowledgeScope;
        int profile = FindSnapshot(entry.Request.Search.Capabilities, entry.Request.Search.KnowledgeScope);
        return profile >= 0 && ReferenceEquals(_snapshots[profile], entry.Snapshot) &&
            _snapshots[profile]!.Revision == entry.Request.Search.Revision;
    }

    private int FindSnapshot(NavigationCapabilities capabilities, ulong knowledgeScope)
    {
        for (int index = 0; index < _snapshotCount; index++)
            if (_snapshots[index]!.Capabilities == capabilities && _snapshots[index]!.KnowledgeScope == knowledgeScope) return index;
        return -1;
    }

    private static NavigationPolicyRequest Token(NavigationBoundSearch bound) => new(
        new NavigationPendingPath(new NavigationPathRequest(bound.Binding.RequestId, bound.Binding.Requester,
            bound.Search.Start.Position, bound.Search.OriginalDestination, bound.Search.Capabilities, bound.Search.Revision.Geometry),
            bound.Binding.OriginalOrder), bound.Binding.CommandGeneration, bound.Binding.DockGeneration, bound.Binding.QueueGeneration);

    private void CancelPolicy(EntityId entity)
    {
        if (_policy.TryGetEntry(entity, out NavigationPolicyEntryState entry)) _policy.Cancel(entry.Request);
    }

    private int OrderEntries()
    {
        int count = 0;
        for (int slot = 0; slot < _entries.Length; slot++) if (_entries[slot] is not null) _orderedEntries[count++] = slot;
        Array.Sort(_orderedEntries, 0, count, _entryComparer);
        return count;
    }

    internal bool IsActive(in NavigationOrderBinding binding)
    {
        int slot = Find(binding.Requester);
        return slot >= 0 && _entries[slot]!.Request.Binding == binding && _entries[slot]!.Session is not null;
    }

    internal (int Retained, int Active, long Reserved, ulong Wait, ulong Work, int TickWork) CaptureViewer(PlayerId viewer)
    {
        int retained = 0, active = 0; long reserved = 0; ulong wait = 0, work = 0;
        foreach (var entry in _entries)
        {
            if (entry is null || entry.Request.Binding.OriginalOrder.Issuer != viewer) continue;
            retained++; if (entry.Session is not null) active++;
            reserved += entry.ReservedBytes;
            if (entry.HasQueuedAt && _hasAdvanced) wait = Math.Max(wait, _lastTick.Value - entry.QueuedAt.Value);
            if (_policy.TryGetEntry(entry.Request.Binding.Requester, out var policy))
                work += (ulong)policy.WorkConsumed;
        }
        return (retained, active, reserved, wait, work, _viewerWork.GetValueOrDefault(viewer));
    }

    private int Find(EntityId entity)
    {
        for (int index = 0; index < _entries.Length; index++)
            if (_entries[index]?.Request.Binding.Requester == entity) return index;
        return -1;
    }

    private int FindEmpty()
    {
        for (int index = 0; index < _entries.Length; index++) if (_entries[index] is null) return index;
        throw new InvalidOperationException("Scheduler queue storage is full.");
    }

    private void Remove(int slot) { _entries[slot] = null; _entryCount--; }

    private void ReleaseSession(Entry entry, bool cancel)
    {
        INavigationSearchSession<TKernelCheckpoint>? session = entry.Session;
        long reservation = entry.ReservedBytes;
        bool released = session is null;
        try
        {
            if (session is not null)
            {
                try { if (cancel) session.Cancel(); }
                finally { session.Dispose(); released = true; }
            }
        }
        catch (Exception failure) { _fault ??= failure; throw; }
        finally
        {
            if (released)
            {
                entry.Session = null;
                entry.ReservedBytes = 0;
                if (reservation > 0) { _reservedBytes -= reservation; _activeCount--; }
            }
        }
    }

    private void ReleaseAllSessions(List<Exception> failures)
    {
        foreach (Entry? entry in _entries)
            if (entry is not null)
                try { ReleaseSession(entry, cancel: true); }
                catch (Exception failure) { failures.Add(failure); }
    }

    private void EnsureMutable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running) throw new InvalidOperationException("Scheduler state belongs to the coordinator outside its worker boundary.");
        if (_fault is not null) throw new InvalidOperationException("The scheduler is faulted; no work may resume from incomplete accounting.", _fault);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_running) throw new InvalidOperationException("Complete the worker boundary before disposing the scheduler.");
        var failures = new List<Exception>();
        ReleaseAllSessions(failures);
        if (failures.Count != 0)
        {
            _fault = new AggregateException("Navigation session disposal failed.", failures);
            throw _fault;
        }
        Array.Clear(_entries);
        Array.Clear(_snapshots);
        _policy.Clear();
        _entryCount = 0;
        _snapshotCount = 0;
        _geometryGrid = null;
        _disposed = true;
    }

    private static ulong Add(ulong value, ulong increment) => increment > ulong.MaxValue - value ? ulong.MaxValue : value + increment;

    private sealed class Entry(NavigationBoundSearch request, NavigationWorldSnapshot snapshot,
        NavigationServiceClass serviceClass, ulong inputSequence)
    {
        internal NavigationBoundSearch Request { get; } = request;
        internal NavigationWorldSnapshot Snapshot { get; } = snapshot;
        internal NavigationServiceClass ServiceClass { get; } = serviceClass;
        internal ulong InputSequence { get; } = inputSequence;
        internal SimulationTick QueuedAt;
        internal bool HasQueuedAt;
        internal bool NeedsAdmission = true;
        internal bool HasAdvanced;
        internal long ReservedBytes;
        internal INavigationSearchSession<TKernelCheckpoint>? Session;
    }

    private struct Outcome
    {
        internal Entry? Entry;
        internal int Allowance;
        internal int WorkerAllowance;
        internal int CoordinatorWork;
        internal bool WorkerRequired;
        internal bool Executed;
        internal NavigationSearchAdvance Advance;
        internal Exception? Failure;
    }

    private sealed class ResultComparer : IComparer<NavigationScheduledResult>
    {
        internal static ResultComparer Instance { get; } = new();
        public int Compare(NavigationScheduledResult left, NavigationScheduledResult right)
        {
            int result = left.Binding.Requester.CompareTo(right.Binding.Requester);
            if (result == 0) result = left.Binding.CommandGeneration.CompareTo(right.Binding.CommandGeneration);
            if (result == 0) result = left.Binding.RequestId.CompareTo(right.Binding.RequestId);
            return result;
        }
    }
}
