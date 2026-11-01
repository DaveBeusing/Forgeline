using System.Reflection;
using ForgeLine.Graphics.RenderContracts;
using ForgeLine.Graphics.Tests.ResourceScheduling;
using Xunit;
using Fixture = ForgeLine.Graphics.Tests.ResourceScheduling.NativeUploadSchedulingTests.NativeFixture;

namespace ForgeLine.Graphics.Tests.RenderTelemetry;

// Functional WARP diagnostics only. PNG/source ordering is tested at the Client boundary separately.
public sealed class PostFrameSnapshotNativeTests
{
    private static GraphicsResourceRetirement Registry(D3D12GraphicsDevice device)
        => (GraphicsResourceRetirement)typeof(D3D12GraphicsDevice).GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
    private static int Capture(Fixture fixture, IGraphicsBuffer vertices, IGraphicsTexture? texture = null)
    {
        int slot = -1;
        _ = fixture.Device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            slot = context.FrameIndex; fixture.BindSolidPipeline(context); context.SetVertexBuffer(vertices, 12);
            if (texture is not null) context.SetPixelTexture(0, texture);
            context.Draw(3);
        });
        return slot;
    }
    private static GraphicsTextureData Texture(GraphicsTextureFormat format, GraphicsTextureColorSpace color)
    {
        var mips = new List<GraphicsTextureMipData>();
        for (int level = 0, width = 8; level < 3; level++, width /= 2)
        {
            int rows = format == GraphicsTextureFormat.Bc7Unorm ? (width + 3) / 4 : width;
            int rowBytes = format == GraphicsTextureFormat.Bc7Unorm ? (width + 3) / 4 * 16 : width * (format == GraphicsTextureFormat.R8Unorm ? 1 : 4);
            int pitch = rowBytes + 7; var bytes = new byte[pitch * rows];
            for (int row = 0; row < rows; row++) for (int x = 0; x < rowBytes; x++) bytes[row * pitch + x] = format == GraphicsTextureFormat.Bc7Unorm ? (byte)255 : (byte)(37 + (row * 11 + x * 17 + level) % 211);
            mips.Add(new(width, width, pitch, bytes));
        }
        return new(new(8, 8, format, color, 3), mips);
    }
    [Theory]
    [InlineData(GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear)]
    [InlineData(GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Srgb)]
    [InlineData(GraphicsTextureFormat.R8Unorm, GraphicsTextureColorSpace.Linear)]
    [InlineData(GraphicsTextureFormat.Bc7Unorm, GraphicsTextureColorSpace.Linear)]
    [InlineData(GraphicsTextureFormat.Bc7Unorm, GraphicsTextureColorSpace.Srgb)]
    public void LateBufferAndEncodedTextureBytesMatchAndSubsequentUseRestoresState(GraphicsTextureFormat format, GraphicsTextureColorSpace color)
    {
        using var fixture = new Fixture(true, requestDebugLayer: true); var device = fixture.Device;
        Assert.True(device.Diagnostics.Device.IsSoftwareAdapter); Console.WriteLine($"[post-frame-native] software={device.Diagnostics.Device.IsSoftwareAdapter} debugLayerEnabled={device.Diagnostics.Device.DebugLayerEnabled}");
        byte[] vertexBytes = Fixture.Vertices(), indexBytes = [0, 0, 1, 0, 2, 0];
        using var vertices = device.CreateBuffer(new((ulong)vertexBytes.Length, GraphicsBufferMemory.Upload)); vertices.SetData<byte>(vertexBytes);
        using var indices = device.CreateInitializedStaticBuffer(new((ulong)indexBytes.Length, StaticGraphicsBufferUsage.Index), indexBytes);
        var data = Texture(format, color); using var texture = device.CreateTexture(data);
        int slot = Capture(fixture, vertices, texture); var receipt = GraphicsPostFrameSnapshots.IssueReceipt(device, slot);
        var roster = new GraphicsPostFrameRoster(); int vb = roster.AddBuffer("upload", vertices), ib = roster.AddBuffer("default", indices), tex = roster.AddTexture("immutable", texture);
        var before = device.LastSubmission; ulong presents = device.PresentedFrameCount;
        var snapshot = GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], []));
        Assert.Equal(before, device.LastSubmission); Assert.Equal(presents, device.PresentedFrameCount);
        Assert.NotEqual(snapshot.OriginalFrameReceipt.FenceDomain, snapshot.CopyReceipt.FenceDomain);
        Assert.True(snapshot.CopyReceipt.CompletionProved);
        Assert.Equal(vertexBytes, snapshot.Resources.Single(r => r.Description.LocalId == vb).CanonicalBytes.Single());
        Assert.Equal(indexBytes, snapshot.Resources.Single(r => r.Description.LocalId == ib).CanonicalBytes.Single());
        var sampled = snapshot.Resources.Single(r => r.Description.LocalId == tex);
        for (int mip = 0; mip < data.Mips.Count; mip++)
        {
            var source = data.Mips[mip]; var logical = GraphicsPostFrameLimits.CanonicalMip(data.Description, mip); var expected = new List<byte>();
            for (int row = 0; row < logical.Rows; row++) expected.AddRange(source.Pixels.AsSpan(row * source.RowPitch, (int)logical.RowBytes).ToArray());
            Assert.Equal(expected.ToArray(), sampled.CanonicalBytes[mip]);
        }
        byte[] after = fixture.Capture(vertices, texture), repeated = fixture.Capture(vertices, texture);
        Assert.Equal(after, repeated); device.WaitForIdle(); Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }
    [Theory]
    [InlineData(DirectionalShadowQuality.Disabled, DirectionalShadowQuality.Low)]
    [InlineData(DirectionalShadowQuality.Low, DirectionalShadowQuality.Disabled)]
    [InlineData(DirectionalShadowQuality.Low, DirectionalShadowQuality.Medium)]
    public void ArenaCreationRemovalOrReplacementInvalidatesTheCapturedWitness(DirectionalShadowQuality before, DirectionalShadowQuality after)
    {
        using var fixture = new Fixture(false, requestDebugLayer: true); var device = fixture.Device;
        Assert.True(device.Diagnostics.Device.IsSoftwareAdapter); Console.WriteLine($"[post-frame-native] software={device.Diagnostics.Device.IsSoftwareAdapter} debugLayerEnabled={device.Diagnostics.Device.DebugLayerEnabled}"); device.ConfigureDirectionalShadows(new(before));
        using var vertices = device.CreateBuffer(new(36, GraphicsBufferMemory.Upload)); vertices.SetData<byte>(Fixture.Vertices());
        int slot = Capture(fixture, vertices); var receipt = GraphicsPostFrameSnapshots.IssueReceipt(device, slot);
        var submission = device.LastSubmission; device.ConfigureDirectionalShadows(new(after)); Assert.Equal(submission, device.LastSubmission);
        var roster = new GraphicsPostFrameRoster(); roster.AddBuffer("vertices", vertices);
        Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], [])));
        Assert.False(receipt.Consumed);
    }
    [Fact]
    public void RejectedForeignThreadDoesNotInvalidateStampIssueOrConsumeTheOwnersReceipt()
    {
        using var fixture = new Fixture(false, requestDebugLayer: true); var device = fixture.Device; Assert.True(device.Diagnostics.Device.IsSoftwareAdapter); Console.WriteLine($"[post-frame-native] software={device.Diagnostics.Device.IsSoftwareAdapter} debugLayerEnabled={device.Diagnostics.Device.DebugLayerEnabled}");
        using var vertices = device.CreateBuffer(new(36, GraphicsBufferMemory.Upload)); vertices.SetData<byte>(Fixture.Vertices());
        int slot = Capture(fixture, vertices); var roster = new GraphicsPostFrameRoster(); roster.AddBuffer("vertices", vertices);
        Exception? rejected = null;
        var foreign = new Thread(() => { try { device.RenderFrame(GraphicsColor.ForgeLineClear); } catch (Exception error) { rejected = error; } });
        foreign.Start(); Assert.True(foreign.Join(TimeSpan.FromSeconds(5))); Assert.IsType<InvalidOperationException>(rejected);
        var receipt = GraphicsPostFrameSnapshots.IssueReceipt(device, slot); rejected = null;
        foreign = new Thread(() => { try { GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], [])); } catch (Exception error) { rejected = error; } });
        foreign.Start(); Assert.True(foreign.Join(TimeSpan.FromSeconds(5))); Assert.IsType<InvalidOperationException>(rejected); Assert.False(receipt.Consumed);
        Assert.True(GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], [])).CopyReceipt.CompletionProved);
        Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], [])));
    }
    [Fact]
    public void TrackerOffPendingInitializerIsRefusedWithoutALateFlush()
    {
        using var fixture = new Fixture(false, requestDebugLayer: true); var device = fixture.Device; Assert.True(device.Diagnostics.Device.IsSoftwareAdapter); Console.WriteLine($"[post-frame-native] software={device.Diagnostics.Device.IsSoftwareAdapter} debugLayerEnabled={device.Diagnostics.Device.DebugLayerEnabled}");
        Assert.Null(device.BoundInputTracker);
        using var vertices = device.CreateBuffer(new(36, GraphicsBufferMemory.Upload)); vertices.SetData<byte>(Fixture.Vertices());
        int slot = Capture(fixture, vertices); var receipt = GraphicsPostFrameSnapshots.IssueReceipt(device, slot);
        using var unused = device.CreateInitializedStaticBuffer(new(16, StaticGraphicsBufferUsage.Vertex), new byte[16]);
        var roster = new GraphicsPostFrameRoster(); roster.AddBuffer("unused", unused); long submissions = device.UploadDiagnostics.Submissions;
        Assert.Throws<InvalidOperationException>(() => GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], [])));
        Assert.Equal(submissions, device.UploadDiagnostics.Submissions);
    }
    [Theory]
    [InlineData((int)GraphicsPostFrameOperation.BeforeExecute)]
    [InlineData((int)GraphicsPostFrameOperation.AfterExecute)]
    [InlineData((int)GraphicsPostFrameOperation.Signal)]
    [InlineData((int)GraphicsPostFrameOperation.Map)]
    public void NativeFailuresRetainActiveLeaseUntilRealDeviceDrainAndPreservePrimary(int pointValue)
    {
        var point = (GraphicsPostFrameOperation)pointValue;
        var fixture = new Fixture(false, requestDebugLayer: true); var device = fixture.Device;
        Assert.True(device.Diagnostics.Device.IsSoftwareAdapter); Console.WriteLine($"[post-frame-native] software={device.Diagnostics.Device.IsSoftwareAdapter} debugLayerEnabled={device.Diagnostics.Device.DebugLayerEnabled}"); var registry = Registry(device);
        try
        {
            using var vertices = device.CreateBuffer(new(36, GraphicsBufferMemory.Upload)); vertices.SetData<byte>(Fixture.Vertices());
            int slot = Capture(fixture, vertices); var receipt = GraphicsPostFrameSnapshots.IssueReceipt(device, slot);
            var roster = new GraphicsPostFrameRoster(); roster.AddBuffer("vertices", vertices); int baseline = registry.LiveCount;
            var original = new IOException("injected late phase"); device.PostFrameFailureInjection = operation => { if (operation == point) throw original; };
            var observed = Assert.Throws<IOException>(() => GraphicsPostFrameSnapshots.Capture(device, receipt, roster, new([], [], []))); Assert.Same(original, observed);
            var prefix = GraphicsPostFrameSnapshots.Failure(original, receipt); Assert.NotNull(prefix);
            bool uncertain = point is GraphicsPostFrameOperation.AfterExecute or GraphicsPostFrameOperation.Signal;
            Assert.Equal(uncertain ? baseline + 1 : baseline, registry.LiveCount);
            Assert.Equal(point != GraphicsPostFrameOperation.BeforeExecute, prefix.MayHaveExecuted);
            vertices.Dispose(); device.PostFrameFailureInjection = null; device.Dispose();
            Assert.Equal(0, registry.LiveCount); Assert.Equal(0, registry.PendingCount);
        }
        finally { device.PostFrameFailureInjection = null; fixture.Dispose(); }
    }
}
