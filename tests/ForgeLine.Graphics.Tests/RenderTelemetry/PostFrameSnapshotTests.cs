using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

public sealed class PostFrameSnapshotTests
{
    [Theory]
    [InlineData(GraphicsTextureColorSpace.Linear)]
    [InlineData(GraphicsTextureColorSpace.Srgb)]
    public void Bc7SmallMipsKeepLogicalDimensionsSeparateFromNativeBlockFootprints(GraphicsTextureColorSpace color)
    {
        var texture = new GraphicsTextureDescription(8, 8, GraphicsTextureFormat.Bc7Unorm, color, 4);
        Assert.Equal((2, 2, 1, 16UL), GraphicsPostFrameLimits.CanonicalMip(texture, 2));
        Assert.Equal((4, 4), GraphicsPostFrameLimits.NativeFootprintDimensions(texture, 2));
        Assert.Equal((1, 1, 1, 16UL), GraphicsPostFrameLimits.CanonicalMip(texture, 3));
        Assert.Equal((4, 4), GraphicsPostFrameLimits.NativeFootprintDimensions(texture, 3));
        Assert.Equal((8, 8), GraphicsPostFrameLimits.NativeFootprintDimensions(texture, 0));
    }
    private static GraphicsPostFrameOriginalReceipt Original() => new(1, "original-device-frame-fence",
        new("0000000000000001", 1, 0, 1, 7, 99, 1, new string('a', 32)), 3, 1, new(9, 20, 7, 2), 21, 8, 9, 64, 64, 1, 1);

    [Fact]
    public void OpaqueReceiptRejectsForeignAndRepeatedConsumption()
    {
        object owner = new(); var receipt = new GraphicsPostFrameReceipt(owner, 9, Original());
        Assert.Throws<InvalidOperationException>(() => receipt.Consume(new object()));
        Assert.False(receipt.Consumed); receipt.Consume(owner); Assert.True(receipt.Consumed);
        Assert.Throws<InvalidOperationException>(() => receipt.Consume(owner));
        Assert.Equal(1, receipt.Original.FrameIndex);
    }
    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    [Fact]
    public void ActiveRegistryLeaseAttemptsEveryNativeReleaseAndNeverThrowsOrRepeats()
    {
        int calls = 0, following = 0;
        var lease = new GraphicsPostFrameCompositeLease([
            new Release(() => { calls++; throw new IOException("first"); }),
            new Release(() => calls++),
            new Release(() => { calls++; throw new IOException("third"); })]);
        var registry = new GraphicsResourceRetirement();
        registry.Register(lease.Dispose, 1024); registry.Register(() => following++);
        lease.MayHaveExecuted = true;
        Assert.False(lease.CompletionProved);
        Assert.Equal(2, registry.LiveCount); registry.ReleaseAll(); lease.Dispose();
        Assert.Equal(3, calls); Assert.Equal(1, following); Assert.Equal(2, lease.Errors.Length);
        Assert.Equal(0, registry.LiveCount);
    }
    [Theory]
    [InlineData(GraphicsTextureFormat.Rgba8Unorm, 8, 8, 0, 8, 32UL)]
    [InlineData(GraphicsTextureFormat.R8Unorm, 8, 8, 3, 1, 1UL)]
    [InlineData(GraphicsTextureFormat.Bc7Unorm, 8, 8, 0, 2, 32UL)]
    [InlineData(GraphicsTextureFormat.Bc7Unorm, 8, 8, 3, 1, 16UL)]
    public void CanonicalEncodedMipRowsExcludePadding(GraphicsTextureFormat format, int width, int height, int mip, int rows, ulong bytes)
    {
        var result = GraphicsPostFrameLimits.CanonicalMip(new(width, height, format, GraphicsTextureColorSpace.Linear, 4), mip);
        Assert.Equal(rows, result.Rows); Assert.Equal(bytes, result.RowBytes);
        Assert.Equal(Math.Max(1, width >> mip), result.Width);
    }
    [Fact]
    public void InvalidFormatsMipIndicesOverflowAndTotalsRefuseBeforeNativeAllocation()
    {
        Assert.Throws<ArgumentException>(() => GraphicsPostFrameLimits.CanonicalMip(new(4, 4, GraphicsTextureFormat.R8Unorm, GraphicsTextureColorSpace.Srgb, 1), 0));
        Assert.Throws<ArgumentException>(() => GraphicsPostFrameLimits.CanonicalMip(new(5, 4, GraphicsTextureFormat.Bc7Unorm, GraphicsTextureColorSpace.Linear, 1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsPostFrameLimits.CanonicalMip(new(4, 4, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1), 1));
        Assert.Throws<OverflowException>(() => GraphicsPostFrameLimits.Align(ulong.MaxValue, 512));
        GraphicsPostFrameLimits.Totals((ulong)GraphicsPostFrameLimits.StagingBytes, (ulong)GraphicsPostFrameLimits.RawBytes);
        Assert.Throws<InvalidDataException>(() => GraphicsPostFrameLimits.Totals((ulong)GraphicsPostFrameLimits.StagingBytes + 1, 1));
        Assert.Throws<InvalidDataException>(() => GraphicsPostFrameLimits.Totals(1, (ulong)GraphicsPostFrameLimits.RawBytes + 1));
    }
    private sealed class Buffer : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => new(16, GraphicsBufferMemory.Upload);
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged => throw new InvalidOperationException("CPU test never uploads.");
        public void Dispose() { }
    }
    [Fact]
    public void LateRosterDeduplicatesActualObjectsAndRejectsConflictingRanges()
    {
        var roster = new GraphicsPostFrameRoster(); var buffer = new Buffer();
        Assert.Equal(1, roster.AddBuffer("first", buffer)); Assert.Equal(1, roster.AddBuffer("second role", buffer));
        Assert.Throws<InvalidDataException>(() => roster.AddBuffer("conflict", buffer, 0, 4));
        Assert.Throws<InvalidDataException>(() => roster.AddBuffer("range", new Buffer(), 15, 2));
        Assert.Throws<InvalidDataException>(() => roster.AddBuffer(new string('x', 4097), new Buffer()));
        Assert.Single(roster.Sources);
    }
    [Fact]
    public void FailurePrefixIsBoundToOneConsumedReceiptEvenForReusedException()
    {
        object owner = new(); var first = new GraphicsPostFrameReceipt(owner, 9, Original()); first.Consume(owner);
        var second = new GraphicsPostFrameReceipt(owner, 10, Original()); second.Consume(owner);
        var error = new IOException("original");
        GraphicsPostFrameFailure.Attach(error, first, "signal", null, [], true, []);
        Assert.NotNull(GraphicsPostFrameSnapshots.Failure(error, first)); Assert.Null(GraphicsPostFrameSnapshots.Failure(error, second));
        GraphicsPostFrameFailure.Attach(error, second, "map", null, [], false, []);
        Assert.Null(GraphicsPostFrameSnapshots.Failure(error, first)); Assert.Equal("map", GraphicsPostFrameSnapshots.Failure(error, second)!.Phase);
        Assert.Null(GraphicsPostFrameSnapshots.Failure(error, null));
    }
    [Fact]
    public void EscapedStringsAndIntentReferencesRemainBounded()
    {
        Assert.Equal(new string('x', 4096), GraphicsPostFrameLimits.ValidateOptionalString(new string('x', 4096)));
        Assert.Throws<InvalidDataException>(() => GraphicsPostFrameLimits.ValidateOptionalString(new string('\u20ac', 1366)));
        var batch = new GraphicsPostFrameBatch(0, null, null, -1, 1, 0, 112, 48, 0, 3, "ThirtyTwoBit", "final retained scratch", [new(2, "vertex")]);
        Assert.Throws<InvalidDataException>(() => GraphicsPostFrameLimits.ValidateIntent(new([], [batch], []), 1));
        GraphicsPostFrameLimits.ValidateIntent(new([], [batch with { Resources = [new(1, "vertex")] }], []), 1);
        Assert.Throws<InvalidDataException>(() => GraphicsPostFrameLimits.MetadataBody(new { text = "small" }, GraphicsPostFrameLimits.MipEntries + 1));
    }
    [Fact]
    public void WrongThreadCannotConsumeOwnersReceiptAndOriginalOwnerCanStillUseIt()
    {
        object owner = new(); var receipt = new GraphicsPostFrameReceipt(owner, 9, Original()); Exception? rejected = null;
        var thread = new Thread(() => { try { receipt.Consume(owner); } catch (Exception error) { rejected = error; } });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5))); Assert.IsType<InvalidOperationException>(rejected);
        Assert.False(receipt.Consumed); receipt.Consume(owner); Assert.True(receipt.Consumed);
    }
    [Fact]
    public void CountingUtf8WriterRefusesEscapingBeforeAnOversizedResultArrayExists()
    {
        string key = new('"', 4096);
        var chunk = new GraphicsPostFrameTerrainChunk(0, 0, 0, 0, default, default, 0, "stored history", 0,
            [key, key, key, key], [new(0, 3, 1)], [new(1, "vertex")]);
        var chunks = Enumerable.Range(0, 4096).Select(i => chunk with { Order = i, ChunkIndex = i }).ToArray();
        using var counter = new GraphicsPostFrameBoundedStream(GraphicsPostFrameLimits.BodyBytes, retain: false);
        Assert.False(counter.RetainsBytes);
        Assert.Throws<InvalidDataException>(() => System.Text.Json.JsonSerializer.Serialize(counter, new GraphicsPostFrameIntent(chunks, [], []), GraphicsPostFrameJson.Options));
        Assert.InRange(counter.WrittenBytes, 1, GraphicsPostFrameLimits.BodyBytes);
        Assert.Throws<InvalidOperationException>(() => counter.Bytes());
    }
    [Fact]
    public void BoundedCompactWireMatchesActualStringEnumAndUnicodeEncoding()
    {
        var value = new { mode = GraphicsTextureFormat.Bc7Unorm, name = "\"\u20ac\u03a9", values = new[] { 0, 1, 2 } };
        byte[] expected = GraphicsWorldBindingJournal.SerializeSidecar(value);
        Assert.Equal(expected.LongLength, GraphicsPostFrameJson.Measure(value, expected.Length));
        Assert.Equal(expected, GraphicsPostFrameJson.Serialize(value, expected.Length));
        Assert.Throws<InvalidDataException>(() => GraphicsPostFrameJson.Serialize(value, expected.Length - 1));
    }

}
