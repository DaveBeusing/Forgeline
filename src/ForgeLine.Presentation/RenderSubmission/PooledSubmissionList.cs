using System.Buffers;

namespace ForgeLine.Presentation.RenderSubmission;

// Leases never escape the render owner; SetData consumes its populated prefix synchronously.
internal sealed class PooledSubmissionList<T> where T : unmanaged
{
    private T[] _items = [];
    internal int Count { get; private set; }
    internal int Capacity => _items.Length;
    internal void Add(T item)
    {
        if (Count == _items.Length)
        {
            T[] next = ArrayPool<T>.Shared.Rent(Math.Max(64, checked(Count * 2)));
            _items.AsSpan(0, Count).CopyTo(next);
            if (_items.Length > 0) ArrayPool<T>.Shared.Return(_items);
            _items = next;
        }
        _items[Count++] = item;
    }
    internal void CopyTo(T[] destination, int offset) => _items.AsSpan(0, Count).CopyTo(destination.AsSpan(offset));
    internal void Clear() => Count = 0;
    internal void Release()
    {
        if (_items.Length > 0) ArrayPool<T>.Shared.Return(_items);
        _items = []; Count = 0;
    }
}
