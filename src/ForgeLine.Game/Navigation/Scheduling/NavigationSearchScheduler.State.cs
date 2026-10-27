using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal sealed partial class NavigationSearchScheduler<TKernelCheckpoint> where TKernelCheckpoint : class
{
    public NavigationSchedulerCheckpoint<TKernelCheckpoint> CaptureState()
    {
        EnsureMutable();
        int scratchBytes = _activeCount == 0 ? 0 : _storage.MaximumKernelCheckpointBytes;
        long estimated = NavigationSchedulerCheckpoint<TKernelCheckpoint>.EstimateMetadataBytes(
            _identity, _snapshotCount, _entryCount, _policy.Diagnostics.Entries, scratchBytes);
        if (estimated > _storage.MaximumCheckpointBytes)
            throw new InvalidOperationException("Checkpoint metadata and serialization scratch exceed the configured cap.");
        NavigationSchedulingPolicyCheckpoint policy = _policy.CaptureState();
        var snapshots = new NavigationSchedulerSnapshotKey[_snapshotCount];
        for (int index = 0; index < snapshots.Length; index++) snapshots[index] = Key(_snapshots[index]!);
        var entries = new NavigationScheduledRequestCheckpoint[_entryCount];
        byte[] scratch = scratchBytes == 0 ? [] : new byte[scratchBytes];
        int orderedCount = OrderEntries();
        for (int index = 0; index < orderedCount; index++)
        {
            Entry entry = _entries[_orderedEntries[index]]!;
            int written = 0;
            if (entry.Session is not null)
            {
                // The concrete kernel's reserved peak must include its temporary
                // CaptureState object; only bounded encoded bytes escape here.
                TKernelCheckpoint kernelState = entry.Session.CaptureState();
                written = _codec.Encode(kernelState, scratch);
                long payloadBytes = NavigationScheduledRequestCheckpoint.PayloadMemoryBytes(written);
                if (payloadBytes > _storage.MaximumCheckpointBytes - estimated)
                    throw new InvalidOperationException("Encoded search checkpoints exceed the complete checkpoint cap.");
                estimated += payloadBytes;
            }
            entries[index] = new NavigationScheduledRequestCheckpoint(entry.Request, entry.ServiceClass,
                entry.InputSequence, entry.QueuedAt, entry.HasQueuedAt, entry.NeedsAdmission, Key(entry.Snapshot),
                entry.Session is not null, scratch.AsSpan(0, written), _storage.MaximumKernelCheckpointBytes);
        }
        return new NavigationSchedulerCheckpoint<TKernelCheckpoint>(NavigationSchedulerCheckpoint<TKernelCheckpoint>.CurrentRevision,
            NavigationIntegrationContract.Revision, _identity, _limits, _policyLimits, _storage,
            _codec.CodecRevision, _geometry, _hasGeometry, _lastTick, _hasAdvanced,
            _nextSubmissionSequence, snapshots, policy, entries, _preservePinnedCosts);
    }

    public void RestoreState(NavigationSchedulerCheckpoint<TKernelCheckpoint> checkpoint)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Revision != NavigationSchedulerCheckpoint<TKernelCheckpoint>.CurrentRevision ||
            checkpoint.ContractRevision != NavigationIntegrationContract.Revision || checkpoint.Identity != _identity ||
            checkpoint.IntegrationLimits != _limits || checkpoint.PolicyLimits != _policyLimits ||
            checkpoint.StorageLimits != _storage || checkpoint.CodecRevision != _codec.CodecRevision || checkpoint.PreservePinnedCosts != _preservePinnedCosts ||
            checkpoint.EstimatedBytes > _storage.MaximumCheckpointBytes ||
            checkpoint.HasAdvanced != checkpoint.Policy.Ledger.HasTick ||
            checkpoint.LastTick != checkpoint.Policy.Ledger.Tick)
            throw new ArgumentException("Scheduler checkpoint identity, configuration or completed tick does not match.", nameof(checkpoint));

        // Structural, policy and immutable-input validation does not destroy the
        // current sessions. No generic frontiers are decoded or retained here.
        var policy = new NavigationSchedulingPolicy(_policyLimits);
        policy.RestoreState(checkpoint.Policy);
        var registry = new NavigationWorldSnapshot[checkpoint.SnapshotKeys.Count];
        var snapshotKeys = new HashSet<NavigationSchedulerSnapshotKey>();
        NavigationGrid? currentGrid = null;
        for (int index = 0; index < registry.Length; index++)
        {
            NavigationSchedulerSnapshotKey key = checkpoint.SnapshotKeys[index];
            if (key.Revision.Geometry != checkpoint.CurrentGeometry || !snapshotKeys.Add(key))
                throw new ArgumentException("Checkpoint snapshot registry must have one coherent geometry generation.", nameof(checkpoint));
            for (int prior = 0; prior < index; prior++)
                if (registry[prior].Capabilities == key.Capabilities && registry[prior].KnowledgeScope == key.KnowledgeScope)
                    throw new ArgumentException("Checkpoint contains duplicate snapshot profiles.", nameof(checkpoint));
            registry[index] = Resolve(key);
            currentGrid ??= registry[index].Grid;
            if (!ReferenceEquals(registry[index].Grid, currentGrid))
                throw new ArgumentException("Checkpoint profiles do not share one immutable geometry publication.", nameof(checkpoint));
        }
        if (checkpoint.HasGeometry != (registry.Length != 0) ||
            (checkpoint.Entries.Count != 0 && !checkpoint.HasGeometry))
            throw new ArgumentException("Checkpoint geometry availability does not match its registry.", nameof(checkpoint));

        var restored = new Entry[checkpoint.Entries.Count];
        var entities = new HashSet<EntityId>();
        var sequences = new HashSet<ulong>();
        long reserved = 0;
        int active = 0;
        for (int index = 0; index < restored.Length; index++)
        {
            NavigationScheduledRequestCheckpoint saved = checkpoint.Entries[index];
            NavigationWorldSnapshot? snapshot = null;
            for (int profile = 0; profile < registry.Length; profile++)
                if (Key(registry[profile]) == saved.SnapshotKey) { snapshot = registry[profile]; break; }
            snapshot ??= Resolve(saved.SnapshotKey);
            if (!ValidRequest(saved.Request, snapshot) || !entities.Add(saved.Request.Binding.Requester) ||
                !sequences.Add(saved.InputSequence) ||
                (saved.HasQueuedAt && (!checkpoint.HasAdvanced || saved.QueuedAt > checkpoint.LastTick ||
                    saved.QueuedAt < saved.Request.Binding.OriginalOrder.AcceptedAtTick)))
                throw new ArgumentException("Checkpoint contains an invalid, future or duplicate request.", nameof(checkpoint));
            bool hasPolicy = policy.TryGetEntry(saved.Request.Binding.Requester, out NavigationPolicyEntryState policyEntry);
            NavigationPolicyRequest token = Token(saved.Request);
            if (!saved.NeedsAdmission && (!hasPolicy || policyEntry.Request != token || !saved.HasQueuedAt ||
                    policyEntry.QueuedAt != saved.QueuedAt || policyEntry.ServiceClass != saved.ServiceClass ||
                    policyEntry.Status is not (NavigationPolicyEntryStatus.Ready or NavigationPolicyEntryStatus.BudgetLimited) ||
                    policyEntry.WorkConsumed > saved.Request.Search.MaximumWork ||
                    (policyEntry.Status == NavigationPolicyEntryStatus.Ready &&
                        policyEntry.WorkConsumed == saved.Request.Search.MaximumWork)))
                throw new ArgumentException("Admitted checkpoint request does not match its retained policy state.", nameof(checkpoint));
            if (saved.NeedsAdmission && hasPolicy && policyEntry.Request != token &&
                !IsReplacement(token, policyEntry.Request))
                throw new ArgumentException("Staged checkpoint request is not a valid replacement.", nameof(checkpoint));
            if (saved.NeedsAdmission && hasPolicy && policyEntry.Request == token &&
                (policyEntry.Status != NavigationPolicyEntryStatus.AdmissionDeferred || !saved.HasQueuedAt ||
                 policyEntry.QueuedAt != saved.QueuedAt || policyEntry.ServiceClass != saved.ServiceClass))
                throw new ArgumentException("A staged identical request must retain admission backpressure.", nameof(checkpoint));
            if (saved.NeedsAdmission && hasPolicy && policyEntry.Request != token &&
                policyEntry.Request.CommandGeneration == token.CommandGeneration &&
                (!saved.HasQueuedAt || policyEntry.QueuedAt != saved.QueuedAt))
                throw new ArgumentException("A staged retry must retain the original command's waiting age.", nameof(checkpoint));
            if (!saved.IsActive && !saved.NeedsAdmission && hasPolicy &&
                policyEntry.Status == NavigationPolicyEntryStatus.Ready && policyEntry.WorkConsumed > 0 &&
                (snapshotKeys.Contains(saved.SnapshotKey) || _preservePinnedCosts && saved.SnapshotKey.Revision.Geometry == checkpoint.CurrentGeometry))
                throw new ArgumentException("A current unfinished search requires its exact active frontier checkpoint.", nameof(checkpoint));
            if (saved.IsActive)
            {
                if (!hasPolicy || policyEntry.Status != NavigationPolicyEntryStatus.Ready ||
                    policyEntry.WorkConsumed >= saved.Request.Search.MaximumWork ||
                    !(snapshotKeys.Contains(saved.SnapshotKey) || _preservePinnedCosts && saved.SnapshotKey.Revision.Geometry == checkpoint.CurrentGeometry))
                    throw new ArgumentException("An active checkpoint must retain current, resumable work.", nameof(checkpoint));
                active++;
                reserved = checked(reserved + saved.Request.Search.MaximumWorkspaceBytes);
            }
            restored[index] = new Entry(saved.Request, snapshot, saved.ServiceClass, saved.InputSequence)
            {
                QueuedAt = saved.QueuedAt,
                HasQueuedAt = saved.HasQueuedAt,
                NeedsAdmission = saved.NeedsAdmission,
                HasAdvanced = saved.IsActive
            };
        }
        foreach (NavigationPolicyEntryState policyEntry in checkpoint.Policy.Entries)
            if (!entities.Contains(policyEntry.Request.Entity))
                throw new ArgumentException("Checkpoint policy state has no retained request owner.", nameof(checkpoint));
        if (active > _limits.MaximumActiveSearches || reserved > _activeWorkspaceBudget)
            throw new ArgumentException("Restored sessions exceed the active memory or count cap.", nameof(checkpoint));

        // Do not overlap old and restored fleet reservations. A deep decoder or
        // kernel rejection faults the service; it never silently restarts search.
        var failures = new List<Exception>();
        ReleaseAllSessions(failures);
        if (failures.Count != 0) throw new AggregateException("Current sessions could not reach the restore boundary.", failures);
        try
        {
            Array.Clear(_entries);
            Array.Clear(_snapshots);
            for (int index = 0; index < restored.Length; index++) _entries[index] = restored[index];
            for (int index = 0; index < registry.Length; index++) _snapshots[index] = registry[index];
            _entryCount = restored.Length;
            _snapshotCount = registry.Length;
            _geometry = checkpoint.CurrentGeometry;
            _hasGeometry = checkpoint.HasGeometry;
            _geometryGrid = currentGrid;
            _lastTick = checkpoint.LastTick;
            _hasAdvanced = checkpoint.HasAdvanced;
            _nextSubmissionSequence = checkpoint.NextSubmissionSequence;
            _policy.RestoreState(checkpoint.Policy);
            for (int index = 0; index < restored.Length; index++)
            {
                NavigationScheduledRequestCheckpoint saved = checkpoint.Entries[index];
                if (!saved.IsActive) continue;
                Entry entry = restored[index];
                entry.ReservedBytes = saved.Request.Search.MaximumWorkspaceBytes;
                _reservedBytes += entry.ReservedBytes;
                _activeCount++;
                TKernelCheckpoint state = _codec.Decode(saved.KernelPayload);
                if (state is VehicleFleetCheckpoint fleet &&
                    (!_policy.TryGetEntry(entry.Request.Binding.Requester, out var ledger) || fleet.WorkConsumed != ledger.WorkConsumed))
                    throw new InvalidDataException("Fleet continuation work does not match the scheduler ledger.");
                entry.Session = _kernel.RestoreSession(entry.Snapshot, state);
                if (entry.Session is null || entry.Session.Request != entry.Request.Search)
                    throw new InvalidDataException("Restored kernel state does not match the original search specification.");
            }
        }
        catch (Exception failure)
        {
            _fault = failure;
            ReleaseAllSessions(failures);
            throw;
        }
        // Diagnostic totals do not affect restored scheduling decisions.
        _started = _continued = _canceled = _stale = 0;
        _workConsumed = checkpoint.Policy.Counters.WorkConsumed;
    }

    internal object CaptureAuthoritativeState()
    {
        var state = CaptureState();
        return new
        {
            state.Revision, state.ContractRevision, state.PreservePinnedCosts, state.Identity, state.IntegrationLimits,
            state.PolicyLimits, state.StorageLimits, state.CodecRevision, state.CurrentGeometry,
            state.HasGeometry, state.LastTick, state.HasAdvanced, state.NextSubmissionSequence,
            state.SnapshotKeys,
            Policy = new { state.Policy.Revision, state.Policy.Limits, state.Policy.Ledger,
                state.Policy.Counters, state.Policy.Entries },
            Entries = state.Entries.Select(entry => new
            {
                entry.Request, entry.ServiceClass, entry.InputSequence, entry.QueuedAt,
                entry.HasQueuedAt, entry.NeedsAdmission, entry.SnapshotKey, entry.IsActive,
                KernelPayload = entry.KernelPayload.ToArray()
            }).ToArray()
        };
    }

    private NavigationWorldSnapshot Resolve(NavigationSchedulerSnapshotKey key)
    {
        NavigationWorldSnapshot? snapshot = _resolveSnapshot(key);
        if (snapshot is null || Key(snapshot) != key)
            throw new InvalidOperationException("The exact immutable checkpoint snapshot is unavailable.");
        return snapshot;
    }

    private static NavigationSchedulerSnapshotKey Key(NavigationWorldSnapshot snapshot) =>
        new(snapshot.Capabilities, snapshot.KnowledgeScope, snapshot.Revision, snapshot.CostAvailability);

    private static bool IsReplacement(in NavigationPolicyRequest newer, in NavigationPolicyRequest older) =>
        newer.CommandGeneration > older.CommandGeneration ||
        (newer.CommandGeneration == older.CommandGeneration &&
         newer.Pending.OriginalOrder == older.Pending.OriginalOrder &&
         newer.Pending.Request.RequestId > older.Pending.Request.RequestId);
}
