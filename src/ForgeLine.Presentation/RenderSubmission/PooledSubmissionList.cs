using System.Runtime.CompilerServices;
using System.Buffers;

namespace ForgeLine.Presentation.RenderSubmission;

// Leases never escape the render owner; SetData consumes its populated prefix synchronously.
internal sealed class PooledSubmissionList<T> where T : unmanaged
{
    // Shared pools retain a processor-dependent number of arrays. Small escape
    // payloads need to cover the renderer's bounded 1,024 metadata slots even
    // on a four-core runner. Large payloads retain the existing shared leases.
    private const int MaximumSmallPayloadLength = 512;
    private static readonly ArrayPool<T> SmallPayloads =
        ArrayPool<T>.Create(MaximumSmallPayloadLength, 1024);
    private T[] _items = [];
    private static ArrayPool<T> PoolFor(int length) =>
        length <= MaximumSmallPayloadLength ? SmallPayloads : ArrayPool<T>.Shared;
    internal int Count { get; private set; }
    internal int Capacity => _items.Length;
    internal T this[int index] { get => _items[index]; set => _items[index] = value; }
    internal void Truncate(int count) => Count = count;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(in T item)
    {
        int count = Count;
        if ((uint)count < (uint)_items.Length) { _items[count] = item; Count = count + 1; }
        else GrowAndAdd(item);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAdd(in T item)
    {
        int capacity = Math.Max(64, checked(Count * 2));
        T[] next = PoolFor(capacity).Rent(capacity);
        _items.AsSpan(0, Count).CopyTo(next);
        if (_items.Length > 0) PoolFor(_items.Length).Return(_items);
        _items = next; _items[Count++] = item;
    }
    internal void CopyTo(T[] destination, int offset) => _items.AsSpan(0, Count).CopyTo(destination.AsSpan(offset));
    internal void Clear() => Count = 0;
    internal void Release()
    {
        if (_items.Length > 0) PoolFor(_items.Length).Return(_items);
        _items = []; Count = 0;
    }
}
