using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

// PURE CONTRACT ONLY. No production factory, context, frame or exporter calls this model yet.
internal enum PostFrameTapeOpcode : ushort
{
    Reset = 1, Request, CacheHit, NativeReturn, NativeFailure, RootInvalidation,
    OwnerPostcondition, RootWords, Draw, DrawIndexed, PassBoundary
}

internal enum PostFrameTapeProvenance : byte
{
    Unknown, SetterRequested, CacheHitDecision, CpuNativeCallReturn, SourceProvedOwnerPostcondition
}

internal enum PostFrameTapeRefusal : uint
{
    None, EventCapacity, RootCapacity, DrawCapacity, IdentityCapacity, CatalogCapacity,
    ForeignInvocation, InvalidTransition, UnknownEffectiveState, MissingCatalog,
    ArmedFactory, InvalidCatalog, InvocationCapacity, OriginalFailure, CleanupFailure
}

internal enum PostFrameTapePass : uint { Reset, Shadow, OpaqueWorld, Particle, Composition, Overlay }
internal enum PostFrameTapeOwnerPostcondition : uint
{
    InitialTargets = 1, ShadowTargets, ShadowReturnTargets, ParticleTargets,
    ParticleReturnTargets, ShadowReceiverImplicit, BufferedVertexConstant, ParticleDepthReceiver
}
internal enum PostFrameTapeBinding : uint
{
    Pipeline, RootSignature, DescriptorHeap, Topology, IndexBuffer, Viewport, Scissor,
    RenderTarget, DepthTarget, VertexBuffer, TextureTable, RootParameter
}

internal enum PostFrameTapeIdentityKind : ulong { Buffer = 1, Texture, Pipeline, RootSignature, ImplicitArena, RetainedTarget, DescriptorHeap }
internal enum PostFrameTapeCreationRole : byte { Ordinary, DirectionalShadowReceiver, SceneDepthReceiver }

// All fields are little-endian when encoded. Payload is NOT a binary sidecar/wire format.
// All unused union operands/flags/auxiliary fields must be zero (typed writers below).
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct PostFrameTapeEvent
{
    [FieldOffset(0)] public uint Sequence;
    [FieldOffset(4)] public PostFrameTapeOpcode Opcode;
    [FieldOffset(6)] public PostFrameTapeProvenance Provenance;
    [FieldOffset(7)] public byte Flags;
    [FieldOffset(8)] public uint PassEpoch;
    [FieldOffset(12)] public uint Id;
    [FieldOffset(16)] public uint SecondId;
    [FieldOffset(20)] public uint Auxiliary;
    [FieldOffset(24)] public ulong A;
    [FieldOffset(32)] public ulong B;
    [FieldOffset(40)] public ulong C;
    [FieldOffset(48)] public ulong D;
    [FieldOffset(56)] public ulong E;
}

[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct PostFrameTapeIdentity
{
    public ulong Id, Kind, Epoch, Slot, Descriptor, Offset, Bytes, Stride;
}

[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct PostFrameTapeCatalogIndex
{
    public uint Id, Start, Length, MetadataWords, VertexStart, VertexBytes, PixelStart, PixelBytes;
    public ulong RootIdentity, PipelineIdentity, Reserved0, Reserved1;
}

[StructLayout(LayoutKind.Sequential, Size = 48)]
internal struct PostFrameTapeEffectiveValue
{
    public ulong Identity, A, B, C, D;
    public uint Known, Provenance;
}

internal readonly record struct PostFrameTapeLimits(int Events, int RootWords, int CatalogBytes,
    int Identities, int Pipelines, int Draws)
{
    internal static PostFrameTapeLimits Production => new(131072, 1048576, 16 * 1024 * 1024, 4096, 4096, 4096);
    internal void Validate()
    {
        PostFrameTapeLimits maximum = Production;
        if (Events <= 0 || Events > maximum.Events || RootWords <= 0 || RootWords > maximum.RootWords ||
            CatalogBytes <= 0 || CatalogBytes > maximum.CatalogBytes || Identities <= 0 || Identities > maximum.Identities ||
            Pipelines <= 0 || Pipelines > maximum.Pipelines || Draws <= 0 || Draws > maximum.Draws)
            throw new ArgumentOutOfRangeException(nameof(PostFrameTapeLimits));
    }
}

// The closed primitive catalog includes final native fields, not a managed Description graph.
// Native integration must supply the FINAL values, including factory mutations, in native order.
internal enum PostFrameCreationField : uint
{
    RootFlags = 1, RootParameter, RootConstants, RootDescriptor, DescriptorRange,
    StaticSampler, InputElement, BlendTarget, Rasterizer, DepthStencil, Sample,
    PrimitiveTopology, RenderTargetFormat, DepthFormat, PipelineFlags, SampleMask,
    PipelineHeader, OptionalShaderStages, StreamOutput, CachedPipelineState
}

internal readonly record struct PostFrameCreationValue(PostFrameCreationField Field, uint Index,
    ulong A, ulong B, ulong C, ulong D);

internal readonly record struct PostFrameTapeRequiredState(uint VertexSlots, ushort TextureSlots, uint RootParameters,
    bool DepthTarget, ushort RetainedTargetTextureSlots = 0);

internal readonly struct PostFrameCreationBuilder
{
    private readonly GraphicsPostFrameCommandTape? _owner;
    internal readonly int Index;
    internal PostFrameCreationBuilder(GraphicsPostFrameCommandTape owner, int index) => (_owner, Index) = (owner, index);
    internal bool BelongsTo(GraphicsPostFrameCommandTape owner) => ReferenceEquals(_owner, owner);
    internal bool Add(PostFrameCreationValue value) => _owner is not null && _owner.AppendCreation(this, value);
    internal bool Reserve(long bytes) => _owner is not null && _owner.ReserveCreationPhase(this, bytes);
}

internal readonly struct PostFrameFrozenCreation
{
    private readonly GraphicsPostFrameCommandTape? _owner;
    internal readonly int Index;
    internal PostFrameFrozenCreation(GraphicsPostFrameCommandTape owner, int index) { _owner = owner; Index = index; }
    internal bool BelongsTo(GraphicsPostFrameCommandTape owner) => ReferenceEquals(_owner, owner);
    internal ReadOnlySpan<byte> Metadata => _owner is null ? default : _owner.CreationMetadata(Index);
    internal ReadOnlySpan<byte> VertexShader => _owner is null ? default : _owner.CreationShader(Index, false);
    internal ReadOnlySpan<byte> PixelShader => _owner is null ? default : _owner.CreationShader(Index, true);
    internal ReadOnlyMemory<byte> VertexShaderMemory => _owner is null ? default : _owner.CreationShaderMemory(Index, false);
    internal ReadOnlyMemory<byte> PixelShaderMemory => _owner is null ? default : _owner.CreationShaderMemory(Index, true);
    internal ReadOnlySpan<byte> SemanticNamesUtf8 => _owner is null ? default : _owner.CreationSemantics(Index);
}

// Opaque, allocation-free per-arm value. A default, stale, or foreign value cannot claim a prefix.
internal readonly struct PostFrameTapeInvocation
{
    private readonly GraphicsPostFrameCommandTape? _owner;
    internal readonly ulong Epoch;
    internal PostFrameTapeInvocation(GraphicsPostFrameCommandTape owner, ulong epoch) { _owner = owner; Epoch = epoch; }
    internal bool BelongsTo(GraphicsPostFrameCommandTape owner, ulong epoch) => ReferenceEquals(_owner, owner) && Epoch == epoch;
}

// Source obligation only: future hooks issue this after the ACTUAL current CaptureCore body/cleanup.
// It is not a substitute for a native receipt. The opaque value prevents cross-invocation borrowing.
internal readonly struct PostFrameTapeCompletion
{
    private readonly GraphicsPostFrameCommandTape? _owner;
    private readonly ulong _invocation;
    internal readonly PostFrameTapeFrame? Frame;
    internal PostFrameTapeCompletion(GraphicsPostFrameCommandTape owner, ulong invocation, PostFrameTapeFrame? frame)
    { _owner = owner; _invocation = invocation; Frame = frame; }
    internal bool BelongsTo(GraphicsPostFrameCommandTape owner, ulong invocation) => ReferenceEquals(_owner, owner) && _invocation == invocation;
}

internal readonly record struct PostFrameTapeFrame(uint CapturedSlot, ulong Submission, ulong SubmittedFence,
    ulong CompletedFence, ulong DeviceGeneration, ulong ResourceGeneration, ulong PresentedBefore, ulong PresentedAfter);

internal readonly record struct PostFrameTapeClosure(PostFrameTapePass Pass, string Source, string Postcondition,
    bool ClearsRoot, bool PreservesIa);

// These are SOURCE OBLIGATIONS for later hooks, not claims that this unhooked model observed native calls.
internal static class PostFrameTapeSourceClosure
{
    internal static ReadOnlySpan<PostFrameTapeClosure> Table => Entries;
    private static readonly PostFrameTapeClosure[] Entries = CreateTable();
    // Same allocation path as the immutable static table, exposed only to the focused setup audit.
    internal static PostFrameTapeClosure[] CreateTable() =>
    [
        new(PostFrameTapePass.Reset, "D3D12GraphicsDevice.RenderFrameCore", "Reset unknown; initial OM/viewport/scissor only after successful source calls", true, false),
        new(PostFrameTapePass.Shadow, "D3D12DirectionalShadows.BeginDirectionalShadowPass", "Shadow OM/viewport/scissor; root and heap invalidated by context", true, true),
        new(PostFrameTapePass.OpaqueWorld, "D3D12DirectionalShadows.EndDirectionalShadowPass", "Restored world OM/viewport/scissor; IA inherited", true, true),
        new(PostFrameTapePass.Particle, "D3D12Particles.BeginParticlePass/EndParticlePass", "OM/depth-state source postconditions; private table bind must be explicit", true, true),
        new(PostFrameTapePass.Composition, "D3D12SceneComposition", "Private native pipeline/root/IA changes are unknown in this checkpoint; require later actual binds", true, false),
        new(PostFrameTapePass.Overlay, "D3D12GraphicsCommandContext.BeginPass", "Composition completed before overlay; do not infer restored private state", true, true)
    ];
}

// Typed inline v2 wire declarations. Materialization occurs only AFTER original PNG closure.
// This pure checkpoint never publishes complete late-copy evidence.
internal sealed record PostFrameTapeWireBinding(PostFrameTapeBinding Binding, uint Slot, ulong Identity,
    ulong A, ulong B, ulong C, ulong D, PostFrameTapeOwnerPostcondition? OwnerSource = null);
internal sealed record PostFrameTapeWireViewport(uint XBits, uint YBits, uint WidthBits, uint HeightBits,
    uint MinimumDepthBits, uint MaximumDepthBits, PostFrameTapeOwnerPostcondition? OwnerSource = null);
internal sealed record PostFrameTapeWireDraw(bool Indexed, uint Count, uint Instances, uint Start, int BaseVertex, uint StartInstance);
internal sealed record PostFrameTapeWireRootWords(uint Parameter, uint OffsetWords, uint WordCount,
    bool Buffered, uint ArenaIdentity, uint ArenaSlot, ulong ResourceEpoch, ulong SourceOffset,
    PostFrameTapeOwnerPostcondition? OwnerSource);
internal sealed record PostFrameTapeWirePass(PostFrameTapePass Pass, bool SourceBranchProved);
internal sealed record PostFrameTapeWireEvent(uint Sequence, PostFrameTapeOpcode Opcode,
    PostFrameTapeProvenance Provenance, uint PassEpoch, PostFrameTapeWireBinding? Binding = null,
    PostFrameTapeWireViewport? Viewport = null, PostFrameTapeWireDraw? Draw = null,
    PostFrameTapeWireRootWords? RootWords = null, PostFrameTapeWirePass? Pass = null);
internal sealed record PostFrameTapeWireCreation(uint Id, PostFrameCreationValue[] Metadata,
    string SemanticNamesUtf8Base64, ulong PipelineIdentity, ulong RootIdentity, PostFrameTapeRequiredState RequiredState,
    int VertexBytes, string VertexSha256, int PixelBytes, string PixelSha256,
    PostFrameTapeCreationRole CreationRole = PostFrameTapeCreationRole.Ordinary);
internal sealed record PostFrameTapeWireIdentity(ulong Id, PostFrameTapeIdentityKind Kind, bool LifetimeAvailable,
    ulong Epoch, ulong Slot, ulong Descriptor, ulong Offset, ulong Bytes, ulong Stride);
internal sealed record PostFrameTapeWire(int SchemaVersion, string Kind, bool Complete,
    string EvidenceScope, Guid DeviceSession, int ManagedOwnerThreadId, int Repetition, ulong Invocation,
    PostFrameTapeRefusal Refusal, PostFrameTapeFrame? OriginalFrame, PostFrameTapeWireEvent[] Events,
    int RootWordCount, int RootByteCount, string RootLittleEndianBase64, string RootSha256,
    PostFrameTapeWireCreation[] Creations, PostFrameTapeWireIdentity[] Identities);

internal sealed class GraphicsPostFrameCommandTape : IDisposable
{
    internal const int SelectedLedgerBytes = 32 * 1024 * 1024;
    internal const long NamedPayloadBytes = 29954048;
    private const string PrefixKey = "ForgeLine.PostFinalColor.CommandTape.v2";
    private readonly PostFrameTapeEvent[] _events;
    private readonly uint[] _roots;
    private readonly byte[] _catalog;
    private readonly PostFrameTapeIdentity[] _identities;
    private readonly PostFrameTapeCatalogIndex[] _pipelines;
    private readonly object?[] _witnesses, _lifetimes;
    // 80 x48-byte cells +256 scratch bytes =4096 fixed-state payload bytes.
    // PSO/root/heap/topology/IB/viewport/scissor/RT/DSV:0..8; VB:9..40; textures:41..56; roots:57..79.
    private readonly PostFrameTapeEffectiveValue[] _state;
    private readonly byte[] _fixedScratch;
    private readonly object _device;
    private readonly Guid _session;
    private readonly int _ownerThread;
    private readonly PostFrameTapeLimits _limits;
    private int _eventCount, _rootCount, _catalogCount, _pipelineCount, _identityCount, _drawCount;
    private int _openCreation = -1;
    private uint _passEpoch;
    private PostFrameTapePass _currentPass;
    private bool _opaquePassProved;
    private ulong _invocation;
    private int _repetition = -1;
    private bool _armed, _sealed, _pngClosed, _disposed, _attachmentClosed;
    private bool _completionObserved, _materialized, _evidenceExported, _finalized;
    private bool _shadowWordsAvailable;
    private PostFrameTapeRefusal _refusal;
    private PostFrameTapeFrame? _frame;
    private PostFrameTapeFrame? _previousFrame;

    internal GraphicsPostFrameCommandTape(object device, Guid session, int ownerThread,
        PostFrameTapeLimits? testLimits = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (session == Guid.Empty || ownerThread <= 0 || ownerThread != Environment.CurrentManagedThreadId)
            throw new ArgumentException("Actual current scope/owner required.");
        _limits = testLimits ?? PostFrameTapeLimits.Production;
        _limits.Validate();
        _device = device;
        _session = session;
        _ownerThread = ownerThread;
        _events = new PostFrameTapeEvent[_limits.Events];
        _roots = new uint[_limits.RootWords];
        _catalog = new byte[_limits.CatalogBytes];
        _identities = new PostFrameTapeIdentity[_limits.Identities];
        _pipelines = new PostFrameTapeCatalogIndex[_limits.Pipelines];
        _witnesses = new object?[_limits.Identities];
        _lifetimes = new object?[_limits.Identities];
        _state = new PostFrameTapeEffectiveValue[80];
        _fixedScratch = new byte[256];
    }

    internal PostFrameTapeRefusal Refusal => _refusal;
    internal bool Armed => _armed;
    internal int EventCount => _eventCount;
    internal int DrawCount => _drawCount;
    internal int RootWordCount => _rootCount;
    internal bool HasWitnessReferences => Array.Exists(_witnesses, static item => item is not null) ||
        Array.Exists(_lifetimes, static item => item is not null);
    internal ReadOnlySpan<PostFrameTapeEvent> RecordedEvents => _events.AsSpan(0, _eventCount);
    internal long OwnedArrayPayloadBytes => (long)_events.Length * 64 + (long)_roots.Length * 4 +
        _catalog.Length + (long)(_identities.Length + _pipelines.Length) * 64 +
        (long)(_witnesses.Length + _lifetimes.Length) * IntPtr.Size +
        (long)_state.Length * Unsafe.SizeOf<PostFrameTapeEffectiveValue>() + _fixedScratch.Length;

    private bool Current(PostFrameTapeInvocation token, object device, Guid session, int ownerThread) =>
        !_disposed && token.BelongsTo(this, _invocation) && ReferenceEquals(device, _device) &&
        session == _session && ownerThread == _ownerThread && ownerThread == Environment.CurrentManagedThreadId;

    internal PostFrameTapeInvocation Arm(object device, Guid session, int ownerThread, int repetition)
    {
        // Foreign callers never mutate owner-thread fields, including the first refusal.
        if (Environment.CurrentManagedThreadId != _ownerThread || ownerThread != _ownerThread) return default;
        if (_invocation != 0 && !_finalized) return default; // never erase an unpublished prefix/image/evidence
        if (_disposed || _armed || _refusal != PostFrameTapeRefusal.None || !ReferenceEquals(device, _device) || session != _session ||
            ownerThread != _ownerThread || ownerThread != Environment.CurrentManagedThreadId || repetition is < 0 or > 7 || repetition != _repetition + 1)
        {
            if (_armed) Refuse(PostFrameTapeRefusal.ForeignInvocation);
            return default;
        }
        if (_invocation == ulong.MaxValue) { Refuse(PostFrameTapeRefusal.InvocationCapacity); return default; }
        _attachmentClosed = true;
        _invocation++;
        _repetition = repetition;
        _eventCount = _rootCount = _drawCount = 0;
        _passEpoch = 0;
        _currentPass = PostFrameTapePass.Reset;
        _opaquePassProved = false;
        _refusal = PostFrameTapeRefusal.None;
        _frame = null;
        _sealed = _pngClosed = false;
        _completionObserved = _materialized = _evidenceExported = _finalized = false;
        _shadowWordsAvailable = false;
        Array.Clear(_state);
        _armed = true;
        Append(new() { Opcode = PostFrameTapeOpcode.Reset });
        return new(this, _invocation);
    }

    private bool Recording(PostFrameTapeInvocation token)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread || !_armed || _disposed) return false;
        if (!token.BelongsTo(this, _invocation))
        {
            Refuse(PostFrameTapeRefusal.ForeignInvocation);
            return false;
        }
        return _refusal == PostFrameTapeRefusal.None;
    }

    internal void Refuse(PostFrameTapeRefusal reason)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) return;
        if (_refusal == PostFrameTapeRefusal.None && reason != PostFrameTapeRefusal.None) _refusal = reason;
    }

    private bool Append(PostFrameTapeEvent value)
    {
        if (_refusal != PostFrameTapeRefusal.None) return false;
        if (_eventCount >= _events.Length) { Refuse(PostFrameTapeRefusal.EventCapacity); return false; }
        value.Sequence = (uint)_eventCount;
        value.PassEpoch = _passEpoch;
        _events[_eventCount++] = value;
        return true;
    }

    private static int Cell(PostFrameTapeBinding binding, uint slot) => binding switch
    {
        >= PostFrameTapeBinding.Pipeline and <= PostFrameTapeBinding.DepthTarget when slot == 0 => (int)binding,
        PostFrameTapeBinding.VertexBuffer when slot < 32 => 9 + (int)slot,
        PostFrameTapeBinding.TextureTable when slot < 16 => 41 + (int)slot,
        PostFrameTapeBinding.RootParameter when slot < 23 => 57 + (int)slot,
        _ => -1
    };

    internal void Request(PostFrameTapeInvocation token, PostFrameTapeBinding binding, uint slot, ulong identity,
        ulong a = 0, ulong b = 0, ulong c = 0, ulong d = 0, ulong e = 0)
    {
        if (!Recording(token)) return;
        if (Cell(binding, slot) < 0 || e != 0) { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        Append(new() { Opcode = PostFrameTapeOpcode.Request, Provenance = PostFrameTapeProvenance.SetterRequested,
            Id = (uint)binding, SecondId = slot, A = identity, B = a, C = b, D = c, E = d });
        // The complete six viewport words use Viewport() below; this general binding has four operands.
    }

    internal void CacheHit(PostFrameTapeInvocation token, PostFrameTapeBinding binding, uint slot)
    {
        if (!Recording(token)) return;
        if (Cell(binding, slot) < 0) { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        Append(new() { Opcode = PostFrameTapeOpcode.CacheHit, Provenance = PostFrameTapeProvenance.CacheHitDecision,
            Id = (uint)binding, SecondId = slot });
        // Deliberately NO assignment to independent effective state.
    }

    internal void NativeReturned(PostFrameTapeInvocation token, PostFrameTapeBinding binding, uint slot,
        ulong identity, ulong a = 0, ulong b = 0, ulong c = 0, ulong d = 0, ulong e = 0)
        => Effective(token, binding, slot, identity, a, b, c, d, e, PostFrameTapeProvenance.CpuNativeCallReturn);

    internal void OwnerPostcondition(PostFrameTapeInvocation token, PostFrameTapeOwnerPostcondition source,
        PostFrameTapeBinding binding, uint slot,
        ulong identity, ulong a = 0, ulong b = 0, ulong c = 0, ulong d = 0, ulong e = 0)
    {
        if (!Recording(token)) return;
        bool allowed = source switch
        {
            >= PostFrameTapeOwnerPostcondition.InitialTargets and <= PostFrameTapeOwnerPostcondition.ParticleReturnTargets =>
                binding is PostFrameTapeBinding.RenderTarget or PostFrameTapeBinding.DepthTarget or PostFrameTapeBinding.Scissor,
            PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit or PostFrameTapeOwnerPostcondition.ParticleDepthReceiver =>
                binding is PostFrameTapeBinding.RootParameter or PostFrameTapeBinding.TextureTable or PostFrameTapeBinding.DescriptorHeap,
            PostFrameTapeOwnerPostcondition.BufferedVertexConstant => binding == PostFrameTapeBinding.RootParameter && slot == 0,
            _ => false
        };
        if (!allowed) { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        Effective(token, binding, slot, identity, a, b, c, d, e, PostFrameTapeProvenance.SourceProvedOwnerPostcondition, (uint)source);
    }

    private void Effective(PostFrameTapeInvocation token, PostFrameTapeBinding binding, uint slot,
        ulong identity, ulong a, ulong b, ulong c, ulong d, ulong e, PostFrameTapeProvenance provenance, uint source = 0)
    {
        if (!Recording(token)) return;
        int cell = Cell(binding, slot);
        if (cell < 0 || e != 0) { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        if (!Append(new() { Opcode = provenance == PostFrameTapeProvenance.CpuNativeCallReturn ?
                PostFrameTapeOpcode.NativeReturn : PostFrameTapeOpcode.OwnerPostcondition,
            Provenance = provenance, Id = (uint)binding, SecondId = slot, Auxiliary = source, A = identity, B = a, C = b, D = c, E = d })) return;
        if (binding == PostFrameTapeBinding.DescriptorHeap && (_state[cell].Known == 0 || _state[cell].Identity != identity))
            Array.Clear(_state, 41, 16);
        _state[cell] = new() { Identity = identity, A = a, B = b, C = c, D = d, Known = 1, Provenance = (uint)provenance };
    }

    internal void NativeFailed(PostFrameTapeInvocation token, PostFrameTapeBinding binding, uint slot)
    {
        if (!Recording(token)) return;
        int cell = Cell(binding, slot);
        if (cell < 0) { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        if (Append(new() { Opcode = PostFrameTapeOpcode.NativeFailure, Id = (uint)binding, SecondId = slot }))
            _state[cell] = default;
    }

    internal PostFrameTapeEffectiveValue EffectiveValue(PostFrameTapeBinding binding, uint slot = 0)
    {
        int cell = Cell(binding, slot);
        return cell < 0 ? default : _state[cell];
    }

    internal void StoreShadowFrameWords(PostFrameTapeInvocation token, ReadOnlySpan<uint> words)
    {
        if (!Recording(token)) return;
        if (words.Length != 24) { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        words.CopyTo(MemoryMarshal.Cast<byte, uint>(_fixedScratch.AsSpan(0, 96)));
        _shadowWordsAvailable = true;
    }
    internal ReadOnlySpan<uint> ShadowFrameWords => _shadowWordsAvailable ?
        MemoryMarshal.Cast<byte, uint>(_fixedScratch.AsSpan(0, 96)) : default;

    internal uint FindIdentity(object actual, object? lifetime, PostFrameTapeIdentityKind kind)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread || _disposed) return 0;
        for (int i = 0; i < _identityCount; i++)
            if (ReferenceEquals(_witnesses[i], actual) && ReferenceEquals(_lifetimes[i], lifetime) && _identities[i].Kind == (ulong)kind)
                return (uint)_identities[i].Id;
        return 0;
    }

    internal void ClearSourceScratch()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) return;
        _shadowWordsAvailable = false;
        Array.Clear(_fixedScratch);
    }

    internal void Viewport(PostFrameTapeInvocation token, uint x, uint y, uint width, uint height, uint minimumDepth, uint maximumDepth,
        PostFrameTapeOwnerPostcondition? ownerSource = null)
    {
        if (!Recording(token)) return;
        if (ownerSource.HasValue && ownerSource is not (>= PostFrameTapeOwnerPostcondition.InitialTargets and <= PostFrameTapeOwnerPostcondition.ParticleReturnTargets))
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        var provenance = ownerSource.HasValue ? PostFrameTapeProvenance.SourceProvedOwnerPostcondition : PostFrameTapeProvenance.CpuNativeCallReturn;
        ulong xy = x | ((ulong)y << 32), wh = width | ((ulong)height << 32), depth = minimumDepth | ((ulong)maximumDepth << 32);
        if (Append(new() { Opcode = ownerSource.HasValue ? PostFrameTapeOpcode.OwnerPostcondition : PostFrameTapeOpcode.NativeReturn,
            Provenance = provenance, Id = (uint)PostFrameTapeBinding.Viewport, Flags = 2,
            Auxiliary = ownerSource.HasValue ? (uint)ownerSource.Value : 0, A = xy, B = wh, C = depth }))
            _state[5] = new() { A = xy, B = wh, C = depth, Known = 1, Provenance = (uint)provenance };
    }

    internal void ViewportRequest(PostFrameTapeInvocation token, uint x, uint y, uint width, uint height, uint minimumDepth, uint maximumDepth)
    {
        if (!Recording(token)) return;
        Append(new() { Opcode = PostFrameTapeOpcode.Request, Provenance = PostFrameTapeProvenance.SetterRequested,
            Id = (uint)PostFrameTapeBinding.Viewport, Flags = 2, A = x | ((ulong)y << 32),
            B = width | ((ulong)height << 32), C = minimumDepth | ((ulong)maximumDepth << 32) });
    }

    internal void InvalidateRoot(PostFrameTapeInvocation token)
    {
        if (!Recording(token) || !Append(new() { Opcode = PostFrameTapeOpcode.RootInvalidation })) return;
        _state[0] = _state[1] = _state[2] = default;
        Array.Clear(_state, 41, 39); // IA, viewport/scissor and OM are inherited, not fabricated/reset.
    }

    internal void Pass(PostFrameTapeInvocation token, PostFrameTapePass pass, bool sourceBranchProved)
    {
        if (!Recording(token)) return;
        if ((uint)pass > (uint)PostFrameTapePass.Overlay || _passEpoch == uint.MaxValue)
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        _passEpoch++;
        _currentPass = pass;
        _opaquePassProved = pass == PostFrameTapePass.OpaqueWorld && sourceBranchProved;
        if (!Append(new() { Opcode = PostFrameTapeOpcode.PassBoundary, Provenance = sourceBranchProved ?
            PostFrameTapeProvenance.SourceProvedOwnerPostcondition : PostFrameTapeProvenance.Unknown,
            Id = (uint)pass, Flags = sourceBranchProved ? (byte)0 : (byte)1 })) return;
        // Unknown excluded/private state is recoverable through subsequent actual binds. It is not
        // promoted to proof, and is refused if still required at the next opaque draw.
        if (!sourceBranchProved || pass == PostFrameTapePass.Composition) Array.Clear(_state);
    }

    internal void RootWords(PostFrameTapeInvocation token, uint parameter, ReadOnlySpan<uint> values, bool buffered,
        uint arenaSlot = 0, ulong resourceEpoch = 0, ulong sourceOffset = 0, uint arenaIdentity = 0,
        PostFrameTapeOwnerPostcondition? ownerSource = null)
    {
        if (!Recording(token)) return;
        if (parameter >= 23 || values.Length is < 1 or > 64 || values.Length > _roots.Length - _rootCount)
        { Refuse(PostFrameTapeRefusal.RootCapacity); return; }
        if (buffered && (sourceOffset % 256 != 0 || arenaSlot > 2 || resourceEpoch == 0))
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        if (buffered)
        {
            ownerSource ??= PostFrameTapeOwnerPostcondition.BufferedVertexConstant;
            if (ownerSource is not (PostFrameTapeOwnerPostcondition.BufferedVertexConstant or PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit) ||
                (ownerSource == PostFrameTapeOwnerPostcondition.BufferedVertexConstant && (parameter != 0 || sourceOffset < 256)) ||
                (ownerSource == PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit && (sourceOffset != 0 || values.Length != 24)))
            { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
            PostFrameTapeEffectiveValue actual = _state[57 + parameter];
            int identityIndex = arenaIdentity == 0 || arenaIdentity > (uint)_identityCount ? -1 : (int)arenaIdentity - 1;
            if (identityIndex < 0 || actual.Known == 0 || actual.Identity != arenaIdentity || actual.A != sourceOffset ||
                actual.B < (uint)values.Length * 4 || actual.C != resourceEpoch || actual.D != arenaSlot)
            { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
            PostFrameTapeIdentity arena = _identities[identityIndex];
            if (arena.Kind != (ulong)PostFrameTapeIdentityKind.ImplicitArena || arena.Epoch != resourceEpoch || arena.Slot != arenaSlot ||
                sourceOffset > arena.Bytes || (uint)values.Length * 4 > arena.Bytes - sourceOffset || actual.B > arena.Bytes - sourceOffset)
            { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        }
        else if (arenaIdentity != 0 || arenaSlot != 0 || resourceEpoch != 0 || sourceOffset != 0 || ownerSource is not null)
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        int offset = _rootCount;
        if (!Append(new() { Opcode = PostFrameTapeOpcode.RootWords, Provenance = buffered ?
                PostFrameTapeProvenance.SourceProvedOwnerPostcondition : PostFrameTapeProvenance.CpuNativeCallReturn,
            Id = parameter, SecondId = arenaSlot, Auxiliary = arenaIdentity, Flags = buffered ? (byte)1 : (byte)0,
            A = (uint)offset, B = (uint)values.Length, C = resourceEpoch, D = sourceOffset,
            E = buffered ? (uint)ownerSource!.Value : 0 })) return;
        values.CopyTo(_roots.AsSpan(offset));
        _rootCount += values.Length;
        // Recording source words never changes the independently effective CBV identity/range.
        if (!buffered) _state[57 + parameter] = new() { A = (uint)offset, B = (uint)values.Length,
            Known = 2, Provenance = (uint)PostFrameTapeProvenance.CpuNativeCallReturn };
    }

    internal void Draw(PostFrameTapeInvocation token, bool indexed, int count, int instances, int start, int baseVertex, int startInstance)
    {
        if (!Recording(token)) return;
        if (_currentPass != PostFrameTapePass.OpaqueWorld) return; // excluded original calls still delegate outside this observer
        if (!_opaquePassProved) { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        if (count <= 0 || instances <= 0 || start < 0 || startInstance < 0 || (!indexed && baseVertex != 0))
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        if (_drawCount >= _limits.Draws) { Refuse(PostFrameTapeRefusal.DrawCapacity); return; }
        if (_state[0].Known == 0 || _state[1].Known == 0 || _state[3].Known == 0 ||
            _state[5].Known == 0 || _state[6].Known == 0 || _state[7].Known == 0 || (indexed && _state[4].Known == 0))
        { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        int pipeline = CommittedPipeline(_state[0].Identity, _state[1].Identity);
        if (pipeline < 0)
        { Refuse(PostFrameTapeRefusal.MissingCatalog); return; }
        PostFrameTapeCatalogIndex catalog = _pipelines[pipeline];
        if (!HasIdentity(_state[7].Identity, PostFrameTapeIdentityKind.RetainedTarget) ||
            (indexed && !BufferRange(_state[4], true)))
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        uint vertexSlots = (uint)catalog.Reserved0;
        uint textureSlots = (ushort)(catalog.Reserved0 >> 32);
        uint retainedTargetSlots = (ushort)(catalog.Reserved1 >> 32);
        uint rootParameters = (uint)catalog.Reserved1;
        for (int slot = 0; slot < 32; slot++)
            if ((vertexSlots & (1U << slot)) != 0 && (_state[9 + slot].Known == 0 || !BufferRange(_state[9 + slot], false)))
            { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        for (int slot = 0; slot < 16; slot++)
            if ((textureSlots & (1U << slot)) != 0 && (_state[41 + slot].Known == 0 || _state[2].Known == 0 ||
                !HasIdentity(_state[41 + slot].Identity, PostFrameTapeIdentityKind.Texture) ||
                !HasIdentity(_state[2].Identity, PostFrameTapeIdentityKind.DescriptorHeap) || !TableDescriptorMatches(pipeline, slot, _state[41 + slot], retainedTarget: false)))
            { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        for (int slot = 0; slot < 16; slot++)
            if ((retainedTargetSlots & (1U << slot)) != 0 && (_state[41 + slot].Known == 0 || _state[2].Known == 0 ||
                !HasIdentity(_state[41 + slot].Identity, PostFrameTapeIdentityKind.RetainedTarget) ||
                !HasIdentity(_state[2].Identity, PostFrameTapeIdentityKind.DescriptorHeap) || !TableDescriptorMatches(pipeline, slot, _state[41 + slot], retainedTarget: true)))
            { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        for (int parameter = 0; parameter < 23; parameter++)
            if ((rootParameters & (1U << parameter)) != 0 && !RootBindingMatches(pipeline, parameter, _state[57 + parameter]))
            { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        if ((catalog.Reserved0 & (1UL << 48)) != 0 && (_state[8].Known == 0 || !HasIdentity(_state[8].Identity, PostFrameTapeIdentityKind.RetainedTarget)))
        { Refuse(PostFrameTapeRefusal.UnknownEffectiveState); return; }
        if (Append(new() { Opcode = indexed ? PostFrameTapeOpcode.DrawIndexed : PostFrameTapeOpcode.Draw,
            Provenance = PostFrameTapeProvenance.CpuNativeCallReturn, A = (uint)count, B = (uint)instances,
            C = (uint)start, D = unchecked((uint)baseVertex), E = (uint)startInstance })) _drawCount++;
    }

    private bool HasIdentity(ulong id, PostFrameTapeIdentityKind kind) => id > 0 && id <= (uint)_identityCount &&
        _identities[(int)id - 1].Id == id && _identities[(int)id - 1].Kind == (ulong)kind;

    private bool BufferRange(PostFrameTapeEffectiveValue value, bool indexed)
    {
        if (!HasIdentity(value.Identity, PostFrameTapeIdentityKind.Buffer) || value.B == 0 || value.B > uint.MaxValue) return false;
        PostFrameTapeIdentity resource = _identities[(int)value.Identity - 1];
        return value.A <= resource.Bytes && value.B <= resource.Bytes - value.A &&
            (indexed ? value.C is 2 or 4 : value.C is > 0 and <= uint.MaxValue);
    }

    private static PostFrameCreationValue CatalogValue(ReadOnlySpan<byte> bytes) => new(
        (PostFrameCreationField)BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]));

    private bool RootKind(int pipeline, uint parameter, out RootParameterType kind)
    {
        kind = default; int found = 0;
        ReadOnlySpan<byte> metadata = CreationMetadata(pipeline);
        for (int offset = 0; offset < metadata.Length; offset += 40)
        {
            var row = CatalogValue(metadata[offset..]);
            if (row.Field != PostFrameCreationField.RootParameter || row.Index != parameter) continue;
            kind = (RootParameterType)(uint)row.A; found++;
        }
        return found == 1;
    }

    private bool RootBindingMatches(int pipeline, int parameter, PostFrameTapeEffectiveValue value)
    {
        if (value.Known == 0 || !RootKind(pipeline, (uint)parameter, out var kind)) return false;
        if (kind == RootParameterType.Constant32Bits)
        {
            uint declared = 0; int found = 0;
            ReadOnlySpan<byte> metadata = CreationMetadata(pipeline);
            for (int offset = 0; offset < metadata.Length; offset += 40)
            {
                var row = CatalogValue(metadata[offset..]);
                if (row.Field != PostFrameCreationField.RootConstants || row.Index != parameter) continue;
                declared = (uint)row.B; found++;
            }
            // Actual setters write from DWORD zero. A short prefix cannot prove the whole
            // effective catalog parameter. A later full native write can recover before draw.
            return found == 1 && declared is > 0 and <= 64 && value.Known == 2 && value.Identity == 0 &&
                value.B == declared && value.A <= (uint)_rootCount && value.B <= (uint)_rootCount - value.A;
        }
        if (kind != RootParameterType.ConstantBufferView || value.Known != 1 ||
            !HasIdentity(value.Identity, PostFrameTapeIdentityKind.ImplicitArena)) return false;
        var arena = _identities[(int)value.Identity - 1];
        return value.A % 256 == 0 && value.B > 0 && value.A <= arena.Bytes && value.B <= arena.Bytes - value.A &&
            value.C == arena.Epoch && value.D == arena.Slot;
    }

    private bool TableDescriptorMatches(int pipeline, int slot, PostFrameTapeEffectiveValue value, bool retainedTarget)
    {
        if (value.Identity == 0 || value.Identity > (uint)_identityCount || value.C != _state[2].Identity) return false;
        PostFrameTapeIdentity resource = _identities[(int)value.Identity - 1];
        if (resource.Descriptor != value.A || (retainedTarget ? value.D != 1 : value.D != ((resource.Stride >> 8) & 0xffff))) return false;
        ReadOnlySpan<byte> metadata = CreationMetadata(pipeline); int found = 0;
        for (int offset = 0; offset < metadata.Length; offset += 40)
        {
            var row = CatalogValue(metadata[offset..]);
            if (row.Field != PostFrameCreationField.DescriptorRange || (uint)(row.B >> 32) != (uint)slot) continue;
            if ((uint)(row.A >> 32) != (uint)DescriptorRangeType.ShaderResourceView || (uint)row.B != 1 ||
                (uint)row.C != 0 || value.B != (uint)row.A ||
                !RootKind(pipeline, (uint)row.A, out var kind) || kind != RootParameterType.DescriptorTable) return false;
            found++;
        }
        return found == 1;
    }

    private int CommittedPipeline(ulong pipeline, ulong root)
    {
        for (int i = 0; i < _pipelineCount; i++)
            if (_pipelines[i].PipelineIdentity == pipeline && _pipelines[i].RootIdentity == root && pipeline != 0 && root != 0) return i;
        return -1;
    }

    internal uint ObserveIdentity(PostFrameTapeInvocation token, object actual, object? lifetime, PostFrameTapeIdentityKind kind,
        ulong epoch, ulong slot, ulong descriptor, ulong offset, ulong bytes, ulong stride)
    {
        if (!Recording(token)) return 0;
        return RegisterIdentity(actual, lifetime, kind, epoch, slot, descriptor, offset, bytes, stride);
    }

    private uint RegisterIdentity(object actual, object? lifetime, PostFrameTapeIdentityKind kind,
        ulong epoch, ulong slot, ulong descriptor, ulong offset, ulong bytes, ulong stride)
    {
        if (actual is null || offset > ulong.MaxValue - bytes || kind is < PostFrameTapeIdentityKind.Buffer or > PostFrameTapeIdentityKind.DescriptorHeap ||
            (lifetime is null && kind is not (PostFrameTapeIdentityKind.ImplicitArena or PostFrameTapeIdentityKind.RetainedTarget or PostFrameTapeIdentityKind.DescriptorHeap)))
        { Refuse(PostFrameTapeRefusal.InvalidTransition); return 0; }
        for (int i = 0; i < _identityCount; i++)
        {
            if (lifetime is not null && ReferenceEquals(_lifetimes[i], lifetime) &&
                _identities[i].Kind == (ulong)kind && !ReferenceEquals(_witnesses[i], actual))
            { Refuse(PostFrameTapeRefusal.InvalidTransition); return 0; }
            if (!ReferenceEquals(_witnesses[i], actual)) continue;
            PostFrameTapeIdentity prior = _identities[i];
            if (!ReferenceEquals(_lifetimes[i], lifetime) || prior.Kind != (ulong)kind || prior.Epoch != epoch || prior.Slot != slot ||
                prior.Descriptor != descriptor || prior.Offset != offset || prior.Bytes != bytes || prior.Stride != stride)
            { Refuse(PostFrameTapeRefusal.InvalidTransition); return 0; }
            return (uint)prior.Id;
        }
        if (_identityCount >= _identities.Length) { Refuse(PostFrameTapeRefusal.IdentityCapacity); return 0; }
        uint id = (uint)_identityCount + 1;
        _identities[_identityCount] = new() { Id = id, Kind = (ulong)kind, Epoch = epoch, Slot = slot,
            Descriptor = descriptor, Offset = offset, Bytes = bytes, Stride = stride };
        _witnesses[_identityCount] = actual;
        _lifetimes[_identityCount++] = lifetime;
        return id;
    }

    // Setup-only freeze. Original arrays are never exposed to the future native factory again.
    internal bool TryBeginCreation(out PostFrameCreationBuilder builder)
    {
        builder = default;
        if (Environment.CurrentManagedThreadId != _ownerThread) return false;
        if (_armed) { Refuse(PostFrameTapeRefusal.ArmedFactory); return false; }
        if (_disposed || _attachmentClosed || _refusal != PostFrameTapeRefusal.None) return false;
        if (_openCreation >= 0 || _pipelineCount >= _pipelines.Length)
        { Refuse(PostFrameTapeRefusal.CatalogCapacity); return false; }
        int index = _pipelineCount++;
        _pipelines[index] = new() { Id = (uint)index + 1, Start = (uint)_catalogCount };
        _openCreation = index;
        builder = new(this, index);
        return true;
    }

    internal bool AppendCreation(PostFrameCreationBuilder builder, PostFrameCreationValue value)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) return false;
        if (_armed) { Refuse(PostFrameTapeRefusal.ArmedFactory); return false; }
        if (_disposed || !builder.BelongsTo(this) || builder.Index != _openCreation || _refusal != PostFrameTapeRefusal.None) return false;
        if (value.Field is < PostFrameCreationField.RootFlags or > PostFrameCreationField.CachedPipelineState)
        { Refuse(PostFrameTapeRefusal.InvalidCatalog); return false; }
        if (_catalog.Length - _catalogCount < 40) { Refuse(PostFrameTapeRefusal.CatalogCapacity); return false; }
        Span<byte> bytes = _catalog.AsSpan(_catalogCount, 40);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value.Field);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], value.Index);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], value.A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], value.B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], value.C);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[32..], value.D);
        _catalogCount += 40;
        _pipelines[builder.Index].MetadataWords++;
        return true;
    }

    internal bool ReserveCreationPhase(PostFrameCreationBuilder builder, long bytes)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) return false;
        if (_armed) { Refuse(PostFrameTapeRefusal.ArmedFactory); return false; }
        if (_disposed || !builder.BelongsTo(this) || builder.Index != _openCreation || _refusal != PostFrameTapeRefusal.None) return false;
        if (bytes < 0 || bytes > _catalog.Length - _catalogCount)
        { Refuse(PostFrameTapeRefusal.CatalogCapacity); return false; }
        return true;
    }

    internal bool FinishCreation(PostFrameCreationBuilder builder, ReadOnlySpan<InputElementDescription> elements,
        ReadOnlySpan<byte> vertexShader, ReadOnlySpan<byte> pixelShader, out PostFrameFrozenCreation frozen)
    {
        frozen = default;
        if (Environment.CurrentManagedThreadId != _ownerThread) return false;
        if (_armed) { Refuse(PostFrameTapeRefusal.ArmedFactory); return false; }
        if (_disposed || !builder.BelongsTo(this) || builder.Index != _openCreation || _refusal != PostFrameTapeRefusal.None) return false;
        long semantics = 0;
        foreach (var element in elements)
        {
            if (!PostFrameNativeCreationEncoder.SemanticNameSupported(element.SemanticName))
            { Refuse(PostFrameTapeRefusal.InvalidCatalog); return false; }
            semantics += Encoding.UTF8.GetByteCount(element.SemanticName);
        }
        long required = semantics + vertexShader.Length + pixelShader.Length;
        if (vertexShader.IsEmpty || pixelShader.IsEmpty || required > _catalog.Length - _catalogCount)
        { Refuse(PostFrameTapeRefusal.CatalogCapacity); return false; }
        foreach (var element in elements)
            _catalogCount += Encoding.UTF8.GetBytes(element.SemanticName, _catalog.AsSpan(_catalogCount));
        ref PostFrameTapeCatalogIndex entry = ref _pipelines[builder.Index];
        entry.VertexStart = (uint)_catalogCount; entry.VertexBytes = (uint)vertexShader.Length;
        vertexShader.CopyTo(_catalog.AsSpan(_catalogCount)); _catalogCount += vertexShader.Length;
        entry.PixelStart = (uint)_catalogCount; entry.PixelBytes = (uint)pixelShader.Length;
        pixelShader.CopyTo(_catalog.AsSpan(_catalogCount)); _catalogCount += pixelShader.Length;
        entry.Length = (uint)(_catalogCount - (int)entry.Start);
        _openCreation = -1;
        frozen = new(this, builder.Index);
        return true;
    }

    internal void AbortCreation(PostFrameCreationBuilder builder)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread || !builder.BelongsTo(this) || builder.Index != _openCreation) return;
        _openCreation = -1;
        Refuse(PostFrameTapeRefusal.InvalidCatalog);
    }

    internal bool TryFreezeCreation(ReadOnlySpan<PostFrameCreationValue> metadata, ReadOnlySpan<byte> vertexShader,
        ReadOnlySpan<byte> pixelShader, out PostFrameFrozenCreation frozen, ReadOnlySpan<byte> semanticNamesUtf8 = default)
    {
        frozen = default;
        if (_armed) { Refuse(PostFrameTapeRefusal.ArmedFactory); return false; }
        if (_disposed || _attachmentClosed || _refusal != PostFrameTapeRefusal.None || Environment.CurrentManagedThreadId != _ownerThread) return false;
        if (_openCreation >= 0) { Refuse(PostFrameTapeRefusal.InvalidCatalog); return false; }
        long bytes = (long)metadata.Length * 40 + semanticNamesUtf8.Length + vertexShader.Length + pixelShader.Length;
        if (metadata.IsEmpty || vertexShader.IsEmpty || pixelShader.IsEmpty || _pipelineCount >= _pipelines.Length ||
            bytes > _catalog.Length - _catalogCount)
        { Refuse(PostFrameTapeRefusal.CatalogCapacity); return false; }
        foreach (PostFrameCreationValue value in metadata)
            if (value.Field is < PostFrameCreationField.RootFlags or > PostFrameCreationField.CachedPipelineState)
            { Refuse(PostFrameTapeRefusal.InvalidCatalog); return false; }
        int index = _pipelineCount++, start = _catalogCount;
        foreach (PostFrameCreationValue value in metadata)
        {
            Span<byte> target = _catalog.AsSpan(_catalogCount, 40);
            BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)value.Field);
            BinaryPrimitives.WriteUInt32LittleEndian(target[4..], value.Index);
            BinaryPrimitives.WriteUInt64LittleEndian(target[8..], value.A);
            BinaryPrimitives.WriteUInt64LittleEndian(target[16..], value.B);
            BinaryPrimitives.WriteUInt64LittleEndian(target[24..], value.C);
            BinaryPrimitives.WriteUInt64LittleEndian(target[32..], value.D);
            _catalogCount += 40;
        }
        semanticNamesUtf8.CopyTo(_catalog.AsSpan(_catalogCount));
        _catalogCount += semanticNamesUtf8.Length;
        int vertexStart = _catalogCount;
        vertexShader.CopyTo(_catalog.AsSpan(_catalogCount));
        _catalogCount += vertexShader.Length;
        int pixelStart = _catalogCount;
        pixelShader.CopyTo(_catalog.AsSpan(_catalogCount));
        _catalogCount += pixelShader.Length;
        _pipelines[index] = new() { Id = (uint)index + 1, Start = (uint)start, Length = (uint)(_catalogCount - start),
            MetadataWords = (uint)metadata.Length, VertexStart = (uint)vertexStart, VertexBytes = (uint)vertexShader.Length,
            PixelStart = (uint)pixelStart, PixelBytes = (uint)pixelShader.Length };
        frozen = new(this, index);
        return true;
    }

    internal bool CommitCreation(PostFrameFrozenCreation frozen, object actualPipeline, object actualRoot,
        object actualLifetime, out uint pipelineIdentity, out uint rootIdentity, PostFrameTapeRequiredState required = default,
        PostFrameTapeCreationRole role = PostFrameTapeCreationRole.Ordinary)
    {
        pipelineIdentity = rootIdentity = 0;
        if (_armed) { Refuse(PostFrameTapeRefusal.ArmedFactory); return false; }
        if (_disposed || _attachmentClosed || _refusal != PostFrameTapeRefusal.None || Environment.CurrentManagedThreadId != _ownerThread ||
            actualPipeline is null || actualRoot is null || actualLifetime is null || !frozen.BelongsTo(this) ||
            frozen.Index < 0 || frozen.Index >= _pipelineCount) return false;
        ref PostFrameTapeCatalogIndex entry = ref _pipelines[frozen.Index];
        if (_openCreation >= 0 || entry.Length == 0 || entry.MetadataWords == 0 || entry.VertexBytes == 0 || entry.PixelBytes == 0 ||
            entry.PipelineIdentity != 0 || entry.RootIdentity != 0 || _identityCount > _identities.Length - 2)
        { Refuse(PostFrameTapeRefusal.InvalidCatalog); return false; }
        pipelineIdentity = RegisterIdentity(actualPipeline, actualLifetime, PostFrameTapeIdentityKind.Pipeline, 0, 0, 0, 0, 0, 0);
        rootIdentity = RegisterIdentity(actualRoot, actualLifetime, PostFrameTapeIdentityKind.RootSignature, 0, 0, 0, 0, 0, 0);
        if (pipelineIdentity == 0 || rootIdentity == 0) return false;
        entry.PipelineIdentity = pipelineIdentity;
        entry.RootIdentity = rootIdentity;
        if ((required.RootParameters >> 23) != 0 || (required.TextureSlots & required.RetainedTargetTextureSlots) != 0 ||
            role is < PostFrameTapeCreationRole.Ordinary or > PostFrameTapeCreationRole.SceneDepthReceiver ||
            (required.RetainedTargetTextureSlots != 0 && (required.RetainedTargetTextureSlots != 1 << 15 || role == PostFrameTapeCreationRole.Ordinary)))
        { Refuse(PostFrameTapeRefusal.InvalidCatalog); return false; }
        entry.Reserved0 = required.VertexSlots | ((ulong)required.TextureSlots << 32) | (required.DepthTarget ? 1UL << 48 : 0) | ((ulong)role << 49);
        entry.Reserved1 = required.RootParameters | ((ulong)required.RetainedTargetTextureSlots << 32);
        return true;
    }

    internal void CloseAttachment()
    {
        if (Environment.CurrentManagedThreadId == _ownerThread) _attachmentClosed = true;
    }
    internal ReadOnlySpan<byte> CreationMetadata(int index) => _catalog.AsSpan((int)_pipelines[index].Start, checked((int)_pipelines[index].MetadataWords * 40));
    internal ReadOnlySpan<byte> CreationSemantics(int index)
    {
        int start = checked((int)_pipelines[index].Start + (int)_pipelines[index].MetadataWords * 40);
        return _catalog.AsSpan(start, (int)_pipelines[index].VertexStart - start);
    }
    internal ReadOnlySpan<byte> CreationShader(int index, bool pixel) => pixel ?
        _catalog.AsSpan((int)_pipelines[index].PixelStart, (int)_pipelines[index].PixelBytes) :
        _catalog.AsSpan((int)_pipelines[index].VertexStart, (int)_pipelines[index].VertexBytes);
    internal ReadOnlyMemory<byte> CreationShaderMemory(int index, bool pixel) => pixel ?
        _catalog.AsMemory((int)_pipelines[index].PixelStart, (int)_pipelines[index].PixelBytes) :
        _catalog.AsMemory((int)_pipelines[index].VertexStart, (int)_pipelines[index].VertexBytes);

    internal PostFrameTapeCompletion ObserveOriginalCompletion(PostFrameTapeInvocation token, PostFrameTapeFrame frame,
        bool bodySucceeded, bool cleanupSucceeded)
    {
        if (!token.BelongsTo(this, _invocation) || Environment.CurrentManagedThreadId != _ownerThread || !_armed || _completionObserved)
            return default;
        _completionObserved = true;
        if (!bodySucceeded) Refuse(PostFrameTapeRefusal.OriginalFailure);
        if (!cleanupSucceeded) Refuse(PostFrameTapeRefusal.CleanupFailure);
        bool valid = bodySucceeded && cleanupSucceeded && frame.CapturedSlot < 3 && frame.Submission > 0 &&
            frame.SubmittedFence > 0 && frame.CompletedFence != ulong.MaxValue && frame.CompletedFence >= frame.SubmittedFence && frame.DeviceGeneration > 0 &&
            frame.ResourceGeneration > 0 && frame.PresentedBefore != ulong.MaxValue && frame.PresentedAfter == frame.PresentedBefore + 1;
        if (_previousFrame is { } previous)
            valid &= frame.Submission > previous.Submission && frame.SubmittedFence > previous.SubmittedFence &&
                frame.CompletedFence >= previous.CompletedFence && frame.DeviceGeneration == previous.DeviceGeneration &&
                frame.ResourceGeneration == previous.ResourceGeneration && frame.PresentedBefore >= previous.PresentedAfter;
        if (!valid) Refuse(PostFrameTapeRefusal.InvalidTransition);
        return new(this, _invocation, valid ? frame : null);
    }

    internal bool Seal(PostFrameTapeInvocation token, object device, Guid session, int ownerThread, PostFrameTapeCompletion completion)
    {
        if (!Current(token, device, session, ownerThread) || !_armed || !completion.BelongsTo(this, _invocation) || !_completionObserved) return false;
        _frame = completion.Frame;
        _armed = false;
        _sealed = true;
        // Session identity references persist, with monotone IDs, across the eight invocations.
        // They clear only on terminal cohort abort/Dispose; managed reachability is NOT a native AddRef.
        Array.Clear(_state);
        return _frame is not null;
    }

    internal bool PngClosed(PostFrameTapeInvocation token)
    {
        if (!token.BelongsTo(this, _invocation) || _armed || !_sealed || _frame is null || _disposed ||
            Environment.CurrentManagedThreadId != _ownerThread || _pngClosed) return false;
        _pngClosed = true;
        return true;
    }

    internal PostFrameTapeWire Materialize(PostFrameTapeInvocation token)
    {
        if (!token.BelongsTo(this, _invocation) || !_pngClosed || _armed || _disposed || Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Current sealed original PNG required before formatting.");
        var events = new PostFrameTapeWireEvent[_eventCount];
        for (int i = 0; i < events.Length; i++)
        {
            PostFrameTapeEvent value = _events[i];
            events[i] = ToWire(value);
        }
        byte[] roots = new byte[checked(_rootCount * 4)];
        for (int i = 0; i < _rootCount; i++) BinaryPrimitives.WriteUInt32LittleEndian(roots.AsSpan(i * 4), _roots[i]);
        var creations = new PostFrameTapeWireCreation[_pipelineCount];
        for (int i = 0; i < creations.Length; i++)
        {
            ReadOnlySpan<byte> bytes = CreationMetadata(i);
            var values = new PostFrameCreationValue[bytes.Length / 40];
            for (int j = 0; j < values.Length; j++)
            {
                ReadOnlySpan<byte> value = bytes.Slice(j * 40, 40);
                values[j] = new((PostFrameCreationField)BinaryPrimitives.ReadUInt32LittleEndian(value),
                    BinaryPrimitives.ReadUInt32LittleEndian(value[4..]), BinaryPrimitives.ReadUInt64LittleEndian(value[8..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(value[16..]), BinaryPrimitives.ReadUInt64LittleEndian(value[24..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(value[32..]));
            }
            PostFrameTapeCatalogIndex catalog = _pipelines[i];
            creations[i] = new(catalog.Id, values, Convert.ToBase64String(CreationSemantics(i)), catalog.PipelineIdentity, catalog.RootIdentity,
                new((uint)catalog.Reserved0, (ushort)(catalog.Reserved0 >> 32), (uint)catalog.Reserved1,
                    (catalog.Reserved0 & (1UL << 48)) != 0, (ushort)(catalog.Reserved1 >> 32)), (int)_pipelines[i].VertexBytes,
                Convert.ToHexStringLower(SHA256.HashData(CreationShader(i, false))), (int)_pipelines[i].PixelBytes,
                Convert.ToHexStringLower(SHA256.HashData(CreationShader(i, true))), (PostFrameTapeCreationRole)((catalog.Reserved0 >> 49) & 3));
        }
        var identities = new PostFrameTapeWireIdentity[_identityCount];
        for (int i = 0; i < identities.Length; i++)
        {
            PostFrameTapeIdentity value = _identities[i];
            identities[i] = new(value.Id, (PostFrameTapeIdentityKind)value.Kind, _lifetimes[i] is not null,
                value.Epoch, value.Slot, value.Descriptor, value.Offset, value.Bytes, value.Stride);
        }
        var result = new PostFrameTapeWire(2, "post-final-color-causal-witness", false, "pure-contract-unhooked-no-late-copy-proof",
            _session, _ownerThread, _repetition, _invocation, _refusal, _frame, events, _rootCount, roots.Length,
            Convert.ToBase64String(roots), Convert.ToHexStringLower(SHA256.HashData(roots)), creations, identities);
        _materialized = true;
        return result;
    }

    internal bool ConfirmEvidenceExported(PostFrameTapeInvocation token)
    {
        if (!Current(token, _device, _session, _ownerThread) || !_pngClosed || !_materialized || _armed || _evidenceExported) return false;
        _evidenceExported = true;
        return true;
    }

    // Explicit PURE MODEL finalization only. Native integration must supply independently validated
    // actual late-copy/export receipts before introducing a production finalization seam. Complete stays false.
    internal bool FinalizePureModel(PostFrameTapeInvocation token, PostFrameTapeCompletion completion)
    {
        if (!Current(token, _device, _session, _ownerThread) || !_evidenceExported || !_pngClosed || _armed ||
            !completion.BelongsTo(this, _invocation) || completion.Frame is null || completion.Frame != _frame ||
            _refusal != PostFrameTapeRefusal.None || _finalized) return false;
        _previousFrame = _frame;
        _finalized = true;
        return true;
    }

    internal PostFrameTapeWire CompleteNativeWitness(PostFrameTapeInvocation token, PostFrameTapeCompletion completion,
        GraphicsPostFrameReceipt receipt, GraphicsPostFrameSnapshotResult result, PostFrameTapeWire materialized)
    {
        if (!Current(token, receipt.Owner, _session, receipt.Original.ManagedOwnerThreadId) || !_pngClosed || !_materialized || _armed ||
            !completion.BelongsTo(this, _invocation) || completion.Frame is not { } frame || frame != _frame ||
            _refusal != PostFrameTapeRefusal.None || !receipt.Consumed || !ReferenceEquals(result.CompletionOwner, _device) ||
            !ReferenceEquals(result.OriginalFrameReceipt, receipt.Original) || receipt.Original.CoverageMode != GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments ||
            receipt.Original.Invocation != _invocation || receipt.Original.FrameIndex != frame.CapturedSlot || receipt.Original.Submission.Submission != frame.Submission ||
            receipt.Original.Submission.SubmittedFence != frame.SubmittedFence || receipt.Original.CompletedFence != frame.CompletedFence ||
            receipt.Original.Submission.Generation != frame.DeviceGeneration || receipt.Original.Submission.ResourceGeneration != frame.ResourceGeneration ||
            receipt.Original.PresentedBefore != frame.PresentedBefore || receipt.Original.PresentedAfter != frame.PresentedAfter ||
            receipt.Original.DeviceIdentity.Generation != frame.DeviceGeneration ||
            !Guid.TryParseExact(receipt.Original.DeviceIdentity.SessionId, "N", out var session) || session != _session ||
            !result.CopyReceipt.MayHaveExecuted || !result.CopyReceipt.CompletionProved || result.CopyReceipt.SubmittedFence == 0 ||
            result.CopyReceipt.CompletedFence is 0 or ulong.MaxValue || result.CopyReceipt.CompletedFence < result.CopyReceipt.SubmittedFence ||
            materialized.Invocation != _invocation || materialized.Refusal != PostFrameTapeRefusal.None || materialized.OriginalFrame != frame)
            throw new InvalidOperationException("Opaque witness lacks the current original and actual completed native copy proof.");
        return materialized with { Complete = true, EvidenceScope = "source-closed-cpu-effective-opaque-state-and-after-PNG-retained-targets; not GPU shader fetch proof" };
    }

    internal bool FinalizeNativeExport(PostFrameTapeInvocation token, PostFrameTapeCompletion completion,
        GraphicsPostFrameReceipt receipt, GraphicsPostFrameSnapshotResult result, PostFrameTapeWire witness)
    {
        _ = CompleteNativeWitness(token, completion, receipt, result, witness);
        if (!witness.Complete || !_evidenceExported || _finalized) return false;
        _previousFrame = _frame;
        _finalized = true;
        return true;
    }

    private static PostFrameTapeWireEvent ToWire(PostFrameTapeEvent value)
    {
        if (value.Opcode is PostFrameTapeOpcode.Draw or PostFrameTapeOpcode.DrawIndexed)
            return new(value.Sequence, value.Opcode, value.Provenance, value.PassEpoch,
                Draw: new(value.Opcode == PostFrameTapeOpcode.DrawIndexed, (uint)value.A, (uint)value.B,
                    (uint)value.C, unchecked((int)(uint)value.D), (uint)value.E));
        if (value.Opcode == PostFrameTapeOpcode.RootWords)
            return new(value.Sequence, value.Opcode, value.Provenance, value.PassEpoch,
                RootWords: new(value.Id, (uint)value.A, (uint)value.B, value.Flags == 1, value.Auxiliary, value.SecondId, value.C, value.D,
                    value.Flags == 1 ? (PostFrameTapeOwnerPostcondition)value.E : null));
        if (value.Opcode == PostFrameTapeOpcode.PassBoundary)
            return new(value.Sequence, value.Opcode, value.Provenance, value.PassEpoch,
                Pass: new((PostFrameTapePass)value.Id, value.Flags == 0));
        if (value.Opcode is PostFrameTapeOpcode.Reset or PostFrameTapeOpcode.RootInvalidation)
            return new(value.Sequence, value.Opcode, value.Provenance, value.PassEpoch);
        if ((value.Opcode is PostFrameTapeOpcode.Request or PostFrameTapeOpcode.NativeReturn or PostFrameTapeOpcode.OwnerPostcondition) &&
            value.Id == (uint)PostFrameTapeBinding.Viewport && value.Flags == 2)
            return new(value.Sequence, value.Opcode, value.Provenance, value.PassEpoch,
                Viewport: new((uint)value.A, (uint)(value.A >> 32), (uint)value.B, (uint)(value.B >> 32), (uint)value.C, (uint)(value.C >> 32),
                    value.Opcode == PostFrameTapeOpcode.OwnerPostcondition ? (PostFrameTapeOwnerPostcondition)value.Auxiliary : null));
        return new(value.Sequence, value.Opcode, value.Provenance, value.PassEpoch,
            Binding: new((PostFrameTapeBinding)value.Id, value.SecondId, value.A, value.B, value.C, value.D, value.E,
                value.Opcode == PostFrameTapeOpcode.OwnerPostcondition ? (PostFrameTapeOwnerPostcondition)value.Auxiliary : null));
    }

    private sealed record FailureAttachment(GraphicsPostFrameCommandTape Owner, ulong Invocation,
        Guid Session, int OwnerThread, int Repetition, PostFrameTapeRefusal Refusal, PostFrameTapeFrame? Frame);

    // Outside the armed interval only. Formatting/report failures must never replace the source exception.
    internal void AttachFailure(Exception original, PostFrameTapeInvocation token)
    {
        if (_armed || _disposed || !token.BelongsTo(this, _invocation) || Environment.CurrentManagedThreadId != _ownerThread) return;
        try { original.Data[PrefixKey] = new FailureAttachment(this, _invocation, _session, _ownerThread, _repetition, _refusal, _frame); }
        catch { /* Best effort; original source exception remains authoritative. */ }
    }

    internal bool HasCurrentFailure(Exception original, PostFrameTapeInvocation token)
    {
        if (_armed || _disposed || !token.BelongsTo(this, _invocation) || Environment.CurrentManagedThreadId != _ownerThread) return false;
        try
        {
            return original.Data[PrefixKey] is FailureAttachment value && ReferenceEquals(value.Owner, this) &&
                value.Invocation == _invocation && value.Session == _session && value.OwnerThread == _ownerThread && value.Repetition == _repetition;
        }
        catch { return false; }
    }

    private void ClearWitnesses()
    {
        Array.Clear(_witnesses);
        Array.Clear(_lifetimes);
        _identityCount = 0;
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) return;
        _armed = false;
        _disposed = true;
        _frame = null;
        ClearSourceScratch();
        ClearWitnesses();
    }
}

// Setup-only encoder of the SAME method-private native inputs passed to Vortice. It never reads
// caller GraphicsPipelineDescription after the private native graph has been constructed.
internal static class PostFrameNativeCreationEncoder
{
    // Pinned Vortice marshals native semantic names with StringToHGlobalAnsi. Only this
    // nonempty, non-NUL ASCII domain is byte-identical to the catalog's UTF8 encoding.
    internal static bool SemanticNameSupported(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (char character in value) if (character is '\0' or > '\x7f') return false;
        return true;
    }
    private static ulong Pair(uint low, uint high) => low | ((ulong)high << 32);
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static uint Bool(bool value) => value ? 1U : 0U;
    private static bool Put(PostFrameCreationBuilder builder, PostFrameCreationField field, uint index,
        uint a = 0, uint b = 0, uint c = 0, uint d = 0, uint e = 0, uint f = 0, uint g = 0, uint h = 0)
        => builder.Add(new(field, index, Pair(a, b), Pair(c, d), Pair(e, f), Pair(g, h)));

    internal static bool Root(PostFrameCreationBuilder builder, RootSignatureDescription1 root,
        PostFrameTapeCreationRole role, out uint rootMask, out ushort textures, out ushort retainedTargets)
    {
        rootMask = 0; textures = retainedTargets = 0;
        uint rangeOrdinal = 0;
        var parameters = root.Parameters ?? [];
        if (parameters.Length > 23) return false;
        long rows = 1 + parameters.Length + (root.StaticSamplers?.Length ?? 0) * 2L;
        foreach (var parameter in parameters)
        {
            if (parameter.ParameterType is RootParameterType.Constant32Bits or RootParameterType.ConstantBufferView) rows++;
            else if (parameter.ParameterType == RootParameterType.DescriptorTable && parameter.DescriptorTable?.Ranges is { } ranges) rows += ranges.Length;
            else return false;
        }
        if (!builder.Reserve(rows * 40) || !Put(builder, PostFrameCreationField.RootFlags, 0, (uint)root.Flags)) return false;
        for (uint index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (!Put(builder, PostFrameCreationField.RootParameter, index, (uint)parameter.ParameterType, (uint)parameter.ShaderVisibility)) return false;
            switch (parameter.ParameterType)
            {
                case RootParameterType.Constant32Bits:
                    var constants = parameter.Constants;
                    rootMask |= 1U << (int)index;
                    if (!Put(builder, PostFrameCreationField.RootConstants, index, constants.ShaderRegister, constants.RegisterSpace, constants.Num32BitValues)) return false;
                    break;
                case RootParameterType.ConstantBufferView:
                    var descriptor = parameter.Descriptor;
                    rootMask |= 1U << (int)index;
                    if (!Put(builder, PostFrameCreationField.RootDescriptor, index, descriptor.ShaderRegister, descriptor.RegisterSpace, (uint)descriptor.Flags)) return false;
                    break;
                case RootParameterType.DescriptorTable:
                    if (parameter.DescriptorTable?.Ranges is not { } ranges || ranges.Length == 0) return false;
                    foreach (var range in ranges)
                    {
                        if (range.RangeType != DescriptorRangeType.ShaderResourceView || range.RegisterSpace != 0 ||
                            range.NumDescriptors != 1 || range.BaseShaderRegister >= 16 || parameter.ShaderVisibility != ShaderVisibility.Pixel)
                            return false;
                        ushort bit = (ushort)(1U << (int)range.BaseShaderRegister);
                        if (range.BaseShaderRegister == 15 && role != PostFrameTapeCreationRole.Ordinary) retainedTargets |= bit;
                        else textures |= bit;
                        if (!Put(builder, PostFrameCreationField.DescriptorRange, rangeOrdinal++, index, (uint)range.RangeType,
                            range.NumDescriptors, range.BaseShaderRegister, range.RegisterSpace, range.OffsetInDescriptorsFromTableStart, (uint)range.Flags)) return false;
                    }
                    break;
                default: return false;
            }
        }
        var samplers = root.StaticSamplers ?? [];
        for (uint index = 0; index < samplers.Length; index++)
        {
            var sampler = samplers[index];
            if (!Put(builder, PostFrameCreationField.StaticSampler, index * 2, (uint)sampler.Filter,
                (uint)sampler.AddressU, (uint)sampler.AddressV, (uint)sampler.AddressW, Bits(sampler.MipLODBias),
                sampler.MaxAnisotropy, (uint)sampler.ComparisonFunction, (uint)sampler.BorderColor) ||
                !Put(builder, PostFrameCreationField.StaticSampler, index * 2 + 1, Bits(sampler.MinLOD), Bits(sampler.MaxLOD),
                    sampler.ShaderRegister, sampler.RegisterSpace, (uint)sampler.ShaderVisibility)) return false;
        }
        return true;
    }

    internal static bool Pipeline(PostFrameCreationBuilder builder, GraphicsPipelineStateDescription native,
        out uint vertexMask)
    {
        vertexMask = 0;
        if (!native.HullShader.IsEmpty || !native.DomainShader.IsEmpty || !native.GeometryShader.IsEmpty || native.StreamOutput is not null ||
            native.CachedPSO.CachedBlob != IntPtr.Zero || native.CachedPSO.CachedBlobSizeInBytes != 0 || native.RenderTargetFormats.Length > 8) return false;
        var inputElements = native.InputLayout?.Elements ?? [];
        long totalSemanticBytes = 0;
        foreach (var element in inputElements)
        {
            if (!SemanticNameSupported(element.SemanticName)) return false;
            totalSemanticBytes += Encoding.UTF8.GetByteCount(element.SemanticName);
        }
        long phaseBytes = (30L + native.RenderTargetFormats.Length + inputElements.Length) * 40 +
            totalSemanticBytes + native.VertexShader.Length + native.PixelShader.Length;
        if (!builder.Reserve(phaseBytes)) return false;
        if (!Put(builder, PostFrameCreationField.PipelineHeader, 0, (uint)native.RenderTargetFormats.Length,
            unchecked((uint)native.NodeMask), (uint)native.IndexBufferStripCutValue, (uint)native.Flags) ||
            !Put(builder, PostFrameCreationField.OptionalShaderStages, 0) || !Put(builder, PostFrameCreationField.StreamOutput, 0) ||
            !Put(builder, PostFrameCreationField.CachedPipelineState, 0) ||
            !Put(builder, PostFrameCreationField.PrimitiveTopology, 0, (uint)native.PrimitiveTopologyType) ||
            !Put(builder, PostFrameCreationField.PipelineFlags, 0, (uint)native.Flags) ||
            !Put(builder, PostFrameCreationField.SampleMask, 0, native.SampleMask) ||
            !Put(builder, PostFrameCreationField.DepthFormat, 0, (uint)native.DepthStencilFormat) ||
            !Put(builder, PostFrameCreationField.Sample, 0, native.SampleDescription.Count, native.SampleDescription.Quality)) return false;
        for (uint index = 0; index < native.RenderTargetFormats.Length; index++)
            if (!Put(builder, PostFrameCreationField.RenderTargetFormat, index, (uint)native.RenderTargetFormats[index])) return false;
        var blend = native.BlendState;
        if (!Put(builder, PostFrameCreationField.BlendTarget, 16, Bool(blend.AlphaToCoverageEnable), Bool(blend.IndependentBlendEnable))) return false;
        for (uint index = 0; index < 8; index++)
        {
            var target = blend.RenderTarget[(int)index];
            if (!Put(builder, PostFrameCreationField.BlendTarget, index * 2, Bool(target.BlendEnable), Bool(target.LogicOpEnable),
                (uint)target.SourceBlend, (uint)target.DestinationBlend, (uint)target.BlendOperation,
                (uint)target.SourceBlendAlpha, (uint)target.DestinationBlendAlpha, (uint)target.BlendOperationAlpha) ||
                !Put(builder, PostFrameCreationField.BlendTarget, index * 2 + 1, (uint)target.LogicOp, (uint)target.RenderTargetWriteMask)) return false;
        }
        var raster = native.RasterizerState;
        if (!Put(builder, PostFrameCreationField.Rasterizer, 0, (uint)raster.FillMode, (uint)raster.CullMode,
            Bool(raster.FrontCounterClockwise), unchecked((uint)raster.DepthBias), Bits(raster.DepthBiasClamp),
            Bits(raster.SlopeScaledDepthBias), Bool(raster.DepthClipEnable), Bool(raster.MultisampleEnable)) ||
            !Put(builder, PostFrameCreationField.Rasterizer, 1, Bool(raster.AntialiasedLineEnable), raster.ForcedSampleCount, (uint)raster.ConservativeRaster)) return false;
        var depth = native.DepthStencilState;
        if (!Put(builder, PostFrameCreationField.DepthStencil, 0, Bool(depth.DepthEnable), (uint)depth.DepthWriteMask, (uint)depth.DepthFunc,
            Bool(depth.StencilEnable), depth.StencilReadMask, depth.StencilWriteMask, (uint)depth.FrontFace.StencilFailOp, (uint)depth.FrontFace.StencilDepthFailOp) ||
            !Put(builder, PostFrameCreationField.DepthStencil, 1, (uint)depth.FrontFace.StencilPassOp, (uint)depth.FrontFace.StencilFunc,
                (uint)depth.BackFace.StencilFailOp, (uint)depth.BackFace.StencilDepthFailOp, (uint)depth.BackFace.StencilPassOp, (uint)depth.BackFace.StencilFunc)) return false;
        uint semanticOffset = 0;
        var elements = native.InputLayout?.Elements ?? [];
        for (uint index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            if (element.Slot >= 32) return false;
            vertexMask |= 1U << (int)element.Slot;
            int semanticBytes = Encoding.UTF8.GetByteCount(element.SemanticName);
            if (!Put(builder, PostFrameCreationField.InputElement, index, semanticOffset, (uint)semanticBytes,
                element.SemanticIndex, (uint)element.Format, element.AlignedByteOffset, element.Slot, (uint)element.Classification, element.InstanceDataStepRate)) return false;
            semanticOffset = checked(semanticOffset + (uint)semanticBytes);
        }
        return true;
    }
}
