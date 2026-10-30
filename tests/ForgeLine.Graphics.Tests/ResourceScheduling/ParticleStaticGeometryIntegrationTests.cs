using System.Numerics;
using System.Reflection;
using Vortice.Direct3D12;
using Xunit;
using Fixture = ForgeLine.Graphics.Tests.ResourceScheduling.NativeUploadSchedulingTests.NativeFixture;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class ParticleStaticGeometryIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParticleIndicesUseDefaultMemoryAndDynamicFramesRemainUpload(bool debug)
    {
        using var fixture = new Fixture(false, debug);
        var device = fixture.Device;
        Assert.SkipWhen(debug && !device.Diagnostics.Debug.Enabled, "D3D12 debug layer is unavailable.");
        device.ConfigureParticleDepth(true);
        int baselineLive = device.Diagnostics.Health.LiveResourceCount;
        var before = device.UploadDiagnostics;
        using var renderer = new GpuParticleRenderer(device, 512);
        var indices = Field<IGraphicsBuffer>(renderer, "_indices");
        Assert.Equal(12UL, indices.Description.SizeInBytes);
        Assert.Equal(GraphicsBufferMemory.GpuLocal, indices.Description.Memory);
        ((D3D12GraphicsBuffer)indices).Resource.GetHeapProperties(out var heap, out _).CheckError();
        Assert.Equal(HeapType.Default, heap.Type);
        foreach (var frame in Field<IGraphicsBuffer[]>(renderer, "_frames"))
        {
            Assert.Equal(GraphicsBufferMemory.Upload, frame.Description.Memory);
            Assert.Equal(512UL * 64, frame.Description.SizeInBytes);
        }
        Assert.Equal(before.Transfers + 1, device.UploadDiagnostics.Transfers);
        Assert.Equal(before.UploadedBytes + 12, device.UploadDiagnostics.UploadedBytes);
        byte[] Capture() => device.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            GraphicsParticle[] particles = [new(new(0, 0, .25f, .25f), new(0, 0, 0, 1), new(0, 1, 0, 1), new(.5f, 0, 2, 0))];
            renderer.Render(context, particles, Matrix4x4.Identity, Vector3.UnitX, Vector3.UnitY, 1, -1, 1, false, false);
        });
        byte[] first = Capture();
        var surface = device.Diagnostics.Surface;
        Assert.True(first[(surface.Height / 2 * surface.Width + surface.Width / 2) * 4 + 1] > 128);
        var resident = device.UploadDiagnostics;
        for (int frame = 0; frame < 8; frame++) Assert.Equal(first, Capture());
        Assert.Equal(resident.Transfers, device.UploadDiagnostics.Transfers);
        Assert.Equal(resident.Submissions, device.UploadDiagnostics.Submissions);
        Assert.Equal(resident.Allocations, device.UploadDiagnostics.Allocations);
        renderer.Dispose(); device.WaitForIdle();
        Assert.Equal(baselineLive, device.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(0, device.Diagnostics.Debug.WarningCount);
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }

    [Fact]
    public void ParticleInitializationFailureRetiresPartialOwnershipAndKeepsDeviceUsable()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        device.ConfigureParticleDepth(true);
        int baselineLive = device.Diagnostics.Health.LiveResourceCount;
        device.SchedulingFailureInjection = operation =>
        { if (operation == SchedulingOperation.MapUpload) throw new InvalidOperationException("Injected particle index staging failure."); };
        Assert.Throws<InvalidOperationException>(() => new GpuParticleRenderer(device, 512));
        device.SchedulingFailureInjection = null;
        device.WaitForIdle();
        Assert.Equal(baselineLive, device.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(0, device.Diagnostics.Health.SubmissionFaultCount);
        using var replacement = new GpuParticleRenderer(device, 512);
        device.WaitForIdle();
    }

    [Fact]
    public void ParticleDisposalBeforeFirstFrameKeepsIndicesUntilCopyRetires()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        device.ConfigureParticleDepth(true);
        int baselineLive = device.Diagnostics.Health.LiveResourceCount;
        var renderer = new GpuParticleRenderer(device, 512);
        var indices = (D3D12GraphicsBuffer)Field<IGraphicsBuffer>(renderer, "_indices");
        renderer.Dispose();
        Assert.True(indices.Lifetime.Recording);
        Assert.True(device.Diagnostics.Health.PendingRetirementCount > 0);
        device.WaitForIdle();
        Assert.False(indices.Lifetime.Recording);
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(baselineLive, device.Diagnostics.Health.LiveResourceCount);
    }


    [Fact]
    public void ParticleGeometrySurvivesNativeResizeMinimizeAndRestore()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        var window = Field<ForgeLine.Platform.IWindow>(fixture, "_window");
        var platform = Field<ForgeLine.Platform.Windows.WindowsPlatform>(fixture, "_platform");
        using var renderer = new GpuParticleRenderer(device, 512);
        void Draw(IGraphicsCommandContext context)
        {
            GraphicsParticle[] particles = [new(new(0, 0, .25f, .25f), new(0, 0, 0, 1), new(0, 1, 0, 1), new(.5f, 0, 2, 0))];
            renderer.Render(context, particles, Matrix4x4.Identity, Vector3.UnitX, Vector3.UnitY, 1, -1, 1, false, false);
        }
        device.Resize(320, 240);
        device.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
        byte[] reference = device.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
        var resident = device.UploadDiagnostics;
        ShowWindow(window.NativeHandle.Value, 6);
        Assert.True(platform.PumpEvents());
        Assert.True(window.IsMinimized);
        device.Resize(0, 0);
        bool called = false;
        device.RenderFrame(GraphicsColor.ForgeLineClear, _ => called = true);
        Assert.False(called);
        Assert.True(device.Diagnostics.Surface.IsSuspended);
        ShowWindow(window.NativeHandle.Value, 9);
        Assert.True(platform.PumpEvents());
        Assert.False(window.IsMinimized);
        device.Resize(640, 360);
        device.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
        byte[] larger = device.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
        Assert.Equal(640 * 360 * 4, larger.Length);
        Assert.True(larger[(180 * 640 + 320) * 4 + 1] > 128);
        device.Resize(320, 240);
        device.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
        Assert.Equal(reference, device.CaptureFrame(GraphicsColor.ForgeLineClear, Draw));
        Assert.Equal(resident.Transfers, device.UploadDiagnostics.Transfers);
        Assert.Equal(resident.Allocations, device.UploadDiagnostics.Allocations);
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
