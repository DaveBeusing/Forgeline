using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Graphics.RenderContracts;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

// Explicit software-backend functional tests. Construction occurs only in test bodies.
// Caller publication below is a CLOSED TEST MARKER, never a product PNG or reference cohort.
public sealed class PostFrameCommandTapeNativeTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] AttachmentRoles = ["retained-scene-linear", "retained-shadow-depth", "retained-final-depth"];
    private static GraphicsResourceRetirement Registry(D3D12GraphicsDevice device)
        => (GraphicsResourceRetirement)typeof(D3D12GraphicsDevice).GetField("_resources", PrivateInstance)!.GetValue(device)!;
    private static GraphicsPostFrameCommandTape? Tape(D3D12GraphicsDevice device)
        => (GraphicsPostFrameCommandTape?)typeof(D3D12GraphicsDevice).GetField("_postFrameTape", PrivateInstance)!.GetValue(device);
    private static void Secondary(Exception original, Exception cleanup)
    {
        try { original.Data["softwareFixtureCleanup-" + original.Data.Count] = cleanup; } catch { }
    }
    private static Exception WithCleanup(Exception? primary, Exception cleanup)
    {
        if (primary is null) return cleanup;
        if (primary is Xunit.Sdk.SkipException)
            return new AggregateException("Expected debug skip also had a cleanup failure; software validation failed.", primary, cleanup);
        Secondary(primary, cleanup); return primary;
    }

    private static void WithFixture(string mode, string scenario, Action<SoftwareFixture> body,
        float scale = 1, bool scene = true, bool shadows = true, bool requireDebug = false)
    {
        SoftwareFixture? fixture = null; Exception? primary = null;
        try { fixture = new(mode, scenario, scale, scene, shadows, requireDebug); body(fixture); }
        catch (Exception error) { primary = error; }
        try { fixture?.Dispose(); }
        catch (Exception cleanup) { primary = WithCleanup(primary, cleanup); }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private sealed record Captured(GraphicsPostFrameCaptureContext Context, int Slot, byte[] Pixels, int Repetition,
        int OutputWidth, int OutputHeight, string ClosedTestMarker);

    private sealed class SoftwareFixture : IDisposable
    {
        private WindowsPlatform? _platform;
        private IWindow? _window;
        private D3D12GraphicsDevice? _device;
        private readonly List<IDisposable> _owned = [];
        private readonly List<string> _markers = [];
        private DirectoryInfo? _directory;
        private bool _disposed;
        private readonly string _scenario;
        internal string Mode { get; }
        internal float Scale { get; }
        internal D3D12GraphicsDevice Device => _device ?? throw new InvalidOperationException("Software owner unavailable.");
        internal GraphicsPostFrameCaptureContext LastContext { get; private set; }
        internal IGraphicsPipeline World { get; private set; } = null!;
        internal IGraphicsPipeline Overlay { get; private set; } = null!;
        internal IGraphicsPipeline? Receiver { get; private set; }
        internal IGraphicsBuffer? Vertices { get; private set; }
        internal IGraphicsBuffer? Indices { get; private set; }
        internal IGraphicsTexture? Texture { get; private set; }
        internal byte[] VertexBytes { get; private set; } = [];
        internal byte[] IndexBytes { get; } = [0, 0, 1, 0, 2, 0];
        internal byte[] TextureBytes { get; } = [51, 102, 153, 255];

        internal SoftwareFixture(string mode, string scenario, float scale, bool scene, bool shadows, bool requireDebug)
        {
            Mode = mode; Scale = scale; _scenario = scenario;
            try
            {
                _directory = Directory.CreateTempSubdirectory("forgeline-software-post-frame-v2-");
                _platform = new WindowsPlatform();
                _window = _platform.CreateWindow(new WindowConfiguration("FORGELINE Software Post-Frame Test", 67, 41, resizable: false));
                _device = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(_window, new GraphicsConfiguration
                {
                    ForceSoftwareAdapter = true,
                    AllowSoftwareAdapterFallback = true,
                    EnableVSync = false,
                    BufferCount = 3,
                    EnableDebugLayer = true
                });
                // This MUST precede Configure, pipeline creation, rendering and resource setup.
                Assert.True(Device.Diagnostics.Device.IsSoftwareAdapter, "Forced software owner selected a nonsoftware adapter.");
                bool deviceDebug = Device.Diagnostics.Device.DebugLayerEnabled, queueDebug = Device.Diagnostics.Debug.Enabled;
                Console.WriteLine($"[post-frame-v2-software] mode={mode} actualSoftware={Device.Diagnostics.Device.IsSoftwareAdapter} deviceDebug={deviceDebug} queueDebug={queueDebug}");
                Assert.SkipWhen(requireDebug && (!deviceDebug || !queueDebug), "Actual D3D12 debug layer/info queue is unavailable; functional success is not debug validation.");
                GraphicsPostFrameSnapshots.Configure(Device, mode);
                Assert.Equal(mode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, Tape(Device) is not null);
                Device.ConfigureSceneOutput(new(scene, 1, false, scale));
                Device.ConfigureDirectionalShadows(new(shadows ? DirectionalShadowQuality.Low : DirectionalShadowQuality.Disabled));
                World = Own(SolidPipeline(GraphicsFrameTargetFormat.Rgba16Float, "float4(1,0,0,1)"));
                Overlay = Own(SolidPipeline(GraphicsFrameTargetFormat.Rgba8Unorm, "float4(0,1,0,1)"));
            }
            catch (Exception primary)
            {
                var cleanup = Cleanup();
                if (cleanup is not null) ExceptionDispatchInfo.Capture(WithCleanup(primary, cleanup)).Throw();
                throw;
            }
        }

        private T Own<T>(T value) where T : IDisposable { _owned.Add(value); return value; }
        private IGraphicsPipeline SolidPipeline(GraphicsFrameTargetFormat target, string color)
        {
            var compiler = new DxcShaderCompiler();
            var vertex = compiler.Compile("cbuffer C:register(b0){float Z;} float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),Z,1); }",
                GraphicsShaderStage.Vertex, "VSMain", "SoftwarePostFrameSolidVertex.hlsl");
            var pixel = compiler.Compile("float4 PSMain():SV_Target{return " + color + ";}", GraphicsShaderStage.Pixel, "PSMain", "SoftwarePostFrameSolidPixel.hlsl");
            return Device.CreateGraphicsPipeline(new(vertex, pixel)
            { TargetFormat = target, CullMode = GraphicsCullMode.None, DepthEnabled = true, VertexRootConstantCount = 1 });
        }

        internal void PrepareReceiver()
        {
            var compiler = new DxcShaderCompiler();
            var vertex = compiler.Compile("cbuffer C:register(b0){float Z;} float4 VSMain(float3 p:POSITION):SV_Position{return float4(p.xy,Z,1);}",
                GraphicsShaderStage.Vertex, "VSMain", "SoftwarePostFrameReceiverVertex.hlsl");
            var pixel = compiler.Compile("Texture2D Color:register(t0); Texture2D<float> Shadow:register(t15); cbuffer S:register(b1){float4x4 P;float4 CenterRadius;float4 ShadowData;} float4 PSMain(float4 p:SV_Position):SV_Target {return float4(Color.Load(int3(0,0,0)).rgb*Shadow.Load(int3(0,0,0))*ShadowData.y,1);}",
                GraphicsShaderStage.Pixel, "PSMain", "SoftwarePostFrameReceiverPixel.hlsl");
            Receiver = Own(Device.CreateGraphicsPipeline(new(vertex, pixel)
            {
                TargetFormat = GraphicsFrameTargetFormat.Rgba16Float, CullMode = GraphicsCullMode.None, DepthEnabled = true,
                VertexRootConstantCount = 1, VertexConstantsInBuffer = true, DirectionalShadowReceiver = true, PixelTextureCount = 1,
                VertexElements = [new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)]
            }));
            VertexBytes = MemoryMarshal.AsBytes<float>(new float[] { -1, -1, 0, -1, 3, 0, 3, -1, 0 }.AsSpan()).ToArray();
            Vertices = Own(Device.CreateInitializedStaticBuffer(new((ulong)VertexBytes.Length, StaticGraphicsBufferUsage.Vertex), VertexBytes));
            Indices = Own(Device.CreateInitializedStaticBuffer(new((ulong)IndexBytes.Length, StaticGraphicsBufferUsage.Index), IndexBytes));
            Texture = Own(Device.CreateTexture(new(new(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1), [new(1, 1, 4, TextureBytes)])));
            Device.WaitForIdle(); // Original setup initializers, before any selected arm.
        }

        internal Captured Capture(int repetition, bool receiver = false, bool draw = true, Action? beforePublication = null, Action? duringRecording = null)
        {
            LastContext = GraphicsPostFrameSnapshots.BeginCapture(Device, Mode, repetition);
            int slot = -1, width = 0, height = 0;
            byte[] pixels = Device.CaptureFrame(new(0, 0, 0, 1), context =>
            {
                slot = context.FrameIndex;
                duringRecording?.Invoke();
                if (Device.DirectionalShadows.Enabled)
                {
                    context.BeginDirectionalShadowPass(new(Matrix4x4.Identity, Vector3.Zero, 1, 1));
                    context.EndDirectionalShadowPass();
                }
                if (draw)
                {
                    context.SetPipeline(receiver ? Receiver! : World);
                    context.SetVertexConstants([.75f]);
                    if (receiver)
                    {
                        context.SetVertexBuffer(Vertices!, 12); context.SetIndexBuffer(Indices!, GraphicsIndexFormat.SixteenBit);
                        context.SetPixelTexture(0, Texture!); context.SetPixelTexture(0, Texture!);
                        context.DrawIndexed(3);
                    }
                    else context.Draw(3);
                }
                context.BeginPass(GraphicsFramePass.Overlay);
                width = context.Width; height = context.Height;
                if (draw && !receiver)
                {
                    context.SetScissor(0, 0, context.Width / 2, context.Height);
                    context.SetPipeline(Overlay); context.SetVertexConstants([.25f]); context.Draw(3);
                }
            });
            Assert.True(slot >= 0); Assert.Equal(width * height * 4, pixels.Length);
            beforePublication?.Invoke();
            string marker = Path.Combine(_directory!.FullName, "caller-publication-" + repetition + ".marker");
            // The closed marker exercises only Graphics' caller-publication seam. It is NOT PNG.
            using (var file = new FileStream(marker, FileMode.CreateNew)) file.Write([1, 2, 3]);
            _markers.Add(marker);
            return new(LastContext, slot, pixels, repetition, width, height, marker);
        }

        internal GraphicsPostFrameReceipt Receipt(Captured captured)
        {
            Assert.True(File.Exists(captured.ClosedTestMarker));
            return GraphicsPostFrameSnapshots.IssueReceipt(Device, captured.Slot, captured.Context);
        }

        internal GraphicsPostFrameSnapshotResult Copy(GraphicsPostFrameReceipt receipt, bool receiver = false)
        {
            var roster = new GraphicsPostFrameRoster();
            if (receiver)
            {
                roster.AddBuffer("fixture-vertex", Vertices!); roster.AddBuffer("fixture-index", Indices!); roster.AddTexture("fixture-texture", Texture!);
            }
            return GraphicsPostFrameSnapshots.Capture(Device, receipt, roster, new([], [], []));
        }

        internal void RetainFunctionalEvidence(Captured captured, GraphicsPostFrameSnapshotResult result)
        {
            string? output = Environment.GetEnvironmentVariable("FORGELINE_POST_FRAME_V2_SOFTWARE_EVIDENCE_OUTPUT");
            if (output is null) return;
            Directory.CreateDirectory(output);
            string file = Path.Combine(output, Mode + "-" + _scenario + "-" + captured.Repetition + ".json");
            byte[] raw = GraphicsPostFrameJson.Serialize(new
            {
                kind = "software-functional-post-frame-backend-evidence", qualified = false, physicalTiming = false,
                actualSoftwareAdapter = Device.Diagnostics.Device.IsSoftwareAdapter,
                actualDeviceDebugLayer = Device.Diagnostics.Device.DebugLayerEnabled, actualInfoQueue = Device.Diagnostics.Debug.Enabled,
                callerPublicationIsProductPng = false, callerPublicationKind = "closed-test-marker-only",
                sourceTreeCleanVerified = false, authoredReferenceCohort = false, captured.Repetition, result
            }, GraphicsPostFrameLimits.WireBytes);
            using var stream = new FileStream(file, FileMode.CreateNew); stream.Write(raw);
        }

        private Exception? Cleanup()
        {
            if (_disposed) return null; _disposed = true; Exception? primary = null;
            void Attempt(Action action)
            {
                try { action(); } catch (Exception error) { if (primary is null) primary = error; else Secondary(primary, error); }
            }
            if (_device is not null)
            {
                _device.PostFrameFailureInjection = null; _device.SchedulingFailureInjection = null;
                Attempt(() => GraphicsPostFrameSnapshots.EndSession(_device));
            }
            for (int index = _owned.Count - 1; index >= 0; index--) Attempt(_owned[index].Dispose);
            if (_device is not null) Attempt(_device.Dispose);
            if (_window is not null) Attempt(_window.Dispose);
            if (_platform is not null) Attempt(_platform.Dispose);
            foreach (string marker in _markers) Attempt(() => File.Delete(marker));
            // No recursive deletion or computed cross-shell file operations.
            if (_directory is not null) Attempt(() => _directory.Delete(recursive: false));
            return primary;
        }
        public void Dispose() { var error = Cleanup(); if (error is not null) ExceptionDispatchInfo.Capture(error).Throw(); }
    }

    private static GraphicsPostFrameResourceBytes Attachment(GraphicsPostFrameSnapshotResult result, string role)
        => Assert.Single(result.Resources, x => x.Description.Attachment?.Role == role);
    private static void ActualMipZeroBacking(SoftwareFixture fixture, GraphicsPostFrameSnapshotResult result)
    {
        object? overlay = typeof(D3D12GraphicsDevice).GetField("_overlayDepthTarget", PrivateInstance)!.GetValue(fixture.Device);
        var actualDepth = (Vortice.Direct3D12.ID3D12Resource)(overlay ?? typeof(D3D12GraphicsDevice).GetField("_depthTarget", PrivateInstance)!.GetValue(fixture.Device)!);
        var actual = actualDepth.Description;
        foreach (string role in AttachmentRoles)
        {
            var resource = Attachment(result, role); var a = resource.Description.Attachment!;
            Assert.Equal(0, a.ObservedMipSlice); Assert.Equal(1, a.ObservedMipLevels); Assert.Equal(0, a.ObservedSubresource);
            Assert.Equal(0, Assert.Single(resource.Description.Mips).MipIndex);
            Assert.Equal((ulong)(a.Width * a.Height * (role == "retained-scene-linear" ? 8 : 4)), (ulong)Assert.Single(resource.CanonicalBytes).LongLength);
            if (role == "retained-final-depth")
            {
                Assert.Equal(actual.MipLevels, a.NativeMipLevels);
                Assert.Equal(GraphicsPostFrameLimits.FullMipLevels(checked((int)actual.Width), checked((int)actual.Height)), a.NativeMipLevels);
            }
            else Assert.Equal(1, a.NativeMipLevels);
        }
    }
    private static void CurrentIncomplete(SoftwareFixture fixture, GraphicsPostFrameCaptureContext context, bool receiptIssued)
    {
        object facts = Assert.IsType<object>(GraphicsPostFrameSnapshots.CurrentFailureFacts(fixture.Device, context), exactMatch: false);
        using var parsed = JsonDocument.Parse(GraphicsPostFrameJson.Serialize(facts, GraphicsPostFrameLimits.EnvelopeBytes));
        var value = parsed.RootElement;
        Assert.Equal(context.Invocation, value.GetProperty("invocation").GetUInt64());
        Assert.Equal(context.Repetition, value.GetProperty("repetition").GetInt32());
        Assert.Equal(context.Mode, value.GetProperty("mode").GetString());
        Assert.True(value.GetProperty("invocationOpen").GetBoolean());
        Assert.Equal(receiptIssued, value.GetProperty("originalReceiptIssued").GetBoolean());
        Assert.False(value.GetProperty("complete").GetBoolean()); Assert.False(value.GetProperty("qualified").GetBoolean());
    }
    private static void KnownContents(GraphicsPostFrameSnapshotResult result, Captured capture, float scale)
    {
        var scene = Attachment(result, "retained-scene-linear"); var shadow = Attachment(result, "retained-shadow-depth"); var depth = Attachment(result, "retained-final-depth");
        int width = GraphicsFramePlan.ScaledDimension(capture.OutputWidth, scale), height = GraphicsFramePlan.ScaledDimension(capture.OutputHeight, scale);
        Assert.Equal(width, scene.Description.Attachment!.Width); Assert.Equal(height, scene.Description.Attachment.Height);
        Assert.Equal((ulong)(width * height * 8), scene.Description.ObservedBytes);
        Assert.Equal(capture.OutputWidth, depth.Description.Attachment!.Width); Assert.Equal(capture.OutputHeight, depth.Description.Attachment.Height);
        Assert.Equal((ulong)(capture.OutputWidth * capture.OutputHeight * 4), depth.Description.ObservedBytes);
        Assert.Equal("R16G16B16A16_Float", scene.Description.Attachment.NativeFormat);
        Assert.Equal("R32_Typeless", shadow.Description.Attachment!.NativeFormat); Assert.Equal("PixelShaderResource", shadow.Description.Attachment.OriginalState);
        Assert.Equal("DepthWrite", depth.Description.Attachment.OriginalState);
        Assert.All(result.Resources, resource => Assert.Equal(resource.Description.ObservedBytes, (ulong)resource.CanonicalBytes.Sum(x => x.LongLength)));
        foreach (float value in MemoryMarshal.Cast<byte, float>(shadow.CanonicalBytes.Single()))
            if (value != 1) Assert.Fail("The completed empty shadow target contains a non-clear depth value.");
        foreach (int x in new[] { width / 4, width * 3 / 4 })
        {
            int texel = ((height / 2) * width + x) * 8; byte[] raw = scene.CanonicalBytes.Single();
            Assert.Equal((Half)1, BitConverter.ToHalf(raw, texel)); Assert.Equal((Half)0, BitConverter.ToHalf(raw, texel + 2));
            Assert.Equal((Half)0, BitConverter.ToHalf(raw, texel + 4)); Assert.Equal((Half)1, BitConverter.ToHalf(raw, texel + 6));
        }
        byte[] finalDepth = depth.CanonicalBytes.Single();
        int left = ((capture.OutputHeight / 2) * capture.OutputWidth + capture.OutputWidth / 4) * 4;
        int right = ((capture.OutputHeight / 2) * capture.OutputWidth + capture.OutputWidth * 3 / 4) * 4;
        // Scaled composition writes sampled World depth through SV_Depth before the left overlay.
        Assert.Equal(.25f, BitConverter.ToSingle(finalDepth, left)); Assert.Equal(.75f, BitConverter.ToSingle(finalDepth, right));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, capture.Pixels.AsSpan(left, 4).ToArray());
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, capture.Pixels.AsSpan(right, 4).ToArray());
        Assert.Equal(scale == 1, result.OriginalFrameReceipt.WorldDepthAliasesRetainedFinalDepth);
    }

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, false)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, false)]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, true)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, true)]
    public void FourCapturesWrapActualSlotsAndRestoreKnownAttachments(string mode, bool requireDebug)
        => WithFixture(mode, "known-debug-" + requireDebug, fixture =>
        {
            var slots = new HashSet<int>(); GraphicsPostFrameSnapshotResult? first = null;
            for (int repetition = 0; repetition < 4; repetition++)
            {
                var capture = fixture.Capture(repetition); var receipt = fixture.Receipt(capture); slots.Add(capture.Slot);
                var submission = fixture.Device.LastSubmission; ulong presents = fixture.Device.PresentedFrameCount;
                var result = fixture.Copy(receipt); Assert.Equal(submission, fixture.Device.LastSubmission); Assert.Equal(presents, fixture.Device.PresentedFrameCount);
                Assert.Equal(capture.Slot, result.OriginalFrameReceipt.FrameIndex); Assert.Equal((ulong)repetition + 1, result.OriginalFrameReceipt.Invocation);
                Assert.NotEqual(result.OriginalFrameReceipt.FenceDomain, result.CopyReceipt.FenceDomain); Assert.True(result.CopyReceipt.CompletionProved);
                ActualMipZeroBacking(fixture, result); KnownContents(result, capture, 1);
                if (mode == GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly) { Assert.Null(result.OpaqueWitness); Assert.Null(Tape(fixture.Device)); }
                else { var witness = Assert.IsType<PostFrameTapeWire>(result.OpaqueWitness); Assert.True(witness.Complete); Assert.Single(witness.Events, x => x.Draw is not null); }
                if (first is not null) foreach (string role in AttachmentRoles)
                    Assert.Equal(Attachment(first, role).CanonicalBytes.Single(), Attachment(result, role).CanonicalBytes.Single());
                fixture.RetainFunctionalEvidence(capture, result);
                GraphicsPostFrameSnapshots.CompleteExport(fixture.Device, receipt, result); first ??= result;
            }
            Assert.Equal(3, slots.Count); Assert.Equal(0, fixture.Device.Diagnostics.Debug.ErrorCount);
            GraphicsPostFrameSnapshots.EndSession(fixture.Device);
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 4));
            ulong before = fixture.Device.PresentedFrameCount; fixture.Device.RenderFrame(new(0, 0, 0, 1)); Assert.Equal(before + 1, fixture.Device.PresentedFrameCount);
        }, requireDebug: requireDebug);

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)]
    public void IndexedTextureAndBufferedShadowReceiverUseActualCurrentArenaAndImmutableBytes(string mode)
        => WithFixture(mode, "receiver", fixture =>
        {
            fixture.PrepareReceiver();
            for (int repetition = 0; repetition < 2; repetition++)
            {
                var capture = fixture.Capture(repetition, receiver: true); var receipt = fixture.Receipt(capture); var result = fixture.Copy(receipt, receiver: true);
                Assert.Equal(fixture.VertexBytes, result.Resources.Single(x => x.Description.SemanticName == "fixture-vertex").CanonicalBytes.Single());
                Assert.Equal(fixture.IndexBytes, result.Resources.Single(x => x.Description.SemanticName == "fixture-index").CanonicalBytes.Single());
                Assert.Equal(fixture.TextureBytes, result.Resources.Single(x => x.Description.SemanticName == "fixture-texture").CanonicalBytes.Single());
                ActualMipZeroBacking(fixture, result);
                var scene = Attachment(result, "retained-scene-linear");
                int width = scene.Description.Attachment!.Width, height = scene.Description.Attachment.Height;
                int center = ((height / 2) * width + width / 2) * 8; byte[] linear = scene.CanonicalBytes.Single();
                // D3D functional spec 3.2.2 narrows FP32 to FP16 toward zero; 3.2.3.5 defines UNORM conversion.
                // https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm
                // These exact bits preserve the original distinct 51/102/153/255 texture; .NET Half rounds nearest.
                Assert.Equal((ushort)0x3266, BitConverter.ToUInt16(linear, center));
                Assert.Equal((ushort)0x3666, BitConverter.ToUInt16(linear, center + 2));
                Assert.Equal((ushort)0x38cc, BitConverter.ToUInt16(linear, center + 4));
                Assert.Equal((ushort)0x3c00, BitConverter.ToUInt16(linear, center + 6));
                var retainedDepth = Attachment(result, "retained-final-depth");
                int depthCenter = ((retainedDepth.Description.Attachment!.Height / 2) * retainedDepth.Description.Attachment.Width + retainedDepth.Description.Attachment.Width / 2) * 4;
                Assert.Equal(.75f, BitConverter.ToSingle(retainedDepth.CanonicalBytes.Single(), depthCenter));
                if (mode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)
                {
                    var witness = Assert.IsType<PostFrameTapeWire>(result.OpaqueWitness); Assert.True(witness.Complete);
                    Assert.True(Assert.Single(witness.Events, x => x.Draw is not null).Draw!.Indexed);
                    Assert.Contains(witness.Creations, x => x.CreationRole == PostFrameTapeCreationRole.DirectionalShadowReceiver);
                    Assert.Contains(witness.Events, x => x.RootWords is { Buffered: true, Parameter: 0, SourceOffset: 256 } words && words.ArenaSlot == capture.Slot && words.ArenaIdentity > 0);
                    Assert.Contains(witness.Events, x => x.RootWords is { Buffered: true, WordCount: 24, SourceOffset: 0, OwnerSource: PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit });
                    Assert.All(result.Resources.Where(x => x.Description.Kind is "buffer" or "texture"), x => Assert.NotNull(x.Description.CausalIdentityId));
                }
                fixture.RetainFunctionalEvidence(capture, result); GraphicsPostFrameSnapshots.CompleteExport(fixture.Device, receipt, result);
            }
            Assert.Equal(0, fixture.Device.Diagnostics.Debug.ErrorCount);
        });

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)]
    public void ScaledFinalDepthIsActualFullOutputAndOpaqueRefusesUncoveredWorldDepth(string mode)
        => WithFixture(mode, "scaled", fixture =>
        {
            var capture = fixture.Capture(0);
            if (mode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)
            {
                Assert.Throws<NotSupportedException>(() => fixture.Receipt(capture));
                Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 1));
            }
            else
            {
                var receipt = fixture.Receipt(capture); var result = fixture.Copy(receipt); ActualMipZeroBacking(fixture, result); KnownContents(result, capture, .5f);
                Assert.Equal("D32_Float", Attachment(result, "retained-final-depth").Description.Attachment!.NativeFormat);
                fixture.RetainFunctionalEvidence(capture, result); GraphicsPostFrameSnapshots.CompleteExport(fixture.Device, receipt, result);
            }
            Assert.Equal(0, fixture.Device.Diagnostics.Debug.ErrorCount);
        }, scale: .5f);

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, "scene")]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, "scene")]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, "shadow")]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, "shadow")]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, "replacement")]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, "replacement")]
    public void MissingOrReplacedRequiredWitnessRefusesWithoutAnotherFrame(string mode, string fault)
        => WithFixture(mode, "missing-" + fault, fixture =>
        {
            var capture = fixture.Capture(0, draw: false); var submission = fixture.Device.LastSubmission;
            if (fault == "replacement") fixture.Device.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Medium));
            Assert.Equal(submission, fixture.Device.LastSubmission);
            Assert.Throws<InvalidOperationException>(() => fixture.Receipt(capture));
            CurrentIncomplete(fixture, capture.Context, receiptIssued: false);
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 1));
        }, scene: fault != "scene", shadows: fault != "shadow");

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, (int)GraphicsPostFrameOperation.BeforeExecute)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, (int)GraphicsPostFrameOperation.BeforeExecute)]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, (int)GraphicsPostFrameOperation.AfterExecute)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, (int)GraphicsPostFrameOperation.AfterExecute)]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, (int)GraphicsPostFrameOperation.Signal)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, (int)GraphicsPostFrameOperation.Signal)]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, (int)GraphicsPostFrameOperation.Map)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, (int)GraphicsPostFrameOperation.Map)]
    public void CopyFailureKeepsPrimaryCurrentContextAndUncertainLeaseUntilActualDrain(string mode, int pointValue)
        => WithFixture(mode, "fault-" + pointValue, fixture =>
        {
            var capture = fixture.Capture(0); var receipt = fixture.Receipt(capture); var registry = Registry(fixture.Device); int baseline = registry.LiveCount;
            var point = (GraphicsPostFrameOperation)pointValue; var primary = new IOException("Injected software late-copy fault " + point);
            fixture.Device.PostFrameFailureInjection = operation => { if (operation == point) throw primary; };
            Assert.Same(primary, Assert.Throws<IOException>(() => fixture.Copy(receipt)));
            var prefix = GraphicsPostFrameSnapshots.Failure(primary, receipt); Assert.NotNull(prefix); Assert.Same(receipt.Original, prefix.OriginalFrameReceipt);
            bool uncertain = point is GraphicsPostFrameOperation.AfterExecute or GraphicsPostFrameOperation.Signal;
            Assert.Equal(uncertain ? baseline + 1 : baseline, registry.LiveCount);
            Assert.Equal(point != GraphicsPostFrameOperation.BeforeExecute, prefix.MayHaveExecuted);
            if (point == GraphicsPostFrameOperation.Map) Assert.True(prefix.CopyReceipt!.CompletionProved);
            else Assert.Null(prefix.CopyReceipt);
            CurrentIncomplete(fixture, capture.Context, receiptIssued: true); Assert.True(File.Exists(capture.ClosedTestMarker));
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 1));
            fixture.Device.PostFrameFailureInjection = null; fixture.Dispose(); Assert.Equal(0, registry.LiveCount); Assert.Equal(0, registry.PendingCount);
        });

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)]
    public void PublicationAndActualResultIdentityGateRearmAndTerminalEndRejectsOldContext(string mode)
        => WithFixture(mode, "publication", fixture =>
        {
            var capture = fixture.Capture(0); var receipt = fixture.Receipt(capture);
            var foreign = new GraphicsPostFrameReceipt(new object(), receipt.Serial, receipt.Original, capture.Context);
            Assert.Throws<InvalidOperationException>(() => fixture.Copy(foreign)); Assert.False(foreign.Consumed); Assert.False(receipt.Consumed);
            var result = fixture.Copy(receipt);
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 1));
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.CompleteExport(fixture.Device, receipt, result with { }));
            fixture.RetainFunctionalEvidence(capture, result); GraphicsPostFrameSnapshots.CompleteExport(fixture.Device, receipt, result);
            GraphicsPostFrameSnapshots.EndSession(fixture.Device); GraphicsPostFrameSnapshots.EndSession(fixture.Device);
            Assert.Null(GraphicsPostFrameSnapshots.CurrentFailureFacts(fixture.Device, capture.Context));
            Assert.Throws<InvalidOperationException>(() => fixture.Receipt(capture));
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 1));
        });

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, false)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, false)]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly, true)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments, true)]
    public void ConfiguredPendingOrUnpublishedInvocationCannotSurviveEnd(string mode, bool pending)
        => WithFixture(mode, "end-pending-" + pending, fixture =>
        {
            if (pending) _ = GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 0);
            GraphicsPostFrameSnapshots.EndSession(fixture.Device); GraphicsPostFrameSnapshots.EndSession(fixture.Device);
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 0));
        });

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)]
    public void CallerPublicationFailureLeavesCurrentUnpublishedInvocationAndPrimary(string mode)
        => WithFixture(mode, "publication-fault", fixture =>
        {
            var primary = new IOException("Injected caller publication failure; no product PNG exists.");
            Assert.Same(primary, Assert.Throws<IOException>(() => fixture.Capture(0, beforePublication: () => throw primary)));
            CurrentIncomplete(fixture, fixture.LastContext, receiptIssued: false);
            Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.BeginCapture(fixture.Device, mode, 1));
            GraphicsPostFrameSnapshots.EndSession(fixture.Device); Assert.Null(GraphicsPostFrameSnapshots.CurrentFailureFacts(fixture.Device, fixture.LastContext));
        });

    [Theory]
    [InlineData(GraphicsPostFrameSnapshotModes.RetainedAttachmentsOnly)]
    [InlineData(GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)]
    public void ActualRecordingAndForeignOwnerCannotTerminateCurrentSession(string mode)
        => WithFixture(mode, "end-guards", fixture =>
        {
            var capture = fixture.Capture(0, duringRecording: () =>
            {
                Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.EndSession(fixture.Device));
                Exception? foreign = null;
                var thread = new Thread(() => { try { GraphicsPostFrameSnapshots.EndSession(fixture.Device); } catch (Exception error) { foreign = error; } });
                thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5))); Assert.IsType<InvalidOperationException>(foreign);
            });
            var receipt = fixture.Receipt(capture); var result = fixture.Copy(receipt);
            fixture.RetainFunctionalEvidence(capture, result); GraphicsPostFrameSnapshots.CompleteExport(fixture.Device, receipt, result);
            Assert.Equal(0, fixture.Device.Diagnostics.Debug.ErrorCount);
        });

    [Fact]
    public void CleanupClassificationPureRegressionNeverConstructsANativeFixture()
    {
        var skip = Xunit.Sdk.SkipException.ForSkip("Synthetic expected debug-unavailable skip; no native construction.");
        var cleanup = new IOException("Synthetic cleanup failure");
        var failed = Assert.IsType<AggregateException>(WithCleanup(skip, cleanup));
        Assert.Equal(2, failed.InnerExceptions.Count); Assert.Same(skip, failed.InnerExceptions[0]); Assert.Same(cleanup, failed.InnerExceptions[1]);
        var primary = new InvalidOperationException("Synthetic ordinary body/constructor failure");
        Assert.Same(primary, WithCleanup(primary, cleanup));
        Assert.Contains(primary.Data.Values.Cast<object>(), item => ReferenceEquals(item, cleanup));
        Assert.Same(cleanup, WithCleanup(null, cleanup));
    }
}
