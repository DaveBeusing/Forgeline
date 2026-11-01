using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeLine.Graphics;

// Explicit late diagnostic contracts. These are not renderer bindings or a complete device inventory.
public sealed record GraphicsPostFrameOriginalReceipt(int SchemaVersion, string FenceDomain,
    GraphicsDeviceTelemetryIdentity DeviceIdentity, int ManagedOwnerThreadId, int FrameIndex,
    GraphicsFrameSubmission Submission, ulong CompletedFence, ulong PresentedBefore, ulong PresentedAfter,
    int Width, int Height, ulong ResizeGeneration, ulong AppliedResizeGeneration);
public sealed record GraphicsPostFrameCopyReceipt(int SchemaVersion, string FenceDomain, string QueueRelationship,
    ulong SubmittedFence, ulong CompletedFence, bool MayHaveExecuted, bool CompletionProved);
public sealed record GraphicsPostFrameResourceReference(int ResourceId, string Role);
public sealed record GraphicsPostFrameIndexRange(int FirstIndex, int IndexCount, int Step);
public sealed record GraphicsPostFrameTerrainChunk(int Order, int ChunkIndex, int ChunkX, int ChunkZ,
    Vector3 BoundsMinimum, Vector3 BoundsMaximum, int StoredMainLod, string LodMeaning, uint PaletteKey,
    string[] MaterialKeys, GraphicsPostFrameIndexRange[] IndexRanges, GraphicsPostFrameResourceReference[] Resources);
public sealed record GraphicsPostFrameBatch(int Order, string? MeshKey, string? MaterialKey, int PartIndex,
    int InstanceCount, int InstanceOffset, int InstanceStride, int VertexStride, int FirstIndex, int IndexCount,
    string IndexFormat, string PassMeaning, GraphicsPostFrameResourceReference[] Resources);
public sealed record GraphicsPostFrameIntent(GraphicsPostFrameTerrainChunk[] TerrainChunks,
    GraphicsPostFrameBatch[] MainBatches, GraphicsPostFrameBatch[] DecorationBatches);
public sealed record GraphicsPostFrameMip(int MipIndex, int Width, int Height, int RowCount, ulong RowBytes,
    ulong RowPitch, ulong PackedOffset, ulong PaddedBytes, ulong CanonicalBytes, string Format);
public sealed record GraphicsPostFrameResourceDescription(int LocalId, string SemanticName, string Kind,
    bool LifetimeAvailable, GraphicsBufferDescription? Buffer, GraphicsTextureDescription? Texture,
    GraphicsWorldImplicitBinding? Implicit, ulong SourceOffset, ulong ObservedBytes, string RangeMeaning,
    GraphicsPostFrameMip[] Mips);
public sealed record GraphicsPostFrameResourceBytes(GraphicsPostFrameResourceDescription Description, byte[][] CanonicalBytes);
public sealed record GraphicsPostFrameSnapshotResult(GraphicsPostFrameOriginalReceipt OriginalFrameReceipt,
    GraphicsPostFrameCopyReceipt CopyReceipt, GraphicsPostFrameResourceBytes[] Resources, GraphicsPostFrameIntent Intent,
    ulong PaddedStagingBytes, ulong CanonicalBytes, string[] SecondaryCleanupErrors);

public static class GraphicsPostFrameLimits
{
    public const int Resources = 4096, MipsPerTexture = 32, MipEntries = Resources * MipsPerTexture;
    public const int TerrainChunks = 4096, MainBatches = 4096, DecorationBatches = 4096;
    public const int ResourceReferences = 131072, IndexRangesPerChunk = 32, StringUtf8Bytes = 4096;
    public const long RawBytes = 128L * 1024 * 1024, StagingBytes = RawBytes;
    public const int EnvelopeBytes = 1024 * 1024;
    public const long WireBytes = 32L * 1024 * 1024, BodyBytes = WireBytes - EnvelopeBytes;
    public static string? ValidateOptionalString(string? value)
    {
        if (value is not null && Encoding.UTF8.GetByteCount(value) > StringUtf8Bytes)
            throw new InvalidDataException("Post-frame optional string exceeds4096 UTF8 bytes.");
        return value;
    }
    public static ulong Align(ulong value, ulong alignment)
    {
        if (alignment is not (256 or 512)) throw new ArgumentOutOfRangeException(nameof(alignment));
        return checked((value + alignment - 1) & ~(alignment - 1));
    }
    public static void Totals(ulong staging, ulong canonical)
    {
        if (staging == 0 || canonical == 0 || staging > StagingBytes || canonical > RawBytes)
            throw new InvalidDataException("Post-frame total staging/canonical structural refusal capacity exceeded.");
    }
    public static (int Width, int Height, int Rows, ulong RowBytes) CanonicalMip(GraphicsTextureDescription texture, int mip)
    {
        texture.Validate();
        if (mip < 0 || mip >= texture.MipCount) throw new ArgumentOutOfRangeException(nameof(mip));
        int width = Math.Max(1, texture.Width >> mip), height = Math.Max(1, texture.Height >> mip);
        int rows = texture.Format == GraphicsTextureFormat.Bc7Unorm ? checked((height + 3) / 4) : height;
        return (width, height, rows, checked((ulong)GraphicsBoundFrozenTexture.RowBytes(texture.Format, width)));
    }
    internal static (int Width, int Height) NativeFootprintDimensions(GraphicsTextureDescription texture, int mip)
    {
        var logical = CanonicalMip(texture, mip);
        // Microsoft DirectX-Headers D3DX12GetCopyableFootprints aligns BC footprints to full blocks.
        return texture.Format == GraphicsTextureFormat.Bc7Unorm
            ? (checked((logical.Width + 3) & ~3), checked((logical.Height + 3) & ~3))
            : (logical.Width, logical.Height);
    }
    internal static void MetadataBody(object value, int rawReferences)
    {
        if (rawReferences is < 1 or > MipEntries) throw new InvalidDataException("Post-frame raw reference capacity exceeded.");
        long reserve = checked((long)rawReferences * 512 + 64 * 1024);
        if (reserve >= BodyBytes) throw new InvalidDataException("Post-frame raw metadata reserve exceeds body capacity.");
        _ = GraphicsPostFrameJson.Measure(value, BodyBytes - reserve);
    }
    public static void ValidateIntent(GraphicsPostFrameIntent intent, int resourceCount)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (resourceCount is < 1 or > Resources || intent.TerrainChunks.Length > TerrainChunks ||
            intent.MainBatches.Length > MainBatches || intent.DecorationBatches.Length > DecorationBatches)
            throw new InvalidDataException("Post-frame resource/intent entry capacity exceeded.");
        int count = 0;
        void References(GraphicsPostFrameResourceReference[] references)
        {
            count = checked(count + references.Length);
            if (count > ResourceReferences) throw new InvalidDataException("Post-frame intent reference capacity exceeded.");
            foreach (var reference in references)
            {
                if (reference.ResourceId < 1 || reference.ResourceId > resourceCount)
                    throw new InvalidDataException("Post-frame dangling resource reference.");
                ValidateOptionalString(reference.Role);
            }
        }
        foreach (var chunk in intent.TerrainChunks)
        {
            if (chunk.IndexRanges.Length is < 1 or > IndexRangesPerChunk || chunk.MaterialKeys.Length > 32)
                throw new InvalidDataException("Post-frame terrain index/material capacity exceeded.");
            ValidateOptionalString(chunk.LodMeaning);
            foreach (string key in chunk.MaterialKeys) ValidateOptionalString(key);
            References(chunk.Resources);
        }
        foreach (var batch in intent.MainBatches.Concat(intent.DecorationBatches))
        {
            ValidateOptionalString(batch.MeshKey); ValidateOptionalString(batch.MaterialKey); ValidateOptionalString(batch.PassMeaning); ValidateOptionalString(batch.IndexFormat);
            References(batch.Resources);
        }
        _ = GraphicsPostFrameJson.Measure(intent, BodyBytes);
    }
}

// Allocated only after PNG publication. Graphics never claims that a filesystem operation occurred.
public sealed class GraphicsPostFrameReceipt
{
    internal object Owner { get; }
    internal ulong Serial { get; }
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    internal bool Consumed { get; private set; }
    public GraphicsPostFrameOriginalReceipt Original { get; }
    internal GraphicsPostFrameReceipt(object owner, ulong serial, GraphicsPostFrameOriginalReceipt original)
        => (Owner, Serial, Original) = (owner, serial, original);
    internal void Consume(object owner)
    {
        if (_ownerThread != Environment.CurrentManagedThreadId || Consumed || !ReferenceEquals(Owner, owner)) throw new InvalidOperationException("Foreign/reused post-frame receipt.");
        Consumed = true;
    }
}

public sealed class GraphicsPostFrameRoster
{
    internal sealed record Source(int Id, string Name, object Resource, ulong Offset, ulong? Length);
    private readonly Dictionary<object, int> _identities = new(ReferenceEqualityComparer.Instance);
    internal List<Source> Sources { get; } = [];
    public int AddBuffer(string semanticName, IGraphicsBuffer buffer, ulong offset = 0, ulong? length = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset > buffer.Description.SizeInBytes || (length ?? buffer.Description.SizeInBytes - offset) == 0 ||
            (length ?? buffer.Description.SizeInBytes - offset) > buffer.Description.SizeInBytes - offset)
            throw new InvalidDataException("Post-frame requested buffer range is invalid.");
        return Add(semanticName, buffer, offset, length);
    }
    public int AddTexture(string semanticName, IGraphicsTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return Add(semanticName, texture, 0, null);
    }
    private int Add(string name, object resource, ulong offset, ulong? length)
    {
        GraphicsPostFrameLimits.ValidateOptionalString(name);
        if (_identities.TryGetValue(resource, out int found))
        {
            var prior = Sources[found - 1];
            if (prior.Offset != offset || prior.Length != length)
                throw new InvalidDataException("One declared resource has incompatible snapshot ranges.");
            return found;
        }
        if (Sources.Count == GraphicsPostFrameLimits.Resources) throw new InvalidDataException("Post-frame resource capacity exceeded.");
        int id = Sources.Count + 1;
        Sources.Add(new(id, name, resource, offset, length)); _identities.Add(resource, id);
        return id;
    }
}

public static class GraphicsPostFrameSnapshots
{
    public static GraphicsPostFrameReceipt IssueReceipt(IGraphicsDevice device, int actualRecordedSlot)
        => device is D3D12GraphicsDevice native ? native.IssuePostFrameReceipt(actualRecordedSlot)
            : throw new NotSupportedException("Post-frame resource snapshots require the actual D3D12 owner.");
    public static GraphicsPostFrameSnapshotFailure? Failure(Exception error, GraphicsPostFrameReceipt? receipt)
        => GraphicsPostFrameFailure.Read(error, receipt);
    public static GraphicsPostFrameSnapshotResult Capture(IGraphicsDevice device, GraphicsPostFrameReceipt receipt,
        GraphicsPostFrameRoster roster, GraphicsPostFrameIntent intent)
        => device is D3D12GraphicsDevice native ? native.CapturePostFrameSnapshot(receipt, roster, intent)
            : throw new NotSupportedException("Post-frame resource snapshots require the actual D3D12 owner.");
}

public sealed record GraphicsPostFrameSnapshotFailure(string Phase, GraphicsPostFrameOriginalReceipt OriginalFrameReceipt,
    GraphicsPostFrameCopyReceipt? CopyReceipt, GraphicsPostFrameResourceBytes[] Resources, bool MayHaveExecuted, string[] SecondaryCleanupErrors);

internal enum GraphicsPostFrameOperation { Footprints, AllocateReadback, CreateCopyObjects, BeforeExecute, AfterExecute, Signal, Wait, Map }

// Registry callback: idempotent, attempt every owned release, never throw through ReleaseAll.
internal sealed class GraphicsPostFrameCompositeLease : IDisposable
{
    private readonly IDisposable[] _parts;
    private readonly List<string> _errors = [];
    private int _disposed;
    internal bool MayHaveExecuted { get; set; }
    internal bool CompletionProved { get; set; }
    internal string[] Errors => _errors.ToArray();
    internal int ErrorCount => _errors.Count;
    internal GraphicsPostFrameCompositeLease(IDisposable[] parts) => _parts = parts;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (IDisposable part in _parts)
        {
            try { part.Dispose(); }
            catch (Exception error)
            {
                try
                {
                    if (_errors.Count < 64) _errors.Add(error.GetType().Name.Length <= 512 ? error.GetType().Name : "secondary-release-error");
                }
                catch { }
            }
        }
    }
}

internal readonly record struct GraphicsPostFrameCompletionStamp(bool Valid, ulong Serial, GraphicsFrameSubmission Submission,
    ulong CompletedFence, int FrameIndex, ulong PresentedBefore, ulong PresentedAfter, int Width, int Height,
    ulong ResizeGeneration, ulong AppliedResizeGeneration, int ConstantUsedPrefix, object? ConstantArenaWitness);

// Stream-level refusal bounds escaped UTF8 while it is emitted, before any oversized result array.
public static class GraphicsPostFrameJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter()); options.MakeReadOnly(populateMissingResolver: true); return options;
    }
    public static long Measure(object value, long maximum)
    {
        using var counter = new GraphicsPostFrameBoundedStream(maximum, retain: false);
        JsonSerializer.Serialize(counter, value, Options); return counter.WrittenBytes;
    }
    public static byte[] Serialize(object value, long maximum)
    {
        using var output = new GraphicsPostFrameBoundedStream(maximum, retain: true);
        JsonSerializer.Serialize(output, value, Options); return output.Bytes();
    }
}
internal sealed class GraphicsPostFrameBoundedStream : Stream
{
    private readonly MemoryStream? _output;
    private readonly long _maximum;
    internal long WrittenBytes { get; private set; }
    internal bool RetainsBytes => _output is not null;
    internal GraphicsPostFrameBoundedStream(long maximum, bool retain)
    {
        if (maximum < 0 || maximum > GraphicsPostFrameLimits.WireBytes) throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum; if (retain) _output = new MemoryStream();
    }
    internal byte[] Bytes() => _output?.ToArray() ?? throw new InvalidOperationException("Count-only writer retains no byte array.");
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => WrittenBytes;
    public override long Position { get => WrittenBytes; set => throw new NotSupportedException(); }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > _maximum - WrittenBytes) throw new InvalidDataException("Post-frame running escaped UTF8 capacity exhausted.");
        _output?.Write(buffer); WrittenBytes += buffer.Length;
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _output?.Dispose(); base.Dispose(disposing); }
}
