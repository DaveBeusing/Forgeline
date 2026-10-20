using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class NativeUploadSchedulingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializedStaticBuffersCopyAndDrawWithoutDebugWarnings(bool requireDebugLayer)
    {
        using var fixture = new NativeFixture(false, requireDebugLayer);
        Assert.SkipWhen(requireDebugLayer && !fixture.Device.Diagnostics.Debug.Enabled,
            "D3D12 debug layer is unavailable.");
        byte[] vertices = NativeFixture.Vertices();
        byte[] indices = MemoryMarshal.AsBytes<ushort>(new ushort[] { 0, 1, 2 }.AsSpan()).ToArray();
        using var vertex = fixture.Device.CreateInitializedStaticBuffer(
            new((ulong)vertices.Length, StaticGraphicsBufferUsage.Vertex), vertices);
        using var index = fixture.Device.CreateInitializedStaticBuffer(
            new((ulong)indices.Length, StaticGraphicsBufferUsage.Index), indices);
        Assert.Contains(fixture.Capture(vertex, indexBuffer: index), value => value != 0);
        fixture.Device.WaitForIdle();
        Assert.Equal(0, fixture.Device.Diagnostics.Debug.WarningCount);
        Assert.Equal(0, fixture.Device.Diagnostics.Debug.ErrorCount);
    }

    [Fact]
    public void InitializedGpuLocalBufferCopiesInputAndMatchesUploadFallback()
    {
        using var fixture = new NativeFixture(false);
        byte[] vertices = NativeFixture.Vertices();
        using IGraphicsBuffer gpu = fixture.Device.CreateInitializedStaticBuffer(new((ulong)vertices.Length, StaticGraphicsBufferUsage.Vertex), vertices);
        using IGraphicsBuffer fallback = fixture.Device.CreateBuffer(new((ulong)vertices.Length, GraphicsBufferMemory.Upload));
        fallback.SetData<byte>(vertices);
        Array.Clear(vertices);
        Assert.Equal(GraphicsBufferMemory.GpuLocal, gpu.Description.Memory);
        Assert.Throws<InvalidOperationException>(() => gpu.SetData<byte>([1]));
        byte[] expected = fixture.Capture(fallback);
        Assert.Equal(expected, fixture.Capture(gpu));
        Assert.Contains(expected, value => value != 0);
    }

    [Theory]
    [InlineData(GraphicsTextureFormat.Rgba8Unorm)]
    [InlineData(GraphicsTextureFormat.R8Unorm)]
    [InlineData(GraphicsTextureFormat.Bc7Unorm)]
    public void DeferredOddRowsAndSmallMipsConsumeInputBeforeReturn(GraphicsTextureFormat format)
    {
        using var fixture = new NativeFixture(true);
        GraphicsTextureData immediate = Texture(format, false);
        GraphicsTextureData deferred = Texture(format, true);
        using IGraphicsTexture control = fixture.Device.CreateTexture(immediate);
        using IGraphicsTexture uploaded = fixture.Device.CreateTexture(deferred);
        foreach (GraphicsTextureMipData mip in deferred.Mips) Array.Clear(mip.Pixels);
        using IGraphicsBuffer vertices = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), NativeFixture.Vertices());
        for (int level = 0; level < 3; level++)
        {
            byte[] actual = fixture.Capture(vertices, uploaded, level);
            Assert.Equal(fixture.Capture(vertices, control, level), actual);
            GraphicsTextureMipData expected = immediate.Mips[level];
            int width = fixture.Device.Diagnostics.Surface.Width;
            int height = fixture.Device.Diagnostics.Surface.Height;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int sourceOffset = (y % expected.Height) * expected.RowPitch + (x % expected.Width) * (format == GraphicsTextureFormat.R8Unorm ? 1 : 4);
                    int destinationOffset = (y * width + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        byte value = format == GraphicsTextureFormat.Bc7Unorm ? (byte)255
                            : format == GraphicsTextureFormat.R8Unorm && channel > 0 ? (channel == 3 ? (byte)255 : (byte)0)
                            : expected.Pixels[sourceOffset + channel];
                        Assert.Equal(value, actual[destinationOffset + channel]);
                    }
                }
        }
        Assert.Equal(0, fixture.Device.Diagnostics.Debug.ErrorCount);
    }

    [Fact]
    public void HundredLoadUnloadCyclesHaveBoundedNativePlateauAndBatchSubmissions()
    {
        using var fixture = new NativeFixture(false);
        var device = fixture.Device;
        int baselineLive = device.Diagnostics.Health.LiveResourceCount;
        for (int cycle = 0; cycle < 100; cycle++)
        {
            using IGraphicsTexture texture = device.CreateTexture(Texture(GraphicsTextureFormat.Rgba8Unorm, true));
        }
        device.WaitForIdle();
        UploadArenaDiagnostics warm = device.UploadDiagnostics;
        Assert.Equal(100, warm.Transfers);
        Assert.True(warm.Submissions <= 4);
        Assert.InRange(warm.Slots, 1, UploadArenaLimits.SlotCount);
        Assert.True(warm.PeakResidentBytes <= UploadArenaLimits.MaximumResidentBytes);
        Assert.Equal(0, device.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.Equal(baselineLive, device.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
        for (int cycle = 0; cycle < 100; cycle++)
        {
            using IGraphicsTexture texture = device.CreateTexture(Texture(GraphicsTextureFormat.Rgba8Unorm, true));
            if (cycle % UploadArenaLimits.MaximumTransfersPerBatch == UploadArenaLimits.MaximumTransfersPerBatch - 1)
                device.WaitForIdle();
        }
        device.WaitForIdle();
        Assert.Equal(warm.Allocations, device.UploadDiagnostics.Allocations);
        Assert.Equal(warm.ResidentBytes, device.UploadDiagnostics.ResidentBytes);
        Assert.Equal(baselineLive, device.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, device.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }

    [Fact]
    public void FailedStagingGrowthPreservesPreviouslyValidBuffer()
    {
        using var fixture = new NativeFixture(false);
        using IGraphicsBuffer original = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), NativeFixture.Vertices());
        fixture.Device.SchedulingFailureInjection = operation =>
        {
            if (operation == SchedulingOperation.AllocateUpload) throw new InvalidOperationException("Injected staging allocation failure.");
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Device.CreateInitializedStaticBuffer(
            new(8 * 1024 * 1024, StaticGraphicsBufferUsage.Vertex), new byte[8 * 1024 * 1024]));
        fixture.Device.SchedulingFailureInjection = null;
        Assert.NotEmpty(fixture.Capture(original));
        fixture.Device.WaitForIdle();
        Assert.Equal(0, fixture.Device.Diagnostics.Health.SubmissionFaultCount);
    }

    [Theory]
    [InlineData((int)SchedulingOperation.CloseUpload)]
    [InlineData((int)SchedulingOperation.ExecuteUpload)]
    [InlineData((int)SchedulingOperation.SignalUpload)]
    public void UploadFaultRetainsOwnershipUntilKnownIdle(int point)
    {
        SchedulingOperation injected = (SchedulingOperation)point;
        using var fixture = new NativeFixture(false);
        IGraphicsTexture texture = fixture.Device.CreateTexture(Texture(GraphicsTextureFormat.Rgba8Unorm, true));
        fixture.Device.SchedulingFailureInjection = operation =>
        {
            if (operation == injected) throw new InvalidOperationException("Injected upload submission failure.");
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Device.WaitForIdle());
        texture.Dispose();
        Assert.True(fixture.Device.Diagnostics.Health.LiveResourceCount >= 1);
        Assert.True(fixture.Device.Diagnostics.Health.SubmissionFaultCount >= 1);
        Assert.Throws<GraphicsDeviceException>(() => fixture.Device.CreateBuffer(new(16)));
        fixture.Device.SchedulingFailureInjection = null;
        // Fixture disposal establishes actual queue idle before releasing retained objects.
    }

    [Fact]
    public void ParticleDescriptorBindsAfterPipelineRootSignature()
    {
        using var fixture = new NativeFixture(false);
        fixture.Device.ConfigureParticleDepth(true);
        using var renderer = new GpuParticleRenderer(fixture.Device, 512);
        using IGraphicsBuffer vertices = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), NativeFixture.Vertices());
        byte[] Capture() => fixture.Device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            fixture.BindSolidPipeline(context);
            context.SetVertexBuffer(vertices, 12);
            context.Draw(3);
            GraphicsParticle[] particles = [new(new(0, 0, .25f, .25f), new(0, 0, 0, 1), new(0, 1, 0, 1), new(.5f, 0, 2, 0))];
            renderer.Render(context, particles, System.Numerics.Matrix4x4.Identity,
                System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, 1, -1, 1, false, false);
            context.BeginPass(GraphicsFramePass.Overlay);
        });
        byte[] image = Capture();
        Assert.Equal(image, Capture());
        var surface = fixture.Device.Diagnostics.Surface;
        Assert.True(image[(surface.Height / 2 * surface.Width + surface.Width / 2) * 4 + 1] > 128);
    }

    [Fact]
    public void SharedTexturesSurviveRootAndPassChangesAndRejectDisposedRebind()
    {
        using var fixture = new NativeFixture(true);
        using IGraphicsTexture texture = fixture.Device.CreateTexture(Texture(GraphicsTextureFormat.Rgba8Unorm, true));
        using IGraphicsBuffer vertices = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), NativeFixture.Vertices());
        byte[] reference = fixture.Capture(vertices, texture);
        byte[] changed = fixture.CaptureRootAndPassChanges(vertices, texture);
        Assert.Equal(reference, changed);
        fixture.Device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            fixture.BindSolidPipeline(context);
            context.SetVertexBuffer(vertices, 12);
            context.SetPixelTexture(0, texture); context.SetPixelTexture(0, texture);
            context.Draw(3); texture.Dispose();
            Assert.Throws<ObjectDisposedException>(() => context.SetPixelTexture(0, texture));
        });
    }

    [Fact]
    public void InitializedIndexBufferMatchesItsUploadFallback()
    {
        using var fixture = new NativeFixture(false);
        using IGraphicsBuffer vertices = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), NativeFixture.Vertices());
        byte[] indices = MemoryMarshal.AsBytes<ushort>(new ushort[] { 0, 1, 2 }.AsSpan()).ToArray();
        using IGraphicsBuffer gpu = fixture.Device.CreateInitializedStaticBuffer(new(6, StaticGraphicsBufferUsage.Index), indices);
        using IGraphicsBuffer upload = fixture.Device.CreateBuffer(new(6, GraphicsBufferMemory.Upload));
        upload.SetData<byte>(indices); Array.Clear(indices);
        Assert.Equal(fixture.Capture(vertices, indexBuffer: upload), fixture.Capture(vertices, indexBuffer: gpu));
        Assert.Equal(GraphicsBufferMemory.GpuLocal, gpu.Description.Memory);
    }

    [Theory]
    [InlineData((int)SchedulingOperation.MapUpload)]
    [InlineData((int)SchedulingOperation.AllocateDescriptor)]
    public void UnsubmittedTransferFailurePreservesExistingResources(int point)
    {
        using var fixture = new NativeFixture(true);
        using IGraphicsTexture existing = fixture.Device.CreateTexture(Texture(GraphicsTextureFormat.Rgba8Unorm, false));
        fixture.Device.SchedulingFailureInjection = operation =>
        {
            if (operation == (SchedulingOperation)point) throw new InvalidOperationException("Injected pre-submission failure.");
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Device.CreateTexture(Texture(GraphicsTextureFormat.Rgba8Unorm, true)));
        fixture.Device.SchedulingFailureInjection = null;
        fixture.Device.WaitForIdle();
        using IGraphicsBuffer vertices = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), NativeFixture.Vertices());
        Assert.NotEmpty(fixture.Capture(vertices, existing));
        Assert.Equal(0, fixture.Device.Diagnostics.Health.SubmissionFaultCount);
        Assert.Equal(0, fixture.Device.Diagnostics.Health.PendingRetirementCount);
    }

    [Theory]
    [InlineData((int)SchedulingOperation.ExecuteFrame)]
    [InlineData((int)SchedulingOperation.SignalFrame)]
    [InlineData((int)SchedulingOperation.Present)]
    [InlineData((int)SchedulingOperation.Resize)]
    public void FrameFaultPreservesStickyShutdownAndClosedContexts(int point)
    {
        using var fixture = new NativeFixture(false);
        SchedulingOperation injected = (SchedulingOperation)point;
        if (injected == SchedulingOperation.Resize) fixture.Device.Resize(400, 300);
        IGraphicsCommandContext? escaped = null;
        fixture.Device.SchedulingFailureInjection = operation =>
        {
            if (operation == injected) throw new InvalidOperationException("Injected graphics lifecycle failure.");
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Device.RenderFrame(GraphicsColor.ForgeLineClear, context => escaped = context));
        if (escaped is not null) Assert.Throws<ObjectDisposedException>(() => escaped.Draw(3));
        Assert.True(fixture.Device.Diagnostics.Health.SubmissionFaultCount >= 1);
        Assert.Throws<GraphicsDeviceException>(() => fixture.Device.RenderFrame(GraphicsColor.ForgeLineClear));
        fixture.Device.SchedulingFailureInjection = null;
    }

    [Fact]
    public void LargeTransfersAndFailedDisposeStayWithinExplicitOwnership()
    {
        using var fixture = new NativeFixture(false);
        byte[] data = new byte[6 * 1024 * 1024];
        for (int i = 0; i < 8; i++)
        {
            using IGraphicsBuffer buffer = fixture.Device.CreateInitializedStaticBuffer(new((ulong)data.Length, StaticGraphicsBufferUsage.Index), data);
        }
        fixture.Device.WaitForIdle();
        Assert.True(fixture.Device.UploadDiagnostics.ResidentBytes <= UploadArenaLimits.MaximumResidentBytes);
        Assert.True(fixture.Device.UploadDiagnostics.Slots <= UploadArenaLimits.SlotCount);
        fixture.Device.SchedulingFailureInjection = operation =>
        {
            if (operation == SchedulingOperation.Dispose) throw new InvalidOperationException("Injected shutdown admission failure.");
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Device.Dispose());
        fixture.Device.SchedulingFailureInjection = null;
        fixture.Device.WaitForIdle();
        Assert.Equal(0, fixture.Device.Diagnostics.Health.PendingRetirementCount);
    }

    private static GraphicsTextureData Texture(GraphicsTextureFormat format, bool defer)
    {
        int width = format == GraphicsTextureFormat.Bc7Unorm ? 8 : 13;
        int height = format == GraphicsTextureFormat.Bc7Unorm ? 4 : 7;
        var mips = new List<GraphicsTextureMipData>();
        for (int level = 0; level < 3; level++)
        {
            int rows = format == GraphicsTextureFormat.Bc7Unorm ? (height + 3) / 4 : height;
            int rowBytes = format == GraphicsTextureFormat.Bc7Unorm ? (width + 3) / 4 * 16 : width * (format == GraphicsTextureFormat.R8Unorm ? 1 : 4);
            int pitch = rowBytes + (format == GraphicsTextureFormat.Bc7Unorm ? 0 : 5);
            byte[] pixels = new byte[pitch * rows];
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < rowBytes; col++) pixels[row * pitch + col] = (byte)(37 + (row * 29 + col * 17 + level * 31) % 211);
            if (format == GraphicsTextureFormat.Bc7Unorm) Array.Fill(pixels, (byte)255);
            mips.Add(new(width, height, pitch, pixels));
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        return new(new(format == GraphicsTextureFormat.Bc7Unorm ? 8 : 13,
            format == GraphicsTextureFormat.Bc7Unorm ? 4 : 7, format, GraphicsTextureColorSpace.Linear, 3) { DeferUploadWait = defer }, mips);
    }

    private sealed class NativeFixture : IDisposable
    {
        private readonly WindowsPlatform _platform = new();
        private readonly IWindow _window;
        private readonly IGraphicsPipeline[] _pipelines;
        internal D3D12GraphicsDevice Device { get; }
        internal NativeFixture(bool textured, bool requestDebugLayer = false)
        {
            _window = _platform.CreateWindow(new WindowConfiguration("FORGELINE Upload Qualification", 64, 64));
            Device = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(_window,
                new GraphicsConfiguration { AllowSoftwareAdapterFallback = true, EnableVSync = false, ForceSoftwareAdapter = requestDebugLayer,
                    EnableDebugLayer = requestDebugLayer || Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_SCHEDULING_DEBUG") == "1" });
            if (Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_SCHEDULING_DEBUG") == "1")
                Assert.True(Device.Diagnostics.Device.DebugLayerEnabled, "Requested native debug layer is unavailable.");
            var compiler = new DxcShaderCompiler();
            GraphicsShaderBytecode vertex = compiler.Compile("float4 VSMain(float3 position:POSITION):SV_Position { return float4(position,1); }",
                GraphicsShaderStage.Vertex, "VSMain", "UploadVertex.hlsl");
            _pipelines = new IGraphicsPipeline[textured ? 3 : 1];
            for (int level = 0; level < _pipelines.Length; level++)
            {
                GraphicsShaderBytecode pixel = compiler.Compile(textured
                    ? $"Texture2D Color:register(t0); float4 PSMain(float4 p:SV_Position):SV_Target {{ uint w,h,n; Color.GetDimensions({level},w,h,n); return Color.Load(int3(uint2(p.xy)%uint2(w,h),{level})); }}"
                    : "float4 PSMain():SV_Target { return float4(0.25,0.5,0.75,1); }",
                    GraphicsShaderStage.Pixel, "PSMain", "UploadPixel.hlsl");
                _pipelines[level] = Device.CreateGraphicsPipeline(new(vertex, pixel) { VertexElements = [new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)], CullMode = GraphicsCullMode.None, PixelTextureCount = textured ? 1 : 0 });
            }
        }
        internal byte[] CaptureRootAndPassChanges(IGraphicsBuffer vertices, IGraphicsTexture texture) => Device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            for (int index = 0; index < 3; index++)
            {
                int level = index == 1 ? 1 : 0;
                context.SetPipeline(_pipelines[level]); context.SetVertexBuffer(vertices, 12);
                context.SetPixelTexture(0, texture); context.SetPixelTexture(0, texture); context.Draw(3);
            }
            context.BeginPass(GraphicsFramePass.Overlay);
            context.SetPipeline(_pipelines[0]); context.SetVertexBuffer(vertices, 12);
            context.SetPixelTexture(0, texture); context.Draw(3);
        });
        internal void BindSolidPipeline(IGraphicsCommandContext context) => context.SetPipeline(_pipelines[0]);
        internal static byte[] Vertices() => MemoryMarshal.AsBytes<float>(new float[] { -1, -1, 0, -1, 3, 0, 3, -1, 0 }.AsSpan()).ToArray();
        internal byte[] Capture(IGraphicsBuffer vertices, IGraphicsTexture? texture = null, int level = 0, IGraphicsBuffer? indexBuffer = null) => Device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.SetPipeline(_pipelines[level]); context.SetPipeline(_pipelines[level]);
            context.SetVertexBuffer(vertices, 12);
            if (texture is not null) context.SetPixelTexture(0, texture);
            if (indexBuffer is not null) { context.SetIndexBuffer(indexBuffer, GraphicsIndexFormat.SixteenBit); context.DrawIndexed(3); }
            else context.Draw(3);
        });
        public void Dispose()
        {
            Device.SchedulingFailureInjection = null;
            foreach (IGraphicsPipeline pipeline in _pipelines) pipeline.Dispose();
            Device.Dispose(); _window.Dispose(); _platform.Dispose();
        }
    }
}
