using System.Numerics;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientWorldPassSequenceTests
{
    private static readonly GraphicsAtmosphereView Atmosphere = new(Matrix4x4.Identity, Vector3.Zero, 100);
    private static readonly GraphicsDirectionalShadowFrame Shadow = new(Matrix4x4.Identity, Vector3.Zero, 10, 20);
    private static readonly string[] ShadedWorldCalls = ["World", "atmosphere", "shadow.begin", "shadow.terrain:128",
        "shadow.instances:1022:254", "shadow.end", "terrain:480", "prepare:480", "instances:480", "particles:480", "Overlay"];
    private static readonly string[] UnshadedWorldCalls = ["World", "terrain:480", "prepare:480", "instances:480", "particles:480", "Overlay"];
    private static readonly string[] InvalidCasterCalls = ["World", "shadow.begin", "shadow.terrain:128", "shadow.end"];

    [Fact]
    public void FixedWorldSequenceUsesScaledWorldAndNativeOverlayWithBoundedShadows()
    {
        var trace = new List<string>();
        var context = new PassContext(trace);
        var recorder = new Recorder(trace);
        ClientWorldPassSequence.Record(context, Atmosphere, Shadow, new(DirectionalShadowQuality.Low), ref recorder);
        Assert.Equal(ShadedWorldCalls, trace);
        Assert.Equal(640, context.Width);
    }

    [Fact]
    public void DisabledEffectsDoNotSubmitMissingInputsOrShadowPasses()
    {
        var trace = new List<string>();
        var recorder = new Recorder(trace);
        ClientWorldPassSequence.Record(new PassContext(trace), null, null, default, ref recorder);
        Assert.Equal(UnshadedWorldCalls, trace);
    }

    [Fact]
    public void MissingShadowInputFailsBeforeRecording()
    {
        var trace = new List<string>();
        var recorder = new Recorder(trace);
        Assert.Throws<ArgumentException>(() => ClientWorldPassSequence.Record(new PassContext(trace), null, null,
            new(DirectionalShadowQuality.Low), ref recorder));
        Assert.Empty(trace);
    }

    [Theory]
    [InlineData("shadow.terrain")]
    [InlineData("shadow.instances")]
    public void FaultingShadowAlwaysClosesItsScopeAndNeverContinuesIntoWorld(string failure)
    {
        var trace = new List<string>();
        var recorder = new Recorder(trace) { Failure = failure };
        Assert.Throws<InvalidOperationException>(() => ClientWorldPassSequence.Record(new PassContext(trace), null, Shadow,
            new(DirectionalShadowQuality.Low), ref recorder));
        Assert.Equal("shadow.end", trace[^1]);
        Assert.DoesNotContain("terrain:480", trace);
        Assert.DoesNotContain("Overlay", trace);
    }

    [Fact]
    public void InvalidCasterAccountingFailsClosedWithoutNegativeBudgets()
    {
        var trace = new List<string>();
        var recorder = new Recorder(trace) { TerrainDraws = 129 };
        Assert.Throws<InvalidOperationException>(() => ClientWorldPassSequence.Record(new PassContext(trace), null, Shadow,
            new(DirectionalShadowQuality.Low), ref recorder));
        Assert.Equal(InvalidCasterCalls, trace);
    }

    private struct Recorder(List<string> trace) : IClientWorldPassRecorder
    {
        internal string? Failure { get; init; }
        internal int TerrainDraws { get; init; } = 2;
        public int TerrainShadow(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumDraws)
        {
            Add("shadow.terrain", $"shadow.terrain:{maximumDraws}");
            return TerrainDraws;
        }
        public void InstanceShadows(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumCasters, int maximumDraws) =>
            Add("shadow.instances", $"shadow.instances:{maximumCasters}:{maximumDraws}");
        public void Terrain(IGraphicsCommandContext context) => Add("terrain", $"terrain:{context.Width}");
        public void PrepareParticles(IGraphicsCommandContext context) => Add("prepare", $"prepare:{context.Width}");
        public void Instances(IGraphicsCommandContext context) => Add("instances", $"instances:{context.Width}");
        public void Particles(IGraphicsCommandContext context) => Add("particles", $"particles:{context.Width}");
        private void Add(string stage, string entry)
        {
            trace.Add(entry);
            if (Failure == stage) throw new InvalidOperationException("Injected " + stage);
        }
    }

    private sealed class PassContext(List<string> trace) : IGraphicsCommandContext
    {
        private GraphicsFramePass _pass;
        public int Width => _pass == GraphicsFramePass.World ? 480 : 640;
        public int Height => _pass == GraphicsFramePass.World ? 270 : 360;
        public int FrameIndex => 0;
        public void BeginPass(GraphicsFramePass pass) { _pass = pass; trace.Add(pass.ToString()); }
        public void SetAtmosphereView(GraphicsAtmosphereView view) => trace.Add("atmosphere");
        public void BeginDirectionalShadowPass(GraphicsDirectionalShadowFrame frame) => trace.Add("shadow.begin");
        public void EndDirectionalShadowPass() => trace.Add("shadow.end");
        public void SetViewport(float x, float y, float width, float height) => throw new NotSupportedException();
        public void SetScissor(int left, int top, int right, int bottom) => throw new NotSupportedException();
        public void SetPipeline(IGraphicsPipeline pipeline) => throw new NotSupportedException();
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0) => throw new NotSupportedException();
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) => throw new NotSupportedException();
        public void SetVertexConstants(ReadOnlySpan<float> values) => throw new NotSupportedException();
        public void Draw(int vertexCount, int startVertex = 0) => throw new NotSupportedException();
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) => throw new NotSupportedException();
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) => throw new NotSupportedException();
    }
}
