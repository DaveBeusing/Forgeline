using ForgeLine.Core;

namespace ForgeLine.Ecs;

public readonly struct EntityQuery<TFirst, TSecond>
    where TFirst : struct
    where TSecond : struct
{
    private readonly EntityRegistry _registry;
    private readonly ComponentStore<TFirst>? _first;
    private readonly ComponentStore<TSecond>? _second;
    private readonly QueryIterationOrder _order;

    internal EntityQuery(
        EntityRegistry registry,
        ComponentStore<TFirst>? first,
        ComponentStore<TSecond>? second,
        QueryIterationOrder order)
    {
        _registry = registry;
        _first = first;
        _second = second;
        _order = order;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_registry, _first, _second, _order);
    }

    public struct Enumerator
    {
        private readonly EntityRegistry _registry;
        private readonly ComponentStore<TFirst>? _first;
        private readonly ComponentStore<TSecond>? _second;
        private readonly QueryIterationOrder _order;
        private readonly ulong _structuralVersion;
        private readonly bool _driveFirst;
        private int _position;
        private EntityId _current;

        internal Enumerator(
            EntityRegistry registry,
            ComponentStore<TFirst>? first,
            ComponentStore<TSecond>? second,
            QueryIterationOrder order)
        {
            _registry = registry;
            _first = first;
            _second = second;
            _order = order;
            _structuralVersion = registry.StructuralVersion;
            _driveFirst = first is not null
                && second is not null
                && first.Count <= second.Count;
            _position = -1;
            _current = default;
        }

        public readonly EntityId Current => _current;

        public bool MoveNext()
        {
            EnsureUnmodified();

            if (_first is null || _second is null)
            {
                return false;
            }

            return _order == QueryIterationOrder.StableByEntityIndex
                ? MoveNextStable()
                : MoveNextDense();
        }

        private bool MoveNextDense()
        {
            int driverCount = _driveFirst ? _first!.Count : _second!.Count;

            while (++_position < driverCount)
            {
                EntityId entity = _driveFirst
                    ? _first!.GetEntityAt(_position)
                    : _second!.GetEntityAt(_position);

                if (_first.Contains(entity) && _second.Contains(entity))
                {
                    _current = entity;
                    return true;
                }
            }

            return false;
        }

        private bool MoveNextStable()
        {
            while (++_position < _registry.SlotCount)
            {
                if (_registry.TryGetEntityAtIndex(_position, out EntityId entity)
                    && _first!.Contains(entity)
                    && _second!.Contains(entity))
                {
                    _current = entity;
                    return true;
                }
            }

            return false;
        }

        private readonly void EnsureUnmodified()
        {
            if (_registry.StructuralVersion != _structuralVersion)
            {
                throw new InvalidOperationException(
                    "Entity/component structure changed while a query was being enumerated.");
            }
        }
    }
}
