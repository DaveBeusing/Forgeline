using System.Numerics;
using ForgeLine.Graphics.RenderContracts;
using Xunit;
using Fixture = ForgeLine.Graphics.Tests.ResourceScheduling.NativeUploadSchedulingTests.NativeFixture;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

// Functional WARP/barrier checks only. These are not a physical timing cohort.
public sealed class StageCaptureNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvenanceRefusalWithQueuedUploadStillExecutesSignalsAndDisposes(bool normalIdleFirst)
    {
        using var scope = new GraphicsBoundInputProvenanceScope();
        var fixture = new Fixture(false, requestDebugLayer: true);
        var operations = new List<SchedulingOperation>();
        var device = fixture.Device;
        Assert.True(device.Diagnostics.Device.IsSoftwareAdapter);
        try
        {
            device.SchedulingFailureInjection = operation =>
            {
                operations.Add(operation);
                if (operation == SchedulingOperation.MapUpload) device.BoundInputTracker!.Invalidate();
            };
            Assert.Throws<InvalidOperationException>(() => device.CreateInitializedStaticBuffer(
                new(16, StaticGraphicsBufferUsage.Vertex), new byte[16]));
            // A normal idle path must finish upload bookkeeping but refuse invalid evidence.
            if (normalIdleFirst) Assert.Throws<InvalidOperationException>(device.WaitForIdle);
            device.Dispose();
            Assert.Equal(1, operations.Count(x => x == SchedulingOperation.ExecuteUpload));
            Assert.Equal(1, operations.Count(x => x == SchedulingOperation.SignalUpload));
        }
        finally
        {
            // Keep only observation; native cleanup failures must still surface.
            fixture.Dispose();
        }
        Assert.Equal(1, operations.Count(x => x == SchedulingOperation.ExecuteUpload));
        Assert.Equal(1, operations.Count(x => x == SchedulingOperation.SignalUpload));
        Assert.True(operations.IndexOf(SchedulingOperation.ExecuteUpload) < operations.IndexOf(SchedulingOperation.SignalUpload));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(.5f)]
    public void FirstCaptureSlotWrapAndOverlayDepthKeepEveryOriginalStage(float scale)
    {
        using var fixture = new Fixture(false, requestDebugLayer: true);
        var device = fixture.Device;
        Assert.True(device.Diagnostics.Device.IsSoftwareAdapter);
        Assert.Equal(0UL, device.LastSubmission!.Value.Submission);
        device.ConfigureSceneOutput(new(true, 1, false, scale));
        device.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
        using var world = Pipeline(device, GraphicsFrameTargetFormat.Rgba16Float, "float4(1,0,0,1)");
        using var green = Pipeline(device, GraphicsFrameTargetFormat.Rgba8Unorm, "float4(0,1,0,1)");
        using var blue = Pipeline(device, GraphicsFrameTargetFormat.Rgba8Unorm, "float4(0,0,1,1)");
        int baseline = device.Diagnostics.Health.LiveResourceCount;
        var slots = new HashSet<int>(); GraphicsStageCapture? first = null; ulong fence = 0; string? firstFingerprint = null;
        int recordedSlot = -1, outputWidth = 0, outputHeight = 0;
        void Record(IGraphicsCommandContext context)
        {
            recordedSlot = context.FrameIndex;
            context.BeginDirectionalShadowPass(new(Matrix4x4.Identity, Vector3.Zero, 1, 1));
            context.EndDirectionalShadowPass();
            context.SetPipeline(world); context.SetVertexConstants([.75f]); context.Draw(3);
            context.BeginPass(GraphicsFramePass.Overlay);
            outputWidth = context.Width; outputHeight = context.Height;
            context.SetScissor(0, 0, context.Width / 2, context.Height);
            context.SetPipeline(green); context.SetVertexConstants([.25f]); context.Draw(3);
            context.SetPipeline(blue); context.SetVertexConstants([.5f]); context.Draw(3);
        }
        for (int repetition = 0; repetition < 4; repetition++)
        {
            var actual = device.CaptureFrameDiagnostics(new(0,0,0,1), Record);
            Assert.Equal(recordedSlot, actual.FrameIndex); slots.Add(actual.FrameIndex);
            Assert.NotNull(actual.WorldBindings); Assert.True(actual.WorldBindings.Complete);
            Assert.Equal(1,actual.WorldBindings.DrawCount); Assert.Equal(actual.Submission,actual.WorldBindings.Submission);
            Assert.Equal(actual.FrameIndex,actual.WorldBindings.FrameIndex); Assert.Equal(actual.CompletedFence,actual.WorldBindings.CompletedFence);
            Assert.Null(device.WorldBindingRecorder); firstFingerprint ??= device.CaptureInputFingerprint;
            Assert.Equal((ulong)repetition + 1, actual.Submission.Submission);
            Assert.True(actual.Submission.SubmittedFence > fence); fence = actual.Submission.SubmittedFence;
            Assert.True(actual.CompletedFence >= fence); Assert.Equal(3, actual.BufferCount);
            Assert.Equal(actual.PresentedBefore + 1, actual.PresentedAfter);
            Assert.Equal(5, actual.Stages.Count); Assert.All(actual.Stages, stage => Assert.Null(stage.UnavailableReason));
            byte[] Bytes(GraphicsCaptureStages name)
            {
                var stage = actual.Stages.Single(s => s.Stage == name);
                return GraphicsReadbackRows.LogicalBytes(stage.Layout!.Value, stage.RawBytes.Span);
            }
            var finalLayout = actual.Stages.Single(s => s.Stage == GraphicsCaptureStages.FinalColor).Layout!.Value;
            Assert.Equal(outputWidth, finalLayout.Width); Assert.Equal(outputHeight, finalLayout.Height);
            int worldWidth = GraphicsFramePlan.ScaledDimension(outputWidth, scale);
            int worldHeight = GraphicsFramePlan.ScaledDimension(outputHeight, scale);
            var depthLayout = actual.Stages.Single(s => s.Stage == GraphicsCaptureStages.WorldDepth).Layout!.Value;
            Assert.Equal(worldWidth, depthLayout.Width); Assert.Equal(worldHeight, depthLayout.Height);
            int worldX = worldWidth / 4, worldY = worldHeight / 2;
            Assert.Equal(.75f, BitConverter.ToSingle(Bytes(GraphicsCaptureStages.WorldDepth), (worldY * worldWidth + worldX) * 4));
            var scene = Bytes(GraphicsCaptureStages.SceneLinear); int texel = (worldY * worldWidth + worldX) * 8;
            Assert.Equal((Half)1, BitConverter.ToHalf(scene, texel)); Assert.Equal((Half)0, BitConverter.ToHalf(scene, texel + 2));
            Assert.Equal(1f, BitConverter.ToSingle(Bytes(GraphicsCaptureStages.ShadowDepth), 0));
            var composed = Bytes(GraphicsCaptureStages.ComposedColor); var final = Bytes(GraphicsCaptureStages.FinalColor);
            Assert.Equal(actual.FinalRgba, final);
            int left = ((outputHeight / 2) * outputWidth + outputWidth / 4) * 4;
            int right = ((outputHeight / 2) * outputWidth + outputWidth * 3 / 4) * 4;
            Assert.Equal(new byte[] {255,0,0,255}, composed.AsSpan(left,4).ToArray());
            Assert.Equal(new byte[] {0,255,0,255}, final.AsSpan(left,4).ToArray());
            Assert.Equal(new byte[] {255,0,0,255}, final.AsSpan(right,4).ToArray());
            if (first is not null)
            {
                Assert.Equal(first.Submission.Generation, actual.Submission.Generation);
                Assert.Equal(first.Submission.ResourceGeneration, actual.Submission.ResourceGeneration);
                foreach (var stage in actual.Stages)
                {
                    var original = first.Stages.Single(s => s.Stage == stage.Stage);
                    Assert.Equal(GraphicsReadbackRows.LogicalBytes(original.Layout!.Value, original.RawBytes.Span), GraphicsReadbackRows.LogicalBytes(stage.Layout!.Value, stage.RawBytes.Span));
                }
            }
            first ??= actual;
            Assert.Equal(baseline, device.Diagnostics.Health.LiveResourceCount);
        }
        Assert.Equal(3, slots.Count);
        Assert.Equal(first!.FinalRgba, device.CaptureFrame(new(0,0,0,1), Record));
        Assert.Equal(firstFingerprint,device.CaptureInputFingerprint); Assert.Null(device.WorldBindingRecorder);
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }
    [Fact]
    public void DisabledStagesStayExplicitlyUnavailableAndLegacyReadbackStillWorks()
    {
        using var fixture = new Fixture(false, requestDebugLayer: true);
        var device = fixture.Device;
        var result = device.CaptureFrameDiagnostics(GraphicsColor.ForgeLineClear, c => c.BeginPass(GraphicsFramePass.Overlay));
        Assert.Equal(4, result.Stages.Count(s => s.UnavailableReason is not null && s.RawBytes.IsEmpty));
        Assert.Equal(result.FinalRgba, device.CaptureFrame(GraphicsColor.ForgeLineClear, c => c.BeginPass(GraphicsFramePass.Overlay)));
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PartialRecordingOrPresentFailureNeverPublishesDiagnosticEvidence(bool present)
    {
        using var fixture = new Fixture(false, requestDebugLayer: true);
        var device = fixture.Device; device.ConfigureSceneOutput(new(true,1,false));
        device.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
        bool entered = false; IGraphicsCommandContext? escaped = null;
        if (present) device.SchedulingFailureInjection = operation =>
        { if (operation == SchedulingOperation.Present) throw new InvalidOperationException("Controlled diagnostic Present failure."); };
        try
        {
            Assert.ThrowsAny<Exception>(() => device.CaptureFrameDiagnostics(GraphicsColor.ForgeLineClear, context =>
            {
                entered = true; escaped = context;
                context.BeginDirectionalShadowPass(new(Matrix4x4.Identity, Vector3.Zero, 1, 1)); context.EndDirectionalShadowPass();
                context.BeginPass(GraphicsFramePass.Overlay);
                if (!present) throw new InvalidOperationException("Controlled partial stage recording failure.");
            }));
            Assert.True(entered); Assert.NotNull(escaped);
            Assert.Throws<ObjectDisposedException>(() => escaped.Draw(3));
            Assert.Throws<GraphicsDeviceException>(() => device.RenderFrame(GraphicsColor.ForgeLineClear));
            Assert.Throws<GraphicsDeviceException>(() => _ = device.IsRecording);
            Assert.Null(device.CaptureInputFingerprint); Assert.True(device.CaptureShadowDepth.IsEmpty);
            Assert.Null(device.WorldBindingRecorder);
            Assert.True(device.Diagnostics.Health.SubmissionFaultCount >= 1);
            // Existing fault ownership may retain native resources until fixture Dispose establishes idle.
        }
        finally { device.SchedulingFailureInjection = null; }
    }
    private static IGraphicsPipeline Pipeline(D3D12GraphicsDevice device, GraphicsFrameTargetFormat target, string color)
    {
        var compiler = new DxcShaderCompiler();
        var vs = compiler.Compile("cbuffer C:register(b0){float Z;} float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),Z,1); }", GraphicsShaderStage.Vertex, "VSMain", "DiagnosticStageVertex.hlsl");
        var ps = compiler.Compile("float4 PSMain():SV_Target{return " + color + ";}", GraphicsShaderStage.Pixel, "PSMain", "DiagnosticStagePixel.hlsl");
        return device.CreateGraphicsPipeline(new(vs,ps) { TargetFormat = target, CullMode = GraphicsCullMode.None, DepthEnabled = true, VertexRootConstantCount = 1 });
    }
}

