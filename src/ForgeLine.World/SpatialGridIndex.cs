using System.Diagnostics;
using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.World;

public sealed class SpatialGridIndex
{
    private readonly SpatialGridSettings _settings;
    private readonly int _cellsPerChunk;
    private readonly Dictionary<EntityId, EntryState> _entries = new();
    private readonly Dictionary<SpatialCellAddress, List<EntityId>> _cells = new();

    private long _queryCount;
    private long _totalQueryStopwatchTicks;
    private long _maximumQueryStopwatchTicks;

    public SpatialGridIndex(SpatialGridSettings? settings = null)
    {
        _settings = settings ?? new SpatialGridSettings();
        _settings.Validate();
        _cellsPerChunk = _settings.CellsPerChunk;
    }

    public SpatialGridSettings Settings => _settings;

    public int Count => _entries.Count;

    public int OccupiedCellCount => _cells.Count;

    public void Insert(in SpatialEntry entry)
    {
        ValidateEntry(entry);

        if (_entries.ContainsKey(entry.Entity))
        {
            throw new InvalidOperationException(
                $"Entity {entry.Entity} is already present in the spatial index.");
        }

        SpatialCellRange range = SpatialAddressing.BoundsToCellRange(
            entry.Bounds,
            _settings);

        _entries.Add(entry.Entity, new EntryState(entry, range));
        AddToCells(entry.Entity, range);
    }

    public bool Update(in SpatialEntry entry)
    {
        ValidateEntry(entry);

        if (!_entries.TryGetValue(entry.Entity, out EntryState? state))
        {
            return false;
        }

        SpatialCellRange newRange = SpatialAddressing.BoundsToCellRange(
            entry.Bounds,
            _settings);

        if (state.Range != newRange)
        {
            RemoveFromCells(entry.Entity, state.Range);
            AddToCells(entry.Entity, newRange);
            state.Range = newRange;
        }

        state.Entry = entry;
        return true;
    }

    public void Upsert(in SpatialEntry entry)
    {
        if (!Update(entry))
        {
            Insert(entry);
        }
    }

    public bool Remove(EntityId entity)
    {
        if (!_entries.Remove(entity, out EntryState? state))
        {
            return false;
        }

        RemoveFromCells(entity, state.Range);
        return true;
    }

    public bool Contains(EntityId entity)
    {
        return _entries.ContainsKey(entity);
    }

    public bool TryGetEntry(
        EntityId entity,
        out SpatialEntry entry)
    {
        if (_entries.TryGetValue(entity, out EntryState? state))
        {
            entry = state.Entry;
            return true;
        }

        entry = default;
        return false;
    }

    public int QueryCell(
        SpatialCellAddress address,
        SpatialQueryBuffer buffer,
        SpatialQueryFilter filter = default,
        SpatialQueryOrder order = SpatialQueryOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ValidateAddress(address);

        long started = BeginQuery();
        try
        {
            buffer.Clear();

            if (!_cells.TryGetValue(address, out List<EntityId>? bucket))
            {
                return 0;
            }

            for (int index = 0; index < bucket.Count; index++)
            {
                EntityId entity = bucket[index];
                if (_entries.TryGetValue(entity, out EntryState? state) &&
                    filter.Matches(state.Entry))
                {
                    buffer.Add(entity);
                }
            }

            ApplyOrdering(buffer, order);
            return buffer.Count;
        }
        finally
        {
            EndQuery(started);
        }
    }

    public int QueryPoint(
        Vector3 point,
        SpatialQueryBuffer buffer,
        SpatialQueryFilter filter = default,
        SpatialQueryOrder order = SpatialQueryOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ValidatePoint(point);

        long started = BeginQuery();
        try
        {
            buffer.Clear();

            SpatialCellAddress address = SpatialAddressing.WorldToCell(
                point,
                _settings);

            if (!_cells.TryGetValue(address, out List<EntityId>? bucket))
            {
                return 0;
            }

            for (int index = 0; index < bucket.Count; index++)
            {
                EntityId entity = bucket[index];
                if (_entries.TryGetValue(entity, out EntryState? state) &&
                    filter.Matches(state.Entry) &&
                    SpatialGeometry.Contains(state.Entry.Bounds, point))
                {
                    buffer.Add(entity);
                }
            }

            ApplyOrdering(buffer, order);
            return buffer.Count;
        }
        finally
        {
            EndQuery(started);
        }
    }

    public int QueryAabb(
        AxisAlignedBounds bounds,
        SpatialQueryBuffer buffer,
        SpatialQueryFilter filter = default,
        SpatialQueryOrder order = SpatialQueryOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        long started = BeginQuery();
        try
        {
            buffer.Clear();

            SpatialCellRange range = SpatialAddressing.BoundsToCellRange(
                bounds,
                _settings);

            VisitAabbRange(
                range,
                bounds,
                buffer,
                filter);

            ApplyOrdering(buffer, order);
            return buffer.Count;
        }
        finally
        {
            EndQuery(started);
        }
    }

    public int QueryRadius(
        Vector3 center,
        float radius,
        SpatialQueryBuffer buffer,
        SpatialQueryFilter filter = default,
        SpatialQueryOrder order = SpatialQueryOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ValidatePoint(center);

        if (!float.IsFinite(radius) || radius < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        long started = BeginQuery();
        try
        {
            buffer.Clear();

            var bounds = new AxisAlignedBounds(
                new Vector3(
                    center.X - radius,
                    float.MinValue,
                    center.Z - radius),
                new Vector3(
                    center.X + radius,
                    float.MaxValue,
                    center.Z + radius));

            SpatialCellRange range = SpatialAddressing.BoundsToCellRange(
                bounds,
                _settings);
            float radiusSquared = radius * radius;

            VisitRadiusRange(
                range,
                center,
                radiusSquared,
                buffer,
                filter);

            ApplyOrdering(buffer, order);
            return buffer.Count;
        }
        finally
        {
            EndQuery(started);
        }
    }

    public bool TryFindNearest(
        Vector3 origin,
        float maximumRadius,
        SpatialQueryBuffer buffer,
        out EntityId entity,
        out float distanceSquared,
        SpatialQueryFilter filter = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        QueryRadius(
            origin,
            maximumRadius,
            buffer,
            filter,
            SpatialQueryOrder.Unspecified);

        entity = EntityId.Invalid;
        distanceSquared = float.PositiveInfinity;

        ReadOnlySpan<EntityId> candidates = buffer.Results;
        for (int index = 0; index < candidates.Length; index++)
        {
            EntityId candidate = candidates[index];
            if (!_entries.TryGetValue(candidate, out EntryState? state))
            {
                continue;
            }

            float candidateDistance = SpatialGeometry.HorizontalDistanceSquared(
                origin,
                state.Entry.Bounds);

            if (candidateDistance < distanceSquared ||
                (candidateDistance == distanceSquared &&
                 (!entity.IsValid || candidate < entity)))
            {
                entity = candidate;
                distanceSquared = candidateDistance;
            }
        }

        return entity.IsValid;
    }

    public SpatialIndexDiagnosticsSnapshot CaptureDiagnostics()
    {
        int maximumOccupancy = 0;
        long totalOccupancy = 0;

        foreach (List<EntityId> bucket in _cells.Values)
        {
            maximumOccupancy = Math.Max(maximumOccupancy, bucket.Count);
            totalOccupancy += bucket.Count;
        }

        TimeSpan totalDuration = ToDuration(_totalQueryStopwatchTicks);
        TimeSpan averageDuration = _queryCount == 0
            ? TimeSpan.Zero
            : ToDuration(_totalQueryStopwatchTicks / _queryCount);

        return new SpatialIndexDiagnosticsSnapshot(
            _entries.Count,
            _cells.Count,
            maximumOccupancy,
            _cells.Count == 0 ? 0.0 : (double)totalOccupancy / _cells.Count,
            _queryCount,
            totalDuration,
            averageDuration,
            ToDuration(_maximumQueryStopwatchTicks));
    }

    private void VisitAabbRange(
        SpatialCellRange range,
        AxisAlignedBounds bounds,
        SpatialQueryBuffer buffer,
        SpatialQueryFilter filter)
    {
        for (int z = range.Minimum.Z; z <= range.Maximum.Z; z++)
        {
            for (int x = range.Minimum.X; x <= range.Maximum.X; x++)
            {
                SpatialCellAddress address = AddressFromGlobalCell(x, z);

                if (!_cells.TryGetValue(address, out List<EntityId>? bucket))
                {
                    continue;
                }

                for (int index = 0; index < bucket.Count; index++)
                {
                    EntityId entity = bucket[index];
                    if (!_entries.TryGetValue(entity, out EntryState? state) ||
                        !filter.Matches(state.Entry) ||
                        !SpatialGeometry.Intersects(state.Entry.Bounds, bounds))
                    {
                        continue;
                    }

                    buffer.Add(entity);
                }
            }
        }
    }

    private void VisitRadiusRange(
        SpatialCellRange range,
        Vector3 center,
        float radiusSquared,
        SpatialQueryBuffer buffer,
        SpatialQueryFilter filter)
    {
        for (int z = range.Minimum.Z; z <= range.Maximum.Z; z++)
        {
            for (int x = range.Minimum.X; x <= range.Maximum.X; x++)
            {
                SpatialCellAddress address = AddressFromGlobalCell(x, z);

                if (!_cells.TryGetValue(address, out List<EntityId>? bucket))
                {
                    continue;
                }

                for (int index = 0; index < bucket.Count; index++)
                {
                    EntityId entity = bucket[index];
                    if (!_entries.TryGetValue(entity, out EntryState? state) ||
                        !filter.Matches(state.Entry) ||
                        SpatialGeometry.HorizontalDistanceSquared(
                            center,
                            state.Entry.Bounds) > radiusSquared)
                    {
                        continue;
                    }

                    buffer.Add(entity);
                }
            }
        }
    }

    private void AddToCells(
        EntityId entity,
        SpatialCellRange range)
    {
        for (int z = range.Minimum.Z; z <= range.Maximum.Z; z++)
        {
            for (int x = range.Minimum.X; x <= range.Maximum.X; x++)
            {
                SpatialCellAddress address = AddressFromGlobalCell(x, z);

                if (!_cells.TryGetValue(address, out List<EntityId>? bucket))
                {
                    bucket = new List<EntityId>(4);
                    _cells.Add(address, bucket);
                }

                bucket.Add(entity);
            }
        }
    }

    private void RemoveFromCells(
        EntityId entity,
        SpatialCellRange range)
    {
        for (int z = range.Minimum.Z; z <= range.Maximum.Z; z++)
        {
            for (int x = range.Minimum.X; x <= range.Maximum.X; x++)
            {
                SpatialCellAddress address = AddressFromGlobalCell(x, z);

                if (!_cells.TryGetValue(address, out List<EntityId>? bucket))
                {
                    continue;
                }

                bucket.Remove(entity);
                if (bucket.Count == 0)
                {
                    _cells.Remove(address);
                }
            }
        }
    }

    private SpatialCellAddress AddressFromGlobalCell(
        int x,
        int z)
    {
        return SpatialAddressing.FromGlobalCellUnchecked(
            new SpatialCellCoordinate(x, z),
            _cellsPerChunk);
    }

    private static void ValidateEntry(in SpatialEntry entry)
    {
        if (!entry.Entity.IsValid)
        {
            throw new ArgumentException(
                "Spatial entries require a valid entity identifier.",
                nameof(entry));
        }
    }

    private void ValidateAddress(SpatialCellAddress address)
    {
        _ = SpatialAddressing.ToGlobalCell(address, _settings);
    }

    private static void ValidatePoint(Vector3 point)
    {
        if (!float.IsFinite(point.X) ||
            !float.IsFinite(point.Y) ||
            !float.IsFinite(point.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
    }

    private static void ApplyOrdering(
        SpatialQueryBuffer buffer,
        SpatialQueryOrder order)
    {
        if (order == SpatialQueryOrder.StableEntityId)
        {
            buffer.SortStable();
        }
    }

    private long BeginQuery()
    {
        return _settings.EnableQueryTiming
            ? Stopwatch.GetTimestamp()
            : 0;
    }

    private void EndQuery(long started)
    {
        _queryCount++;

        if (!_settings.EnableQueryTiming)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        _totalQueryStopwatchTicks += elapsed;
        _maximumQueryStopwatchTicks = Math.Max(
            _maximumQueryStopwatchTicks,
            elapsed);
    }

    private static TimeSpan ToDuration(long stopwatchTicks)
    {
        return stopwatchTicks == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(
                (double)stopwatchTicks / Stopwatch.Frequency);
    }

    private sealed class EntryState
    {
        public EntryState(
            SpatialEntry entry,
            SpatialCellRange range)
        {
            Entry = entry;
            Range = range;
        }

        public SpatialEntry Entry { get; set; }

        public SpatialCellRange Range { get; set; }
    }
}
