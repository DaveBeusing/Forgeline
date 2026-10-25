using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal enum CongestionObservationKind : byte
{
    Clear = 1,
    TrafficDelay = 2,
    DockQueueWait = 3
}

internal readonly record struct CongestionObservation(
    NavigationCellCoordinate Cell,
    CongestionObservationKind Kind);

internal sealed record CongestionPenaltySettings(
    int MaximumCells,
    int MaximumObservationsPerTick,
    int ActivationTicks,
    int DockQueueGraceTicks,
    int RampTicks,
    int ExpiryTicks,
    float MaximumPenalty,
    float DecayPerTick)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumObservationsPerTick);
        ArgumentOutOfRangeException.ThrowIfLessThan(ActivationTicks, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(DockQueueGraceTicks, ActivationTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RampTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ExpiryTicks);
        if (!float.IsFinite(MaximumPenalty) || MaximumPenalty <= 0.0f ||
            !float.IsFinite(DecayPerTick) || DecayPerTick <= 0.0f)
        {
            throw new InvalidOperationException(
                "Congestion limits and decay must be finite and positive.");
        }
    }
}

internal readonly record struct CongestionMemoryEntry(
    NavigationCellCoordinate Cell,
    CongestionObservationKind LastObservation,
    ulong DelayStartedAt,
    ulong LastObservedAt,
    float Penalty);

internal sealed class CongestionPenaltyCheckpoint
{
    public CongestionPenaltyCheckpoint(
        int revision,
        CongestionPenaltySettings settings,
        ulong knowledgeScope,
        bool hasTick,
        ulong tick,
        ReadOnlySpan<CongestionMemoryEntry> entries)
    {
        Revision = revision;
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        KnowledgeScope = knowledgeScope;
        HasTick = hasTick;
        Tick = tick;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public int Revision { get; }
    public CongestionPenaltySettings Settings { get; }
    public ulong KnowledgeScope { get; }
    public bool HasTick { get; }
    public ulong Tick { get; }
    public IReadOnlyList<CongestionMemoryEntry> Entries { get; }
}

/// <summary>
/// Retains only faction-authorized observations; the caller must filter before
/// recording. Costs never change grid traversability or service reservations.
/// This preparation helper is not registered with the movement pipeline.
/// </summary>
internal sealed class CongestionPenaltyMemory
{
    private const int StateRevision = 1;
    private readonly CongestionPenaltySettings _settings;
    private readonly CongestionMemoryEntry[] _entries;
    private int _count;
    private bool _hasTick;
    private ulong _tick;

    public CongestionPenaltyMemory(ulong knowledgeScope, CongestionPenaltySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        KnowledgeScope = knowledgeScope;
        _settings = settings;
        _entries = new CongestionMemoryEntry[settings.MaximumCells];
    }

    public ulong KnowledgeScope { get; }
    public int Count => _count;

    /// <summary>
    /// Records one complete aggregate batch for a tick. Cells must be unique and
    /// sorted by Z, then X. Existing observations are updated before admitting
    /// new cells, so eviction compares current observation ages. Equal ages
    /// retain the smaller coordinates, including when an existing cell is updated.
    /// Missing samples break sustained-delay evidence and permit only decay.
    /// The return value counts observations not retained because capacity is full.
    /// </summary>
    public int Record(
        ulong knowledgeScope,
        ulong tick,
        ReadOnlySpan<CongestionObservation> observations)
    {
        ValidateScope(knowledgeScope);
        if (_hasTick && tick <= _tick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick),
                "Each observation batch must use a later simulation tick.");
        }
        if (observations.Length > _settings.MaximumObservationsPerTick)
            throw new ArgumentOutOfRangeException(nameof(observations));

        for (int index = 0; index < observations.Length; index++)
        {
            CongestionObservation observation = observations[index];
            ValidateCell(observation.Cell);
            if (!Enum.IsDefined(observation.Kind) ||
                (index > 0 && Compare(observations[index - 1].Cell, observation.Cell) >= 0))
            {
                throw new ArgumentException("Observations must be valid, sorted and unique.",
                    nameof(observations));
            }
        }

        for (int index = 0; index < observations.Length; index++)
        {
            int existing = Find(observations[index].Cell);
            if (existing >= 0)
            {
                _entries[existing] = Update(_entries[existing], observations[index], tick);
            }
        }

        int retained = 0;
        for (int index = 0; index < _count; index++)
        {
            CongestionMemoryEntry entry = _entries[index];
            if (tick - entry.LastObservedAt < (ulong)_settings.ExpiryTicks &&
                (entry.LastObservation != CongestionObservationKind.Clear || entry.Penalty > 0.0f))
            {
                _entries[retained++] = entry;
            }
        }
        Array.Clear(_entries, retained, _count - retained);
        _count = retained;

        for (int index = 0; index < observations.Length; index++)
        {
            CongestionObservation observation = observations[index];
            if (observation.Kind == CongestionObservationKind.Clear || Find(observation.Cell) >= 0)
            {
                continue;
            }

            var entry = new CongestionMemoryEntry(observation.Cell, observation.Kind, tick, tick,
                ActivatedPenalty(observation.Kind, 0));
            if (_count == _entries.Length)
            {
                int worst = 0;
                for (int candidate = 1; candidate < _count; candidate++)
                {
                    if (IsLowerPriority(_entries[candidate], _entries[worst])) worst = candidate;
                }
                if (IsLowerPriority(entry, _entries[worst])) continue;
                Array.Copy(_entries, worst + 1, _entries, worst, _count - worst - 1);
                _count--;
            }

            int insertion = ~Find(entry.Cell);
            Array.Copy(_entries, insertion, _entries, insertion + 1, _count - insertion);
            _entries[insertion] = entry;
            _count++;
        }

        int omitted = 0;
        for (int index = 0; index < observations.Length; index++)
        {
            if (observations[index].Kind != CongestionObservationKind.Clear &&
                Find(observations[index].Cell) < 0) omitted++;
        }

        _hasTick = true;
        _tick = tick;
        return omitted;
    }

    /// <summary>Pure lookup. Future reads decay costs without advancing observation history.</summary>
    public float GetAdditionalCost(ulong knowledgeScope, NavigationCellCoordinate cell, ulong tick)
    {
        ValidateScope(knowledgeScope);
        ValidateCell(cell);
        if (_hasTick && tick < _tick) throw new ArgumentOutOfRangeException(nameof(tick));
        int index = Find(cell);
        return index < 0 ? 0.0f : DecayedPenalty(_entries[index], tick);
    }

    public CongestionPenaltyCheckpoint CaptureState() =>
        new(StateRevision, _settings, KnowledgeScope, _hasTick, _tick, _entries.AsSpan(0, _count));

    public void RestoreState(CongestionPenaltyCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Revision != StateRevision || checkpoint.Settings != _settings ||
            checkpoint.KnowledgeScope != KnowledgeScope || checkpoint.Entries.Count > _entries.Length ||
            (!checkpoint.HasTick && (checkpoint.Tick != 0 || checkpoint.Entries.Count != 0)))
        {
            throw new ArgumentException("Congestion checkpoint identity or configuration differs.",
                nameof(checkpoint));
        }

        for (int index = 0; index < checkpoint.Entries.Count; index++)
        {
            CongestionMemoryEntry entry = checkpoint.Entries[index];
            ValidateCell(entry.Cell);
            if (!Enum.IsDefined(entry.LastObservation) || entry.DelayStartedAt > entry.LastObservedAt ||
                entry.LastObservedAt > checkpoint.Tick ||
                checkpoint.Tick - entry.LastObservedAt >= (ulong)_settings.ExpiryTicks ||
                !float.IsFinite(entry.Penalty) || entry.Penalty < 0.0f || entry.Penalty > _settings.MaximumPenalty ||
                (entry.LastObservation == CongestionObservationKind.Clear &&
                    (entry.DelayStartedAt != entry.LastObservedAt || entry.Penalty == 0.0f)) ||
                (entry.LastObservation != CongestionObservationKind.Clear &&
                    entry.Penalty < ActivatedPenalty(entry.LastObservation,
                        entry.LastObservedAt - entry.DelayStartedAt)) ||
                (index > 0 && Compare(checkpoint.Entries[index - 1].Cell, entry.Cell) >= 0))
            {
                throw new ArgumentException("Congestion checkpoint entries are invalid or reordered.",
                    nameof(checkpoint));
            }
        }

        Array.Clear(_entries);
        for (int index = 0; index < checkpoint.Entries.Count; index++) _entries[index] = checkpoint.Entries[index];
        _count = checkpoint.Entries.Count;
        _hasTick = checkpoint.HasTick;
        _tick = checkpoint.Tick;
    }

    private CongestionMemoryEntry Update(
        CongestionMemoryEntry previous,
        CongestionObservation observation,
        ulong tick)
    {
        bool continues = observation.Kind != CongestionObservationKind.Clear &&
            previous.LastObservation == observation.Kind && tick - previous.LastObservedAt == 1;
        ulong started = continues ? previous.DelayStartedAt : tick;
        float penalty = MathF.Max(DecayedPenalty(previous, tick),
            ActivatedPenalty(observation.Kind, tick - started));
        return new CongestionMemoryEntry(observation.Cell, observation.Kind, started, tick, penalty);
    }

    private float ActivatedPenalty(CongestionObservationKind kind, ulong elapsed)
    {
        if (kind == CongestionObservationKind.Clear) return 0.0f;
        int threshold = kind == CongestionObservationKind.DockQueueWait
            ? _settings.DockQueueGraceTicks : _settings.ActivationTicks;
        if (elapsed < (ulong)(threshold - 1)) return 0.0f;
        ulong activated = elapsed - (ulong)(threshold - 1);
        if (activated >= (ulong)(_settings.RampTicks - 1)) return _settings.MaximumPenalty;
        return (float)(_settings.MaximumPenalty * ((double)activated + 1.0) / _settings.RampTicks);
    }

    private float DecayedPenalty(CongestionMemoryEntry entry, ulong tick)
    {
        ulong elapsed = tick - entry.LastObservedAt;
        if (elapsed >= (ulong)_settings.ExpiryTicks) return 0.0f;
        return (float)Math.Max(0.0, entry.Penalty - (double)_settings.DecayPerTick * elapsed);
    }

    private int Find(NavigationCellCoordinate cell)
    {
        int low = 0;
        int high = _count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int comparison = Compare(_entries[middle].Cell, cell);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return ~low;
    }

    private static bool IsLowerPriority(CongestionMemoryEntry candidate, CongestionMemoryEntry other) =>
        candidate.LastObservedAt < other.LastObservedAt ||
        (candidate.LastObservedAt == other.LastObservedAt && Compare(candidate.Cell, other.Cell) > 0);

    private static int Compare(NavigationCellCoordinate left, NavigationCellCoordinate right)
    {
        int row = left.Z.CompareTo(right.Z);
        return row == 0 ? left.X.CompareTo(right.X) : row;
    }

    private static void ValidateCell(NavigationCellCoordinate cell)
    {
        if (cell.X < 0 || cell.Z < 0) throw new ArgumentOutOfRangeException(nameof(cell));
    }

    private void ValidateScope(ulong knowledgeScope)
    {
        if (knowledgeScope != KnowledgeScope)
        {
            throw new ArgumentException("Congestion observations belong to a different knowledge scope.",
                nameof(knowledgeScope));
        }
    }
}
