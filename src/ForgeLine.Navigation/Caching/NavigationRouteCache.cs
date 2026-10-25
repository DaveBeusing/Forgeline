using System.Buffers.Binary;
using System.Numerics;

namespace ForgeLine.Navigation;

internal enum NavigationRouteCacheLookup : byte
{
    Miss, Found, Unreachable, BudgetExhausted, BufferTooSmall, MemoryPressure
}

internal enum NavigationRouteCacheWrite : byte
{
    Stored, NotCacheable, CapacityExhausted, BudgetExhausted, BufferTooSmall, MemoryPressure
}

/// <summary>
/// Exact route-result cache owned by the search coordinator. A world identity is a stable,
/// nonzero match/content generation supplied by composition, never a worker-completion counter.
/// Snapshot inputs are read during calls and are never retained by this cache.
/// </summary>
internal sealed class NavigationRouteCache : IDisposable
{
    private const int HeaderBytes = 64;
    private const int PointBytes = 12;
    private const int RegionBytes = 16;
    private const int PayloadRevision = 1;
    private const long WrapperBytes = 256;
    private readonly NavigationResultCache<NavigationRouteCacheKey, byte> _storage;
    private ulong _positiveHits;
    private ulong _negativeHits;
    private ulong _misses;
    private ulong _budgetDeferrals;

    public NavigationRouteCache(ulong worldIdentity, int algorithmRevision,
        int maximumEntries, long maximumEstimatedBytes, int maximumMaintenanceEntriesPerTick,
        int maximumEvictionsPerWrite, int maximumWaypoints, int maximumRegions)
    {
        ArgumentOutOfRangeException.ThrowIfZero(worldIdentity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(algorithmRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWaypoints);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRegions);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEstimatedBytes, WrapperBytes);
        WorldIdentity = worldIdentity;
        AlgorithmRevision = algorithmRevision;
        MaximumWaypoints = maximumWaypoints;
        MaximumRegions = maximumRegions;
        MaximumPayloadBytes = EncodedLength(maximumWaypoints, maximumRegions);
        _storage = new NavigationResultCache<NavigationRouteCacheKey, byte>(maximumEntries,
            maximumEstimatedBytes - WrapperBytes, maximumMaintenanceEntriesPerTick, maximumEvictionsPerWrite);
    }

    private NavigationRouteCache(NavigationRouteCacheState state,
        NavigationResultCache<NavigationRouteCacheKey, byte> storage)
    {
        WorldIdentity = state.WorldIdentity;
        AlgorithmRevision = state.AlgorithmRevision;
        MaximumWaypoints = state.MaximumWaypoints;
        MaximumRegions = state.MaximumRegions;
        MaximumPayloadBytes = EncodedLength(MaximumWaypoints, MaximumRegions);
        _storage = storage;
        _positiveHits = state.PositiveHits;
        _negativeHits = state.NegativeHits;
        _misses = state.Misses;
        _budgetDeferrals = state.BudgetDeferrals;
    }

    public ulong WorldIdentity { get; }
    public int AlgorithmRevision { get; }
    public int MaximumWaypoints { get; }
    public int MaximumRegions { get; }
    public int MaximumPayloadBytes { get; }

    public NavigationRouteCacheMetrics GetMetrics() => new(_storage.GetMetrics(), WrapperBytes,
        _positiveHits, _negativeHits, _misses, _budgetDeferrals);

    /// <summary>
    /// A cache deferral is not a search failure: the kernel may bypass caching and continue its
    /// ordinary resumable search. Scratch belongs to the caller; additionalWorkspaceBytes reserves
    /// both temporary decode arrays and copied result arrays, in addition to that existing scratch.
    /// </summary>
    public NavigationRouteCacheLookup TryGet(ulong worldIdentity, NavigationWorldSnapshot snapshot,
        NavigationSearchSpecification request, bool costSensitive, ulong tick, Span<byte> scratch,
        int workAllowance, long additionalWorkspaceBytes, out NavigationRouteProposal? route,
        out NavigationFailureReason failureReason, out int workConsumed, out long reconstructionBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        ArgumentOutOfRangeException.ThrowIfNegative(additionalWorkspaceBytes);
        route = null;
        failureReason = NavigationFailureReason.None;
        workConsumed = 0;
        reconstructionBytes = 0;
        if (!Charge(1, workAllowance, ref workConsumed)) return Defer();
        ValidateInput(worldIdentity, snapshot, request);
        workAllowance = Math.Min(workAllowance, request.MaximumWork);
        NavigationRouteCacheKey key = CreateKey(snapshot, request, costSensitive, false);
        NavigationCacheOperationStatus probe = _storage.TryPeekLength(key, tick, workAllowance - workConsumed,
            out int length, out NavigationFailureReason cachedFailure, out int storageWork);
        workConsumed += storageWork;
        if (probe == NavigationCacheOperationStatus.BudgetExhausted) return Defer();
        if (probe == NavigationCacheOperationStatus.Miss)
        {
            key = CreateKey(snapshot, request, costSensitive, true);
            probe = _storage.TryPeekLength(key, tick, workAllowance - workConsumed,
                out length, out cachedFailure, out storageWork);
            workConsumed += storageWork;
            if (probe == NavigationCacheOperationStatus.BudgetExhausted) return Defer();
        }
        if (probe == NavigationCacheOperationStatus.Miss) return Miss();
        if (cachedFailure != NavigationFailureReason.None)
        {
            if (key.Regional || !key.Geometry.IsValid || length != 0)
                throw new InvalidOperationException("Negative cache entries require an exact global geometry version.");
            NavigationCacheOperationStatus copiedNegative = _storage.TryCopy(key, tick, [], workAllowance - workConsumed,
                out _, out failureReason, out storageWork);
            workConsumed += storageWork;
            if (copiedNegative == NavigationCacheOperationStatus.BudgetExhausted) return Defer();
            if (copiedNegative != NavigationCacheOperationStatus.Hit)
                throw new InvalidOperationException("Cache ownership changed during a coordinator-owned read.");
            Increment(ref _negativeHits);
            return NavigationRouteCacheLookup.Unreachable;
        }
        if (length < HeaderBytes || length > MaximumPayloadBytes)
            throw new InvalidOperationException("Cached route payload exceeds its declared format or capacity.");
        if (scratch.Length < length) return NavigationRouteCacheLookup.BufferTooSmall;
        NavigationCacheOperationStatus copiedPositive = _storage.TryCopy(key, tick, scratch, workAllowance - workConsumed,
            out int copied, out _, out storageWork);
        workConsumed += storageWork;
        if (copiedPositive == NavigationCacheOperationStatus.BudgetExhausted) return Defer();
        if (copiedPositive != NavigationCacheOperationStatus.Hit || copied != length)
            throw new InvalidOperationException("Cache ownership changed during a coordinator-owned read.");
        if (!Charge(1, workAllowance, ref workConsumed)) return Defer();
        ReadOnlySpan<byte> payload = scratch[..length];
        Header header = ReadHeader(payload);
        if (header.Geometry > request.Revision.Geometry || (!key.Regional && header.Geometry != request.Revision.Geometry) ||
            (costSensitive && header.Costs != request.Revision.Costs))
            return RejectHit(key, tick, workAllowance, ref workConsumed);
        if (key.Regional)
        {
            if (!header.CompleteRegions || header.RegionCount == 0)
                throw new InvalidOperationException("Regional cache entries require an explicit complete dependency proof.");
            DependencyCheck check = CheckDependencies(payload, header, snapshot, workAllowance, ref workConsumed);
            if (check == DependencyCheck.BudgetExhausted) return Defer();
            if (check != DependencyCheck.Valid) return RejectHit(key, tick, workAllowance, ref workConsumed);
        }

        reconstructionBytes = checked(512L + 2 * (64L + (long)header.WaypointCount * PointBytes) +
            2 * (64L + (long)header.RegionCount * RegionBytes));
        if (scratch.Length > request.MaximumWorkspaceBytes || reconstructionBytes > additionalWorkspaceBytes ||
            reconstructionBytes > request.MaximumWorkspaceBytes - scratch.Length)
            return NavigationRouteCacheLookup.MemoryPressure;
        long reconstructionWork = 1L + 3L * header.WaypointCount + 3L * header.RegionCount;
        if (reconstructionWork > workAllowance - workConsumed) return Defer();
        workConsumed += (int)reconstructionWork;
        var points = new Vector3[header.WaypointCount];
        var regions = new NavigationRegionRevision[header.RegionCount];
        for (int i = 0; i < points.Length; i++) points[i] = ReadPoint(payload, HeaderBytes + i * PointBytes);
        for (int i = 0; i < regions.Length; i++) regions[i] = ReadRegion(payload, header, i);
        route = new NavigationRouteProposal(request, header.ResolvedDestination, points, regions,
            header.Diagnostics, MaximumWaypoints);
        Increment(ref _positiveHits);
        return NavigationRouteCacheLookup.Found;
    }

    /// <summary>
    /// completeRegionDependencies is a kernel proof covering every geometric dependency of the
    /// positive result, including endpoint connectors. A list's presence alone is not such proof.
    /// Missing current stamps conservatively selects a global-geometry key. Negative results
    /// always remain globally pinned. costSensitive must describe the actual search semantics.
    /// </summary>
    public NavigationRouteCacheWrite TryStore(ulong worldIdentity, NavigationWorldSnapshot snapshot,
        NavigationSearchSpecification request, NavigationSearchAdvance result, bool costSensitive,
        bool completeRegionDependencies, ulong tick, ulong expiresAtTick, Span<byte> scratch,
        int workAllowance, out int workConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        workConsumed = 0;
        if (!Charge(1, workAllowance, ref workConsumed)) return DeferWrite();
        ValidateInput(worldIdentity, snapshot, request);
        workAllowance = Math.Min(workAllowance, request.MaximumWork);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAtTick, tick);
        if (result.WorkConsumed < 0 || result.WorkConsumed > request.MaximumWork ||
            result.WorkspaceBytes < 0 || result.WorkspaceBytes > request.MaximumWorkspaceBytes)
            return NavigationRouteCacheWrite.NotCacheable;
        if (result.Status == NavigationSearchStatus.Unreachable && result.Route is null &&
            result.FailureReason is NavigationFailureReason.NoHighLevelRoute or NavigationFailureReason.NoLocalRoute)
        {
            NavigationCacheOperationStatus stored = _storage.TryStoreUnreachable(CreateKey(snapshot, request, costSensitive, false),
                result.FailureReason, tick, expiresAtTick, workAllowance - workConsumed, out int storageWork);
            workConsumed += storageWork;
            if (stored == NavigationCacheOperationStatus.BudgetExhausted) return DeferWrite();
            return stored == NavigationCacheOperationStatus.Stored ? NavigationRouteCacheWrite.Stored : NavigationRouteCacheWrite.CapacityExhausted;
        }
        if (result.Status != NavigationSearchStatus.Found || result.FailureReason != NavigationFailureReason.None ||
            result.Route is not NavigationRouteProposal proposal || proposal.Request != request ||
            proposal.Waypoints.Count <= 0 || proposal.Waypoints.Count > MaximumWaypoints ||
            proposal.Regions.Count > MaximumRegions || proposal.Regions.Count > request.MaximumWork || !ValidDiagnostics(proposal.Diagnostics))
            return NavigationRouteCacheWrite.NotCacheable;
        int length = EncodedLength(proposal.Waypoints.Count, proposal.Regions.Count);
        if (scratch.Length < length) return NavigationRouteCacheWrite.BufferTooSmall;
        if (scratch.Length > request.MaximumWorkspaceBytes) return NavigationRouteCacheWrite.MemoryPressure;
        long prepareWork = 2L + proposal.Waypoints.Count + proposal.Regions.Count;
        if (prepareWork > workAllowance - workConsumed) return DeferWrite();
        workConsumed += (int)prepareWork;
        Span<byte> payload = scratch[..length];
        WritePayload(payload, request, proposal, completeRegionDependencies);
        Header header = ReadHeader(payload);
        DependencyCheck check = CheckDependencies(payload, header, snapshot, workAllowance, ref workConsumed);
        if (check == DependencyCheck.BudgetExhausted) return DeferWrite();
        if (check == DependencyCheck.Invalid) return NavigationRouteCacheWrite.NotCacheable;
        bool regional = completeRegionDependencies && header.RegionCount > 0 && check == DependencyCheck.Valid;
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], regional ? 1 : 0);
        NavigationCacheOperationStatus success = _storage.TryStoreSuccess(CreateKey(snapshot, request, costSensitive, regional),
            payload, tick, expiresAtTick, workAllowance - workConsumed, out int writeWork);
        workConsumed += writeWork;
        if (success == NavigationCacheOperationStatus.BudgetExhausted) return DeferWrite();
        return success == NavigationCacheOperationStatus.Stored ? NavigationRouteCacheWrite.Stored : NavigationRouteCacheWrite.CapacityExhausted;
    }

    /// <summary>Expiration and reclamation continue through the storage's shared per-tick allowance.</summary>
    public int Maintain(ulong tick, int workAllowance) => _storage.Maintain(tick, workAllowance, out _);

    public NavigationRouteCacheState CaptureState(long maximumCheckpointBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumCheckpointBytes, WrapperBytes);
        return new NavigationRouteCacheState(WorldIdentity, AlgorithmRevision, MaximumWaypoints, MaximumRegions,
            _positiveHits, _negativeHits, _misses, _budgetDeferrals,
            _storage.CaptureState(maximumCheckpointBytes - WrapperBytes));
    }

    public static NavigationRouteCache RestoreState(NavigationRouteCacheState state,
        ulong expectedWorldIdentity, int expectedAlgorithmRevision, long maximumRestoreBytes)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FormatRevision != 1 || state.ContractRevision != NavigationIntegrationContract.Revision ||
            state.WorldIdentity == 0 || state.WorldIdentity != expectedWorldIdentity ||
            state.AlgorithmRevision <= 0 || state.AlgorithmRevision != expectedAlgorithmRevision ||
            state.MaximumWaypoints <= 0 || state.MaximumRegions < 0)
            throw new ArgumentException("Route cache checkpoint identity, version or configuration is invalid.", nameof(state));
        ArgumentNullException.ThrowIfNull(state.Storage);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumRestoreBytes, WrapperBytes * 2);
        NavigationResultCache<NavigationRouteCacheKey, byte>.ValidateState(state.Storage,
            maximumRestoreBytes - WrapperBytes * 2);
        _ = EncodedLength(state.MaximumWaypoints, state.MaximumRegions);
        Span<byte> headerBytes = stackalloc byte[HeaderBytes];
        Span<byte> itemBytes = stackalloc byte[RegionBytes];
        foreach (NavigationCacheSlotState<NavigationRouteCacheKey, byte> slot in state.Storage.Slots)
        {
            if (slot is null) throw new ArgumentException("Route cache checkpoint contains a null slot.", nameof(state));
            if (!slot.Used) continue;
            NavigationRouteCacheKey key = slot.Key;
            if (key.WorldIdentity != expectedWorldIdentity || key.AlgorithmRevision != expectedAlgorithmRevision ||
                key.ContractRevision != NavigationIntegrationContract.Revision ||
                !ValidKeyValues(key) ||
                (!key.CostSensitive && (key.Costs != 0 || key.CostAvailability != NavigationModuleAvailability.NotConnected)) ||
                (key.Regional ? key.Geometry.IsValid : !key.Geometry.IsValid) ||
                (slot.FailureReason != NavigationFailureReason.None && key.Regional))
                throw new ArgumentException("Route cache checkpoint key scope is invalid.", nameof(state));
            if (slot.FailureReason != NavigationFailureReason.None) continue;
            if (slot.Value.Count < HeaderBytes)
                throw new ArgumentException("Route cache checkpoint payload is truncated.", nameof(state));
            CopyStateBytes(slot.Value, 0, headerBytes);
            Header header = ReadHeader(headerBytes, slot.Value.Count, state.MaximumWaypoints, state.MaximumRegions);
            if ((key.Regional && (!header.CompleteRegions || header.RegionCount == 0)) ||
                (!key.Regional && (header.CompleteRegions || header.Geometry != key.Geometry)) ||
                (key.CostSensitive && header.Costs != key.Costs) || header.RegionCount > key.MaximumWork)
                throw new ArgumentException("Route cache checkpoint payload scope is invalid.", nameof(state));
            for (int i = 0; i < header.WaypointCount; i++)
            {
                CopyStateBytes(slot.Value, HeaderBytes + i * PointBytes, itemBytes[..PointBytes]);
                Vector3 point = ReadPoint(itemBytes, 0);
                if (!Finite(point) || (i == header.WaypointCount - 1 && point != header.ResolvedDestination))
                    throw new ArgumentException("Route cache checkpoint waypoint is invalid.", nameof(state));
            }
            for (int i = 0; i < header.RegionCount; i++)
            {
                int offset = HeaderBytes + header.WaypointCount * PointBytes + i * RegionBytes;
                CopyStateBytes(slot.Value, offset, itemBytes);
                var region = new NavigationSectorCoordinate(BinaryPrimitives.ReadInt32LittleEndian(itemBytes),
                    BinaryPrimitives.ReadInt32LittleEndian(itemBytes[4..]));
                ulong revision = BinaryPrimitives.ReadUInt64LittleEndian(itemBytes[8..]);
                if (region.X < 0 || region.Z < 0 || region.X >= key.SectorWidth || region.Z >= key.SectorHeight ||
                    revision == 0 || revision > header.Geometry.Value)
                    throw new ArgumentException("Route cache checkpoint dependency is invalid.", nameof(state));
                for (int previous = 0; previous < i; previous++)
                {
                    CopyStateBytes(slot.Value, HeaderBytes + header.WaypointCount * PointBytes + previous * RegionBytes, itemBytes);
                    if (BinaryPrimitives.ReadInt32LittleEndian(itemBytes) == region.X &&
                        BinaryPrimitives.ReadInt32LittleEndian(itemBytes[4..]) == region.Z)
                        throw new ArgumentException("Route cache checkpoint repeats a regional dependency.", nameof(state));
                }
            }
        }
        var storage = NavigationResultCache<NavigationRouteCacheKey, byte>.RestoreState(state.Storage,
            maximumRestoreBytes - WrapperBytes * 2);
        return new NavigationRouteCache(state, storage);
    }

    public void Clear() => _storage.Clear();

    public void Dispose()
    {
        _storage.Dispose();
        GC.SuppressFinalize(this);
    }

    private NavigationRouteCacheKey CreateKey(NavigationWorldSnapshot snapshot,
        NavigationSearchSpecification request, bool costSensitive, bool regional) => new(
            WorldIdentity, AlgorithmRevision, NavigationIntegrationContract.Revision,
            regional ? default : request.Revision.Geometry,
            costSensitive ? request.Revision.Costs : 0,
            costSensitive ? snapshot.CostAvailability : NavigationModuleAvailability.NotConnected,
            costSensitive, regional, request.Start, request.OriginalDestination, request.TargetSemantics,
            request.Goal, request.Capabilities, request.Vehicle, request.KnowledgeScope,
            request.MaximumWork, request.MaximumWorkspaceBytes, request.MaximumEndpointCandidates,
            snapshot.Grid.Origin, snapshot.Grid.Width, snapshot.Grid.Height,
            snapshot.Grid.Settings.CellSizeMeters, snapshot.Grid.Settings.StaticObstacleClearanceMeters,
            snapshot.SectorGraph.Settings.SectorSizeCells, snapshot.SectorGraph.Width, snapshot.SectorGraph.Height);

    private void ValidateInput(ulong worldIdentity, NavigationWorldSnapshot snapshot,
        NavigationSearchSpecification request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (worldIdentity != WorldIdentity || snapshot.Revision != request.Revision ||
            snapshot.Capabilities != request.Capabilities || snapshot.KnowledgeScope != request.KnowledgeScope)
            throw new ArgumentException("Cache access requires the exact request snapshot and owning world identity.", nameof(request));
    }

    private NavigationRouteCacheLookup RejectHit(NavigationRouteCacheKey key, ulong tick,
        int workAllowance, ref int workConsumed)
    {
        _storage.Invalidate(key, tick, workAllowance - workConsumed, out int storageWork);
        workConsumed += storageWork;
        return Miss();
    }

    private NavigationRouteCacheLookup Miss()
    {
        Increment(ref _misses);
        return NavigationRouteCacheLookup.Miss;
    }

    private NavigationRouteCacheLookup Defer()
    {
        Increment(ref _budgetDeferrals);
        return NavigationRouteCacheLookup.BudgetExhausted;
    }

    private NavigationRouteCacheWrite DeferWrite()
    {
        Increment(ref _budgetDeferrals);
        return NavigationRouteCacheWrite.BudgetExhausted;
    }

    private static DependencyCheck CheckDependencies(ReadOnlySpan<byte> payload, Header header,
        NavigationWorldSnapshot snapshot, int allowance, ref int consumed)
    {
        bool missing = false;
        for (int i = 0; i < header.RegionCount; i++)
        {
            if (!Charge(1, allowance, ref consumed)) return DependencyCheck.BudgetExhausted;
            NavigationRegionRevision dependency = ReadRegion(payload, header, i);
            if (!dependency.Revision.IsValid || dependency.Revision > header.Geometry ||
                dependency.Region.X < 0 || dependency.Region.Z < 0 ||
                dependency.Region.X >= snapshot.SectorGraph.Width || dependency.Region.Z >= snapshot.SectorGraph.Height)
                return DependencyCheck.Invalid;
            for (int previous = 0; previous < i; previous++)
            {
                if (!Charge(1, allowance, ref consumed)) return DependencyCheck.BudgetExhausted;
                if (ReadRegion(payload, header, previous).Region == dependency.Region) return DependencyCheck.Invalid;
            }
            bool found = false;
            for (int currentIndex = 0; currentIndex < snapshot.Regions.Count; currentIndex++)
            {
                if (!Charge(1, allowance, ref consumed)) return DependencyCheck.BudgetExhausted;
                NavigationRegionRevision current = snapshot.Regions[currentIndex];
                if (current.Region != dependency.Region) continue;
                if (current.Revision != dependency.Revision) return DependencyCheck.Invalid;
                found = true;
                break;
            }
            missing |= !found;
        }
        return missing ? DependencyCheck.Missing : DependencyCheck.Valid;
    }

    private Header ReadHeader(ReadOnlySpan<byte> payload) =>
        ReadHeader(payload, payload.Length, MaximumWaypoints, MaximumRegions);

    private static Header ReadHeader(ReadOnlySpan<byte> payload, int payloadLength, int maximumWaypoints, int maximumRegions)
    {
        if (payload.Length < HeaderBytes || BinaryPrimitives.ReadInt32LittleEndian(payload) != PayloadRevision)
            throw new InvalidOperationException("Cached route payload revision is invalid.");
        int complete = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        var geometry = new NavigationVersion(BinaryPrimitives.ReadUInt64LittleEndian(payload[8..]));
        ulong costs = BinaryPrimitives.ReadUInt64LittleEndian(payload[16..]);
        int points = BinaryPrimitives.ReadInt32LittleEndian(payload[24..]);
        int regions = BinaryPrimitives.ReadInt32LittleEndian(payload[28..]);
        Vector3 resolved = ReadPoint(payload, 32);
        int cacheHit = BinaryPrimitives.ReadInt32LittleEndian(payload[60..]);
        var diagnostics = new NavigationPathDiagnostics(BinaryPrimitives.ReadInt32LittleEndian(payload[44..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[48..]), BinaryPrimitives.ReadInt32LittleEndian(payload[52..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[56..]), cacheHit == 1);
        if ((complete != 0 && complete != 1) || !geometry.IsValid || points <= 0 || points > maximumWaypoints ||
            regions < 0 || regions > maximumRegions || (cacheHit != 0 && cacheHit != 1) ||
            EncodedLength(points, regions) != payloadLength || !Finite(resolved) || !ValidDiagnostics(diagnostics))
            throw new InvalidOperationException("Cached route payload metadata is invalid.");
        return new Header(geometry, costs, complete == 1, points, regions, resolved, diagnostics);
    }

    private static void WritePayload(Span<byte> payload, NavigationSearchSpecification request,
        NavigationRouteProposal proposal, bool complete)
    {
        BinaryPrimitives.WriteInt32LittleEndian(payload, PayloadRevision);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], complete ? 1 : 0);
        BinaryPrimitives.WriteUInt64LittleEndian(payload[8..], request.Revision.Geometry.Value);
        BinaryPrimitives.WriteUInt64LittleEndian(payload[16..], request.Revision.Costs);
        BinaryPrimitives.WriteInt32LittleEndian(payload[24..], proposal.Waypoints.Count);
        BinaryPrimitives.WriteInt32LittleEndian(payload[28..], proposal.Regions.Count);
        WritePoint(payload, 32, proposal.ResolvedDestination);
        BinaryPrimitives.WriteInt32LittleEndian(payload[44..], proposal.Diagnostics.ExpandedHighLevelNodes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[48..], proposal.Diagnostics.ExpandedLocalNodes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[52..], proposal.Diagnostics.RefinedCellCount);
        BinaryPrimitives.WriteSingleLittleEndian(payload[56..], proposal.Diagnostics.RouteLengthMeters);
        BinaryPrimitives.WriteInt32LittleEndian(payload[60..], proposal.Diagnostics.HighLevelCacheHit ? 1 : 0);
        for (int i = 0; i < proposal.Waypoints.Count; i++) WritePoint(payload, HeaderBytes + i * PointBytes, proposal.Waypoints[i]);
        for (int i = 0; i < proposal.Regions.Count; i++)
        {
            int offset = HeaderBytes + proposal.Waypoints.Count * PointBytes + i * RegionBytes;
            NavigationRegionRevision region = proposal.Regions[i];
            BinaryPrimitives.WriteInt32LittleEndian(payload[offset..], region.Region.X);
            BinaryPrimitives.WriteInt32LittleEndian(payload[(offset + 4)..], region.Region.Z);
            BinaryPrimitives.WriteUInt64LittleEndian(payload[(offset + 8)..], region.Revision.Value);
        }
    }

    private static NavigationRegionRevision ReadRegion(ReadOnlySpan<byte> payload, Header header, int index)
    {
        int offset = HeaderBytes + header.WaypointCount * PointBytes + index * RegionBytes;
        return new NavigationRegionRevision(new NavigationSectorCoordinate(
            BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + 4)..])),
            new NavigationVersion(BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 8)..])));
    }

    private static void WritePoint(Span<byte> bytes, int offset, Vector3 point)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes[offset..], point.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[(offset + 4)..], point.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[(offset + 8)..], point.Z);
    }

    private static Vector3 ReadPoint(ReadOnlySpan<byte> bytes, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(offset + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(offset + 8)..]));

    private static void CopyStateBytes(IReadOnlyList<byte> source, int offset, Span<byte> destination)
    {
        if (offset < 0 || source.Count - offset < destination.Length)
            throw new ArgumentException("Route cache checkpoint payload is truncated.", nameof(source));
        for (int i = 0; i < destination.Length; i++) destination[i] = source[offset + i];
    }

    private static bool ValidKeyValues(NavigationRouteCacheKey key) => key.Start.IsValid && Finite(key.Destination) &&
        key.Goal.IsValid && key.Vehicle.IsValid && Enum.IsDefined(key.Capabilities.MovementClass) &&
        float.IsFinite(key.Capabilities.MaximumSlopeDegrees) && key.Capabilities.MaximumSlopeDegrees >= 0 &&
        key.Capabilities.MaximumSlopeDegrees < 90 && float.IsFinite(key.Capabilities.SlopeCostWeight) && key.Capabilities.SlopeCostWeight >= 0 &&
        Enum.IsDefined(key.Semantics) && key.Semantics != NavigationTargetSemantics.Unspecified &&
        Enum.IsDefined(key.CostAvailability) &&
        (!key.CostSensitive || key.CostAvailability != NavigationModuleAvailability.Available || key.Costs != 0) &&
        key.MaximumWork > 0 && key.MaximumWorkspaceBytes > 0 && key.MaximumEndpointCandidates > 0 &&
        float.IsFinite(key.GridOrigin.X) && float.IsFinite(key.GridOrigin.Y) && key.GridWidth > 0 && key.GridHeight > 0 &&
        float.IsFinite(key.CellSizeMeters) && key.CellSizeMeters > 0 &&
        float.IsFinite(key.StaticClearanceMeters) && key.StaticClearanceMeters >= 0 &&
        key.SectorSizeCells > 0 && key.SectorWidth > 0 && key.SectorHeight > 0;

    private static int EncodedLength(int points, int regions) => checked(HeaderBytes + points * PointBytes + regions * RegionBytes);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool ValidDiagnostics(NavigationPathDiagnostics value) => value.ExpandedHighLevelNodes >= 0 &&
        value.ExpandedLocalNodes >= 0 && value.RefinedCellCount >= 0 && float.IsFinite(value.RouteLengthMeters) && value.RouteLengthMeters >= 0;
    private static bool Charge(int work, int allowance, ref int consumed)
    {
        if (work > allowance - consumed) return false;
        consumed += work;
        return true;
    }
    private static void Increment(ref ulong value)
    {
        if (value < ulong.MaxValue) value++;
    }

    private enum DependencyCheck : byte { Valid, Missing, Invalid, BudgetExhausted }
    private readonly record struct Header(NavigationVersion Geometry, ulong Costs, bool CompleteRegions,
        int WaypointCount, int RegionCount, Vector3 ResolvedDestination, NavigationPathDiagnostics Diagnostics);
}

internal readonly record struct NavigationRouteCacheKey(
    ulong WorldIdentity, int AlgorithmRevision, int ContractRevision, NavigationVersion Geometry,
    ulong Costs, NavigationModuleAvailability CostAvailability, bool CostSensitive, bool Regional,
    NavigationPose Start, Vector3 Destination, NavigationTargetSemantics Semantics, NavigationGoalConstraints Goal,
    NavigationCapabilities Capabilities, NavigationVehicleProfile Vehicle, ulong KnowledgeScope,
    int MaximumWork, long MaximumWorkspaceBytes, int MaximumEndpointCandidates,
    Vector2 GridOrigin, int GridWidth, int GridHeight, float CellSizeMeters, float StaticClearanceMeters,
    int SectorSizeCells, int SectorWidth, int SectorHeight)
{
    // Do not use HashCode.Combine: bucket-chain work is part of deterministic replay state.
    public override int GetHashCode()
    {
        uint hash = 2166136261;
        Add(ref hash, WorldIdentity); Add(ref hash, AlgorithmRevision); Add(ref hash, ContractRevision);
        Add(ref hash, Geometry.Value); Add(ref hash, Costs); Add(ref hash, (int)CostAvailability);
        Add(ref hash, CostSensitive); Add(ref hash, Regional);
        Add(ref hash, Start.Position.X); Add(ref hash, Start.Position.Y); Add(ref hash, Start.Position.Z);
        Add(ref hash, Start.Rotation.X); Add(ref hash, Start.Rotation.Y); Add(ref hash, Start.Rotation.Z); Add(ref hash, Start.Rotation.W);
        Add(ref hash, Destination.X); Add(ref hash, Destination.Y); Add(ref hash, Destination.Z); Add(ref hash, (int)Semantics);
        Add(ref hash, Goal.MaximumProjectionDistance); Add(ref hash, Goal.PositionTolerance);
        Add(ref hash, Goal.RequiredHeadingRadians.HasValue); Add(ref hash, Goal.RequiredHeadingRadians.GetValueOrDefault());
        Add(ref hash, Goal.HeadingToleranceRadians); Add(ref hash, (int)Capabilities.MovementClass);
        Add(ref hash, Capabilities.MaximumSlopeDegrees); Add(ref hash, Capabilities.SlopeCostWeight);
        Add(ref hash, Vehicle.HalfExtents.X); Add(ref hash, Vehicle.HalfExtents.Y); Add(ref hash, Vehicle.HalfExtents.Z);
        Add(ref hash, Vehicle.ClearanceMeters); Add(ref hash, Vehicle.MaximumSpeed); Add(ref hash, Vehicle.Acceleration);
        Add(ref hash, Vehicle.Deceleration); Add(ref hash, Vehicle.MaximumYawRate); Add(ref hash, KnowledgeScope);
        Add(ref hash, MaximumWork); Add(ref hash, unchecked((ulong)MaximumWorkspaceBytes)); Add(ref hash, MaximumEndpointCandidates);
        Add(ref hash, GridOrigin.X); Add(ref hash, GridOrigin.Y); Add(ref hash, GridWidth); Add(ref hash, GridHeight);
        Add(ref hash, CellSizeMeters); Add(ref hash, StaticClearanceMeters); Add(ref hash, SectorSizeCells);
        Add(ref hash, SectorWidth); Add(ref hash, SectorHeight);
        return unchecked((int)hash);
    }

    private static void Add(ref uint hash, uint value) => hash = unchecked((hash ^ value) * 16777619);
    private static void Add(ref uint hash, int value) => Add(ref hash, unchecked((uint)value));
    private static void Add(ref uint hash, bool value) => Add(ref hash, value ? 1u : 0u);
    private static void Add(ref uint hash, float value) => Add(ref hash, value == 0 ? 0u : BitConverter.SingleToUInt32Bits(value));
    private static void Add(ref uint hash, ulong value)
    {
        Add(ref hash, unchecked((uint)value));
        Add(ref hash, unchecked((uint)(value >> 32)));
    }
}

internal readonly record struct NavigationRouteCacheMetrics(NavigationCacheMetrics Storage,
    long WrapperEstimatedBytes, ulong PositiveHits, ulong NegativeHits, ulong Misses, ulong BudgetDeferrals);

internal sealed record NavigationRouteCacheState(
    ulong WorldIdentity, int AlgorithmRevision, int MaximumWaypoints, int MaximumRegions,
    ulong PositiveHits, ulong NegativeHits, ulong Misses, ulong BudgetDeferrals,
    NavigationResultCacheState<NavigationRouteCacheKey, byte> Storage,
    int FormatRevision = 1, int ContractRevision = NavigationIntegrationContract.Revision);
