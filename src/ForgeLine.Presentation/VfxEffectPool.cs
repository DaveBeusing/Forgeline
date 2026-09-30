using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed class VfxEffectPool
{
    public const int DefaultCapacity = 2_048;

    private readonly VfxPooledEffect[] _slots;
    private int _activeCount;
    private int _searchStart;
    private ulong _totalSpawned;
    private ulong _totalReused;
    private ulong _totalDropped;

    public VfxEffectPool(
        int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            capacity,
            1);

        _slots =
            new VfxPooledEffect[
                capacity];
    }

    public int Capacity =>
        _slots.Length;

    public int ActiveCount =>
        _activeCount;

    public VfxPresentationMetrics Metrics =>
        new(
            _activeCount,
            Capacity,
            _totalSpawned,
            _totalReused,
            _totalDropped);

    public void BeginTick(
        SimulationTick tick)
    {
        int active =
            0;

        for (int index = 0;
             index < _slots.Length;
             index++)
        {
            if (!_slots[index].Active)
            {
                continue;
            }

            if (tick >=
                _slots[index].ExpiresAtTick)
            {
                _slots[index] =
                    _slots[index] with
                    {
                        Active = false
                    };
                continue;
            }

            active++;
        }

        _activeCount =
            active;
    }

    public bool TrySpawn(
        VfxEffectKind kind,
        in RenderTransform transform,
        SimulationTick tick)
    {
        VfxPresentationDefinition definition =
            VfxPresentationCatalog.Get(
                kind);

        int slot =
            FindFreeSlot();

        if (slot < 0)
        {
            _totalDropped++;
            return false;
        }

        bool reused =
            _slots[slot].Generation >
            0;

        ulong expiresAt =
            checked(
                tick.Value +
                definition.LifetimeTicks);

        _slots[slot] =
            new VfxPooledEffect(
                true,
                checked(
                    _slots[slot].Generation +
                    1),
                kind,
                transform,
                tick,
                new SimulationTick(
                    expiresAt));

        _activeCount++;
        _totalSpawned++;

        if (reused)
        {
            _totalReused++;
        }

        _searchStart =
            (slot + 1) %
            _slots.Length;

        return true;
    }

    public int CopyActiveTo(
        Span<VfxPooledEffect> destination)
    {
        if (destination.Length <
            _activeCount)
        {
            throw new ArgumentException(
                "Destination is smaller than the active VFX count.",
                nameof(destination));
        }

        int written =
            0;

        for (int index = 0;
             index < _slots.Length;
             index++)
        {
            if (!_slots[index].Active)
            {
                continue;
            }

            destination[written++] =
                _slots[index] with
                {
                    SlotIndex =
                        index
                };
        }

        return written;
    }

    private int FindFreeSlot()
    {
        for (int offset = 0;
             offset < _slots.Length;
             offset++)
        {
            int index =
                (_searchStart + offset) %
                _slots.Length;

            if (!_slots[index].Active)
            {
                return index;
            }
        }

        return -1;
    }
}

public readonly record struct VfxPooledEffect(
    bool Active,
    uint Generation,
    VfxEffectKind Kind,
    RenderTransform Transform,
    SimulationTick SpawnedAtTick,
    SimulationTick ExpiresAtTick)
{
    public int SlotIndex { get; init; } = -1;
}
