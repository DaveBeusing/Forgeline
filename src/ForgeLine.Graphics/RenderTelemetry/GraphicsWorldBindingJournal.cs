using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ForgeLine.Graphics;

public sealed record GraphicsWorldPipelineBinding(string VertexShaderSha256, string PixelShaderSha256,
    string VertexEntryPoint, string PixelEntryPoint, string VertexSourceName, string PixelSourceName,
    GraphicsVertexElement[] VertexElements, int VertexRootConstantCount, int PixelTextureCount,
    GraphicsPrimitiveTopology PrimitiveTopology, GraphicsCullMode CullMode, bool DepthEnabled, bool DepthAlwaysPass,
    bool AlphaBlendEnabled, bool SceneDepthReceiver, bool VertexConstantsInBuffer, bool DirectionalShadowReceiver,
    GraphicsFrameTargetFormat TargetFormat);
public sealed record GraphicsWorldBindingDefinition(ulong Id, string Kind, GraphicsBufferDescription? Buffer = null,
    GraphicsTextureDescription? Texture = null, int? Descriptor = null, GraphicsWorldPipelineBinding? Pipeline = null);
public sealed record GraphicsWorldImplicitBinding(string Kind, ulong ResourceGeneration, int FrameIndex,
    int? Descriptor, int? RootParameter, int? TextureRegister, int ConstantByteOffset, int? TextureRootParameter = null);
public sealed record GraphicsWorldBindingEvent(int Sequence, string Kind, ulong? IdentityId = null, int? Slot = null,
    int? Stride = null, int? Offset = null, ulong? RangeBytes = null, string? IndexFormat = null, int[]? DrawArguments = null,
    string? ConstantBytesBase64 = null, string? ConstantSha256 = null, string? ConstantMode = null, bool? Emitted = null,
    GraphicsWorldImplicitBinding? Implicit = null, float[]? Viewport = null, int[]? Scissor = null, int? DrawOrdinal = null);
public sealed record GraphicsWorldBindingJournal(int SchemaVersion, string Kind, bool Complete, string? Failure,
    ulong DeviceGeneration, ulong ResourceGeneration, string DeviceSession, int FrameIndex,
    GraphicsFrameSubmission? Submission, ulong? CompletedFence, ulong PresentedBefore, ulong? PresentedAfter,
    int DrawCount, long AccountedPayloadBytes, GraphicsWorldBindingDefinition[] Definitions, GraphicsWorldBindingEvent[] Events)
{
    public const int DrawCapacity = 4096, EventCapacity = 131072, IdentityCapacity = 4096;
    public const long PayloadCapacity = 32L * 1024 * 1024;
    public const int EnvelopeCapacity = 1024 * 1024;
    public const long JournalCapacity = PayloadCapacity - EnvelopeCapacity;
    private static readonly JsonSerializerOptions Wire = CreateWireOptions();
    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    public static byte[] SerializeSidecar(object value) => JsonSerializer.SerializeToUtf8Bytes(value, Wire);

    private const string FailureKey = "ForgeLine.Graphics.WorldBindingJournal.v1";
    private sealed record FailureAttachment(GraphicsWorldBindingAttempt Attempt, GraphicsWorldBindingJournal Prefix);
    public static GraphicsWorldBindingAttempt BeginAttempt() => new();
    public static GraphicsWorldBindingJournal? FailurePrefix(Exception error, GraphicsWorldBindingAttempt? attempt = null)
    {
        if (attempt is null || !attempt.Claimed) return null;
        return error.Data[FailureKey] is FailureAttachment attachment && ReferenceEquals(attachment.Attempt, attempt)
            ? attachment.Prefix : null;
    }
    internal static void AttachFailure(Exception error, GraphicsWorldBindingJournal prefix, GraphicsWorldBindingAttempt? attempt)
    {
        if (attempt is null || !attempt.Claimed) return;
        try { error.Data[FailureKey] = new FailureAttachment(attempt, prefix); }
        catch { }
    }

}

// Opaque invocation ownership only; never serialized or used as a device/resource identity.
public sealed class GraphicsWorldBindingAttempt : IDisposable
{
    [ThreadStatic] private static GraphicsWorldBindingAttempt? _current;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    internal bool Claimed { get; private set; }
    internal GraphicsWorldBindingAttempt()
    {
        if (_current is not null) throw new InvalidOperationException("World capture attempt already active.");
        _current = this;
    }
    internal static GraphicsWorldBindingAttempt? ClaimCurrent()
    {
        var attempt = _current;
        if (attempt is null) return null;
        if (attempt._owner != Environment.CurrentManagedThreadId || attempt.Claimed)
            throw new InvalidOperationException("World capture attempt cannot be reused.");
        attempt.Claimed = true;
        return attempt;
    }
    public void Dispose()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("World capture attempt belongs to another owner.");
        if (ReferenceEquals(_current, this)) _current = null;
    }
}

// Weak existing Lifetime keys only. No native wrapper/resource is retained or assigned ownership.
internal sealed class GraphicsWorldBindingRegistry
{
    private readonly WeakReference<object>?[] _keys = new WeakReference<object>[GraphicsWorldBindingJournal.IdentityCapacity];
    private readonly ulong[] _ids = new ulong[GraphicsWorldBindingJournal.IdentityCapacity];
    private ulong _next;
    internal ulong Identity(object key)
    {
        int free = -1;
        for (int i = 0; i < _keys.Length; i++)
        {
            if (_keys[i] is { } weak && weak.TryGetTarget(out object? found))
            { if (ReferenceEquals(found, key)) return _ids[i]; }
            else if (free < 0) free = i;
        }
        if (free < 0) throw new InvalidOperationException("World binding identity registry capacity exhausted.");
        ulong identity = checked(_next + 1);
        _next = identity;
        _keys[free] = new WeakReference<object>(key);
        _ids[free] = identity;
        return identity;
    }
}

// Diagnostic-only event reducer. Arrays are bounded and allocated before RenderFrame.
internal sealed class GraphicsWorldBindingRecorder
{
    private readonly GraphicsWorldBindingRegistry _registry;
    private readonly GraphicsWorldBindingDefinition?[] _definitions = new GraphicsWorldBindingDefinition[GraphicsWorldBindingJournal.IdentityCapacity];
    private readonly GraphicsWorldBindingEvent?[] _events = new GraphicsWorldBindingEvent[GraphicsWorldBindingJournal.EventCapacity];
    private readonly bool[] _vertexSlots = new bool[32];
    private readonly ulong?[] _textureSlots = new ulong?[16];
    private ulong? _pipelineId;
    private readonly ulong _generation, _resource;
    private readonly string _session;
    private readonly int _frame;
    private readonly ulong _presentedBefore, _priorSubmission, _priorFence;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private int _definitionCount, _eventCount, _drawCount;
    private long _bytes = 4096;
    private bool _invalid, _sealed, _index;
    private GraphicsWorldPipelineBinding? _pipeline;
    private bool _constants, _shadow, _shadowFrame, _viewport, _scissor;
    internal GraphicsWorldBindingRecorder(GraphicsWorldBindingRegistry registry, ulong generation, ulong resource,
        string session, int frame, ulong presentedBefore, ulong priorSubmission = 0, ulong priorFence = 0)
    {
        _registry = registry;
        _generation = generation;
        _resource = resource;
        _session = session;
        _frame = frame;
        _presentedBefore = presentedBefore;
        _priorSubmission = priorSubmission;
        _priorFence = priorFence;
    }
    private void Check()
    {
        if (_invalid || _sealed || Environment.CurrentManagedThreadId != _owner)
            throw Invalid("World binding recorder is invalid, sealed or foreign-owner.");
    }
    private InvalidOperationException Invalid(string message)
    {
        _invalid = true;
        return new(message);
    }
    internal void Invalidate() => _invalid = true;
    private void Budget(object value)
    {
        try
        {
            long size = GraphicsWorldBindingJournal.SerializeSidecar(value).Length + 1L;
            if (size > GraphicsWorldBindingJournal.JournalCapacity - _bytes)
                throw Invalid("World binding payload capacity exhausted.");
            _bytes += size;
        }
        catch
        {
            _invalid = true;
            throw;
        }
    }

    private static void ValidateDefinition(GraphicsWorldBindingDefinition value)
    {
        bool buffer = value.Kind == "buffer", texture = value.Kind == "texture", pipeline = value.Kind == "pipeline";
        if ((!buffer && !texture && !pipeline) || (value.Buffer is not null) != buffer ||
            (value.Texture is not null) != texture || (value.Pipeline is not null) != pipeline ||
            value.Descriptor.HasValue != texture)
            throw new InvalidOperationException("World identity kind/definition is malformed.");
        if (value.Buffer is { } b) b.Validate();
        value.Texture?.Validate();
        if (value.Descriptor < 0) throw new InvalidOperationException("World descriptor is negative.");
        if (value.Pipeline is { } p)
        {
            if (p.VertexElements.Length > 64 || p.VertexRootConstantCount is < 0 or > 64 || p.PixelTextureCount is < 0 or > 16 ||
                p.VertexEntryPoint.Length is 0 or > 128 || p.PixelEntryPoint.Length is 0 or > 128 ||
                p.VertexSourceName.Length is 0 or > 512 || p.PixelSourceName.Length is 0 or > 512 ||
                p.VertexShaderSha256.Length != 64 || p.PixelShaderSha256.Length != 64)
                throw new InvalidOperationException("World pipeline metadata exceeds diagnostic bounds.");
            foreach (var element in p.VertexElements)
            {
                element.Validate();
                if (element.SemanticName.Length > 128 || element.InputSlot >= 32)
                    throw new InvalidOperationException("World vertex metadata exceeds diagnostic bounds.");
            }
        }
    }

    internal ulong Define(object key, string kind, GraphicsBufferDescription? buffer = null,
        GraphicsTextureDescription? texture = null, int? descriptor = null, GraphicsWorldPipelineBinding? pipeline = null)
    {
        Check();
        try
        {
            ulong id = _registry.Identity(key);
            var value = new GraphicsWorldBindingDefinition(id, kind, buffer, texture, descriptor, pipeline);
            ValidateDefinition(value);
            for (int i = 0; i < _definitionCount; i++)
            {
                if (_definitions[i]!.Id != id) continue;
                byte[] before = GraphicsWorldBindingJournal.SerializeSidecar(_definitions[i]!);
                byte[] after = GraphicsWorldBindingJournal.SerializeSidecar(value);
                if (!before.AsSpan().SequenceEqual(after))
                    throw Invalid("Existing World Lifetime identity changed its recorded definition.");
                return id;
            }
            if (_definitionCount == _definitions.Length)
                throw Invalid("World binding definition capacity exhausted.");
            Budget(value);
            _definitions[_definitionCount++] = value;
            return id;
        }
        catch
        {
            _invalid = true;
            throw;
        }
    }

    internal void Event(GraphicsWorldBindingEvent value)
    {
        Check();
        if (_eventCount == _events.Length) throw Invalid("World binding event capacity exhausted.");
        if (value.Sequence != _eventCount) throw Invalid("World binding event sequence differs.");
        if (value.Kind is not ("InvalidateRoot" or "Pipeline" or "VertexBuffer" or "IndexBuffer" or "Constants" or "ShadowFrame" or "ShadowReceiver" or "Texture" or "Viewport" or "Scissor" or "DrawIndexedInstanced" or "DrawInstanced")) throw Invalid("Unknown World event.");
        Budget(value); _events[_eventCount++] = value;
    }
    internal int Next => _eventCount;
    internal void InvalidateRoot()
    {
        Check(); _pipeline = null; _pipelineId = null; _constants = false; _shadow = false; Array.Clear(_textureSlots);
        Event(new(Next, "InvalidateRoot"));
    }
    internal void Pipeline(ulong identity, GraphicsWorldPipelineBinding pipeline, bool emitted)
    {
        Check();
        if (!emitted && _pipelineId != identity) throw Invalid("World pipeline cache hit differs from actual previous binding.");
        if (emitted) { _constants = false; _shadow = false; Array.Clear(_textureSlots); }
        _pipelineId = identity;
        _pipeline = pipeline;
        Event(new(Next, "Pipeline", identity, Emitted: emitted));
    }
    internal void Vertex(ulong identity, int slot, int stride, int offset, ulong bytes)
    {
        Check();
        if ((uint)slot >= 32) throw Invalid("World binding vertex slot exceeds D3D12 supported slots.");
        _vertexSlots[slot] = true;
        Event(new(Next, "VertexBuffer", identity, slot, stride, offset, bytes));
    }
    internal void Index(ulong identity, GraphicsIndexFormat format, int offset, ulong bytes)
    { Check(); _index = true;
        Event(new(Next, "IndexBuffer", identity, Offset: offset, RangeBytes: bytes, IndexFormat: format.ToString())); }
    internal void Constants(ReadOnlySpan<float> values, string mode, GraphicsWorldImplicitBinding? implicitBinding = null)
    {
        Check();
        if (values.Length > 64 || _pipeline is null || values.Length != _pipeline.VertexRootConstantCount) throw Invalid("World root constants are missing or malformed.");
        var bytes = MemoryMarshal.AsBytes(values);
        Event(new(Next, "Constants", ConstantBytesBase64: Convert.ToBase64String(bytes), ConstantSha256: Convert.ToHexString(SHA256.HashData(bytes)), ConstantMode: mode, Implicit: implicitBinding)); _constants = true;
    }
    internal void ShadowFrame(ReadOnlySpan<float> values, GraphicsWorldImplicitBinding binding)
    {
        Check();
        if (values.Length != 24) throw Invalid("Implicit shadow frame constants differ.");
        var bytes = MemoryMarshal.AsBytes(values);
        _shadowFrame = true;
        Event(new(Next, "ShadowFrame", ConstantBytesBase64: Convert.ToBase64String(bytes), ConstantSha256: Convert.ToHexString(SHA256.HashData(bytes)), ConstantMode: "implicit-shadow-frame", Implicit: binding));
    }
    internal void ShadowReceiver(GraphicsWorldImplicitBinding binding, bool emitted = true)
    {
        Check();
        if (!_shadowFrame || binding.ResourceGeneration != _resource || binding.FrameIndex != _frame)
            throw Invalid("Implicit shadow binding lacks its actual frame.");
        _shadow = true;
        Event(new(Next, "ShadowReceiver", Implicit: binding, Emitted: emitted));
    }

    internal void Texture(ulong identity, int slot, bool emitted)
    {
        Check();
        if (_pipeline is null || (uint)slot >= (uint)_pipeline.PixelTextureCount || (uint)slot >= 16) throw Invalid("World texture slot lacks actual pipeline.");
        if (!emitted && _textureSlots[slot] != identity) throw Invalid("World texture cache hit differs from actual prior binding.");
        _textureSlots[slot] = identity;
        Event(new(Next, "Texture", identity, slot, Emitted: emitted));
    }
    internal void Viewport(float x, float y, float width, float height)
    { Event(new(Next, "Viewport", Viewport: [x, y, width, height])); _viewport = true; }
    internal void Scissor(int left, int top, int right, int bottom)
    { Event(new(Next, "Scissor", Scissor: [left, top, right, bottom])); _scissor = true; }
    internal int RecordedDrawCount => _drawCount;
    internal void Draw(bool indexed, int count, int instances, int start, int baseVertex, int startInstance)
    {
        Check();
        if (_pipeline is null || !_viewport || !_scissor || (_pipeline.VertexRootConstantCount > 0 && !_constants) ||
            (_pipeline.DirectionalShadowReceiver && !_shadow) || (indexed && !_index))
            throw Invalid("World draw has missing actual pipeline/constants/shadow/index state.");
        foreach (var element in _pipeline.VertexElements)
            if ((uint)element.InputSlot >= 32 || !_vertexSlots[element.InputSlot]) throw Invalid("World draw has missing actual vertex view.");
        for (int i = 0; i < _pipeline.PixelTextureCount; i++)
            if (_textureSlots[i] is null)
                throw Invalid("World draw has missing actual texture state.");
        if (_drawCount == GraphicsWorldBindingJournal.DrawCapacity) throw Invalid("World draw capacity exhausted.");
        Event(new(Next, indexed ? "DrawIndexedInstanced" : "DrawInstanced", DrawArguments: [count, instances, start, baseVertex, startInstance], DrawOrdinal: _drawCount));
        _drawCount++;
    }
    internal GraphicsWorldBindingJournal Finish(GraphicsFrameSubmission submission, ulong completed, int frame, ulong before, ulong after)
    {
        Check();
        if (frame != _frame || submission.Generation != _generation || submission.ResourceGeneration != _resource ||
            submission.Submission != _priorSubmission + 1 || submission.SubmittedFence <= _priorFence || completed < submission.SubmittedFence || completed == ulong.MaxValue ||
            before != _presentedBefore || after != before + 1) throw Invalid("World journal completion tuple differs.");
        _sealed = true;
        return Snapshot(true, null, submission, completed, after);
    }
    internal GraphicsWorldBindingJournal Prefix(Exception error)
    {
        _invalid = true;
        string name = error.GetType().Name;
        if (System.Text.Encoding.UTF8.GetByteCount(name) > 512) name = "exception-type-name-unavailable";
        return Snapshot(false, name, null, null, null);
    }
    private GraphicsWorldBindingJournal Snapshot(bool complete, string? failure, GraphicsFrameSubmission? submission, ulong? completed, ulong? after)
        => new(1, "opaque-world-binding-journal", complete, failure, _generation, _resource, _session, _frame, submission, completed, _presentedBefore, after,
            _drawCount, _bytes, _definitions.AsSpan(0, _definitionCount).ToArray()!, _events.AsSpan(0, _eventCount).ToArray()!);
    internal static GraphicsWorldPipelineBinding Describe(GraphicsPipelineDescription p)
    {
        if (p.VertexElements.Count > 64 || p.VertexShader.SourceName.Length > 512 || p.PixelShader.SourceName.Length > 512 ||
            p.VertexShader.EntryPoint.Length > 128 || p.PixelShader.EntryPoint.Length > 128) throw new InvalidOperationException("World pipeline metadata exceeds diagnostic bounds.");
        foreach (var e in p.VertexElements) if (e.SemanticName.Length > 128) throw new InvalidOperationException("World vertex semantic exceeds diagnostic bounds.");
        return new(Convert.ToHexString(SHA256.HashData(p.VertexShader.Data.Span)), Convert.ToHexString(SHA256.HashData(p.PixelShader.Data.Span)),
            p.VertexShader.EntryPoint, p.PixelShader.EntryPoint, p.VertexShader.SourceName, p.PixelShader.SourceName, p.VertexElements.ToArray(),
            p.VertexRootConstantCount, p.PixelTextureCount, p.PrimitiveTopology, p.CullMode, p.DepthEnabled, p.DepthAlwaysPass, p.AlphaBlendEnabled, p.SceneDepthReceiver,
            p.VertexConstantsInBuffer, p.DirectionalShadowReceiver, p.TargetFormat);
    }
}

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsWorldBindingRegistry? _worldBindingRegistry;
    private GraphicsWorldBindingRecorder? _worldBindingRecorder;
    internal GraphicsWorldBindingRecorder? WorldBindingRecorder => _worldBindingRecorder;
    internal bool OpaqueWorldBindingPass => IsWorldPass && !_shadowRecording && !_particleRecording;
    private void ArmWorldBindingJournal()
    {
        if (_worldBindingRecorder is not null) throw new InvalidOperationException("World journal already armed.");
        _worldBindingRegistry ??= new();
        _worldBindingRecorder = new(_worldBindingRegistry, _telemetryGeneration, checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1),
            _telemetryIdentitySession, _frameIndex, _presentedFrameCount, _submittedFrameCount, _lastSubmittedFrameFence);
    }
    internal void RecordWorldPipeline(D3D12GraphicsPipeline pipeline, bool emitted)
    {
        if (_worldBindingRecorder is not { } r) return;
        try
        {
            _boundInputTracker?.Pipeline(emitted, pipeline.Description.DirectionalShadowReceiver, pipeline.Description.VertexConstantsInBuffer);
            var description = GraphicsWorldBindingRecorder.Describe(pipeline.Description);
            r.Pipeline(r.Define(pipeline.Lifetime, "pipeline", pipeline: description), description, emitted);
            if (pipeline.Description.DirectionalShadowReceiver)
                r.ShadowReceiver(new("directional-shadow-receiver", checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1),
                    _frameIndex, _shadowDescriptors![_frameIndex], 1 + pipeline.Description.PixelTextureCount, 15, 0, 2 + pipeline.Description.PixelTextureCount), emitted);
        }
        catch
        {
            r.Invalidate();
            throw;
        }
    }
    internal void RecordWorldShadowFrame(GraphicsDirectionalShadowFrame frame)
    {
        if (_worldBindingRecorder is null && _boundInputTracker is null) return;
        Span<float> data = stackalloc float[24];
        var matrix = frame.ViewProjection; MemoryMarshal.Cast<float, System.Numerics.Matrix4x4>(data[..16])[0] = matrix;
        data[16] = frame.Center.X; data[17] = frame.Center.Y; data[18] = frame.Center.Z; data[19] = frame.Radius;
        data[20] = _directionalShadows.Resolution; data[21] = frame.DepthRange; data[22] = frame.ContactStrength; data[23] = 1;
        _boundInputTracker?.ImplicitWrite(_shadowConstants![_frameIndex], checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1), _frameIndex, 0, MemoryMarshal.AsBytes(data), shadow: true);
        _worldBindingRecorder?.ShadowFrame(data, new("directional-shadow-frame", checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1), _frameIndex, null, null, null, 0));
    }
    internal void RecordWorldConstants(ReadOnlySpan<float> values, bool buffered)
    {
        if (_boundInputTracker is { } tracker)
        {
            if (buffered) tracker.ImplicitWrite(_shadowConstants![_frameIndex], checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1), _frameIndex,
                checked((ulong)(_constantOffset - 256)), MemoryMarshal.AsBytes(values), shadow: false);
            else tracker.RootConstants(Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(values))));
        }
        _worldBindingRecorder?.Constants(values, buffered ? "buffered" : "root", buffered ? new("buffered-vertex-constants",
            checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1), _frameIndex, null, 0, null, _constantOffset - 256) : null);
    }
}
