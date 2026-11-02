using System.Security.Cryptography;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

public sealed class GraphicsBoundInputProvenanceTests
{
    private const string Session = "cccccccccccccccccccccccccccccccc";
    private static readonly byte[] First = [1, 2, 3, 4];
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static GraphicsBoundInputTracker Tracker() => new("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 7, Session);
    private static GraphicsWorldBindingJournal Completion(int draws = 1) =>
        new(1, "opaque-world-binding-journal", true, null, 7, 3, Session, 1, new(1, 21, 7, 3), 22, 12, 13, draws, 4096, [], []);
    private static void Write(GraphicsBoundInputTracker tracker, object key, ulong offset, byte[] bytes)
        => tracker.Write(key, "upload-set-data", offset, (ulong)bytes.Length, 1, Hash(bytes), (ulong)bytes.Length);

    [Fact]
    public void FrozenSourceIsExactlyWhatConsumerAndHashObserveAfterCallerMutation()
    {
        byte[] caller = First.ToArray();
        using var frozen = new GraphicsBoundFrozenSource(caller);
        caller.AsSpan().Fill(99);
        byte[] consumed = frozen.Bytes.ToArray();
        Assert.Equal(First, consumed);
        Assert.Equal(Hash(consumed), frozen.Sha256);
        Assert.NotEqual(Hash(caller), frozen.Sha256);
    }

    [Fact]
    public void TextureFreezeIgnoresSourcePaddingAndSurvivesMutableCaller()
    {
        byte[] source = [1, 2, 3, 4, 99, 99, 99, 99, 5, 6, 7, 8, 88, 88, 88, 88];
        var texture = new GraphicsTextureData(new(1, 2, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(1, 2, 8, source)]);
        using var frozen = new GraphicsBoundFrozenTexture(texture);
        source.AsSpan().Clear();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, frozen.Bytes(0).ToArray());
        Assert.Equal(8, frozen.Sources[0].RowPitch);
        Assert.Empty(frozen.Sources[0].Pixels);
    }

    [Fact]
    public void Bc7MipFreezePreservesLogicalBlocksAcrossPaddingAndCallerMutation()
    {
        byte[] top = Enumerable.Range(0, 96).Select(i => (byte)i).ToArray();
        byte[] lower = Enumerable.Range(0, 32).Select(i => (byte)(i + 100)).ToArray();
        var texture = new GraphicsTextureData(new(8, 8, GraphicsTextureFormat.Bc7Unorm, GraphicsTextureColorSpace.Srgb, 2),
            [new(8, 8, 48, top), new(4, 4, 32, lower)]);
        using var frozen = new GraphicsBoundFrozenTexture(texture);
        byte[] expected = top.AsSpan(0, 32).ToArray().Concat(top.AsSpan(48, 32).ToArray()).ToArray();
        top.AsSpan().Clear(); lower.AsSpan().Clear();
        Assert.Equal(expected, frozen.Bytes(0).ToArray());
        Assert.Equal(Enumerable.Range(100, 16).Select(i => (byte)i).ToArray(), frozen.Bytes(1).ToArray());
        Assert.Equal(48, frozen.Sources[0].RowPitch);
    }

    [Fact]
    public void PreArmSourceAndDistinctIdentityNamespacesJoinTheActualDraw()
    {
        var tracker = Tracker(); object buffer = new();
        ulong id = tracker.Register(buffer, "buffer", new(16, GraphicsBufferMemory.Upload));
        Write(tracker, buffer, 4, First);
        tracker.Arm(3, 1, 12);
        tracker.BindBuffer(buffer, 97, false, 0, 0, 16);
        tracker.Draw(0);
        var capture = tracker.Seal(Completion());
        Assert.True(capture.Complete);
        var association = capture.Events.Single(e => e.Draw is not null).Draw!.Bindings.Single();
        Assert.Equal(id, association.ResourceId);
        Assert.Equal(97UL, association.JournalIdentity);
        Assert.Equal(new GraphicsBoundUnknownRange[] { new(0, 4), new(8, 8) }, association.UnknownRanges);
        Assert.Equal(1UL, capture.BaselineWriteWatermark);
        Assert.Equal(Hash(First), capture.Baseline.Single().Segments.Single().Write.SourceSha256);
    }

    [Fact]
    public void OverlappingWriteRetainsWholeSourceDigestAndSliceOffsets()
    {
        var tracker = Tracker(); object buffer = new(); tracker.Register(buffer, "buffer", new(16, GraphicsBufferMemory.Upload));
        byte[] initial = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        Write(tracker, buffer, 0, initial); Write(tracker, buffer, 4, First);
        tracker.Arm(3, 1, 12);
        var segments = tracker.Seal(Completion(0)).Baseline.Single().Segments;
        Assert.Equal(3, segments.Length);
        Assert.Equal(Hash(initial), segments[2].Write.SourceSha256);
        Assert.Equal(8UL, segments[2].SourceOffset);
        Assert.Equal(16UL, segments[2].Write.SourceByteCount);
        Assert.Equal(4UL, segments[0].Length);
    }

    [Fact]
    public void LaterOffWorldWriteRemainsAfterDrawWithoutMutatingItsSnapshot()
    {
        var tracker = Tracker(); object buffer = new(); tracker.Register(buffer, "buffer", new(16, GraphicsBufferMemory.Upload));
        Write(tracker, buffer, 0, First); tracker.Arm(3, 1, 12);
        tracker.BindBuffer(buffer, 9, false, 0, 0, 4); tracker.Draw(0);
        Write(tracker, buffer, 1, First);
        var capture = tracker.Seal(Completion());
        var draw = capture.Events[0].Draw!;
        Assert.Equal(1UL, draw.WriteWatermark);
        Assert.Equal(2UL, capture.Events[1].Write!.Order);
        Assert.Equal(1UL, draw.Bindings[0].SourceWriteOrders.Single());
        Assert.Equal(2UL, capture.FinalWriteWatermark);
    }

    [Fact]
    public void SameBytesAtDifferentDestinationsAreDifferentAssociations()
    {
        var tracker = Tracker(); object a = new(), b = new();
        ulong first = tracker.Register(a, "buffer", new(4, GraphicsBufferMemory.Upload));
        ulong second = tracker.Register(b, "buffer", new(4, GraphicsBufferMemory.Upload));
        Write(tracker, a, 0, First); Write(tracker, b, 0, First); tracker.Arm(3, 1, 12);
        tracker.BindBuffer(a, 11, false, 0, 0, 4); tracker.Draw(0);
        tracker.BindBuffer(b, 12, false, 0, 0, 4); tracker.Draw(1);
        var draws = tracker.Seal(Completion(2)).Events.Where(e => e.Draw is not null).Select(e => e.Draw!).ToArray();
        Assert.Equal(first, draws[0].Bindings[0].ResourceId); Assert.Equal(second, draws[1].Bindings[0].ResourceId);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void QueueExecuteAndSignalAreDistinctActualTransferObservations()
    {
        var tracker = Tracker(); object buffer = new(); tracker.Register(buffer, "buffer", new(4, GraphicsBufferMemory.GpuLocal));
        ulong generation = tracker.NewBatch(); tracker.Queue(buffer, 2, generation, 512, 4);
        tracker.Write(buffer, "initialized-static", 0, 4, 1, Hash(First), 4, tracker.Pending(buffer));
        tracker.Arm(3, 1, 12);
        tracker.UploadState(2, generation, "execute-returned"); tracker.UploadState(2, generation, "signaled", 17);
        tracker.BindBuffer(buffer, 4, false, 0, 0, 4); tracker.Draw(0);
        var capture = tracker.Seal(Completion());
        Assert.Equal("queued", capture.BaselineTransfers.Single().State);
        Assert.Null(capture.BaselineTransfers.Single().SignaledFence);
        Assert.Equal(17UL, capture.Events.Single(e => e.Transfer?.State == "signaled").Transfer!.SignaledFence);
        Assert.Equal(512UL, capture.BaselineTransfers.Single().StagingOffset);
    }

    [Fact]
    public void SignalFailureNeverGetsSuccessfulCaptureTuple()
    {
        var tracker = Tracker(); object buffer = new(); tracker.Register(buffer, "buffer", new(4, GraphicsBufferMemory.GpuLocal));
        ulong generation = tracker.NewBatch(); tracker.Queue(buffer, 0, generation, 0, 4); tracker.Arm(3, 1, 12);
        tracker.UploadState(0, generation, "execute-returned"); tracker.UploadState(0, generation, "failed", failure: "signal");
        Assert.Throws<InvalidOperationException>(() => tracker.Seal(Completion(0)));
        var prefix = tracker.Prefix(new IOException());
        Assert.False(prefix.Complete); Assert.Null(prefix.Submission); Assert.Null(prefix.CompletedFence);
        Assert.Null(prefix.Events.Last().Transfer!.SignaledFence);
    }

    [Fact]
    public void SameImplicitArenaCombinesShadowAndBufferedRangesWithoutFakeLifetime()
    {
        var tracker = Tracker(); object rawArena = new(); tracker.ImplicitWrite(rawArena, 3, 1, 0, new byte[96], true);
        tracker.ImplicitWrite(rawArena, 3, 1, 256, First, false); tracker.Arm(3, 1, 12);
        var resource = tracker.Seal(Completion(0)).Baseline.Single();
        Assert.False(resource.LifetimeAvailable); Assert.Equal(2, resource.Segments.Length);
        Assert.Equal("shadow-and-vertex-constant-arena", resource.Implicit!.Kind);
        Assert.Equal(1, resource.Implicit.FrameIndex);
    }

    [Fact]
    public void MutableMipRecordCannotChangeFrozenCopyShapeOrBytes()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8];
        var texture = new GraphicsTextureData(new(2, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(2, 1, 8, source)]);
        using var frozen = new GraphicsBoundFrozenTexture(texture);
        ((GraphicsTextureMipData[])texture.Mips)[0] = new(1, 1, 4, [99, 99, 99, 99]);
        var consumed = frozen.Sources[0];
        int bytesPerRow = GraphicsBoundFrozenTexture.RowBytes(texture.Description.Format, consumed.Width);
        Assert.Equal(8, bytesPerRow);
        Assert.Equal(source, frozen.Bytes(0).Slice(0, bytesPerRow).ToArray());
        Assert.Equal(Hash(source), Hash(frozen.Bytes(0)));
        Assert.Equal(2, consumed.Width);
    }

    [Fact]
    public void MipRecordsMutatedBeforeFreezeAreRevalidatedBeforeNativeAllocation()
    {
        var texture = new GraphicsTextureData(new(2, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(2, 1, 8, new byte[8])]);
        var mips = (GraphicsTextureMipData[])texture.Mips;
        mips[0] = new(1, 1, 4, new byte[4]);
        Assert.Throws<InvalidOperationException>(() => new GraphicsBoundFrozenTexture(texture));
        mips[0] = new(2, 1, 4, new byte[4]);
        Assert.Throws<InvalidOperationException>(() => new GraphicsBoundFrozenTexture(texture));
        mips[0] = new(2, 1, 8, new byte[7]);
        Assert.Throws<InvalidOperationException>(() => new GraphicsBoundFrozenTexture(texture));
    }

    [Fact]
    public void SameEpochSlotCannotReuseAnImplicitMapAfterActualArenaReplacement()
    {
        var tracker = Tracker(); tracker.ImplicitWrite(new object(), 3, 1, 0, First, true);
        Assert.Throws<InvalidOperationException>(() => tracker.ImplicitWrite(new object(), 3, 1, 256, First, false));
        Assert.Throws<InvalidOperationException>(() => tracker.Arm(3, 1, 12));
    }

    [Fact]
    public void InheritedInputAssemblerSurvivesRootInvalidation()
    {
        var tracker = Tracker(); object buffer = new(); tracker.Register(buffer, "buffer", new(4, GraphicsBufferMemory.Upload));
        Write(tracker, buffer, 0, First); tracker.Arm(3, 1, 12); tracker.BindBuffer(buffer, 9, false, 0, 0, 4);
        tracker.Draw(0); tracker.InvalidateRoot(); tracker.Pipeline(true, false, false); tracker.Draw(1);
        var draws = tracker.Seal(Completion(2)).Events.Where(e => e.Draw is not null).Select(e => e.Draw!).ToArray();
        Assert.Equal(GraphicsWorldBindingJournal.SerializeSidecar(draws[0].Bindings), GraphicsWorldBindingJournal.SerializeSidecar(draws[1].Bindings));
    }

    [Fact]
    public void SegmentOverflowIsStickyAndRetainsAcceptedBaseline()
    {
        var tracker = Tracker(); object buffer = new(); tracker.Register(buffer, "buffer", new(512, GraphicsBufferMemory.Upload));
        for (int i = 0; i < 64; i++) Write(tracker, buffer, (ulong)(i * 2), First[..1]);
        tracker.Arm(3, 1, 12);
        Assert.Throws<InvalidOperationException>(() => Write(tracker, buffer, 128, First[..1]));
        Assert.Throws<InvalidOperationException>(() => tracker.Seal(Completion(0)));
        Assert.Equal(64, tracker.Prefix(new IOException()).Baseline.Single().Segments.Length);
    }

    [Fact]
    public void MissingDisposedAndForeignOwnerCannotBecomeComplete()
    {
        var tracker = Tracker(); object key = new(); tracker.Arm(3, 1, 12);
        Assert.Throws<InvalidOperationException>(() => tracker.BindBuffer(key, 1, false, 0, 0, 4));
        tracker = Tracker(); tracker.Register(key, "buffer", new(4, GraphicsBufferMemory.Upload)); tracker.Retire(key); tracker.Arm(3, 1, 12);
        Assert.Throws<InvalidOperationException>(() => tracker.BindBuffer(key, 1, false, 0, 0, 4));
        tracker = Tracker(); Exception? failure = null;
        var thread = new Thread(() => { try { tracker.NewBatch(); } catch (Exception error) { failure = error; } });
        thread.Start(); thread.Join(); Assert.IsType<InvalidOperationException>(failure);
        Assert.Throws<InvalidOperationException>(() => tracker.NewBatch());
    }

    [Fact]
    public void SharedTransientLimitRefusesBeforeAllocatingAndNeverMeansHeapBudget()
    {
        GraphicsBoundFrozenSource.ValidateCount(GraphicsBoundInputProvenance.TransientSourceCapacity);
        Assert.Throws<InvalidOperationException>(() => GraphicsBoundFrozenSource.ValidateCount((long)GraphicsBoundInputProvenance.TransientSourceCapacity + 1));
        using var scope = new GraphicsBoundInputProvenanceScope();
        scope.ReserveFrozen(GraphicsBoundInputProvenance.TransientSourceCapacity);
        Assert.Throws<InvalidOperationException>(() => scope.ReserveFrozen(1));
        scope.ReturnFrozen(GraphicsBoundInputProvenance.TransientSourceCapacity);
        using var source = new GraphicsBoundFrozenSource(First);
        Assert.Equal(First, source.Bytes.ToArray());
    }

    [Fact]
    public void ClaimedFailureCannotReuseAnotherInvocationOrEarlyScope()
    {
        var tracker = Tracker(); tracker.Arm(3, 1, 12);
        var error = new IOException(); var prefix = tracker.Prefix(error);
        using (var unarmed = GraphicsWorldBindingJournal.BeginAttempt())
            Assert.Null(GraphicsBoundInputProvenance.FailurePrefix(error, unarmed));
        GraphicsWorldBindingAttempt first;
        using (first = GraphicsWorldBindingJournal.BeginAttempt())
        {
            GraphicsWorldBindingAttempt.ClaimCurrent();
            GraphicsBoundInputProvenance.AttachFailure(error, prefix, first);
            Assert.Same(prefix, GraphicsBoundInputProvenance.FailurePrefix(error, first));
        }
        using var current = GraphicsWorldBindingJournal.BeginAttempt();
        GraphicsWorldBindingAttempt.ClaimCurrent();
        Assert.Null(GraphicsBoundInputProvenance.FailurePrefix(error, current));
        var next = prefix with { Failure = "current capture" };
        GraphicsBoundInputProvenance.AttachFailure(error, next, current);
        Assert.Same(next, GraphicsBoundInputProvenance.FailurePrefix(error, current));
        Assert.Null(GraphicsBoundInputProvenance.FailurePrefix(error, first));
    }

    [Fact]
    public void ScopeRejectsNestedAndReusedDevicesAndEarlyFactsHaveNoCapture()
    {
        Assert.Null(GraphicsBoundInputProvenanceScope.Current);
        using var scope = new GraphicsBoundInputProvenanceScope();
        Assert.Throws<InvalidOperationException>(() => new GraphicsBoundInputProvenanceScope());
        scope.Claim(7, Session);
        Assert.Throws<InvalidOperationException>(() => scope.Claim(8, Session));
        byte[] facts = GraphicsWorldBindingJournal.SerializeSidecar(scope.ScopeFailureFacts());
        using var json = System.Text.Json.JsonDocument.Parse(facts);
        Assert.False(json.RootElement.GetProperty("captureFactsIncluded").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("submission").ValueKind);
        Assert.Equal("before-capture-arm", json.RootElement.GetProperty("capturePhase").GetString());
        scope.Tracker!.Arm(3, 1, 12);
        using var late = System.Text.Json.JsonDocument.Parse(GraphicsWorldBindingJournal.SerializeSidecar(scope.ScopeFailureFacts()));
        Assert.Equal("capture-arm-attempted", late.RootElement.GetProperty("capturePhase").GetString());
        Assert.False(late.RootElement.GetProperty("captureFactsIncluded").GetBoolean());
    }
}
