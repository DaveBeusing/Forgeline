using System.Runtime.InteropServices;
using ForgeLine.Core;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

// Copies remain GC-owned indefinitely. Published copies are never mutated or pooled.
internal sealed class ClientRenderPublication
{
    internal object Gate { get; } = new();
    private ClientRenderFrame? _latest;
    private ulong _published, _consumed, _taken, _replaced;
    internal bool NeedsFramePublication
    {
        get { lock (Gate) return !_latest.HasValue || _consumed == _published; }
    }
    internal ClientPublicationMetrics Metrics
    {
        get { lock (Gate) return new(_published, _taken, _replaced, _consumed != _published); }
    }
    private const int MaximumCachedElements = 1024;
    private const int MaximumCachedLabelCharacters = 16_384;
    [ThreadStatic] private static PublishedCopies? _copies;

    // Producers compare full values against private owned copies, never retain caller arrays.
    // Per-thread state keeps concurrent publishers independent; only bounded immutable data survives.
    internal static ClientRenderFrame Copy(in ClientRenderFrame frame)
    {
        var copies = _copies ??= new PublishedCopies();
        return frame with
        {
            GameplayLines = CopyValues(frame.GameplayLines, ref copies.GameplayLines),
            GameplayLabels = CopyLabels(frame.GameplayLabels, ref copies.GameplayLabels),
            DebugLines = CopyValues(frame.DebugLines, ref copies.DebugLines),
            DebugLabels = CopyLabels(frame.DebugLabels, ref copies.DebugLabels),
            InformationLayer = frame.InformationLayer with
            { SelectedEntities = CopySelection(frame.InformationLayer.SelectedEntities, ref copies.Selection) }
        };
    }

    // The live producer supplies borrowed channels directly. Only private immutable
    // copies enter the mailbox; unchanged channels avoid producer-side ToArray too.
    internal static ClientRenderFrame CopyDraws(in ClientRenderFrame frame,
        DebugDraw gameplay, DebugDraw debug, IReadOnlyCollection<EntityId> selection)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(debug);
        ArgumentNullException.ThrowIfNull(selection);
        var copies = _copies ??= new PublishedCopies();
        return frame with
        {
            GameplayLines = CopySpan(gameplay.Lines, ref copies.GameplayLines),
            GameplayLabels = CopyLabelList(gameplay.Labels, ref copies.GameplayLabels),
            DebugLines = CopySpan(debug.Lines, ref copies.DebugLines),
            DebugLabels = CopyLabelList(debug.Labels, ref copies.DebugLabels),
            InformationLayer = frame.InformationLayer with
            { SelectedEntities = CopySelectionSet(selection, ref copies.Selection) }
        };
    }

    private static T[] CopySpan<T>(ReadOnlySpan<T> source, ref T[]? cached) where T : unmanaged
    {
        if (source.IsEmpty) { cached = null; return []; }
        if (cached is not null && MemoryMarshal.AsBytes(source).SequenceEqual(MemoryMarshal.AsBytes(cached.AsSpan())))
            return cached;
        T[] result = source.ToArray();
        cached = result.Length <= MaximumCachedElements ? result : null;
        return result;
    }

    private static DebugLabel[] CopyLabelList(IReadOnlyList<DebugLabel> source, ref DebugLabel[]? cached)
    {
        if (source.Count == 0) { cached = null; return []; }
        bool same = cached is not null && cached.Length == source.Count;
        for (int i = 0; same && i < source.Count; i++) same = cached![i].Equals(source[i]);
        if (same) return cached!;
        var result = new DebugLabel[source.Count];
        long characters = 0;
        for (int i = 0; i < result.Length; i++)
        { result[i] = source[i]; characters += result[i].Text?.Length ?? 0; }
        cached = result.Length <= MaximumCachedElements && characters <= MaximumCachedLabelCharacters ? result : null;
        return result;
    }

    private static EntityId[] CopySelectionSet(IReadOnlyCollection<EntityId> source, ref EntityId[]? cached)
    {
        if (source.Count == 0) { cached = null; return []; }
        if (cached is not null && cached.Length == source.Count)
        {
            int index = 0;
            bool same = true;
            foreach (EntityId entity in source)
                if (cached[index++] != entity) { same = false; break; }
            if (same) return cached;
        }
        EntityId[] result = source.ToArray();
        cached = result.Length <= MaximumCachedElements ? result : null;
        return result;
    }

    // Bitwise equality is conservative: differing NaN/zero representations may copy again,
    // but no caller value is omitted. These channels contain only unmanaged values.
    private static T[] CopyValues<T>(T[] source, ref T[]? cached) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0) { cached = null; return []; }
        if (cached is not null && !ReferenceEquals(source, cached) && MemoryMarshal.AsBytes(source.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(cached.AsSpan())))
            return cached;
        T[] copy = source.ToArray();
        cached = copy.Length <= MaximumCachedElements ? copy : null;
        return copy;
    }

    private static DebugLabel[] CopyLabels(DebugLabel[] source, ref DebugLabel[]? cached)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0) { cached = null; return []; }
        if (cached is not null && !ReferenceEquals(source, cached) && source.AsSpan().SequenceEqual(cached))
            return cached;
        DebugLabel[] result = source.ToArray();
        cached = null;
        if (result.Length <= MaximumCachedElements)
        {
            long characters = 0;
            foreach (var label in result) characters += label.Text?.Length ?? 0;
            if (characters <= MaximumCachedLabelCharacters) cached = result;
        }
        return result;
    }

    private static EntityId[] CopySelection(EntityId[]? source, ref EntityId[]? cached)
    {
        if (source is null) { cached = null; return []; }
        return CopyValues(source, ref cached);
    }

    internal static void ResetThreadCopies() => _copies = null;
    internal static int CurrentThreadCachedElements => _copies is { } copies
        ? (copies.GameplayLines?.Length ?? 0) + (copies.GameplayLabels?.Length ?? 0) +
          (copies.DebugLines?.Length ?? 0) + (copies.DebugLabels?.Length ?? 0) + (copies.Selection?.Length ?? 0) : 0;

    internal void StoreCopied(in ClientRenderFrame frame)
    {
        if (!Monitor.IsEntered(Gate)) throw new InvalidOperationException("Publication requires the shared mailbox gate.");
        if (_latest.HasValue && _consumed != _published) _replaced++;
        _latest = frame;
        _published++;
    }
    internal ClientRenderFrame? ReadLatest()
    {
        lock (Gate)
        {
            if (_latest.HasValue && _consumed != _published) _taken++;
            _consumed = _published;
            return _latest;
        }
    }
    private sealed class PublishedCopies
    {
        internal DebugLine[]? GameplayLines, DebugLines;
        internal DebugLabel[]? GameplayLabels, DebugLabels;
        internal EntityId[]? Selection;
    }
}

internal readonly record struct ClientPublicationMetrics(ulong Published, ulong Consumed, ulong Replaced, bool Pending);
