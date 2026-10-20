namespace ForgeLine.Presentation.TerrainResidency;

internal sealed class TerrainPreparedCache
{
    private sealed class Entry(int index, PreparedTerrainChunk data)
    {
        internal readonly int Index = index;
        internal readonly PreparedTerrainChunk Data = data;
        internal Entry? Previous, Next;
    }
    private readonly Dictionary<int, Entry> _entries = [];
    private readonly TerrainPreparationSettings _policy;
    private Entry? _oldest, _newest;
    internal long Bytes { get; private set; }
    internal long Hits { get; private set; }
    internal long Evictions { get; private set; }
    internal int Count => _entries.Count;
    internal TerrainPreparedCache(TerrainPreparationSettings policy) => _policy = policy;
    internal bool Contains(int index) => _entries.ContainsKey(index);
    internal PreparedTerrainChunk? Get(int index)
    {
        if (!_entries.TryGetValue(index, out Entry? entry)) return null;
        Hits++; Unlink(entry); Append(entry); return entry.Data;
    }
    internal void Admit(int index, PreparedTerrainChunk data)
    {
        if (_entries.ContainsKey(index)) return;
        if (data.Bytes > _policy.MaximumPreparedBytes) return;
        while (_entries.Count >= _policy.MaximumPreparedChunks || Bytes + data.Bytes > _policy.MaximumPreparedBytes)
        {
            Entry victim = _oldest!; Unlink(victim); _entries.Remove(victim.Index);
            Bytes -= victim.Data.Bytes; Evictions++;
        }
        var entry = new Entry(index, data); _entries.Add(index, entry); Append(entry); Bytes += data.Bytes;
    }
    private void Unlink(Entry entry)
    {
        if (entry.Previous is { } p) p.Next = entry.Next; else _oldest = entry.Next;
        if (entry.Next is { } n) n.Previous = entry.Previous; else _newest = entry.Previous;
        entry.Previous = entry.Next = null;
    }
    private void Append(Entry entry)
    {
        entry.Previous = _newest;
        if (_newest is not null) _newest.Next = entry; else _oldest = entry;
        _newest = entry;
    }
    internal void Clear() { _entries.Clear(); _oldest = _newest = null; Bytes = 0; }
}

/// <summary>One bounded work item on the existing thread pool, with no private queue or GPU callback.</summary>
internal sealed class TerrainPreparationWorker : IDisposable
{
    internal sealed record Result(long Generation, int[] Indices, PreparedTerrainChunk[] Chunks, Exception? Failure);
    private Task<Result>? _pending;
    private CancellationTokenSource? _stop;
    private long _generation;
    internal long Discarded { get; private set; }
    internal int PendingCount { get; private set; }
    internal bool Busy => _pending is not null;

    internal void Start(int[] indices, Func<int, CancellationToken, PreparedTerrainChunk> build)
    {
        if (indices.Length is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(indices));
        if (_pending is not null) throw new InvalidOperationException("Only one terrain CPU work item may be pending.");
        _stop = new(); CancellationToken token = _stop.Token; long generation = _generation;
        PendingCount = indices.Length;
        _pending = Task.Run(() =>
        {
            try
            {
                var chunks = new PreparedTerrainChunk[indices.Length];
                for (int i = 0; i < indices.Length; i++) { token.ThrowIfCancellationRequested(); chunks[i] = build(indices[i], token); }
                token.ThrowIfCancellationRequested();
                return new Result(generation, indices, chunks, null);
            }
            catch (Exception error) { return new Result(generation, indices, [], error); }
        });
    }
    internal Result? Harvest()
    {
        if (_pending?.IsCompleted != true) return null;
        Result result = _pending.GetAwaiter().GetResult(); _pending = null; PendingCount = 0;
        _stop?.Dispose(); _stop = null;
        if (result.Generation != _generation || result.Failure is not null) { Discarded++; return null; }
        return result;
    }
    internal void Cancel() { _generation++; _stop?.Cancel(); }
    public void Dispose() { Cancel(); _pending = null; PendingCount = 0; _stop?.Dispose(); _stop = null; }
}
