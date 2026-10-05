namespace ForgeLine.Graphics;

internal sealed class D3D12DescriptorAllocator
{
    private readonly bool[] _allocated;
    private readonly Stack<int> _free = [];
    private int _next;

    internal D3D12DescriptorAllocator(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _allocated = new bool[capacity];
    }

    internal int Capacity { get; }

    internal int UsedCount { get; private set; }

    internal int Allocate()
    {
        int index;

        if (_free.TryPop(out int recycled))
        {
            index = recycled;
        }
        else
        {
            if (_next >= Capacity)
            {
                throw new GraphicsDeviceException(
                    $"Shader-resource descriptor capacity {Capacity} is exhausted.");
            }

            index = _next++;
        }

        if (_allocated[index])
        {
            throw new InvalidOperationException(
                $"Descriptor {index} is already allocated.");
        }

        _allocated[index] = true;
        UsedCount++;
        return index;
    }

    internal void Release(int index)
    {
        if ((uint)index >= (uint)Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (!_allocated[index])
        {
            throw new InvalidOperationException(
                $"Descriptor {index} is not allocated.");
        }

        _allocated[index] = false;
        UsedCount--;
        _free.Push(index);
    }
}
