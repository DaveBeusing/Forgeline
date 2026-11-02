using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

// All call spies in this class are SIMULATED source calls. No production/native hook is installed.
public sealed class PostFrameCommandTapeTests
{
    private static readonly Guid Session = new("672e5e3a-d91c-4143-aa87-a8cde84d8560");
    private static PostFrameTapeLimits Tiny(int events = 128, int words = 128, int catalog = 4096,
        int identities = 16, int pipelines = 4, int draws = 8) => new(events, words, catalog, identities, pipelines, draws);
    private static GraphicsPostFrameCommandTape Tape(object device, PostFrameTapeLimits? limits = null) =>
        new(device, Session, Environment.CurrentManagedThreadId, limits ?? Tiny());
    private static PostFrameTapeFrame Frame(uint slot = 0, ulong ordinal = 1) => new(slot, ordinal, ordinal * 4, ordinal * 4 + 1, 1, 1, ordinal - 1, ordinal);
    private static PostFrameCreationValue[] Metadata() =>
    [
        new(PostFrameCreationField.RootFlags, 0, 0x31, 0, 0, 0),
        new(PostFrameCreationField.RootConstants, 0, 0, 0, 16, 1),
        new(PostFrameCreationField.BlendTarget, 0, 1, 2, 3, 4),
        new(PostFrameCreationField.DepthStencil, 0, 1, 2, 3, 4),
        new(PostFrameCreationField.SampleMask, 0, uint.MaxValue, 0, 0, 0)
    ];
    private static void Catalog(GraphicsPostFrameCommandTape tape)
    {
        Assert.True(tape.TryFreezeCreation(Metadata(), [1, 2, 3], [4, 5], out var frozen));
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out uint pipeline, out uint root));
        Assert.Equal(1U, pipeline); Assert.Equal(2U, root);
    }
    private static PostFrameTapeInvocation Arm(GraphicsPostFrameCommandTape tape, object device, int repetition = 0) =>
        tape.Arm(device, Session, Environment.CurrentManagedThreadId, repetition);
    private static void Bind(GraphicsPostFrameCommandTape tape, PostFrameTapeInvocation token, bool indexed = true)
    {
        tape.NativeReturned(token, PostFrameTapeBinding.Pipeline, 0, 1);
        tape.NativeReturned(token, PostFrameTapeBinding.RootSignature, 0, 2);
        tape.NativeReturned(token, PostFrameTapeBinding.Topology, 0, 4);
        tape.Viewport(token, 0, 0, 0x42800000, 0x42800000, 0, 0x3f800000);
        tape.NativeReturned(token, PostFrameTapeBinding.Scissor, 0, 0, 0, 0, 64, 64);
        uint target = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 1, 0, 0, 0, 4096, 0);
        tape.NativeReturned(token, PostFrameTapeBinding.RenderTarget, 0, target);
        if (indexed)
        {
            uint index = tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 64, 4);
            tape.NativeReturned(token, PostFrameTapeBinding.IndexBuffer, 0, index, 0, 64, 4);
        }
        tape.Pass(token, PostFrameTapePass.OpaqueWorld, true);
    }
    private static PostFrameTapeCompletion Seal(GraphicsPostFrameCommandTape tape, object device, PostFrameTapeInvocation token, uint slot = 0, ulong ordinal = 1)
    {
        var completion = tape.ObserveOriginalCompletion(token, Frame(slot, ordinal), true, true);
        Assert.True(tape.Seal(token, device, Session, Environment.CurrentManagedThreadId, completion));
        return completion;
    }
    private static void FinalizeModel(GraphicsPostFrameCommandTape tape, PostFrameTapeInvocation token, PostFrameTapeCompletion completion)
    {
        Assert.True(tape.PngClosed(token)); _ = tape.Materialize(token);
        Assert.True(tape.ConfirmEvidenceExported(token)); Assert.True(tape.FinalizePureModel(token, completion));
    }
    private static uint BindArena(GraphicsPostFrameCommandTape tape, PostFrameTapeInvocation token, uint parameter, uint slot, ulong epoch, ulong offset)
    {
        uint id = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.ImplicitArena, epoch, slot, 0, 0, 1024 * 1024, 4);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.BufferedVertexConstant, PostFrameTapeBinding.RootParameter,
            0, id, offset, 256, epoch, slot);
        if (parameter != 0) tape.NativeReturned(token, PostFrameTapeBinding.RootParameter, parameter, id, offset, 256, epoch, slot);
        return id;
    }

    [Fact]
    public void Exact64ByteUnionOffsetsAndEveryOwnedPrimitiveHasNoManagedReferences()
    {
        Assert.Equal(64, Unsafe.SizeOf<PostFrameTapeEvent>());
        Assert.Equal(64, Unsafe.SizeOf<PostFrameTapeIdentity>());
        Assert.Equal(64, Unsafe.SizeOf<PostFrameTapeCatalogIndex>());
        Assert.Equal(48, Unsafe.SizeOf<PostFrameTapeEffectiveValue>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PostFrameTapeEvent>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PostFrameTapeIdentity>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PostFrameTapeCatalogIndex>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PostFrameTapeEffectiveValue>());
        Assert.Equal(0, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.Sequence)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.Opcode)).ToInt32());
        Assert.Equal(6, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.Provenance)).ToInt32());
        Assert.Equal(7, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.Flags)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.PassEpoch)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.Id)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.SecondId)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.Auxiliary)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.A)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.B)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.C)).ToInt32());
        Assert.Equal(48, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.D)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<PostFrameTapeEvent>(nameof(PostFrameTapeEvent.E)).ToInt32());
    }

    [Fact]
    public void ProductionConstructorActualPinnedX64AllocationIncludesAllHeadersWithin32MiB()
    {
        Assert.Equal(8, IntPtr.Size);
        object device = new();
        using (var warm = Tape(device)) { _ = warm.OwnedArrayPayloadBytes; }
        // Warm the production constructor/array types once. Do not attribute runtime first-use to the ledger.
        using (var warm = new GraphicsPostFrameCommandTape(device, Session, Environment.CurrentManagedThreadId)) { }
        _ = PostFrameTapeSourceClosure.Table.Length;
        _ = PostFrameTapeSourceClosure.CreateTable();
        long tableBefore = GC.GetAllocatedBytesForCurrentThread();
        var table = PostFrameTapeSourceClosure.CreateTable();
        long tableAllocation = GC.GetAllocatedBytesForCurrentThread() - tableBefore;
        long before = GC.GetAllocatedBytesForCurrentThread();
        using var tape = new GraphicsPostFrameCommandTape(device, Session, Environment.CurrentManagedThreadId);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(GraphicsPostFrameCommandTape.NamedPayloadBytes, tape.OwnedArrayPayloadBytes);
        Assert.InRange(allocated + tableAllocation, tape.OwnedArrayPayloadBytes, GraphicsPostFrameCommandTape.SelectedLedgerBytes);
        GC.KeepAlive(table);
        string line = $"payload={tape.OwnedArrayPayloadBytes};actualConstructor={allocated};headersAndOwner={allocated - tape.OwnedArrayPayloadBytes};sourceClosureSetup={tableAllocation};selectedTotal={allocated + tableAllocation};ledger={GraphicsPostFrameCommandTape.SelectedLedgerBytes};pointerBytes={IntPtr.Size}";
        Console.WriteLine(line);
        string? directory = Environment.GetEnvironmentVariable("FORGELINE_TAPE_CONTRACT_EVIDENCE");
        if (directory is not null)
        {
            using var output = new FileStream(Path.Combine(directory, "allocation-proof.txt"), FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(output);
            writer.WriteLine(line);
        }
    }

    [Fact]
    public void FrozenShaderAndFinalMetadataMemoryIsTheOnlyFutureFactorySource()
    {
        object device = new(); using var tape = Tape(device);
        byte[] vertex = [1, 2, 3], pixel = [4, 5]; var metadata = Metadata();
        byte[] semantics = [80, 79, 83, 73, 84, 73, 79, 78, 0];
        Assert.True(tape.TryFreezeCreation(metadata, vertex, pixel, out var frozen, semantics));
        byte[] consumedVertex = frozen.VertexShader.ToArray(), consumedMetadata = frozen.Metadata.ToArray();
        vertex[0] = 77; pixel[0] = 88; semantics[0] = 0; metadata[0] = metadata[0] with { A = 999 };
        Assert.Equal(new byte[] { 1, 2, 3 }, frozen.VertexShader.ToArray());
        Assert.Equal(new byte[] { 4, 5 }, frozen.PixelShader.ToArray());
        Assert.Equal(consumedVertex, frozen.VertexShader.ToArray());
        Assert.Equal(consumedMetadata, frozen.Metadata.ToArray());
        Assert.Equal((byte)80, frozen.SemanticNamesUtf8[0]);
        // No commit represents failed native creation: it cannot become a usable pipeline.
        var token = Arm(tape, device); Bind(tape, token);
        tape.Draw(token, true, 3, 1, 0, -2, 0);
        Assert.Equal(PostFrameTapeRefusal.MissingCatalog, tape.Refusal);
    }

    [Fact]
    public void CatalogForeignDuplicateLateAndArmedFactoryRefuseWithoutFreezingArmedInputs()
    {
        object device = new(); using var tape = Tape(device); using var foreign = Tape(new object());
        Assert.True(tape.TryFreezeCreation(Metadata(), [1], [2], out var frozen));
        Assert.False(foreign.CommitCreation(frozen, new object(), new object(), new object(), out _, out _));
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out _, out _));
        Assert.False(tape.CommitCreation(frozen, new object(), new object(), new object(), out _, out _));
        using var armed = Tape(device); var token = Arm(armed, device);
        Assert.False(armed.TryFreezeCreation(Metadata(), [1], [2], out _));
        Assert.Equal(PostFrameTapeRefusal.ArmedFactory, armed.Refusal);
        armed.Request(token, PostFrameTapeBinding.Pipeline, 0, 55);
        Assert.Equal(1, armed.EventCount);
        using var late = Tape(device); late.CloseAttachment();
        Assert.False(late.TryFreezeCreation(Metadata(), [1], [2], out _));
    }

    [Theory]
    [InlineData(0, 0U)]
    [InlineData(1, 0U)]
    [InlineData(10, 5U)]
    [InlineData(9, 3U)]
    [InlineData(5, 0U)]
    public void SimulatedProductionCacheASuppressedRequestCannotOverwriteIndependentNativeB(int bindingValue, uint slot)
    {
        var binding = (PostFrameTapeBinding)bindingValue;
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        tape.NativeReturned(token, binding, slot, 22, 1, 2, 3, 4);
        tape.Request(token, binding, slot, 11); tape.CacheHit(token, binding, slot);
        Assert.Equal(22UL, tape.EffectiveValue(binding, slot).Identity);
        Assert.Equal((uint)PostFrameTapeProvenance.CpuNativeCallReturn, tape.EffectiveValue(binding, slot).Provenance);
        tape.NativeFailed(token, binding, slot);
        tape.Request(token, binding, slot, 11); tape.CacheHit(token, binding, slot);
        Assert.Equal(0U, tape.EffectiveValue(binding, slot).Known);
        Assert.Equal(PostFrameTapeRefusal.None, tape.Refusal); // Unknown is allowed until required at an opaque draw.
    }

    [Fact]
    public void ResetUnknownRebindNonindexedWithoutIbAndExcludedPassUnknownAreHonest()
    {
        object device = new(); using var tape = Tape(device); Catalog(tape); var token = Arm(tape, device);
        Assert.Equal(0U, tape.EffectiveValue(PostFrameTapeBinding.Pipeline).Known);
        tape.Pass(token, PostFrameTapePass.Shadow, false);
        tape.InvalidateRoot(token);
        Bind(tape, token, indexed: false);
        tape.Pass(token, PostFrameTapePass.OpaqueWorld, true);
        tape.Draw(token, false, 3, 1, 0, 0, 0);
        Assert.Equal(1, tape.DrawCount); Assert.Equal(PostFrameTapeRefusal.None, tape.Refusal);
    }

    [Fact]
    public void RequiredUnknownAtOpaqueDrawRefusesAndRootInvalidationPreservesIa()
    {
        object device = new(); using var tape = Tape(device); Catalog(tape); var token = Arm(tape, device);
        tape.NativeReturned(token, PostFrameTapeBinding.VertexBuffer, 31, 200, 12, 1024, 48);
        tape.NativeReturned(token, PostFrameTapeBinding.IndexBuffer, 0, 201, 0, 256, 4);
        tape.InvalidateRoot(token);
        Assert.Equal(200UL, tape.EffectiveValue(PostFrameTapeBinding.VertexBuffer, 31).Identity);
        Assert.Equal(201UL, tape.EffectiveValue(PostFrameTapeBinding.IndexBuffer).Identity);
        tape.CacheHit(token, PostFrameTapeBinding.Pipeline, 0);
        tape.Pass(token, PostFrameTapePass.OpaqueWorld, true);
        tape.Draw(token, true, 3, 1, 0, 0, 0);
        Assert.Equal(PostFrameTapeRefusal.UnknownEffectiveState, tape.Refusal);
    }

    [Fact]
    public void RequiredCatalogMasksDistinguishUnknownAndKnownNullAndRequireActualRebinds()
    {
        object device = new(); using var tape = Tape(device);
        PostFrameCreationValue[] metadata =
        [
            new(PostFrameCreationField.RootFlags, 0, 0, 0, 0, 0),
            new(PostFrameCreationField.RootParameter, 0, (uint)RootParameterType.DescriptorTable, 0, 0, 0),
            new(PostFrameCreationField.DescriptorRange, 0, 0, 1UL | (15UL << 32), 0, 0),
            new(PostFrameCreationField.RootParameter, 18, (uint)RootParameterType.Constant32Bits, 0, 0, 0),
            new(PostFrameCreationField.RootConstants, 18, 0, 1, 0, 0)
        ];
        Assert.True(tape.TryFreezeCreation(metadata, [1], [2], out var frozen));
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out _, out _, new(1U << 31, 1 << 15, 1U << 18, false)));
        var token = Arm(tape, device); Bind(tape, token);
        uint vertex = tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 112, 112);
        uint heap = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.DescriptorHeap, 1, 0, 0, 0, 0, 0);
        uint texture = tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Texture, 1, 0, 17, 0, 64, 0);
        tape.NativeReturned(token, PostFrameTapeBinding.VertexBuffer, 31, vertex, 0, 112, 112);
        tape.NativeReturned(token, PostFrameTapeBinding.DescriptorHeap, 0, heap);
        tape.NativeReturned(token, PostFrameTapeBinding.TextureTable, 15, texture, 17, 0, heap);
        tape.RootWords(token, 18, [1], false);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.InitialTargets, PostFrameTapeBinding.DepthTarget, 0, 0); // explicit known-null; no invented unknown
        tape.Draw(token, true, 3, 1, 0, 0, 0);
        Assert.Equal(1, tape.DrawCount);
        tape.InvalidateRoot(token);
        tape.NativeReturned(token, PostFrameTapeBinding.Pipeline, 0, 1);
        tape.NativeReturned(token, PostFrameTapeBinding.RootSignature, 0, 2);
        tape.CacheHit(token, PostFrameTapeBinding.TextureTable, 15);
        tape.Draw(token, true, 3, 1, 0, 0, 0);
        Assert.Equal(PostFrameTapeRefusal.UnknownEffectiveState, tape.Refusal);
        Assert.Equal(1, tape.DrawCount);
    }

    [Fact]
    public void RootIeeeWordsAndImplicitArenaAssociationAreExactAndNotPaddingContent()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        uint[] words = [0x80000000, 0x7fc00001, 0x3f800000];
        uint arena = BindArena(tape, token, 0, 2, 17, 256);
        tape.RootWords(token, 0, words, true, 2, 17, 256, arena);
        words[0] = 123;
        var root = tape.RecordedEvents.ToArray().Single(row => row.Opcode == PostFrameTapeOpcode.RootWords);
        Assert.Equal(256UL, root.D); Assert.Equal(17UL, root.C); Assert.Equal(2U, root.SecondId);
        Assert.Equal(arena, root.Auxiliary);
        Seal(tape, device, token); Assert.True(tape.PngClosed(token)); var wire = tape.Materialize(token);
        Assert.Equal(new byte[] { 0, 0, 0, 128, 1, 0, 192, 127, 0, 0, 128, 63 }, Convert.FromBase64String(wire.RootLittleEndianBase64));
        Assert.Equal(12, wire.RootByteCount); Assert.Equal(3, wire.RootWordCount);
        Assert.False(wire.Complete); Assert.Contains("unhooked", wire.EvidenceScope);
    }

    [Fact]
    public void SessionIdentityReferencesPreserveReplacementAcrossRepetitionsAndClearOnTerminalDispose()
    {
        object device = new(), resource = new(), lifetime = new(); using var tape = Tape(device);
        var first = Arm(tape, device);
        Assert.Equal(1U, tape.ObserveIdentity(first, resource, lifetime, PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 64, 48));
        var firstCompletion = Seal(tape, device, first); FinalizeModel(tape, first, firstCompletion);
        var next = Arm(tape, device, 1);
        Assert.Equal(1U, tape.ObserveIdentity(next, resource, lifetime, PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 64, 48));
        Assert.Equal(2U, tape.ObserveIdentity(next, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 64, 48));
        Assert.Equal(3U, tape.ObserveIdentity(next, new object(), null, PostFrameTapeIdentityKind.ImplicitArena, 1, 0, 3, 0, 256, 4));
        Assert.Equal(0U, tape.ObserveIdentity(next, resource, new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 64, 48));
        Assert.Equal(PostFrameTapeRefusal.InvalidTransition, tape.Refusal);
        tape.Dispose(); Assert.False(tape.HasWitnessReferences);
    }

    [Fact]
    public void OpaqueTokensRejectForeignOwnerSessionOldRepetitionAndStaleExceptionPrefix()
    {
        object device = new(); using var tape = Tape(device); using var other = Tape(new object());
        var first = Arm(tape, device); var firstCompletion = Seal(tape, device, first);
        Exception reused = new IOException("same source exception"); tape.AttachFailure(reused, first);
        Assert.True(tape.HasCurrentFailure(reused, first));
        FinalizeModel(tape, first, firstCompletion);
        var second = Arm(tape, device, 1);
        Assert.False(tape.HasCurrentFailure(reused, first)); Assert.False(tape.HasCurrentFailure(reused, second));
        var secondCompletion = tape.ObserveOriginalCompletion(second, Frame(2, 2), true, true);
        Assert.False(tape.Seal(second, new object(), Session, Environment.CurrentManagedThreadId, secondCompletion));
        Assert.False(tape.Seal(second, device, Guid.NewGuid(), Environment.CurrentManagedThreadId, secondCompletion));
        Assert.False(tape.Seal(second, device, Session, Environment.CurrentManagedThreadId + 1, secondCompletion));
        Assert.False(tape.Seal(first, device, Session, Environment.CurrentManagedThreadId, secondCompletion));
        Assert.True(tape.Seal(second, device, Session, Environment.CurrentManagedThreadId, secondCompletion)); tape.AttachFailure(reused, second);
        Assert.True(tape.HasCurrentFailure(reused, second)); Assert.False(other.HasCurrentFailure(reused, second));
        Assert.False(tape.HasCurrentFailure(reused, default));
    }

    [Fact]
    public void SameExceptionCannotBorrowAnotherTapePrefixEvenAtSameSlotAndActualForeignThreadCannotSeal()
    {
        object device = new(); using var first = Tape(device); using var second = Tape(device);
        var a = Arm(first, device); var b = Arm(second, device);
        var bCompletion = second.ObserveOriginalCompletion(b, Frame(), true, true);
        int owner = Environment.CurrentManagedThreadId;
        bool accepted = true;
        var foreignThread = new Thread(() => accepted = second.Seal(b, device, Session, owner, bCompletion));
        foreignThread.Start(); foreignThread.Join();
        Assert.False(accepted);
        Seal(first, device, a); Assert.True(second.Seal(b, device, Session, owner, bCompletion));
        var reused = new IOException("reused source error"); first.AttachFailure(reused, a);
        Assert.True(first.HasCurrentFailure(reused, a));
        second.AttachFailure(reused, b);
        Assert.True(second.HasCurrentFailure(reused, b)); Assert.False(first.HasCurrentFailure(reused, a));
        Assert.False(second.PngClosed(a)); Assert.True(second.PngClosed(b));
    }

    [Fact]
    public void SimulatedArmedFactoryRefusalStillDelegatesOriginalFactoryExactlyOnce()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        int calls = 0; byte[] callerShader = [1, 2, 3];
        bool frozen = tape.TryFreezeCreation(Metadata(), callerShader, callerShader, out _);
        // This models the future hook's required branch; it does not call the real D3D12 factory.
        calls++; Assert.Equal(new byte[] { 1, 2, 3 }, callerShader);
        Assert.False(frozen); Assert.Equal(1, calls); Assert.Equal(PostFrameTapeRefusal.ArmedFactory, tape.Refusal);
        tape.Draw(token, false, 3, 1, 0, 0, 0); Assert.Equal(0, tape.DrawCount);
    }

    [Theory]
    [InlineData(false, true, (int)PostFrameTapeRefusal.OriginalFailure)]
    [InlineData(true, false, (int)PostFrameTapeRefusal.CleanupFailure)]
    public void FailedBodyOrCleanupNeverInventsFrameOrPng(bool body, bool cleanup, int reason)
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        var completion = tape.ObserveOriginalCompletion(token, Frame(), body, cleanup);
        Assert.False(tape.Seal(token, device, Session, Environment.CurrentManagedThreadId, completion));
        Assert.Equal((PostFrameTapeRefusal)reason, tape.Refusal); Assert.False(tape.PngClosed(token));
        Assert.Throws<InvalidOperationException>(() => tape.Materialize(token));
    }

    [Fact]
    public void EveryCapacityUsesTinyLimitsAndFirstRefusalIsSticky()
    {
        object device = new();
        using var events = Tape(device, Tiny(events: 1)); var e = Arm(events, device);
        events.CacheHit(e, PostFrameTapeBinding.Pipeline, 0); events.RootWords(e, 0, [1], false);
        Assert.Equal(PostFrameTapeRefusal.EventCapacity, events.Refusal); Assert.Equal(1, events.EventCount);
        using var roots = Tape(device, Tiny(words: 1)); var r = Arm(roots, device);
        roots.RootWords(r, 0, [1, 2], false); Assert.Equal(PostFrameTapeRefusal.RootCapacity, roots.Refusal);
        using var identities = Tape(device, Tiny(identities: 1)); var i = Arm(identities, device);
        identities.ObserveIdentity(i, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 1, 1);
        identities.ObserveIdentity(i, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 1, 1);
        Assert.Equal(PostFrameTapeRefusal.IdentityCapacity, identities.Refusal);
        using var catalog = Tape(device, Tiny(catalog: 1));
        Assert.False(catalog.TryFreezeCreation(Metadata(), [1], [2], out _)); Assert.Equal(PostFrameTapeRefusal.CatalogCapacity, catalog.Refusal);
        using var draws = Tape(device, Tiny(draws: 1)); Catalog(draws); var d = Arm(draws, device); Bind(draws, d);
        draws.Draw(d, true, 3, 1, 0, 0, 0); draws.Draw(d, true, 3, 1, 0, 0, 0);
        Assert.Equal(PostFrameTapeRefusal.DrawCapacity, draws.Refusal); Assert.Equal(1, draws.DrawCount);
    }

    private static void AllocationBranch(GraphicsPostFrameCommandTape tape, PostFrameTapeInvocation token, object actual, object lifetime, ReadOnlySpan<uint> words)
    {
        tape.Request(token, PostFrameTapeBinding.Pipeline, 0, 1);
        tape.CacheHit(token, PostFrameTapeBinding.Pipeline, 0);
        tape.NativeReturned(token, PostFrameTapeBinding.VertexBuffer, 0, 3, 0, 128, 48);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.InitialTargets, PostFrameTapeBinding.DepthTarget, 0, 4);
        tape.NativeFailed(token, PostFrameTapeBinding.RootSignature, 0);
        tape.InvalidateRoot(token);
        tape.Viewport(token, 0, 0, 1, 1, 0, 1);
        uint arena = tape.ObserveIdentity(token, actual, null, PostFrameTapeIdentityKind.ImplicitArena, 1, 0, 0, 0, 1024, 4);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.BufferedVertexConstant,
            PostFrameTapeBinding.RootParameter, 0, arena, 256, 256, 1, 0);
        tape.RootWords(token, 0, words, true, 0, 1, 256, arena);
        tape.Pass(token, PostFrameTapePass.Shadow, true);
    }

    [Fact]
    public void ArmedSuccessUnknownForeignAndMidFrameOverflowBranchesAllocateExactlyZero()
    {
        object device = new(), actual = new(), lifetime = new(); uint[] words = [1, 2];
        using (var warm = Tape(device)) { var t = Arm(warm, device); AllocationBranch(warm, t, actual, lifetime, words); }
        using var tape = Tape(device, Tiny(events: 16)); var token = Arm(tape, device);
        long before = GC.GetAllocatedBytesForCurrentThread();
        AllocationBranch(tape, token, actual, lifetime, words);
        AllocationBranch(tape, token, actual, lifetime, words); // mid-frame capacity
        tape.Pass(token, PostFrameTapePass.Overlay, false); // sticky refusal, no error allocation
        tape.Request(default, PostFrameTapeBinding.Pipeline, 0, 1);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, delta); Assert.Equal(PostFrameTapeRefusal.EventCapacity, tape.Refusal);
        using var foreign = Tape(device); var current = Arm(foreign, device);
        before = GC.GetAllocatedBytesForCurrentThread();
        foreign.Request(default, PostFrameTapeBinding.Pipeline, 0, 1);
        foreign.Draw(current, true, 1, 1, 0, 0, 0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(PostFrameTapeRefusal.ForeignInvocation, foreign.Refusal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ArmedRefusalAndSuccessMatrixHasNoManagedAllocation(int branch)
    {
        object device = new(), resource = new(), lifetime = new();
        var metadata = Metadata(); byte[] shader = [1]; uint[] words = [1, 2];
        using var tape = Tape(device, Tiny(events: branch == 0 ? 1 : 128, words: branch == 1 ? 1 : 128,
            identities: branch == 2 ? 4 : 16, draws: 1));
        Catalog(tape); var token = Arm(tape, device); Bind(tape, token);
        // Prior warm-up tests cover shared methods; branch-specific JIT itself is not a managed tape allocation.
        long before = GC.GetAllocatedBytesForCurrentThread();
        switch (branch)
        {
            case 0: tape.Request(token, PostFrameTapeBinding.Pipeline, 0, 1); break;
            case 1: tape.RootWords(token, 0, words, false); break;
            case 2: tape.ObserveIdentity(token, resource, lifetime, PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 8, 4); break;
            case 3: tape.Draw(token, true, 3, 1, 0, 0, 0); tape.Draw(token, true, 3, 1, 0, 0, 0); break;
            case 4: tape.TryFreezeCreation(metadata, shader, shader, out _); break;
            case 5: tape.InvalidateRoot(token); tape.Draw(token, true, 3, 1, 0, 0, 0); break;
            case 6: tape.Pass(token, PostFrameTapePass.OpaqueWorld, false); tape.Draw(token, true, 3, 1, 0, 0, 0); break;
            case 7: tape.Request(default, PostFrameTapeBinding.Pipeline, 0, 1); break;
            case 8: tape.Draw(token, true, 3, 1, 0, -1, 0); break;
        }
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, delta);
        Assert.Equal(branch == 8 ? PostFrameTapeRefusal.None : branch switch
        {
            0 => PostFrameTapeRefusal.EventCapacity, 1 => PostFrameTapeRefusal.RootCapacity,
            2 => PostFrameTapeRefusal.IdentityCapacity, 3 => PostFrameTapeRefusal.DrawCapacity,
            4 => PostFrameTapeRefusal.ArmedFactory, 7 => PostFrameTapeRefusal.ForeignInvocation,
            _ => PostFrameTapeRefusal.UnknownEffectiveState
        }, tape.Refusal);
    }

    private sealed record SimulatedResult(string[] Calls, byte[] LegacyBytes, bool Png, bool LateCopy, bool Stopped);
    private static SimulatedResult Simulate(int mode, bool failOriginal = false, int eventCapacity = 2)
    {
        object device = new(); using var tape = mode >= 2 ? Tape(device, Tiny(events: mode == 3 ? eventCapacity : 128)) : null;
        if (tape is not null) Catalog(tape);
        var token = tape is null ? default : Arm(tape, device);
        var calls = new List<string>(); var legacy = new List<byte>();
        uint vertex = tape?.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 128, 48) ?? 0;
        uint index = tape?.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 64, 4) ?? 0;
        uint heap = tape?.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.DescriptorHeap, 1, 0, 0, 0, 0, 0) ?? 0;
        uint texture = tape?.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Texture, 1, 0, 3, 0, 64, 0) ?? 0;
        uint target = tape?.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 1, 0, 0, 0, 4096, 0) ?? 0;
        // Native/source spy: observers are adjacent to calls, never the authority for delegation.
        try
        {
            calls.Add("factory-setup-already-returned");
            tape?.Pass(token, PostFrameTapePass.Shadow, true); calls.Add("begin-shadow");
            tape?.InvalidateRoot(token); calls.Add("end-shadow");
            tape?.Request(token, PostFrameTapeBinding.Pipeline, 0, 1); calls.Add("set-root:2");
            tape?.NativeReturned(token, PostFrameTapeBinding.RootSignature, 0, 2);
            calls.Add("set-pso:1"); tape?.NativeReturned(token, PostFrameTapeBinding.Pipeline, 0, 1);
            tape?.CacheHit(token, PostFrameTapeBinding.Pipeline, 0); // no native call on suppressed repeat
            tape?.NativeReturned(token, PostFrameTapeBinding.Topology, 0, 4); calls.Add("topology:triangle");
            tape?.NativeReturned(token, PostFrameTapeBinding.VertexBuffer, 0, vertex, 0, 128, 48); calls.Add("vb:10:0:128:48");
            tape?.NativeReturned(token, PostFrameTapeBinding.IndexBuffer, 0, index, 0, 64, 4); calls.Add("ib:11:0:64:32");
            tape?.NativeReturned(token, PostFrameTapeBinding.DescriptorHeap, 0, heap); calls.Add("heap:12");
            tape?.NativeReturned(token, PostFrameTapeBinding.TextureTable, 0, texture, 3, 0, heap); calls.Add("table:0:13:3");
            tape?.Viewport(token, 0, 0, 0x42800000, 0x42800000, 0, 0x3f800000); calls.Add("viewport:0:0:64:64:0:1");
            tape?.NativeReturned(token, PostFrameTapeBinding.Scissor, 0, 0, 0, 0, 64, 64); calls.Add("scissor:0:0:64:64");
            tape?.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.InitialTargets, PostFrameTapeBinding.RenderTarget, 0, target); calls.Add("om:14");
            uint[] constants = [0x80000000, 0x3f800000];
            foreach (uint word in constants) legacy.AddRange(BitConverter.GetBytes(word));
            tape?.RootWords(token, 0, constants, false); calls.Add("constants:80000000:3f800000");
            tape?.Pass(token, PostFrameTapePass.OpaqueWorld, true);
            tape?.Draw(token, true, 6, 2, 3, -4, 5); calls.Add("draw-indexed:6:2:3:-4:5"); // one delegated call
            if (mode == 2) Assert.Equal(PostFrameTapeRefusal.None, tape!.Refusal);
            tape?.Pass(token, PostFrameTapePass.Particle, true); calls.Add("particles");
            tape?.Pass(token, PostFrameTapePass.Composition, true); calls.Add("composition");
            tape?.Pass(token, PostFrameTapePass.Overlay, true); calls.Add("overlay");
            if (failOriginal) throw new IOException("original simulated source failure");
            calls.Add("cpu-readback");
        }
        finally { calls.Add("original-cleanup"); }
        if (tape is not null) Seal(tape, device, token);
        calls.Add("png-closed");
        if (tape is not null)
        {
            Assert.True(tape.PngClosed(token)); _ = tape.Materialize(token);
        }
        // No modeled late/native copy is performed. Refused observer is discovered only after PNG.
        return new(calls.ToArray(), legacy.ToArray(), true, false, true);
    }

    [Fact]
    public void SimulatedNullV1V2AndMidframeRefusalPreserveAllOriginalArgumentsOrderAndLegacyBytes()
    {
        var baseline = Simulate(0);
        foreach (int mode in new[] { 1, 2, 3 })
        {
            var observed = Simulate(mode);
            Assert.Equal(baseline.Calls, observed.Calls); Assert.Equal(baseline.LegacyBytes, observed.LegacyBytes);
            Assert.True(observed.Png); Assert.False(observed.LateCopy); Assert.True(observed.Stopped);
        }
        for (int capacity = 1; capacity < 24; capacity++)
        {
            var observed = Simulate(3, eventCapacity: capacity);
            Assert.Equal(baseline.Calls, observed.Calls); Assert.Equal(baseline.LegacyBytes, observed.LegacyBytes);
        }
        Assert.True(Array.IndexOf(baseline.Calls, "cpu-readback") < Array.IndexOf(baseline.Calls, "original-cleanup"));
        Assert.True(Array.IndexOf(baseline.Calls, "original-cleanup") < Array.IndexOf(baseline.Calls, "png-closed"));
    }

    [Fact]
    public void RealOriginalExceptionRemainsAuthoritativeAcrossLateReportingFailure()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        var original = new IOException("actual source exception object"); Exception? caught = null;
        try
        {
            try { throw original; }
            catch
            {
                var completion = tape.ObserveOriginalCompletion(token, default, false, false);
                tape.Seal(token, device, Session, Environment.CurrentManagedThreadId, completion);
                try { tape.AttachFailure(original, token); _ = tape.Materialize(token); }
                catch { }
                throw;
            }
        }
        catch (Exception error) { caught = error; }
        Assert.Same(original, caught); Assert.True(tape.HasCurrentFailure(original, token));
        Assert.False(tape.PngClosed(token));
    }

    [Fact]
    public void OwnerSourcesAndViewportRestorationSurviveTypedJsonWithoutUpgradingOrdinaryCalls()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.ShadowReturnTargets, PostFrameTapeBinding.RenderTarget, 0, 17);
        tape.NativeReturned(token, PostFrameTapeBinding.RenderTarget, 0, 18);
        tape.Viewport(token, 0, 0, 0x42800000, 0x42800000, 0, 0x3f800000, PostFrameTapeOwnerPostcondition.ShadowReturnTargets);
        tape.Viewport(token, 0, 0, 0x42800000, 0x42800000, 0, 0x3f800000);
        Seal(tape, device, token); Assert.True(tape.PngClosed(token));
        var wire = JsonSerializer.Deserialize<PostFrameTapeWire>(JsonSerializer.Serialize(tape.Materialize(token)))!;
        var restoredTarget = wire.Events.Single(row => row.Binding is not null && row.Opcode == PostFrameTapeOpcode.OwnerPostcondition);
        Assert.Equal(PostFrameTapeOwnerPostcondition.ShadowReturnTargets, restoredTarget.Binding!.OwnerSource);
        Assert.Equal(PostFrameTapeProvenance.SourceProvedOwnerPostcondition, restoredTarget.Provenance);
        var restoredViewport = wire.Events.Single(row => row.Viewport is not null && row.Opcode == PostFrameTapeOpcode.OwnerPostcondition);
        Assert.Equal(PostFrameTapeOwnerPostcondition.ShadowReturnTargets, restoredViewport.Viewport!.OwnerSource);
        Assert.Equal(PostFrameTapeProvenance.SourceProvedOwnerPostcondition, restoredViewport.Provenance);
        Assert.All(wire.Events.Where(row => row.Opcode == PostFrameTapeOpcode.NativeReturn), row =>
        {
            Assert.Null(row.Binding?.OwnerSource); Assert.Null(row.Viewport?.OwnerSource);
        });
    }

    [Fact]
    public void RearmCannotEraseAnyUnpublishedPrefixAndOldCompletionOrTupleCannotSealNewInvocation()
    {
        object device = new(); using var tape = Tape(device); var first = Arm(tape, device);
        int count = tape.EventCount;
        Assert.False(Arm(tape, device, 1).BelongsTo(tape, 2)); Assert.Equal(count, tape.EventCount);
        var completion = Seal(tape, device, first);
        Assert.False(Arm(tape, device, 1).BelongsTo(tape, 2));
        Assert.True(tape.PngClosed(first)); Assert.False(Arm(tape, device, 1).BelongsTo(tape, 2));
        _ = tape.Materialize(first); Assert.False(Arm(tape, device, 1).BelongsTo(tape, 2));
        Assert.True(tape.ConfirmEvidenceExported(first)); Assert.False(Arm(tape, device, 1).BelongsTo(tape, 2));
        Assert.True(tape.FinalizePureModel(first, completion)); Assert.False(tape.FinalizePureModel(first, completion));
        var next = Arm(tape, device, 1); Assert.True(next.BelongsTo(tape, 2));
        Assert.False(tape.Seal(next, device, Session, Environment.CurrentManagedThreadId, completion));
        var reusedTuple = tape.ObserveOriginalCompletion(next, Frame(2), true, true); // old submission/fence with a new slot is still old
        Assert.False(tape.Seal(next, device, Session, Environment.CurrentManagedThreadId, reusedTuple));
        Assert.Equal(PostFrameTapeRefusal.InvalidTransition, tape.Refusal); Assert.False(tape.PngClosed(next));
    }

    [Fact]
    public void DeviceRemovalCompletionSentinelNeverCreatesAnOriginalReceipt()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        var completion = tape.ObserveOriginalCompletion(token, Frame() with { CompletedFence = ulong.MaxValue }, true, true);
        Assert.False(tape.Seal(token, device, Session, Environment.CurrentManagedThreadId, completion));
        Assert.False(tape.PngClosed(token)); Assert.Equal(PostFrameTapeRefusal.InvalidTransition, tape.Refusal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExcludedSimulatedDrawsNeverBecomeOpaqueWitnesses(int passValue)
    {
        object device = new(); using var tape = Tape(device); Catalog(tape); var token = Arm(tape, device); Bind(tape, token);
        tape.Pass(token, (PostFrameTapePass)passValue, true);
        int before = tape.EventCount; int originalCalls = 0;
        tape.Draw(token, true, 3, 1, 0, 0, 0); originalCalls++;
        Assert.Equal(1, originalCalls); Assert.Equal(before, tape.EventCount); Assert.Equal(0, tape.DrawCount);
        Assert.Equal(PostFrameTapeRefusal.None, tape.Refusal);
    }

    [Fact]
    public void BufferedWordsAssociateActualArenaBAndCannotReplaceItWithSuppressedRequestedAOrSlot()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        uint b = BindArena(tape, token, 0, 2, 7, 256);
        uint a = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.ImplicitArena, 7, 2, 0, 0, 1024, 4);
        tape.Request(token, PostFrameTapeBinding.RootParameter, 0, a, 256, 256, 7, 2);
        tape.CacheHit(token, PostFrameTapeBinding.RootParameter, 0);
        Assert.Equal((ulong)b, tape.EffectiveValue(PostFrameTapeBinding.RootParameter).Identity);
        tape.RootWords(token, 0, [0x3f800000], true, 2, 7, 256, b);
        Assert.Equal((ulong)b, tape.EffectiveValue(PostFrameTapeBinding.RootParameter).Identity);
        tape.RootWords(token, 0, [0x40000000], true, 2, 7, 256, a);
        Assert.Equal(PostFrameTapeRefusal.InvalidTransition, tape.Refusal);
        Assert.Equal((ulong)b, tape.EffectiveValue(PostFrameTapeBinding.RootParameter).Identity);
        Seal(tape, device, token); Assert.True(tape.PngClosed(token)); var wire = tape.Materialize(token);
        var words = wire.Events.Single(row => row.RootWords is not null).RootWords!;
        Assert.Equal(b, words.ArenaIdentity); Assert.Equal(2U, words.ArenaSlot);
        Assert.Equal(PostFrameTapeOwnerPostcondition.BufferedVertexConstant, words.OwnerSource);
    }

    [Fact]
    public void BufferedWordsBeforeCbvRefuseButImmediateNativeConstantsCanEstablishState()
    {
        object device = new(); using var buffered = Tape(device); var token = Arm(buffered, device);
        uint arena = buffered.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.ImplicitArena, 1, 0, 0, 0, 1024, 4);
        buffered.RootWords(token, 0, [1], true, 0, 1, 256, arena);
        Assert.Equal(PostFrameTapeRefusal.InvalidTransition, buffered.Refusal);
        using var immediate = Tape(device); var current = Arm(immediate, device);
        immediate.RootWords(current, 0, [1], false);
        Assert.Equal(2U, immediate.EffectiveValue(PostFrameTapeBinding.RootParameter).Known);
        Assert.Equal(0UL, immediate.EffectiveValue(PostFrameTapeBinding.RootParameter).Identity);
        Assert.Equal(PostFrameTapeRefusal.None, immediate.Refusal);
    }

    [Fact]
    public void ForeignThreadArmRecordRefuseAndDisposeNeverMutateOwnerFields()
    {
        object device = new(); using var tape = Tape(device); var token = Arm(tape, device);
        int owner = Environment.CurrentManagedThreadId; int count = tape.EventCount;
        var thread = new Thread(() =>
        {
            tape.Arm(device, Session, owner, 1);
            tape.Request(token, PostFrameTapeBinding.Pipeline, 0, 1);
            tape.Refuse(PostFrameTapeRefusal.EventCapacity); tape.CloseAttachment(); tape.Dispose();
        });
        thread.Start(); thread.Join();
        Assert.True(tape.Armed); Assert.Equal(count, tape.EventCount); Assert.Equal(PostFrameTapeRefusal.None, tape.Refusal);
        tape.Request(token, PostFrameTapeBinding.Pipeline, 0, 1); Assert.Equal(count + 1, tape.EventCount);
    }

    [Fact]
    public void FabricatedResourceIdAndWrongKindCannotSatisfyKnownRequiredDrawBindings()
    {
        object device = new(); using var missing = Tape(device); Catalog(missing); var a = Arm(missing, device); Bind(missing, a);
        missing.NativeReturned(a, PostFrameTapeBinding.IndexBuffer, 0, 999, 0, 64, 4);
        missing.Draw(a, true, 3, 1, 0, 0, 0);
        Assert.Equal(PostFrameTapeRefusal.InvalidTransition, missing.Refusal);
        using var wrong = Tape(device); Catalog(wrong); var b = Arm(wrong, device); Bind(wrong, b);
        wrong.NativeReturned(b, PostFrameTapeBinding.RenderTarget, 0, 1); // actual catalog pipeline, not a target
        wrong.Draw(b, true, 3, 1, 0, 0, 0);
        Assert.Equal(PostFrameTapeRefusal.InvalidTransition, wrong.Refusal);
    }

    [Fact]
    public void TwoStageActualNativeDtoEncodingDerivesMasksAndNativeSpyConsumesSameFrozenShaderMemory()
    {
        object device = new(); using var tape = Tape(device, Tiny(catalog: 16384, identities: 64));
        byte[] callerVertex = [1, 2, 3], callerPixel = [4, 5];
        var privateRoot = TerrainRoot();
        Assert.True(tape.TryBeginCreation(out var builder));
        Assert.True(PostFrameNativeCreationEncoder.Root(builder, privateRoot, PostFrameTapeCreationRole.DirectionalShadowReceiver,
            out uint roots, out ushort assets, out ushort retained));
        Assert.Equal((1U << 0) | (1U << 14), roots); Assert.Equal((ushort)0x1fff, assets); Assert.Equal((ushort)0x8000, retained);
        byte[] frozenRootBytes = tape.CreationMetadata(builder.Index).ToArray();
        int nativeRootSpyCalls = 0, nativePsoSpyCalls = 0;
        // Simulated native-call boundary: consumes SAME private graph before later PSO conversion.
        nativeRootSpyCalls++; Assert.Equal(16, privateRoot.Parameters!.Length);
        var callerElements = new[] { new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0) };
        var privateElements = callerElements.ToArray();
        var pso = NativePso(callerVertex, callerPixel, privateElements);
        Assert.True(PostFrameNativeCreationEncoder.Pipeline(builder, pso, out uint vertices)); Assert.Equal(1U, vertices);
        Assert.True(tape.FinishCreation(builder, privateElements, pso.VertexShader.Span, pso.PixelShader.Span, out var frozen));
        pso.VertexShader = frozen.VertexShaderMemory; pso.PixelShader = frozen.PixelShaderMemory;
        callerVertex[0] = 99; callerPixel[0] = 88; callerElements[0] = new("CHANGED", 3, Format.R32G32_Float, 4, 1);
        nativePsoSpyCalls++;
        var consumedLayout = pso.InputLayout!;
        Assert.Same(privateElements, consumedLayout.Elements);
        Assert.Equal("POSITION", consumedLayout.Elements![0].SemanticName);
        Assert.Equal(new byte[] { 1, 2, 3 }, pso.VertexShader.ToArray()); Assert.Equal(new byte[] { 4, 5 }, pso.PixelShader.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(frozen.VertexShaderMemory, out var stored));
        Assert.True(MemoryMarshal.TryGetArray(pso.VertexShader, out var consumed)); Assert.Same(stored.Array, consumed.Array);
        Assert.Equal(stored.Offset, consumed.Offset); Assert.Equal(stored.Count, consumed.Count);
        Assert.Equal(frozenRootBytes, frozen.Metadata[..frozenRootBytes.Length].ToArray());
        Assert.Equal(1, nativeRootSpyCalls); Assert.Equal(1, nativePsoSpyCalls);
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out _, out _,
            new(vertices, assets, roots, pso.DepthStencilState.DepthEnable || pso.DepthStencilState.StencilEnable, retained),
            PostFrameTapeCreationRole.DirectionalShadowReceiver));
        var token = Arm(tape, device); Seal(tape, device, token); Assert.True(tape.PngClosed(token));
        var creation = tape.Materialize(token).Creations.Single();
        Assert.Equal((ushort)0x8000, creation.RequiredState.RetainedTargetTextureSlots);
        Assert.Equal(PostFrameTapeCreationRole.DirectionalShadowReceiver, creation.CreationRole);
        Assert.Contains(creation.Metadata, row => row.Field == PostFrameCreationField.InputElement);
        Assert.Contains(creation.Metadata, row => row.Field == PostFrameCreationField.BlendTarget && row.Index == 15);
        Assert.Contains(creation.Metadata, row => row.Field == PostFrameCreationField.CachedPipelineState && row.A == 0);
    }

    private static RootSignatureDescription1 TerrainRoot()
    {
        var parameters = new List<RootParameter1>
        {
            new(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.Vertex)
        };
        for (uint slot = 0; slot < 13; slot++)
            parameters.Add(new(new RootDescriptorTable1(new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, slot)), ShaderVisibility.Pixel));
        parameters.Add(new(RootParameterType.ConstantBufferView, new RootDescriptor1(1, 0), ShaderVisibility.Pixel));
        parameters.Add(new(new RootDescriptorTable1(new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 15)), ShaderVisibility.Pixel));
        return new(RootSignatureFlags.AllowInputAssemblerInputLayout, parameters.ToArray(),
            [new StaticSamplerDescription(0, Filter.MinMagMipLinear, TextureAddressMode.Wrap, TextureAddressMode.Wrap, TextureAddressMode.Wrap)]);
    }

    private static GraphicsPipelineStateDescription NativePso(byte[] vertex, byte[] pixel, InputElementDescription[] elements) => new()
    {
        VertexShader = vertex, PixelShader = pixel, InputLayout = new(elements),
        BlendState = BlendDescription.Opaque, RasterizerState = RasterizerDescription.CullNone,
        DepthStencilState = DepthStencilDescription.Default, SampleMask = uint.MaxValue,
        PrimitiveTopologyType = PrimitiveTopologyType.Triangle, RenderTargetFormats = [Format.R16G16B16A16_Float],
        DepthStencilFormat = Format.D32_Float, SampleDescription = SampleDescription.Default
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ActualRootDerivedImplicitTargetMaskRequiresCorrectResourceHeapDescriptorAndView(int fault)
    {
        object device = new(); using var tape = Tape(device, Tiny(catalog: 16384, identities: 64));
        Assert.True(tape.TryBeginCreation(out var builder));
        Assert.True(PostFrameNativeCreationEncoder.Root(builder, TerrainRoot(), PostFrameTapeCreationRole.DirectionalShadowReceiver,
            out uint roots, out ushort assets, out ushort retained));
        var elements = new[] { new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0) };
        var pso = NativePso([1], [2], elements);
        Assert.True(PostFrameNativeCreationEncoder.Pipeline(builder, pso, out uint vertices));
        Assert.True(tape.FinishCreation(builder, elements, pso.VertexShader.Span, pso.PixelShader.Span, out var frozen));
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out _, out _,
            new(vertices, assets, roots, true, retained), PostFrameTapeCreationRole.DirectionalShadowReceiver));
        var token = Arm(tape, device); Bind(tape, token);
        uint vb = tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 0, 0, 128, 0);
        tape.NativeReturned(token, PostFrameTapeBinding.VertexBuffer, 0, vb, 0, 128, 48);
        uint arena = BindArena(tape, token, 0, 0, 1, 256);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit, PostFrameTapeBinding.RootParameter, 14, arena, 0, 256, 1, 0);
        uint[] shadowWords = new uint[24];
        tape.RootWords(token, 14, shadowWords, true, 0, 1, 0, arena, PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit);
        uint heap = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.DescriptorHeap, 1, 0, 0, 0, 0, 0);
        tape.NativeReturned(token, PostFrameTapeBinding.DescriptorHeap, 0, heap);
        for (uint slot = 0; slot < 13; slot++)
        {
            uint texture = tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Texture, 1, 0, 100 + slot, 4UL | (4UL << 32), 0, 1);
            tape.NativeReturned(token, PostFrameTapeBinding.TextureTable, slot, texture, 100 + slot,
                fault == 5 && slot == 0 ? 0 : slot + 1, heap, 0);
        }
        uint depth = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 1, 0, 0, 0, 0, 0);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.InitialTargets, PostFrameTapeBinding.DepthTarget, 0, depth);
        if (fault != 1)
        {
            uint shadow = fault == 2 ? tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Texture, 1, 0, 500, 0, 0, 1) :
                tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 1, 0, 500, 0, 0, 0);
            uint requestedHeap = fault == 3 ? tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.DescriptorHeap, 1, 0, 0, 0, 0, 0) : heap;
            tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit, PostFrameTapeBinding.TextureTable, 15, shadow, 500,
                fault == 4 ? 14UL : 15UL, requestedHeap, 1);
        }
        int originalCalls = 0; tape.Draw(token, true, 3, 1, 0, 0, 0); originalCalls++;
        Assert.Equal(1, originalCalls);
        Assert.Equal(fault == 0 ? PostFrameTapeRefusal.None : PostFrameTapeRefusal.UnknownEffectiveState, tape.Refusal);
        Assert.Equal(fault == 0 ? 1 : 0, tape.DrawCount);
    }

    [Theory]
    [InlineData("POSITION", true)]
    [InlineData("TEXCOORD7", true)]
    [InlineData("PÖSITION", false)]
    [InlineData("POS\0ITION", false)]
    [InlineData("", false)]
    public void ActualEncoderRefusesUnsupportedAnsiSemanticDomainWithoutChangingOriginalInputs(string semantic, bool supported)
    {
        object device = new(); using var tape = Tape(device, Tiny(catalog: 16384));
        Assert.True(tape.TryBeginCreation(out var builder));
        var elements = new[] { new InputElementDescription(semantic, 0, Format.R32G32B32_Float, 0, 0) };
        var pso = NativePso([1], [2], elements);
        Assert.Equal(supported, PostFrameNativeCreationEncoder.SemanticNameSupported(semantic));
        bool encoded = PostFrameNativeCreationEncoder.Pipeline(builder, pso, out _);
        Assert.Equal(supported, encoded);
        if (!encoded) tape.Refuse(PostFrameTapeRefusal.InvalidCatalog);
        int simulatedOriginalCalls = 0;
        // Production source still delegates the original native factory after refusal. This spy
        // only proves that the managed encoder did not mutate the exact original descriptor.
        simulatedOriginalCalls++; Assert.Equal(semantic, pso.InputLayout!.Elements![0].SemanticName);
        Assert.Equal(new byte[] { 1 }, pso.VertexShader.ToArray()); Assert.Equal(new byte[] { 2 }, pso.PixelShader.ToArray());
        Assert.Equal(1, simulatedOriginalCalls);
        Assert.Equal(supported ? PostFrameTapeRefusal.None : PostFrameTapeRefusal.InvalidCatalog, tape.Refusal);
        if (!supported)
        {
            using var direct = Tape(new object()); Assert.True(direct.TryBeginCreation(out var alternate));
            Assert.False(direct.FinishCreation(alternate, elements, [1], [2], out _));
            Assert.Equal(PostFrameTapeRefusal.InvalidCatalog, direct.Refusal);
        }
    }

    [Theory]
    [InlineData("effective-eight-requested-four", false)]
    [InlineData("words-cannot-satisfy-cbv", false)]
    [InlineData("full-eight", true)]
    [InlineData("short-then-full-before-draw", true)]
    public void RequiredRootUsesEffectiveCatalogKindAndWholeDeclaredWordCoverage(string scenario, bool accepted)
    {
        object device = new(); using var tape = Tape(device);
        bool cbv = scenario == "words-cannot-satisfy-cbv";
        PostFrameCreationValue[] metadata =
        [
            new(PostFrameCreationField.RootFlags, 0, 0, 0, 0, 0),
            new(PostFrameCreationField.RootParameter, 0, (uint)(cbv ? RootParameterType.ConstantBufferView : RootParameterType.Constant32Bits), 0, 0, 0),
            new(cbv ? PostFrameCreationField.RootDescriptor : PostFrameCreationField.RootConstants, 0, 0, cbv ? 0UL : 8UL, 0, 0)
        ];
        Assert.True(tape.TryFreezeCreation(metadata, [1], [2], out var frozen));
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out _, out _, new(0, 0, 1, false)));
        var token = Arm(tape, device); Bind(tape, token);
        // The production cache/request may describe four words, but cannot replace the
        // committed effective eight-word catalog or make a short observed write complete.
        tape.Request(token, PostFrameTapeBinding.RootParameter, 0, 0, 4);
        int simulatedOriginalCalls = 1;
        tape.RootWords(token, 0, scenario == "full-eight" || cbv ? new uint[8] : new uint[4], false);
        if (scenario == "short-then-full-before-draw")
        {
            Assert.Equal(PostFrameTapeRefusal.None, tape.Refusal);
            tape.RootWords(token, 0, new uint[8], false); simulatedOriginalCalls++;
        }
        tape.Draw(token, true, 3, 1, 0, 0, 0);
        Assert.Equal(accepted ? PostFrameTapeRefusal.None : PostFrameTapeRefusal.UnknownEffectiveState, tape.Refusal);
        Assert.Equal(accepted ? 1 : 0, tape.DrawCount);
        Assert.Equal(scenario == "short-then-full-before-draw" ? 2 : 1, simulatedOriginalCalls);
    }

    private static FieldInfo DeviceField(string name) => typeof(D3D12GraphicsDevice).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing actual production field " + name);
    private static void SetDevice(D3D12GraphicsDevice device, string name, object? value) => DeviceField(name).SetValue(device, value);
    private static T GetDevice<T>(D3D12GraphicsDevice device, string name) => (T)DeviceField(name).GetValue(device)!;
    private static void InvokeManagedDevice(D3D12GraphicsDevice device, string name)
        => typeof(D3D12GraphicsDevice).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(device, null);
    private static D3D12GraphicsDevice ManagedLifecycleOwner(string mode)
    {
        // No constructor, native pointer, window or device is created. Only the actual
        // production managed selection/termination methods are exercised on synthetic state.
        var device = (D3D12GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(D3D12GraphicsDevice));
        SetDevice(device, "_ownerThreadId", Environment.CurrentManagedThreadId);
        SetDevice(device, "_telemetryIdentitySession", Session.ToString("N"));
        SetDevice(device, "_postFrameRepetition", -1);
        device.ConfigurePostFrameSnapshots(mode);
        return device;
    }

    [Theory]
    [InlineData("pending", false)]
    [InlineData("pending", true)]
    [InlineData("claimed", false)]
    [InlineData("claimed", true)]
    [InlineData("stamped-issued", false)]
    [InlineData("stamped-issued", true)]
    [InlineData("failed", false)]
    [InlineData("failed", true)]
    [InlineData("exported", false)]
    [InlineData("exported", true)]
    public void ActualManagedEndSessionIsTerminalAndClearsContextsWithoutRetiringNativeLeases(string phase, bool opaque)
    {
        string mode = opaque ? GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments : GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly;
        var device = ManagedLifecycleOwner(mode);
        var context = device.BeginPostFrameCapture(mode, 0);
        var registry = new GraphicsResourceRetirement(); int released = 0;
        _ = registry.Register(() => released++, 512); SetDevice(device, "_resources", registry);
        GraphicsPostFrameCommandTape? tape = GetDevice<GraphicsPostFrameCommandTape?>(device, "_postFrameTape");
        if (tape is not null) Catalog(tape);
        if (phase != "pending")
        {
            InvokeManagedDevice(device, "ClaimPostFrameCapture");
            if (tape is not null) InvokeManagedDevice(device, "ArmPostFrameOpaqueTape");
        }
        var original = new GraphicsPostFrameOriginalReceipt(2, "original-device-frame-fence",
            new("0000000000000001", 1, 0, 1, 1, 123, 456, Session.ToString("N")), Environment.CurrentManagedThreadId,
            0, new(1, 4, 1, 1), 4, 0, 1, 2, 2, 0, 0) { CoverageMode = mode, Invocation = 1, WorldDepthAliasesRetainedFinalDepth = true };
        var receipt = new GraphicsPostFrameReceipt(device, 1, original, context);
        if (phase == "stamped-issued")
        {
            SetDevice(device, "_postFrameStamp", new GraphicsPostFrameCompletionStamp(true, 1, original.Submission, 4, 0, 0, 1, 2, 2, 0, 0, 256, new object())
            { CoverageMode = mode, Invocation = 1, SceneWitness = new object(), ShadowWitness = new object(), FinalDepthWitness = new object(), WorldDepthWitness = new object(), ShadowRecorded = true });
            SetDevice(device, "_postFrameReceiptIssued", true);
        }
        if (phase == "failed") SetDevice(device, "_failure", new GraphicsDeviceException("Synthetic original failure", "synthetic"));
        if (phase == "exported") SetDevice(device, "_postFrameInvocationOpen", false);
        device.EndPostFrameSnapshotSession(); device.EndPostFrameSnapshotSession();
        Assert.True(GetDevice<bool>(device, "_postFrameSessionEnded"));
        Assert.Equal(mode, GetDevice<string>(device, "_postFrameCoverageMode"));
        Assert.False(GetDevice<bool>(device, "_postFrameCapturePending")); Assert.False(GetDevice<bool>(device, "_postFrameInvocationOpen"));
        Assert.Equal(default, GetDevice<GraphicsPostFrameCaptureContext>(device, "_postFrameContext"));
        Assert.Equal(default, GetDevice<GraphicsPostFrameCompletionStamp>(device, "_postFrameStamp"));
        Assert.Null(GetDevice<object?>(device, "_postFrameTape")); Assert.Null(device.CurrentPostFrameFailureFacts(context));
        if (tape is not null) Assert.False(tape.HasWitnessReferences);
        Assert.Same(registry, GetDevice<GraphicsResourceRetirement>(device, "_resources")); Assert.Equal(1, registry.LiveCount); Assert.Equal(0, released);
        // Remove only the synthetic failure to exercise the terminal rejection itself.
        SetDevice(device, "_failure", null);
        Assert.Throws<InvalidOperationException>(() => device.BeginPostFrameCapture(mode, phase == "pending" ? 0 : 1));
        Assert.Throws<InvalidOperationException>(() => device.IssuePostFrameReceipt(0, context));
        Assert.Throws<InvalidOperationException>(() => device.CapturePostFrameSnapshot(receipt, new(), new([], [], [])));
        Assert.False(receipt.Consumed);
    }

    [Fact]
    public void ActualManagedEndSessionRecordingAndForeignOwnerGuardsPrecedeMutation()
    {
        var device = ManagedLifecycleOwner(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments);
        var context = device.BeginPostFrameCapture(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, 0);
        var tape = GetDevice<GraphicsPostFrameCommandTape>(device, "_postFrameTape"); Catalog(tape);
        InvokeManagedDevice(device, "ClaimPostFrameCapture"); InvokeManagedDevice(device, "ArmPostFrameOpaqueTape");
        SetDevice(device, "_recording", true);
        Assert.Throws<InvalidOperationException>(() => device.EndPostFrameSnapshotSession());
        Assert.False(GetDevice<bool>(device, "_postFrameSessionEnded")); Assert.Same(tape, GetDevice<GraphicsPostFrameCommandTape>(device, "_postFrameTape")); Assert.True(tape.Armed);
        SetDevice(device, "_recording", false); Exception? foreign = null;
        var thread = new Thread(() => { try { device.EndPostFrameSnapshotSession(); } catch (Exception error) { foreign = error; } });
        thread.Start(); thread.Join(); Assert.IsType<InvalidOperationException>(foreign);
        Assert.False(GetDevice<bool>(device, "_postFrameSessionEnded")); Assert.True(tape.Armed); Assert.True(tape.HasWitnessReferences);
        device.EndPostFrameSnapshotSession(); Assert.False(tape.HasWitnessReferences); Assert.Null(device.CurrentPostFrameFailureFacts(context));
    }

    [Fact]
    public void SetupBuilderCapacityAndAbortedCreationNeverBecomeCommittedCatalogEntries()
    {
        object device = new(); using var tape = Tape(device, Tiny(catalog: 40));
        Assert.True(tape.TryBeginCreation(out var builder)); Assert.True(builder.Add(new(PostFrameCreationField.RootFlags, 0, 1, 0, 0, 0)));
        Assert.False(builder.Add(new(PostFrameCreationField.RootParameter, 0, 1, 0, 0, 0)));
        tape.AbortCreation(builder); Assert.Equal(PostFrameTapeRefusal.CatalogCapacity, tape.Refusal);
        Assert.False(tape.FinishCreation(builder, [], [1], [2], out _));
    }

    [Fact]
    public void InactiveV1ReceiptAndResourceWireOmitEveryNewV2PropertyByteForByte()
    {
        var identity = new GraphicsDeviceTelemetryIdentity("0000000000000001", 1, 0, 1, 1, 123, 456, Session.ToString("N"));
        var original = new GraphicsPostFrameOriginalReceipt(1, "original-device-frame-fence", identity, 7, 0,
            new(3, 4, 1, 1), 5, 2, 3, 64, 64, 0, 0);
        var legacy = new { original.SchemaVersion, original.FenceDomain, original.DeviceIdentity, original.ManagedOwnerThreadId,
            original.FrameIndex, original.Submission, original.CompletedFence, original.PresentedBefore, original.PresentedAfter,
            original.Width, original.Height, original.ResizeGeneration, original.AppliedResizeGeneration };
        Assert.Equal(GraphicsPostFrameJson.Serialize(legacy, GraphicsPostFrameLimits.WireBytes), GraphicsPostFrameJson.Serialize(original, GraphicsPostFrameLimits.WireBytes));
        var resource = new GraphicsPostFrameResourceDescription(1, "buffer", "buffer", true, new(16, GraphicsBufferMemory.Upload), null, null, 0, 16, "resident", []);
        var oldResource = new { resource.LocalId, resource.SemanticName, resource.Kind, resource.LifetimeAvailable, resource.Buffer,
            resource.Texture, resource.Implicit, resource.SourceOffset, resource.ObservedBytes, resource.RangeMeaning, resource.Mips };
        Assert.Equal(GraphicsPostFrameJson.Serialize(oldResource, GraphicsPostFrameLimits.WireBytes), GraphicsPostFrameJson.Serialize(resource, GraphicsPostFrameLimits.WireBytes));
        Assert.False(GraphicsPostFrameSnapshotModes.IsSupported(null)); Assert.False(GraphicsPostFrameSnapshotModes.IsSupported("retainedattachmentsonly"));
        Assert.True(GraphicsPostFrameSnapshotModes.IsSupported("RetainedAttachmentsOnly")); Assert.True(GraphicsPostFrameSnapshotModes.IsSupported("OpaqueStateAndRetainedAttachments"));
    }

    [Fact]
    public void ActualTypedOpaqueWireFixtureRemainsPureIncompleteAndUsesRealSerializer()
    {
        object device = new(); using var tape = Tape(device, Tiny(catalog: 16384, identities: 64));
        Assert.True(tape.TryBeginCreation(out var builder));
        var root = new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout,
            [new RootParameter1(new RootConstants(0, 0, 8), ShaderVisibility.Vertex)], null);
        Assert.True(PostFrameNativeCreationEncoder.Root(builder, root, PostFrameTapeCreationRole.Ordinary, out uint roots, out ushort textures, out ushort retained));
        var elements = new[] { new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0) };
        var pso = NativePso([1], [2], elements);
        Assert.True(PostFrameNativeCreationEncoder.Pipeline(builder, pso, out uint vertices));
        Assert.True(tape.FinishCreation(builder, elements, pso.VertexShader.Span, pso.PixelShader.Span, out var frozen));
        Assert.True(tape.CommitCreation(frozen, new object(), new object(), new object(), out uint pipeline, out uint signature,
            new(vertices, textures, roots, true, retained)));
        var token = Arm(tape, device);
        uint buffer = tape.ObserveIdentity(token, new object(), new object(), PostFrameTapeIdentityKind.Buffer, 1, 0, 1, 0, 64, 0);
        uint scene = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 2, 0, 0, 0, 0, 0);
        _ = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 2, 0, 500, 0, 0, 0);
        uint depth = tape.ObserveIdentity(token, new object(), null, PostFrameTapeIdentityKind.RetainedTarget, 2, ulong.MaxValue, 0, 0, 0, 0);
        tape.NativeReturned(token, PostFrameTapeBinding.Pipeline, 0, pipeline);
        tape.NativeReturned(token, PostFrameTapeBinding.RootSignature, 0, signature);
        tape.NativeReturned(token, PostFrameTapeBinding.Topology, 0, 4);
        tape.NativeReturned(token, PostFrameTapeBinding.VertexBuffer, 0, buffer, 0, 64, 12);
        tape.NativeReturned(token, PostFrameTapeBinding.IndexBuffer, 0, buffer, 0, 64, 4);
        tape.Viewport(token, 0, 0, BitConverter.SingleToUInt32Bits(2), BitConverter.SingleToUInt32Bits(2), 0, BitConverter.SingleToUInt32Bits(1));
        tape.NativeReturned(token, PostFrameTapeBinding.Scissor, 0, 0, 0, 0, 2, 2);
        tape.Pass(token, PostFrameTapePass.OpaqueWorld, true);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.InitialTargets, PostFrameTapeBinding.RenderTarget, 0, scene);
        tape.OwnerPostcondition(token, PostFrameTapeOwnerPostcondition.InitialTargets, PostFrameTapeBinding.DepthTarget, 0, depth);
        tape.RootWords(token, 0, new uint[8], false);
        tape.Draw(token, true, 3, 1, 0, 0, 0);
        // This is a modeled original-completion value, never a native copy receipt or proof.
        var completion = tape.ObserveOriginalCompletion(token, new(0, 1, 4, 5, 1, 2, 0, 1), true, true);
        Assert.True(tape.Seal(token, device, Session, Environment.CurrentManagedThreadId, completion));
        Assert.True(tape.PngClosed(token)); var wire = tape.Materialize(token);
        Assert.False(wire.Complete); Assert.Contains("pure-contract-unhooked", wire.EvidenceScope);
        byte[] raw = GraphicsPostFrameJson.Serialize(wire, GraphicsPostFrameLimits.BodyBytes);
        using var parsed = JsonDocument.Parse(raw); Assert.False(parsed.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(1, parsed.RootElement.GetProperty("creations").GetArrayLength());
        string? output = Environment.GetEnvironmentVariable("FORGELINE_POST_FRAME_V2_PURE_WIRE_OUTPUT");
        if (output is not null)
        {
            Assert.False(Directory.Exists(output)); Directory.CreateDirectory(output);
            using (var file = new FileStream(Path.Combine(output, "opaque-pure-wire.json"), FileMode.CreateNew)) file.Write(raw);
            byte[] provenance = GraphicsPostFrameJson.Serialize(new { synthetic = true, pureModel = true, complete = false,
                nativeDeviceCreated = false, rendererExecuted = false, actualNativeCompletionProved = false, sourceTreeCleanVerified = false }, GraphicsPostFrameLimits.EnvelopeBytes);
            using var record = new FileStream(Path.Combine(output, "synthetic-provenance.json"), FileMode.CreateNew); record.Write(provenance);
        }
    }

    [Fact]
    public void InlineWireRetainsIntegerBitsAndNeverPretendsLateCopyComplete()
    {
        object device = new(); using var tape = Tape(device); Catalog(tape); var token = Arm(tape, device); Bind(tape, token);
        tape.Draw(token, true, 3, 1, 0, int.MinValue, 0);
        Seal(tape, device, token); Assert.Throws<InvalidOperationException>(() => tape.Materialize(token));
        Assert.True(tape.PngClosed(token)); Assert.False(tape.PngClosed(token));
        var wire = tape.Materialize(token); string json = JsonSerializer.Serialize(wire);
        Assert.False(wire.Complete); Assert.Equal(2, wire.SchemaVersion);
        Assert.Equal(int.MinValue, wire.Events.Single(e => e.Opcode == PostFrameTapeOpcode.DrawIndexed).Draw!.BaseVertex);
        Assert.DoesNotContain("VertexShader", json); // bytecode digest only, no shader byte wire sidecar
        Assert.Equal(6, PostFrameTapeSourceClosure.Table.Length);
        Assert.All(PostFrameTapeSourceClosure.Table.ToArray(), row => Assert.NotEmpty(row.Source));
    }
}
