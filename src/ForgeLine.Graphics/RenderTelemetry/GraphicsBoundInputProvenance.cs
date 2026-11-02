using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ForgeLine.Graphics;

public sealed record GraphicsBoundTextureSource(int MipIndex, int Width, int Height, int SourceRowPitch,
    int LogicalRowBytes, int RowCount, GraphicsTextureReadbackLayout StagingLayout);
public sealed record GraphicsBoundSourceWrite(ulong Order, ulong ResourceId, string Kind, ulong DestinationOffset,
    ulong ByteCount, int ElementSize, string SourceSha256, ulong SourceByteCount, ulong? TransferId,
    GraphicsBoundTextureSource? TextureSource);
public sealed record GraphicsBoundSourceSegment(ulong Start, ulong Length, ulong SourceOffset, GraphicsBoundSourceWrite Write);
public sealed record GraphicsBoundResource(ulong Id, string Kind, bool LifetimeAvailable, GraphicsBufferDescription? Buffer,
    GraphicsTextureDescription? Texture, GraphicsWorldImplicitBinding? Implicit, int? Descriptor, string InitializationPath,
    string? StaticUsage, bool Committed, GraphicsBoundSourceSegment[] Segments);
public sealed record GraphicsBoundTransfer(ulong Id, int BatchSlot, ulong BatchGeneration, ulong StagingOffset,
    ulong ByteCount, string State, ulong? SignaledFence, string? FailureStage);
public sealed record GraphicsBoundUnknownRange(ulong Start, ulong Length);
public sealed record GraphicsBoundAssociation(string Role, int? Slot, ulong? JournalIdentity, ulong ResourceId,
    ulong Offset, ulong Length, int? MipIndex, ulong[] SourceWriteOrders, GraphicsBoundUnknownRange[] UnknownRanges);
public sealed record GraphicsBoundDraw(int JournalDrawOrdinal, ulong WriteWatermark, string? RootCommandSha256,
    GraphicsBoundAssociation[] Bindings);
public sealed record GraphicsBoundEvent(int Sequence, string Kind, GraphicsBoundSourceWrite? Write = null,
    GraphicsBoundTransfer? Transfer = null, GraphicsBoundDraw? Draw = null, ulong? ResourceId = null,
    GraphicsBoundResource? Resource = null);
public sealed record GraphicsBoundInputProvenance(int SchemaVersion, string Kind, bool Complete, string? Failure,
    string ScopeId, int OwnerThreadId, ulong DeviceGeneration, string DeviceSession, ulong ResourceGeneration,
    int FrameIndex, ulong BaselineWriteWatermark, ulong FinalWriteWatermark, GraphicsFrameSubmission? Submission,
    ulong? CompletedFence, ulong PresentedBefore, ulong? PresentedAfter, GraphicsBoundResource[] Baseline,
    GraphicsBoundTransfer[] BaselineTransfers, GraphicsBoundEvent[] Events)
{
    public const int IdentityCapacity = 4096, DrawCapacity = 4096, EventCapacity = 131072;
    public const int SegmentCapacity = 32768, SegmentsPerResource = 64;
    public const long MetadataCapacity = 32L * 1024 * 1024;
    public const int TransientSourceCapacity = 128 * 1024 * 1024;
    public const long WireCapacity = 32L * 1024 * 1024;
    public const int EnvelopeCapacity = 1024 * 1024;
    internal const long BodyCapacity = WireCapacity - EnvelopeCapacity;
    private const string FailureKey = "ForgeLine.Graphics.BoundInputProvenance.v1";
    private sealed record FailureAttachment(GraphicsWorldBindingAttempt Attempt, GraphicsBoundInputProvenance Prefix);
    public static GraphicsBoundInputProvenance? FailurePrefix(Exception error, GraphicsWorldBindingAttempt? attempt)
        => attempt is { Claimed: true } && error.Data[FailureKey] is FailureAttachment value && ReferenceEquals(value.Attempt, attempt)
            ? value.Prefix : null;
    internal static void AttachFailure(Exception error, GraphicsBoundInputProvenance prefix, GraphicsWorldBindingAttempt? attempt)
    {
        if (attempt is not { Claimed: true }) return;
        try { error.Data[FailureKey] = new FailureAttachment(attempt, prefix); } catch { }
    }
}

// Explicit native diagnostic scope; holds no native ownership and has no environment activation.
public sealed class GraphicsBoundInputProvenanceScope : IDisposable
{
    [ThreadStatic] private static GraphicsBoundInputProvenanceScope? _current;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed, _claimed;
    private long _frozenBytes;
    internal static GraphicsBoundInputProvenanceScope? Current => _current;
    internal GraphicsBoundInputTracker? Tracker { get; private set; }
    public string ScopeId { get; } = Guid.NewGuid().ToString("N");
    public GraphicsBoundInputProvenanceScope()
    {
        if (_current is not null) throw new InvalidOperationException("Bound input provenance scopes cannot nest.");
        _current = this;
    }
    internal GraphicsBoundInputTracker Claim(ulong generation, string session)
    {
        Verify();
        if (_claimed) throw new InvalidOperationException("Bound input scope already claimed a device.");
        _claimed = true;
        Tracker = new(ScopeId, generation, session);
        return Tracker;
    }
    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner != Environment.CurrentManagedThreadId || !ReferenceEquals(_current, this))
            throw new InvalidOperationException("Bound input scope belongs to another owner.");
    }
    internal void ReserveFrozen(long bytes)
    {
        Verify();
        GraphicsBoundFrozenSource.ValidateCount(bytes);
        if (bytes > GraphicsBoundInputProvenance.TransientSourceCapacity - _frozenBytes)
        {
            Tracker?.Invalidate();
            throw new InvalidOperationException("Shared bound source freeze exceeds128MiB.");
        }
        _frozenBytes = checked(_frozenBytes + bytes);
    }
    internal void ReturnFrozen(long bytes)
    {
        if (_owner != Environment.CurrentManagedThreadId || bytes < 0 || bytes > _frozenBytes)
            throw new InvalidOperationException("Invalid bound source freeze release.");
        _frozenBytes -= bytes;
    }
    public object ScopeFailureFacts() => new
    {
        schemaVersion = 1, kind = "bound-input-scope-failure", scopeId = ScopeId, ownerThreadId = _owner,
        deviceClaimed = _claimed, deviceGeneration = Tracker?.Generation, deviceSession = Tracker?.Session,
        captureFactsIncluded = false, capturePhase = Tracker?.CaptureAttempted == true ? "capture-arm-attempted" : "before-capture-arm",
        complete = false, submission = (GraphicsFrameSubmission?)null,
        reason = "Supplementary scope facts only; capture evidence and tokens, when available, are retained separately."
    };
    public void Dispose()
    {
        if (_disposed) return;
        Verify();
        Tracker?.Clear();
        Tracker = null;
        _current = null;
        _disposed = true;
    }
}

internal sealed class GraphicsBoundFrozenSource : IDisposable
{
    private byte[]? _bytes;
    private readonly GraphicsBoundInputProvenanceScope? _scope;
    private readonly int _length;
    internal ReadOnlySpan<byte> Bytes => _bytes ?? throw new ObjectDisposedException(nameof(GraphicsBoundFrozenSource));
    internal string Sha256 => Convert.ToHexString(SHA256.HashData(Bytes));
    internal static void ValidateCount(long bytes)
    {
        if (bytes < 0 || bytes > GraphicsBoundInputProvenance.TransientSourceCapacity)
            throw new InvalidOperationException("Bound input transient source exceeds128MiB.");
    }
    internal GraphicsBoundFrozenSource(ReadOnlySpan<byte> source)
    {
        ValidateCount(source.Length);
        _scope = GraphicsBoundInputProvenanceScope.Current;
        _length = source.Length;
        _scope?.ReserveFrozen(_length);
        try { _bytes = source.ToArray(); }
        catch { _scope?.ReturnFrozen(_length); throw; }
    }
    public void Dispose()
    {
        if (_bytes is null) return;
        _bytes = null;
        _scope?.ReturnFrozen(_length);
    }

}

internal sealed class GraphicsBoundFrozenTexture : IDisposable
{
    private byte[][]? _rows;
    private readonly GraphicsBoundInputProvenanceScope? _scope;
    private readonly long _length;
    internal GraphicsTextureMipData[] Sources { get; }
    internal GraphicsBoundFrozenTexture(GraphicsTextureData texture)
    {
        int count = texture.Description.MipCount;
        if (count is < 1 or > 32 || texture.Mips.Count != count)
            throw new InvalidOperationException("Bound texture source mip count invalid.");
        long bytes = 0;
        Sources = new GraphicsTextureMipData[count];
        int expectedWidth = texture.Description.Width, expectedHeight = texture.Description.Height;
        for (int i = 0; i < count; i++)
        {
            var mip = texture.Mips[i];
            if (mip.Width != expectedWidth || mip.Height != expectedHeight)
                throw new InvalidOperationException("Captured mip shape differs from immutable texture description.");
            int rowBytes = RowBytes(texture.Description.Format, mip.Width);
            int rows = texture.Description.Format == GraphicsTextureFormat.Bc7Unorm ? checked((mip.Height + 3) / 4) : mip.Height;
            if (mip.RowPitch < rowBytes || mip.Pixels is null || mip.Pixels.LongLength != checked((long)mip.RowPitch * rows))
                throw new InvalidOperationException("Captured mip pitch/length differs from its actual format dimensions.");
            expectedWidth = Math.Max(1, expectedWidth / 2); expectedHeight = Math.Max(1, expectedHeight / 2);
            bytes = checked(bytes + (long)rowBytes * rows);
            if (bytes > GraphicsBoundInputProvenance.TransientSourceCapacity)
                throw new InvalidOperationException("Bound texture transient source exceeds128MiB.");
            Sources[i] = mip;
        }
        _scope = GraphicsBoundInputProvenanceScope.Current;
        _length = bytes;
        _scope?.ReserveFrozen(bytes);
        try
        {
        _rows = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            var mip = Sources[i];
            int rowBytes = RowBytes(texture.Description.Format, mip.Width);
            int rows = texture.Description.Format == GraphicsTextureFormat.Bc7Unorm ? checked((mip.Height + 3) / 4) : mip.Height;
            var target = new byte[checked(rowBytes * rows)];
            for (int row = 0; row < rows; row++)
                mip.Pixels.AsSpan(checked(row * mip.RowPitch), rowBytes).CopyTo(target.AsSpan(row * rowBytes, rowBytes));
            _rows[i] = target;
            // Retain source pitch/dimensions, never the caller's mutable pixel array.
            Sources[i] = mip with { Pixels = [] };
        }
        }
        catch { _rows = null; _scope?.ReturnFrozen(bytes); throw; }
    }
    internal static int RowBytes(GraphicsTextureFormat format, int width) => format == GraphicsTextureFormat.Bc7Unorm
        ? checked(((width + 3) / 4) * 16) : checked(width * (format == GraphicsTextureFormat.R8Unorm ? 1 : 4));
    internal ReadOnlySpan<byte> Bytes(int mip) => (_rows ?? throw new ObjectDisposedException(nameof(GraphicsBoundFrozenTexture)))[mip];
    public void Dispose()
    {
        if (_rows is null) return;
        _rows = null;
        _scope?.ReturnFrozen(_length);
    }
}

internal sealed class GraphicsBoundInputTracker
{
    private sealed class ResourceState(object key, GraphicsBoundResource value)
    {
        internal readonly WeakReference<object> Key = new(key);
        internal GraphicsBoundResource Value = value;
    }
    private sealed record BoundView(ulong ResourceId, ulong JournalId, ulong Offset, ulong Length);
    private readonly Dictionary<ulong, ResourceState> _resources = [];
    private readonly GraphicsWorldBindingRegistry _identities = new();
    private readonly Dictionary<ulong, GraphicsBoundTransfer> _transfers = [];
    private readonly Dictionary<ulong, ulong> _pending = [];
    private readonly Dictionary<(ulong Epoch, int Slot), ulong> _implicit = [];
    private readonly Dictionary<int, BoundView> _vertices = [];
    private readonly Dictionary<int, BoundView> _textures = [];
    private BoundView? _index, _constants, _shadow, _shadowFrame;
    private string? _rootSha;
    private GraphicsBoundResource[] _baseline = [];
    private GraphicsBoundTransfer[] _baselineTransfers = [];
    private readonly List<GraphicsBoundEvent> _events = new(GraphicsBoundInputProvenance.EventCapacity);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly string _scope;
    private ulong _writeOrder, _transferId, _batchGeneration, _baselineOrder;
    private ulong _resourceEpoch, _presentedBefore;
    private int _frame, _draws;
    private bool _active, _invalid;
    private long _wireBytes;
    internal ulong Generation { get; }
    internal string Session { get; }
    internal GraphicsBoundInputTracker(string scope, ulong generation, string session)
    {
        _scope = scope; Generation = generation; Session = session;
        if (generation == 0 || session.Length != 32) throw new ArgumentException("Actual device identity required.");
    }
    private void Check()
    {
        if (_owner != Environment.CurrentManagedThreadId || _invalid) throw Refuse("Invalid/foreign bound input tracker.");
    }
    private InvalidOperationException Refuse(string message) { _invalid = true; return new(message); }
    internal void Invalidate() => _invalid = true;
    internal void VerifyAvailable() => Check();
    internal bool IsInvalid => _invalid;
    private static long Wire(object value) => GraphicsWorldBindingJournal.SerializeSidecar(value).LongLength;
    private void CheckMap()
    {
        int count = 0; long bytes = 4096;
        foreach (var resource in _resources.Values)
        {
            count = checked(count + resource.Value.Segments.Length);
            if (resource.Value.Segments.Length > GraphicsBoundInputProvenance.SegmentsPerResource)
                throw Refuse("Bound source resource exceeds64segments.");
            bytes = checked(bytes + Wire(resource.Value) + 1);
        }
        foreach (var transfer in _transfers.Values) bytes = checked(bytes + Wire(transfer) + 1);
        if (_resources.Count > GraphicsBoundInputProvenance.IdentityCapacity || count > GraphicsBoundInputProvenance.SegmentCapacity ||
            bytes > GraphicsBoundInputProvenance.MetadataCapacity || bytes > GraphicsBoundInputProvenance.BodyCapacity)
            throw Refuse("Bound pre-arm logical metadata/refusal capacity exhausted.");
    }
    private void Event(GraphicsBoundEvent value)
    {
        if (!_active) return;
        if (_events.Count == GraphicsBoundInputProvenance.EventCapacity) throw Refuse("Bound capture event capacity exhausted.");
        long bytes = checked(Wire(value) + 1);
        if (bytes > GraphicsBoundInputProvenance.BodyCapacity - _wireBytes) throw Refuse("Bound capture wire capacity exhausted.");
        _wireBytes += bytes; _events.Add(value);
    }
    internal ulong Register(object key, string kind, GraphicsBufferDescription? buffer = null, GraphicsTextureDescription? texture = null,
        GraphicsWorldImplicitBinding? implicitIdentity = null, bool committed = true)
    {
        Check();
        try
        {
            ulong id = _identities.Identity(key);
            if (_resources.ContainsKey(id)) throw Refuse("Bound resource cannot be registered twice.");
            var value = new GraphicsBoundResource(id, kind, implicitIdentity is null, buffer, texture, implicitIdentity, null,
                "created", null, committed, []);
            _resources.Add(id, new(key, value)); CheckMap();
            Event(new(_events.Count, "resource-created", ResourceId: id, Resource: value));
            return id;
        }
        catch { _invalid = true; throw; }
    }
    private ulong Id(object key)
    {
        Check();
        ulong id = _identities.Identity(key);
        if (!_resources.TryGetValue(id, out var state) || !state.Key.TryGetTarget(out object? found) || !ReferenceEquals(found, key))
            throw Refuse("Missing/stale bound resource identity.");
        if (key is GraphicsResourceRetirement.Resource lifetime && lifetime.IsDisposed) throw Refuse("Disposed bound resource.");
        return id;
    }
    internal void Commit(object key, int? descriptor = null, string? path = null, string? usage = null)
    {
        ulong id = Id(key); var state = _resources[id];
        state.Value = state.Value with { Committed = true, Descriptor = descriptor, InitializationPath = path ?? state.Value.InitializationPath, StaticUsage = usage };
        try { CheckMap(); Event(new(_events.Count, "resource-committed", ResourceId: id, Resource: state.Value)); } catch { _invalid = true; throw; }
    }
    internal void Retire(object key)
    {
        if (_invalid) return;
        // Private native resources (readbacks/targets) were never registered here.
        foreach (var entry in _resources)
        {
            if (!entry.Value.Key.TryGetTarget(out object? found) || !ReferenceEquals(found, key)) continue;
            try
            {
                Event(new(_events.Count, "resource-retired", ResourceId: entry.Key));
                _resources.Remove(entry.Key); _pending.Remove(entry.Key); PruneTransfers();
            }
            catch { _invalid = true; }
            return;
        }
    }
    private void PruneTransfers()
    {
        if (_active) return;
        var used = new HashSet<ulong>();
        foreach (var state in _resources.Values)
            foreach (var segment in state.Value.Segments)
                if (segment.Write.TransferId is { } transfer) used.Add(transfer);
        foreach (ulong id in _transfers.Keys.Where(id => !used.Contains(id) && !_pending.ContainsValue(id)).ToArray()) _transfers.Remove(id);
    }
    internal GraphicsBoundSourceWrite Write(object key, string kind, ulong offset, ulong length, int elementSize,
        string hash, ulong sourceBytes, ulong? transfer = null, GraphicsBoundTextureSource? texture = null)
    {
        ulong id = Id(key); var state = _resources[id];
        try
        {
            if (length == 0 || sourceBytes < length || elementSize < 1 || hash.Length != 64) throw Refuse("Invalid successful source write.");
            ulong end = checked(offset + length);
            if (state.Value.Buffer is { } buffer && end > buffer.SizeInBytes) throw Refuse("Bound source write escaped buffer.");
            var write = new GraphicsBoundSourceWrite(checked(++_writeOrder), id, kind, offset, length, elementSize, hash, sourceBytes, transfer, texture);
            var next = new List<GraphicsBoundSourceSegment>(GraphicsBoundInputProvenance.SegmentsPerResource + 2);
            foreach (var old in state.Value.Segments)
            {
                if (texture is not null && old.Write.TextureSource?.MipIndex != texture.MipIndex) { next.Add(old); continue; }
                ulong oldEnd = checked(old.Start + old.Length);
                if (oldEnd <= offset || old.Start >= end) { next.Add(old); continue; }
                if (old.Start < offset) next.Add(old with { Length = offset - old.Start });
                if (oldEnd > end) next.Add(old with { Start = end, Length = oldEnd - end, SourceOffset = checked(old.SourceOffset + end - old.Start) });
            }
            next.Add(new(offset, length, 0, write));
            if (next.Count > GraphicsBoundInputProvenance.SegmentsPerResource) throw Refuse("Bound source split exceeds64segments.");
            state.Value = state.Value with { Segments = next.OrderBy(s => s.Write.TextureSource?.MipIndex ?? -1).ThenBy(s => s.Start).ToArray() };
            PruneTransfers(); CheckMap(); Event(new(_events.Count, "source-write", Write: write));
            return write;
        }
        catch { _invalid = true; throw; }
    }
    internal ulong NewBatch()
    {
        Check();
        try { return checked(++_batchGeneration); }
        catch { _invalid = true; throw; }
    }
    internal void Queue(object key, int slot, ulong generation, ulong stagingOffset, ulong bytes)
    {
        try
        {
            ulong id = Id(key); ulong transfer = checked(++_transferId);
            var value = new GraphicsBoundTransfer(transfer, slot, generation, stagingOffset, bytes, "queued", null, null);
            _transfers.Add(transfer, value); _pending[id] = transfer; CheckMap(); Event(new(_events.Count, "upload-state", Transfer: value));

        }
        catch { _invalid = true; throw; }
    }
    internal ulong Pending(object key) => _pending[Id(key)];
    internal void UploadState(int slot, ulong generation, string state, ulong? fence = null, string? failure = null)
    {
        try
        {
            Check();
            foreach (var entry in _transfers.Values.Where(x => x.BatchSlot == slot && x.BatchGeneration == generation).ToArray())
            {
                if (state == "signaled" && (fence is null or 0 or ulong.MaxValue)) throw Refuse("Actual successful upload fence required.");
                var value = entry with { State = state, SignaledFence = fence, FailureStage = failure };
                _transfers[entry.Id] = value; Event(new(_events.Count, "upload-state", Transfer: value));
            }
            if (failure is not null) _invalid = true;

        }
        catch { _invalid = true; throw; }
    }
    internal bool CaptureAttempted { get; private set; }
    internal void Arm(ulong epoch, int frame, ulong presented)
    {
        try
        {
            CaptureAttempted = true;
            Check();
            if (_active) throw Refuse("Bound capture already active.");
            _baseline = _resources.Values.OrderBy(x => x.Value.Id).Select(x => x.Value with { Segments = x.Value.Segments.ToArray() }).ToArray();
            _baselineTransfers = _transfers.Values.OrderBy(x => x.Id).ToArray();
            _baselineOrder = _writeOrder; _resourceEpoch = epoch; _frame = frame; _presentedBefore = presented;
            _wireBytes = checked(4096 + Wire(_baseline) + Wire(_baselineTransfers));
            if (_wireBytes > GraphicsBoundInputProvenance.BodyCapacity) throw Refuse("Bound baseline wire capacity exhausted.");
            _events.Clear(); _vertices.Clear(); _textures.Clear(); _index = _constants = _shadow = _shadowFrame = null; _rootSha = null; _draws = 0;
            _active = true;

        }
        catch { _invalid = true; throw; }
    }
    internal void BindBuffer(object key, ulong journalId, bool index, int slot, ulong offset, ulong length)
    {
        try
        {
            if (!_active) return;
            var value = new BoundView(Id(key), journalId, offset, length);
            if (index) _index = value; else _vertices[slot] = value;

        }
        catch { _invalid = true; throw; }
    }
    internal void BindTexture(object key, ulong journalId, int slot)
    {
        try
        {
            if (!_active) return;
            ulong id = Id(key); _textures[slot] = new(id, journalId, 0, 0);

        }
        catch { _invalid = true; throw; }
    }
    internal void InvalidateRoot()
    { if (_active) { _textures.Clear(); _constants = _shadow = null; _rootSha = null; } }
    internal void Pipeline(bool emitted, bool shadowReceiver, bool buffered)
    {
        if (!_active) return;
        if (emitted) { _textures.Clear(); _constants = null; _rootSha = null; }
        _shadow = shadowReceiver ? _shadowFrame : null;
        if (!buffered) _constants = null;
    }
    internal void RootConstants(string hash) { if (_active) _rootSha = hash; }
    internal void ImplicitWrite(object rawResourceWitness, ulong epoch, int slot, ulong offset, ReadOnlySpan<byte> bytes, bool shadow)
    {
        Check();
        if (!_implicit.TryGetValue((epoch, slot), out ulong id))
        {
            var identity = new GraphicsWorldImplicitBinding("shadow-and-vertex-constant-arena", epoch, slot, null, null, null, 0);
            id = Register(rawResourceWitness, "implicit-constant-arena", new(4096 * 256, GraphicsBufferMemory.Upload), implicitIdentity: identity);
            _implicit.Add((epoch, slot), id);
        }
        var state = _resources[id];
        if (!state.Key.TryGetTarget(out object? resource) || !ReferenceEquals(resource, rawResourceWitness))
            throw Refuse("Implicit raw arena was replaced or its witness is unavailable at the same epoch/slot.");
        Write(resource, shadow ? "implicit-shadow-frame" : "implicit-buffered-constants", offset, (ulong)bytes.Length, 4,
            Convert.ToHexString(SHA256.HashData(bytes)), (ulong)bytes.Length);
        if (_active)
        {
            var view = new BoundView(id, 0, offset, (ulong)bytes.Length);
            if (shadow) _shadowFrame = view; else _constants = view;
        }
    }
    private GraphicsBoundAssociation Association(BoundView view, string role, int? slot, int? mip = null)
    {
        var resource = _resources[view.ResourceId].Value;
        if (!resource.Committed) throw Refuse("Bound resource initialization never committed.");
        var segments = resource.Segments.Where(x => (x.Write.TextureSource?.MipIndex) == mip).ToArray();
        ulong length = role == "texture" ? (segments.Length == 0 ? 0 : segments.Max(x => x.Start + x.Length)) : view.Length;
        ulong end = checked(view.Offset + length); ulong cursor = view.Offset;
        var writes = new List<ulong>(); var holes = new List<GraphicsBoundUnknownRange>();
        foreach (var segment in segments.Where(x => x.Start < end && x.Start + x.Length > view.Offset).OrderBy(x => x.Start))
        {
            if (cursor < segment.Start) holes.Add(new(cursor, segment.Start - cursor));
            cursor = Math.Max(cursor, Math.Min(end, segment.Start + segment.Length)); writes.Add(segment.Write.Order);
        }
        if (cursor < end) holes.Add(new(cursor, end - cursor));
        return new(role, slot, view.JournalId == 0 ? null : view.JournalId, view.ResourceId, view.Offset, length, mip, writes.Distinct().ToArray(), holes.ToArray());
    }
    internal void Draw(int ordinal)
    {
        try
        {
            if (!_active) return;
            Check();
            if (ordinal != _draws || _draws == GraphicsBoundInputProvenance.DrawCapacity) throw Refuse("Bound draw ordinal/capacity differs from journal.");
            var bindings = new List<GraphicsBoundAssociation>(52);
            foreach (var pair in _vertices.OrderBy(x => x.Key)) bindings.Add(Association(pair.Value, "vertex", pair.Key));
            if (_index is { } index) bindings.Add(Association(index, "index", null));
            foreach (var pair in _textures.OrderBy(x => x.Key))
            {
                int count = _resources[pair.Value.ResourceId].Value.Texture!.MipCount;
                for (int mip = 0; mip < count; mip++) bindings.Add(Association(pair.Value, "texture", pair.Key, mip));
            }
            if (_constants is { } constants) bindings.Add(Association(constants, "buffered-constants", null));
            if (_shadow is { } shadow) bindings.Add(Association(shadow, "shadow-frame", null));
            Event(new(_events.Count, "world-draw", Draw: new(ordinal, _writeOrder, _rootSha, bindings.ToArray()))); _draws++;

        }
        catch { _invalid = true; throw; }
    }
    private GraphicsBoundInputProvenance Snapshot(bool complete, string? failure, GraphicsFrameSubmission? submission, ulong? completed, ulong? after)
        => new(1, "bound-input-cpu-source-provenance", complete, failure, _scope, _owner, Generation, Session,
            _resourceEpoch, _frame, _baselineOrder, _writeOrder, submission, completed, _presentedBefore, after,
            _baseline, _baselineTransfers, _events.ToArray());
    internal GraphicsBoundInputProvenance Seal(GraphicsWorldBindingJournal journal)
    {
        Check();
        if (!_active || !journal.Complete || journal.DrawCount != _draws || journal.DeviceGeneration != Generation || journal.DeviceSession != Session ||
            journal.ResourceGeneration != _resourceEpoch || journal.FrameIndex != _frame || journal.PresentedBefore != _presentedBefore)
            throw Refuse("Bound capture/journal completion mismatch.");
        var value = Snapshot(true, null, journal.Submission, journal.CompletedFence, journal.PresentedAfter);
        if (Wire(value) > GraphicsBoundInputProvenance.BodyCapacity) throw Refuse("Bound capture final wire capacity exceeded.");
        _active = false; return value;
    }
    internal GraphicsBoundInputProvenance Prefix(Exception error)
    { _invalid = true; return Snapshot(false, error.GetType().Name, null, null, null); }
    internal void EndCapture() => _active = false;
    internal void Clear()
    { _resources.Clear(); _transfers.Clear(); _pending.Clear(); _implicit.Clear(); _events.Clear(); _invalid = true; }
}

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsBoundInputTracker? _boundInputTracker;
    internal GraphicsBoundInputTracker? BoundInputTracker => _boundInputTracker;
    private void AttachBoundInputProvenance()
        => _boundInputTracker = GraphicsBoundInputProvenanceScope.Current?.Claim(_telemetryGeneration, _telemetryIdentitySession);
    internal GraphicsBoundFrozenSource FreezeBoundSource(ReadOnlySpan<byte> bytes)
    {
        var tracker = _boundInputTracker ?? throw new InvalidOperationException("Bound source freeze requires the selected scope.");
        tracker.VerifyAvailable();
        try { return new(bytes); }
        catch { tracker.Invalidate(); throw; }
    }
    private GraphicsBoundFrozenTexture? FreezeBoundTexture(GraphicsTextureData texture)
    {
        if (_boundInputTracker is not { } tracker) return null;
        tracker.VerifyAvailable();
        try { return new(texture); }
        catch { tracker.Invalidate(); throw; }
    }
    internal void RegisterBoundBuffer(GraphicsResourceRetirement.Resource lifetime, GraphicsBufferDescription description)
        => _boundInputTracker?.Register(lifetime, "buffer", buffer: description);
    internal void RecordBoundWrite(GraphicsResourceRetirement.Resource lifetime, int offset, ReadOnlySpan<byte> bytes, int elementSize)
        => _boundInputTracker?.Write(lifetime, "upload-set-data", checked((ulong)offset), (ulong)bytes.Length, elementSize,
            Convert.ToHexString(SHA256.HashData(bytes)), (ulong)bytes.Length);
}
