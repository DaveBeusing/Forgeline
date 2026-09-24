using ForgeLine.Core;

namespace ForgeLine.Ecs;

internal sealed class EntityAllocator
{
    private uint[] _generations;
    private bool[] _alive;
    private int[] _freeIndices;
    private int _freeCount;
    private int _nextIndex;

    public EntityAllocator(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        int capacity = Math.Max(1, initialCapacity);
        _generations = new uint[capacity];
        _alive = new bool[capacity];
        _freeIndices = new int[capacity];
    }

    public int Count { get; private set; }

    public int Capacity => _generations.Length;

    public int SlotCount => _nextIndex;

    public EntityId Create()
    {
        int index;

        if (_freeCount > 0)
        {
            index = _freeIndices[--_freeCount];
        }
        else
        {
            index = _nextIndex;
            EnsureCapacity(index + 1);
            _nextIndex++;

            if (_generations[index] == 0)
            {
                _generations[index] = 1;
            }
        }

        _alive[index] = true;
        Count++;

        return new EntityId((uint)index, _generations[index]);
    }

    public bool Destroy(EntityId entity)
    {
        if (!IsAlive(entity))
        {
            return false;
        }

        int index = (int)entity.Index;
        _alive[index] = false;

        uint nextGeneration = unchecked(_generations[index] + 1);
        _generations[index] = nextGeneration == 0 ? 1u : nextGeneration;

        _freeIndices[_freeCount++] = index;
        Count--;

        return true;
    }

    public bool IsAlive(EntityId entity)
    {
        if (entity.Generation == 0 || entity.Index >= (uint)_nextIndex)
        {
            return false;
        }

        int index = (int)entity.Index;
        return _alive[index] && _generations[index] == entity.Generation;
    }

    public bool TryGetAliveEntity(int index, out EntityId entity)
    {
        if ((uint)index >= (uint)_nextIndex || !_alive[index])
        {
            entity = default;
            return false;
        }

        entity = new EntityId((uint)index, _generations[index]);
        return true;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _generations.Length)
        {
            return;
        }

        int newCapacity = _generations.Length;

        while (newCapacity < requiredCapacity)
        {
            newCapacity = checked(newCapacity * 2);
        }

        Array.Resize(ref _generations, newCapacity);
        Array.Resize(ref _alive, newCapacity);
        Array.Resize(ref _freeIndices, newCapacity);
    }
}
