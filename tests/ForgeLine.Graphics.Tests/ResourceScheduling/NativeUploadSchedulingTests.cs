using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class NativeUploadSchedulingTests
{
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
        Assert.Equal(fixture.Capture(vertices, control), fixture.Capture(vertices, uploaded));
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
        private readonly IGraphicsPipeline _pipeline;
        internal D3D12GraphicsDevice Device { get; }
        internal NativeFixture(bool textured)
        {
            _window = _platform.CreateWindow(new WindowConfiguration("FORGELINE Upload Qualification", 64, 64));
            Device = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(_window,
                new GraphicsConfiguration { AllowSoftwareAdapterFallback = true, EnableVSync = false });
            var compiler = new DxcShaderCompiler();
            GraphicsShaderBytecode vertex = compiler.Compile("float4 VSMain(float3 position:POSITION):SV_Position { return float4(position,1); }",
                GraphicsShaderStage.Vertex, "VSMain", "UploadVertex.hlsl");
            GraphicsShaderBytecode pixel = compiler.Compile(textured
                ? "Texture2D Color:register(t0); SamplerState MaterialSampler:register(s0); float4 PSMain(float4 p:SV_Position):SV_Target { return Color.SampleLevel(MaterialSampler,p.xy/64,0); }"
                : "float4 PSMain():SV_Target { return float4(0.25,0.5,0.75,1); }",
                GraphicsShaderStage.Pixel, "PSMain", "UploadPixel.hlsl");
            _pipeline = Device.CreateGraphicsPipeline(new(vertex, pixel) { VertexElements = [new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)], CullMode = GraphicsCullMode.None, PixelTextureCount = textured ? 1 : 0 });
        }
        internal static byte[] Vertices() => MemoryMarshal.AsBytes<float>(new float[] { -1, -1, 0, -1, 3, 0, 3, -1, 0 }.AsSpan()).ToArray();
        internal byte[] Capture(IGraphicsBuffer vertices, IGraphicsTexture? texture = null) => Device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.SetPipeline(_pipeline); context.SetPipeline(_pipeline);
            context.SetVertexBuffer(vertices, 12);
            if (texture is not null) context.SetPixelTexture(0, texture);
            context.Draw(3);
        });
        public void Dispose()
        {
            Device.SchedulingFailureInjection = null;
            _pipeline.Dispose(); Device.Dispose(); _window.Dispose(); _platform.Dispose();
        }
    }
}
