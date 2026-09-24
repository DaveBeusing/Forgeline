using ForgeLine.Core;

namespace ForgeLine.Ecs;

public readonly struct EntityQuery<T>
    where T : struct
{
    private readonly EntityRegistry _registry;
    private readonly ComponentStore<T>? _store;
    private readonly QueryIterationOrder _order;

    internal EntityQuery(
        EntityRegistry registry,
        ComponentStore<T>? store,
        QueryIterationOrder order)
    {
        _registry = registry;
        _store = store;
        _order = order;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_registry, _store, _order);
    }

    public struct Enumerator
    {
        private readonly EntityRegistry _registry;
        private readonly ComponentStore<T>? _store;
        private readonly QueryIterationOrder _order;
        private readonly ulong _structuralVersion;
        private int _position;
        private EntityId _current;

        internal Enumerator(
            EntityRegistry registry,
            ComponentStore<T>? store,
            QueryIterationOrder order)
        {
            _registry = registry;
            _store = store;
            _order = order;
            _structuralVersion = registry.StructuralVersion;
            _position = -1;
            _current = default;
        }

        public readonly EntityId Current => _current;

        public bool MoveNext()
        {
            EnsureUnmodified();

            if (_store is null)
            {
                return false;
            }

            return _order == QueryIterationOrder.StableByEntityIndex
                ? MoveNextStable()
                : MoveNextDense();
        }

        private bool MoveNextDense()
        {
            int next = _position + 1;
            if (next >= _store!.Count)
            {
                return false;
            }

            _position = next;
            _current = _store.GetEntityAt(next);
            return true;
        }

        private bool MoveNextStable()
        {
            while (++_position < _registry.SlotCount)
            {
                if (_registry.TryGetEntityAtIndex(_position, out EntityId entity)
                    && _store!.Contains(entity))
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
