using System.Text.Json;

namespace ForgeLine.Navigation;

public sealed class VehicleFleetStorageCheckpoint
{
    internal VehicleFleetStorageCheckpoint(ulong tick, NavigationRouteCacheState cache) { Tick = tick; Cache = cache; }
    public ulong Tick { get; }
    internal NavigationRouteCacheState Cache { get; }
}

public sealed record VehicleFleetCheckpoint(
    int Revision, NavigationSearchSpecification Request, int InitializedBytes,
    int WorkConsumed, bool CacheProbed, string CoreJson);

public readonly record struct VehicleFleetStorageDiagnostics(
    int ActiveArenas, int RetainedArenas, long PoolBytes, long Rents, long Reuses,
    long Returns, long InitializedBytes, int CacheEntries, long CacheBytes,
    ulong PositiveHits, ulong NegativeHits, ulong Misses, ulong Writes,
    ulong Evictions, ulong Expirations, ulong BudgetDeferrals);

/// <summary>Coordinator-owned bounded pool/cache; the existing host dispatches only core Advance work.</summary>
public readonly record struct VehicleFleetScopeDiagnostics(long Rents, long Reuses, long Returns,
    ulong PositiveHits, ulong NegativeHits, ulong Misses, ulong Writes, ulong BudgetDeferrals);

public sealed class VehicleFleetKernel : INavigationSearchKernel<VehicleFleetCheckpoint>, IDisposable
{
    private const int ArenaBytes = 128 * 1024;
    private const long AdditionalPeakBytes = 1024 * 1024;
    private readonly NavigationWorkspacePool _pool;
    private NavigationRouteCache _cache;
    private readonly VehicleRoutingKernel _core;
    private readonly Dictionary<ulong, ScopeCounters> _scopes = new();
    private ulong _tick;
    private bool _disposed;

    public VehicleFleetKernel(ulong worldIdentity, int maximumActiveSearches, long cacheBytes)
    {
        long arena = ArenaBytes + 192L;
        _pool = new(ArenaBytes, maximumActiveSearches, 2, 512 + (maximumActiveSearches + 2L) * arena);
        _cache = new(worldIdentity, VehicleRoutingKernel.AlgorithmRevision, 64, cacheBytes, 16, 4, 4096, 4096);
        _core = new(AdditionalPeakBytes + _pool.AccountedBytesPerWorkspace);
    }

    public NavigationModuleAvailability Availability => _disposed ? NavigationModuleAvailability.Unavailable : NavigationModuleAvailability.Available;
    public long RetainedReservationBytes => _pool.RetainedReservationBytes;

    public int BeginTick(ulong tick, int maintenanceAllowance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (tick <= _tick) throw new InvalidOperationException("Fleet storage tick must increase.");
        _tick = tick;
        return _cache.Maintain(tick, maintenanceAllowance);
    }

    public INavigationSearchSession<VehicleFleetCheckpoint> CreateSession(NavigationWorldSnapshot snapshot,
        NavigationSearchSpecification request) => Create(snapshot, request, null);

    public INavigationSearchSession<VehicleFleetCheckpoint> RestoreSession(NavigationWorldSnapshot snapshot,
        VehicleFleetCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Revision != 1 || checkpoint.WorkConsumed < 0 || checkpoint.WorkConsumed >= checkpoint.Request.MaximumWork ||
            checkpoint.InitializedBytes < 0 || checkpoint.InitializedBytes > ArenaBytes ||
            checkpoint.InitializedBytes % 64 != 0 || string.IsNullOrEmpty(checkpoint.CoreJson) ||
            (checkpoint.CacheProbed && checkpoint.InitializedBytes != ArenaBytes))
            throw new ArgumentException("Invalid fleet kernel continuation.", nameof(checkpoint));
        return Create(snapshot, checkpoint.Request, checkpoint);
    }

    private Session Create(NavigationWorldSnapshot snapshot, NavigationSearchSpecification request, VehicleFleetCheckpoint? state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_scopes.TryGetValue(request.KnowledgeScope, out var scope))
        {
            if (_scopes.Count == 16) throw new InvalidOperationException("Fleet scope capacity exhausted.");
            scope = new(); _scopes.Add(request.KnowledgeScope, scope);
        }
        long reusedBefore = _pool.GetStatistics().ReusedCount;
        if (!_pool.TryReserve(request, out var lease))
            throw new InvalidOperationException("The fleet scheduler did not reserve an available arena.");
        scope.Rents++;
        if (_pool.GetStatistics().ReusedCount > reusedBefore) scope.Reuses++;
        try
        {
            INavigationSearchSession<VehicleRoutingCheckpoint> core;
            if (state is null) core = _core.CreateSession(snapshot, request);
            else
            {
                var checkpoint = VehicleRoutingKernel.DecodeCheckpoint(request, state.CoreJson);
                if (checkpoint.State.Status != NavigationSearchStatus.Pending ||
                    state.WorkConsumed < checkpoint.State.Work + state.InitializedBytes / 64 ||
                    (!state.CacheProbed && checkpoint.State.Work != 0))
                    throw new ArgumentException("Fleet and core continuation work do not match.", nameof(state));
                core = _core.RestoreSession(snapshot, checkpoint);
            }
            return new(this, snapshot, core, lease, state);
        }
        catch { lease.Dispose(); scope.Returns++; throw; }
    }

    public VehicleFleetStorageDiagnostics Diagnostics
    {
        get
        {
            var pool = _pool.GetStatistics(); var cache = _cache.GetMetrics();
            return new(pool.ActiveWorkspaces, pool.RetainedWorkspaces, pool.TotalAccountedBytes,
                pool.RentedCount, pool.ReusedCount, pool.ReturnedCount, pool.InitializedBytes,
                cache.Storage.Entries, cache.Storage.EstimatedBytes + cache.WrapperEstimatedBytes,
                cache.PositiveHits, cache.NegativeHits, cache.Misses, cache.Storage.Writes,
                cache.Storage.Evictions, cache.Storage.Expirations, cache.BudgetDeferrals);
        }
    }

    // Explicit DTOs avoid exposing span-constructor checkpoint types to reflection serialization.
    public VehicleFleetStorageCheckpoint CaptureStorageState() =>
        new(_tick, _cache.CaptureState(4 * 1024 * 1024));

    public void RestoreStorageState(VehicleFleetStorageCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (_pool.GetStatistics().ActiveWorkspaces != 0)
            throw new InvalidOperationException("Release active sessions before restoring coordinator storage.");
        var restored = NavigationRouteCache.RestoreState(checkpoint.Cache, _cache.WorldIdentity,
            VehicleRoutingKernel.AlgorithmRevision, 8 * 1024 * 1024);
        _cache.Dispose(); _cache = restored; _tick = checkpoint.Tick;
        _scopes.Clear(); // Diagnostic counters are not scheduling authority.
    }

    public VehicleFleetScopeDiagnostics CaptureScopeDiagnostics(ulong scope) =>
        _scopes.TryGetValue(scope, out var counters) ? new(counters.Rents, counters.Reuses, counters.Returns,
            counters.PositiveHits, counters.NegativeHits, counters.Misses, counters.Writes, counters.Deferrals) : default;

    public object CaptureCacheState()
    {
        var cache = _cache.CaptureState(4 * 1024 * 1024);
        var storage = cache.Storage;
        return new
        {
            cache.WorldIdentity, cache.AlgorithmRevision, cache.MaximumWaypoints, cache.MaximumRegions,
            cache.PositiveHits, cache.NegativeHits, cache.Misses, cache.BudgetDeferrals,
            cache.FormatRevision, cache.ContractRevision, Tick = _tick,
            Storage = new
            {
                storage.FormatRevision, storage.MaximumEntries, storage.MaximumEstimatedBytes,
                storage.MaximumMaintenanceEntriesPerTick, storage.MaximumEvictionsPerWrite,
                storage.FreeHead, storage.Oldest, storage.Newest, storage.MaintenanceCursor,
                storage.RemainingMaintenance, storage.LastTick, storage.HasTick, storage.Metrics,
                Slots = storage.Slots.Select(s => new
                {
                    s.Used, s.Key, Value = s.Value.ToArray(), s.FailureReason, s.ExpiresAtTick,
                    s.Previous, s.Next, s.Bucket, s.PreviousInBucket, s.NextInBucket
                }).ToArray()
            }
        };
    }

    public static int Encode(VehicleFleetCheckpoint checkpoint, Span<byte> destination)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, VehicleRoutingKernel.JsonOptions);
        if (bytes.Length > destination.Length) throw new InvalidOperationException("Encoded fleet continuation exceeds its cap.");
        bytes.CopyTo(destination);
        return bytes.Length;
    }
    public static VehicleFleetCheckpoint Decode(ReadOnlySpan<byte> source, out int bytesRead)
    {
        if (source.Length > 3 * 1024 * 1024) throw new ArgumentException("Encoded fleet continuation exceeds its cap.", nameof(source));
        var state = JsonSerializer.Deserialize<VehicleFleetCheckpoint>(source, VehicleRoutingKernel.JsonOptions) ??
            throw new ArgumentException("Empty fleet continuation.", nameof(source));
        bytesRead = source.Length; return state;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_pool.GetStatistics().ActiveWorkspaces != 0)
            throw new InvalidOperationException("Release all fleet sessions after joining workers before disposing storage.");
        _cache.Dispose(); _pool.Dispose(); _disposed = true;
    }

    private sealed class ScopeCounters
    {
        internal long Rents, Reuses, Returns;
        internal ulong PositiveHits, NegativeHits, Misses, Writes, Deferrals;
    }

    private sealed class Session : INavigationSearchSession<VehicleFleetCheckpoint>, INavigationCoordinatorSearchSession
    {
        private readonly VehicleFleetKernel _owner;
        private readonly NavigationWorldSnapshot _snapshot;
        private readonly INavigationSearchSession<VehicleRoutingCheckpoint> _core;
        private readonly NavigationWorkspaceLease _lease;
        private int _work;
        private bool _cacheProbed, _disposed;

        internal Session(VehicleFleetKernel owner, NavigationWorldSnapshot snapshot,
            INavigationSearchSession<VehicleRoutingCheckpoint> core, NavigationWorkspaceLease lease, VehicleFleetCheckpoint? state)
        {
            _owner = owner; _snapshot = snapshot; _core = core; _lease = lease;
            if (state is not null)
            {
                _work = state.WorkConsumed; _cacheProbed = state.CacheProbed;
                _lease.AdvanceInitialization(state.InitializedBytes);
            }
        }

        public NavigationSearchSpecification Request => _core.Request;
        private long WorkspaceBytes => AdditionalPeakBytes + _lease.AccountedBytes;
        private int Limit(int allowance)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(allowance);
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Math.Min(allowance, Request.MaximumWork - _work);
        }

        public NavigationSearchAdvance Prepare(int workAllowance)
        {
            int allowance = Limit(workAllowance), used = 0;
            if (allowance == 0) return PendingOrLimited(0);
            if (!_lease.IsInitialized)
            {
                int bytes = _lease.AdvanceInitialization((int)Math.Min((long)allowance * 64, ArenaBytes));
                int units = (bytes + 63) / 64; used += units; _work += units;
            }
            if (_lease.IsInitialized && !_cacheProbed && used < allowance)
            {
                _cacheProbed = true;
                var lookup = _owner._cache.TryGet(_owner._cache.WorldIdentity, _snapshot, Request,
                    true, _owner._tick, _lease.Buffer, allowance - used,
                    Request.MaximumWorkspaceBytes - _lease.AccountedBytes, out var route, out var failure,
                    out int cacheWork, out _);
                used += cacheWork; _work += cacheWork;
                var counters = _owner._scopes[Request.KnowledgeScope];
                if (lookup == NavigationRouteCacheLookup.Found) counters.PositiveHits++;
                else if (lookup == NavigationRouteCacheLookup.Unreachable) counters.NegativeHits++;
                else if (lookup == NavigationRouteCacheLookup.Miss) counters.Misses++;
                else counters.Deferrals++;
                if (lookup == NavigationRouteCacheLookup.Found)
                    return new(NavigationSearchStatus.Found, used, WorkspaceBytes, route, NavigationFailureReason.None);
                if (lookup == NavigationRouteCacheLookup.Unreachable)
                    return new(NavigationSearchStatus.Unreachable, used, WorkspaceBytes, null, failure);
            }
            return PendingOrLimited(used);
        }

        public NavigationSearchAdvance Advance(int workAllowance)
        {
            int allowance = Limit(workAllowance);
            if (allowance == 0) return PendingOrLimited(0);
            if (!_lease.IsInitialized || !_cacheProbed)
                throw new InvalidOperationException("Complete coordinator preparation before worker search.");
            var result = _core.Advance(allowance); _work += result.WorkConsumed;
            if (result.Status == NavigationSearchStatus.Pending && _work == Request.MaximumWork)
                return result with { Status = NavigationSearchStatus.BudgetExhausted,
                    FailureReason = NavigationFailureReason.SearchBudgetExhausted };
            return result;
        }

        public NavigationSearchAdvance Complete(NavigationSearchAdvance result, int workAllowance)
        {
            int allowance = Limit(workAllowance);
            if (allowance > 0 && result.Status is NavigationSearchStatus.Found or NavigationSearchStatus.Unreachable)
            {
                // Whole-world keys only: the current kernel has no complete connector region stamp proof.
                var stored = _owner._cache.TryStore(_owner._cache.WorldIdentity, _snapshot, Request, result,
                    true, false, _owner._tick, checked(_owner._tick + 200), _lease.Buffer, allowance, out int work);
                if (stored == NavigationRouteCacheWrite.Stored) _owner._scopes[Request.KnowledgeScope].Writes++;
                else if (stored == NavigationRouteCacheWrite.BudgetExhausted) _owner._scopes[Request.KnowledgeScope].Deferrals++;
                _work += work; result = result with { WorkConsumed = result.WorkConsumed + work };
            }
            return result;
        }

        private NavigationSearchAdvance PendingOrLimited(int work) =>
            _work == Request.MaximumWork
                ? new(NavigationSearchStatus.BudgetExhausted, work, WorkspaceBytes, null, NavigationFailureReason.SearchBudgetExhausted)
                : new(NavigationSearchStatus.Pending, work, WorkspaceBytes, null, NavigationFailureReason.None);

        public VehicleFleetCheckpoint CaptureState() =>
            new(1, Request, _lease.InitializedBytes, _work, _cacheProbed, _core.CaptureState().Json);
        public void Cancel() => _core.Cancel();
        public void Dispose()
        {
            if (_disposed) return;
            try { _core.Dispose(); } finally { _lease.Dispose(); _owner._scopes[Request.KnowledgeScope].Returns++; _disposed = true; }
        }
    }
}
