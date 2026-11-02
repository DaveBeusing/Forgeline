using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

public sealed class GraphicsWorldBindingJournalTests
{
    private static readonly byte[] NegativeZeroBytes = [0, 0, 0, 128];
    private static readonly int[] FirstDrawArguments = [3, 1, 2, 0, 0];
    private static readonly int[] LaterDrawArguments = [9, 3, 4, -1, 5];
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static GraphicsWorldPipelineBinding Pipeline(bool vertex = false, int textures = 0, bool shadow = false) =>
        new(new string('a', 64), new string('b', 64), "VSMain", "PSMain", "vertex.hlsl", "pixel.hlsl",
            vertex ? [new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)] : [],
            1, textures, GraphicsPrimitiveTopology.TriangleList, GraphicsCullMode.None, true, false,
            false, false, shadow, shadow, GraphicsFrameTargetFormat.Rgba16Float);

    private static GraphicsWorldBindingRecorder Recorder(GraphicsWorldBindingRegistry? registry = null)
    {
        var result = new GraphicsWorldBindingRecorder(registry ?? new(), 7, 3, new string('c', 32), 1, 12);
        result.Viewport(0,0,1920,1080);
        result.Scissor(0,0,1920,1080);
        return result;
    }

    private static GraphicsWorldBindingJournal Finish(GraphicsWorldBindingRecorder recorder) =>
        recorder.Finish(new(1, 21, 7, 3), 22, 1, 12, 13);

    private static ulong BindPipeline(GraphicsWorldBindingRecorder recorder, GraphicsWorldPipelineBinding description, object? key = null)
    {
        ulong id = recorder.Define(key ?? new object(), "pipeline", pipeline: description);
        recorder.Pipeline(id, description, true);
        return id;
    }

    [Fact]
    public void OrderedArgumentsAndIeeeConstantsAreRetainedInActualDtoShape()
    {
        var recorder = Recorder();
        BindPipeline(recorder, Pipeline());
        recorder.Constants([BitConverter.Int32BitsToSingle(unchecked((int)0x80000000))], "root");
        recorder.Draw(false, 3, 1, 2, 0, 0);
        var result = Finish(recorder);
        Assert.True(result.Complete);
        Assert.Equal(FirstDrawArguments, result.Events.Last().DrawArguments);
        var constants = result.Events.Single(e => e.Kind == "Constants");
        Assert.Equal(NegativeZeroBytes, Convert.FromBase64String(constants.ConstantBytesBase64!));
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(result, Json));
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal("DrawInstanced", json.RootElement.GetProperty("events").EnumerateArray().Last().GetProperty("kind").GetString());
        Assert.Equal("Rgba16Float", json.RootElement.GetProperty("definitions")[0].GetProperty("pipeline").GetProperty("targetFormat").GetString());
        Assert.Null(GraphicsWorldBindingJournal.FailurePrefix(new InvalidOperationException()));
    }

    [Fact]
    public void InputAssemblerPersistsAcrossRootInvalidationAndCacheHits()
    {
        var recorder = Recorder();
        object pipelineKey = new(), bufferKey = new(), textureKey = new();
        var pipeline = Pipeline(vertex: true, textures: 1);
        ulong id = BindPipeline(recorder, pipeline, pipelineKey);
        ulong buffer = recorder.Define(bufferKey, "buffer", buffer: new(128, GraphicsBufferMemory.Upload));
        recorder.Vertex(buffer, 0, 12, 8, 120);
        recorder.Index(buffer, GraphicsIndexFormat.SixteenBit, 4, 124);
        ulong texture = recorder.Define(textureKey, "texture", texture: new(4, 4, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1), descriptor: 9);
        recorder.Texture(texture, 0, true);
        recorder.Constants([1], "root");
        recorder.Pipeline(id, pipeline, false);
        recorder.Texture(texture, 0, false);
        recorder.Draw(true, 6, 2, 1, -2, 3);
        recorder.InvalidateRoot();
        recorder.Pipeline(id, pipeline, true);
        recorder.Texture(texture, 0, true);
        recorder.Constants([2], "root");
        recorder.Draw(true, 9, 3, 4, -1, 5);
        var result = Finish(recorder);
        Assert.Equal(2, result.DrawCount);
        Assert.Single(result.Events, e => e.Kind == "VertexBuffer");
        Assert.Single(result.Events, e => e.Kind == "IndexBuffer");
        Assert.Contains(result.Events, e => e.Kind == "Texture" && e.Emitted is false);
        Assert.Equal(LaterDrawArguments, result.Events.Last().DrawArguments);
    }

    [Fact]
    public void RootInvalidationCannotBorrowConstantsOrTextures()
    {
        var recorder = Recorder();
        ulong id = BindPipeline(recorder, Pipeline());
        recorder.Constants([1], "root");
        recorder.InvalidateRoot();
        recorder.Pipeline(id, Pipeline(), true);
        Assert.Throws<InvalidOperationException>(() => recorder.Draw(false, 3, 1, 0, 0, 0));
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

    [Fact]
    public void FalseTextureCacheHitAndForeignOwnerAreSticky()
    {
        var recorder = Recorder();
        BindPipeline(recorder, Pipeline(textures: 1));
        Assert.Throws<InvalidOperationException>(() => recorder.Texture(123, 0, false));
        Assert.Throws<InvalidOperationException>(() => recorder.Constants([1], "root"));
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

    [Fact]
    public void ActualForeignManagedOwnerCannotResume()
    {
        var recorder = Recorder();
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try { recorder.InvalidateRoot(); } catch (Exception error) { observed = error; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

    [Fact]
    public void ImplicitShadowUsesEpochSlotAndDescriptorWithoutLifetimeIdentity()
    {
        var recorder = Recorder();
        recorder.ShadowFrame(new float[24], new("directional-shadow-frame", 3, 1, null, null, null, 0));
        BindPipeline(recorder, Pipeline(shadow: true));
        recorder.ShadowReceiver(new("directional-shadow-receiver", 3, 1, 23, 1, 15, 0, 2));
        recorder.Constants([1], "buffered", new("buffered-vertex-constants", 3, 1, null, 0, null, 256));
        recorder.Draw(false, 3, 1, 0, 0, 0);
        var result = Finish(recorder);
        Assert.Single(result.Definitions);
        Assert.Equal(23, result.Events.Single(e => e.Kind == "ShadowReceiver").Implicit!.Descriptor);
    }

    [Fact]
    public void CrossCaptureResourceSwapsReceiveNewNonReusedLocalOrdinals()
    {
        var registry = new GraphicsWorldBindingRegistry();
        object firstKey = new(), secondKey = new();
        var first = Recorder(registry);
        ulong a = first.Define(firstKey, "buffer", buffer: new(64));
        var second = Recorder(registry);
        Assert.Equal(a, second.Define(firstKey, "buffer", buffer: new(64)));
        ulong b = second.Define(secondKey, "buffer", buffer: new(64));
        Assert.True(b > a);
        GC.KeepAlive(firstKey);
        GC.KeepAlive(secondKey);
    }

    [Fact]
    public void SameLifetimeCannotAcquireDifferentMetadata()
    {
        var recorder = Recorder(); object key = new();
        recorder.Define(key,"buffer",buffer:new(64));
        Assert.Throws<InvalidOperationException>(() => recorder.Define(key,"buffer",buffer:new(128)));
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

    [Fact]
    public void DrawOverflowRetainsIncompletePrefixAndCannotBeCorrected()
    {
        var recorder = Recorder();
        BindPipeline(recorder, Pipeline());
        recorder.Constants([1], "root");
        for (int i = 0; i < GraphicsWorldBindingJournal.DrawCapacity; i++)
            recorder.Draw(false, 3, 1, 0, 0, 0);
        var error = Assert.Throws<InvalidOperationException>(() => recorder.Draw(false, 3, 1, 0, 0, 0));
        var prefix = recorder.Prefix(error);
        Assert.False(prefix.Complete);
        Assert.Equal(GraphicsWorldBindingJournal.DrawCapacity, prefix.DrawCount);
        Assert.Null(prefix.Submission);
        Assert.Null(prefix.CompletedFence);
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
        using var attempt = GraphicsWorldBindingJournal.BeginAttempt();
        Assert.Same(attempt, GraphicsWorldBindingAttempt.ClaimCurrent());
        GraphicsWorldBindingJournal.AttachFailure(error, prefix, attempt);
        Assert.Same(prefix, GraphicsWorldBindingJournal.FailurePrefix(error, attempt));
    }

    [Fact]
    public void ReusedExceptionRequiresExactClaimedInvocationAndKeepsCurrentPrefix()
    {
        var error = new InvalidOperationException("reused");
        var first = Recorder().Prefix(error);
        GraphicsWorldBindingAttempt old;
        using (old = GraphicsWorldBindingJournal.BeginAttempt())
        {
            Assert.Same(old, GraphicsWorldBindingAttempt.ClaimCurrent());
            GraphicsWorldBindingJournal.AttachFailure(error, first, old);
        }
        using (var unarmed = GraphicsWorldBindingJournal.BeginAttempt())
        {
            Assert.Null(GraphicsWorldBindingJournal.FailurePrefix(error, unarmed));
            Assert.Null(GraphicsWorldBindingJournal.FailurePrefix(error));
        }
        var current = Recorder().Prefix(error) with { Failure = "current invocation" };
        using (var attempt = GraphicsWorldBindingJournal.BeginAttempt())
        {
            Assert.Same(attempt, GraphicsWorldBindingAttempt.ClaimCurrent());
            GraphicsWorldBindingJournal.AttachFailure(error, current, attempt);
            Assert.Same(current, GraphicsWorldBindingJournal.FailurePrefix(error, attempt));
            Assert.Null(GraphicsWorldBindingJournal.FailurePrefix(error, old));
            Assert.Throws<InvalidOperationException>(() => GraphicsWorldBindingAttempt.ClaimCurrent());
        }
        using var next = GraphicsWorldBindingJournal.BeginAttempt();
        Assert.Null(GraphicsWorldBindingJournal.FailurePrefix(error, next));
    }

    [Fact]
    public void SerializationRefusalCaughtByCallerStillInvalidatesRecorder()
    {
        var recorder = Recorder();
        Assert.Throws<ArgumentException>(() => recorder.Viewport(float.NaN, 0, 1, 1));
        Assert.Throws<InvalidOperationException>(() => recorder.InvalidateRoot());
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

    [Theory]
    [InlineData(0, 1, 12, 13)]
    [InlineData(1, 2, 12, 13)]
    [InlineData(1, 1, 12, 12)]
    public void WrongSubmissionSlotOrPresentNeverSeals(int ordinal, int slot, int before, int after)
    {
        var recorder = Recorder();
        Assert.Throws<InvalidOperationException>(() => recorder.Finish(new((ulong)ordinal, 21, 7, 3), 22, slot, (ulong)before, (ulong)after));
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

    [Fact]
    public void NearBoundWireUsesActualEscapingAndEnumNamesAndRetainsWholePrefix()
    {
        var recorder = Recorder();
        var keys = new List<object>();
        var description = Pipeline(vertex: true) with
        {
            VertexSourceName = new string('\u03bb', 512),
            PixelSourceName = new string('\u03bb', 512),
            VertexEntryPoint = new string('\u03bb', 128),
            PixelEntryPoint = new string('\u03bb', 128),
            VertexElements = [new(new string('\u03bb', 128), 0, GraphicsVertexElementFormat.Float3, 0)]
        };
        InvalidOperationException? refused = null;
        for (int i = 0; i < GraphicsWorldBindingJournal.IdentityCapacity; i++)
        {
            object key = new();
            keys.Add(key);
            try { recorder.Define(key, "pipeline", pipeline: description); }
            catch (InvalidOperationException error) { refused = error; break; }
        }
        Assert.NotNull(refused);
        var prefix = recorder.Prefix(refused);
        Assert.False(prefix.Complete);
        Assert.True(prefix.Definitions.Length < GraphicsWorldBindingJournal.IdentityCapacity);
        byte[] raw = GraphicsWorldBindingJournal.SerializeSidecar(prefix);
        Assert.InRange(raw.LongLength, GraphicsWorldBindingJournal.JournalCapacity - 64 * 1024, GraphicsWorldBindingJournal.JournalCapacity);
        using var parsed = JsonDocument.Parse(raw);
        Assert.Equal("Rgba16Float", parsed.RootElement.GetProperty("definitions")[0].GetProperty("pipeline").GetProperty("targetFormat").GetString());
        Assert.Equal(prefix.Definitions.Length, parsed.RootElement.GetProperty("definitions").GetArrayLength());
        Assert.Null(prefix.Submission);
        byte[] envelope = GraphicsWorldBindingJournal.SerializeSidecar(new { qualified = false, contextUnavailable = "bounded context unavailable", journal = prefix });
        Assert.True(envelope.LongLength <= GraphicsWorldBindingJournal.PayloadCapacity);
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
        GC.KeepAlive(keys);
    }

    [Fact]
    public void LiveWeakIdentityCapacityRefusesWithoutReusingAnOrdinal()
    {
        var recorder = Recorder();
        var keys = new List<object>();
        for (int i = 0; i < GraphicsWorldBindingJournal.IdentityCapacity; i++)
        {
            object key = new();
            keys.Add(key);
            Assert.Equal((ulong)i + 1, recorder.Define(key, "buffer", buffer: new(64)));
        }
        var refused = Assert.Throws<InvalidOperationException>(() => recorder.Define(new object(), "buffer", buffer: new(64)));
        Assert.Equal(GraphicsWorldBindingJournal.IdentityCapacity, recorder.Prefix(refused).Definitions.Length);
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
        GC.KeepAlive(keys);
    }

    [Fact]
    public void FullEventCounterRefusesBeforeWritingBeyondPreallocatedStorage()
    {
        var recorder = Recorder();
        // Controlled internal-counter injection tests the independent event guard;
        // the actual-wire test exercises the usually earlier UTF8 refusal.
        var field = typeof(GraphicsWorldBindingRecorder).GetField("_eventCount",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(recorder, GraphicsWorldBindingJournal.EventCapacity);
        Assert.Throws<InvalidOperationException>(() => recorder.InvalidateRoot());
        Assert.Throws<InvalidOperationException>(() => Finish(recorder));
    }

}
