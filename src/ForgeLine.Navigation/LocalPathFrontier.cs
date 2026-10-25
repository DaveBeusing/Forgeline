namespace ForgeLine.Navigation;

/// <summary>Exclusively leased local-search workspace with bounded retained storage.</summary>
internal sealed class LocalPathFrontier
{
    private const int MaximumRetainedQueues = 8;
    private const int MaximumRetainedEntries = 16_384;
    private static readonly LocalPathFrontier?[] s_available = new LocalPathFrontier[MaximumRetainedQueues];
    private readonly PriorityQueue<(int Index, float Cost), (float Estimate, float Remaining, int Index)> _queue = new();
    private int _peakCount;

    public Dictionary<int, float> BestCost { get; } = new();
    public Dictionary<int, int> Previous { get; } = new();
    public HashSet<NavigationSectorCoordinate> Corridor { get; } = new();

    private LocalPathFrontier() { }

    public static LocalPathFrontier Rent()
    {
        for (int index = 0; index < s_available.Length; index++)
        {
            LocalPathFrontier? frontier = Interlocked.Exchange(ref s_available[index], null);
            if (frontier is not null) return frontier;
        }
        return new LocalPathFrontier();
    }

    public static void Return(LocalPathFrontier frontier)
    {
        // All success, failure and exception paths release their exclusive
        // lease. Queue values contain no world, entity or path references.
        bool oversized = frontier._peakCount > MaximumRetainedEntries ||
            frontier.BestCost.Count > MaximumRetainedEntries ||
            frontier.Previous.Count > MaximumRetainedEntries ||
            frontier.Corridor.Count > MaximumRetainedEntries;
        frontier._queue.Clear();
        frontier.BestCost.Clear();
        frontier.Previous.Clear();
        frontier.Corridor.Clear();
        if (oversized) return;
        frontier._peakCount = 0;
        for (int index = 0; index < s_available.Length; index++)
        {
            if (Interlocked.CompareExchange(ref s_available[index], frontier, null) is null) return;
        }
        // Discard excess concurrency instead of retaining an unbounded pool.
    }

    public void Enqueue(int index, float cost, float remaining)
    {
        _queue.Enqueue((index, cost), (cost + remaining, remaining, index));
        _peakCount = Math.Max(_peakCount, _queue.Count);
    }

    public bool TryDequeue(out (int Index, float Cost) entry) =>
        _queue.TryDequeue(out entry, out _);
}
