using System.Reflection;
using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using Vortice.Direct3D12;
using Xunit;
using Fixture = ForgeLine.Graphics.Tests.ResourceScheduling.NativeUploadSchedulingTests.NativeFixture;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class StaticGeometryLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeHeapStatesFirstDrawAndResidentFramesAreQualified(bool debug)
    {
        using var fixture = new Fixture(false, debug);
        var device = fixture.Device;
        Assert.SkipWhen(debug && !device.Diagnostics.Debug.Enabled, "D3D12 debug layer is unavailable.");
        byte[] vertices = Fixture.Vertices();
        byte[] indices = MemoryMarshal.AsBytes<ushort>(new ushort[] { 0, 1, 2 }.AsSpan()).ToArray();
        using var vertex = device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), vertices);
        using var index = device.CreateInitializedStaticBuffer(new(6, StaticGraphicsBufferUsage.Index), indices);
        foreach (var buffer in new[] { vertex, index })
        {
            Assert.Equal(GraphicsBufferMemory.GpuLocal, buffer.Description.Memory);
            ((D3D12GraphicsBuffer)buffer).Resource.GetHeapProperties(out var heap, out _).CheckError();
            Assert.Equal(HeapType.Default, heap.Type);
        }
        Array.Clear(vertices); Array.Clear(indices);
        Assert.Equal(2, device.UploadDiagnostics.Transfers);
        Assert.Equal(0, device.UploadDiagnostics.Submissions);
        byte[] first = fixture.Capture(vertex, indexBuffer: index);
        var surface = device.Diagnostics.Surface;
        // UNORM conversion of 0.5 may round to either adjacent byte on native adapters.
        Assert.InRange(first[(surface.Height / 2 * surface.Width + surface.Width / 2) * 4 + 1], (byte)127, (byte)128);
        Assert.Equal(1, device.UploadDiagnostics.Submissions);
        var settled = device.UploadDiagnostics;
        for (int frame = 0; frame < 16; frame++) Assert.Equal(first, fixture.Capture(vertex, indexBuffer: index));
        Assert.Equal(settled.Transfers, device.UploadDiagnostics.Transfers);
        Assert.Equal(settled.Submissions, device.UploadDiagnostics.Submissions);
        Assert.Equal(settled.Allocations, device.UploadDiagnostics.Allocations);
        Assert.Equal(0, device.Diagnostics.Debug.WarningCount);
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }

    [Fact]
    public void EarlyDisposalRetainsUnsubmittedGeometryAndHundredCyclesReachPlateau()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        int baseline = device.Diagnostics.Health.LiveResourceCount;
        var early = device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        var lifetime = ((D3D12GraphicsBuffer)early).Lifetime;
        early.Dispose(); early.Dispose();
        Assert.True(lifetime.Recording);
        Assert.True(device.Diagnostics.Health.LiveResourceCount > baseline);
        Assert.Equal(1, device.Diagnostics.Health.PendingRetirementCount);
        device.WaitForIdle();
        Assert.False(lifetime.Recording);
        Assert.Equal(baseline, device.Diagnostics.Health.LiveResourceCount);
        for (int cycle = 0; cycle < 100; cycle++)
        {
            using var buffer = device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        }
        device.WaitForIdle();
        var plateau = device.UploadDiagnostics;
        Assert.Equal(baseline, device.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
        for (int cycle = 0; cycle < 100; cycle++)
        {
            using var buffer = device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
            if (cycle % 16 == 15) device.WaitForIdle();
        }
        device.WaitForIdle();
        Assert.Equal(plateau.Allocations, device.UploadDiagnostics.Allocations);
        Assert.Equal(plateau.ResidentBytes, device.UploadDiagnostics.ResidentBytes);
        Assert.Equal(baseline, device.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
    }

    [Fact]
    public void ThreeInFlightSlotsApplyBackpressureAndReuseOnlyAfterCopyCompletion()
    {
        using var fixture = new Fixture(false);
        var device = fixture.Device;
        var native = Field<ID3D12Device>(device, "_device");
        var queue = Field<ID3D12CommandQueue>(device, "_commandQueue");
        using var gate = native.CreateFence(0);
        using var release = new ManualResetEventSlim();
        // Hold real GPU work, rather than assuming that this adapter copies slowly.
        queue.Wait(gate, 1).CheckError();
        Exception? signalingFailure = null;
        var signal = new Thread(() => { try { release.Wait(); Thread.Sleep(250); gate.Signal(1); } catch (Exception error) { signalingFailure = error; } }) { IsBackground = true };
        signal.Start();
        var buffers = new List<IGraphicsBuffer>();
        try
        {
            byte[] bytes = new byte[checked((int)UploadArenaLimits.NormalBytes)];
            for (int i = 0; i < 3; i++)
                buffers.Add(device.CreateInitializedStaticBuffer(new((ulong)bytes.Length, StaticGraphicsBufferUsage.Vertex), bytes));
            Assert.Equal(3, device.UploadDiagnostics.Slots);
            release.Set();
            buffers.Add(device.CreateInitializedStaticBuffer(new((ulong)bytes.Length, StaticGraphicsBufferUsage.Vertex), bytes));
            Assert.True(device.UploadDiagnostics.BackpressureWaits >= 1);
            Assert.Equal(3, device.UploadDiagnostics.Allocations);
            Assert.Equal(3 * UploadArenaLimits.NormalBytes, device.UploadDiagnostics.ResidentBytes);
            device.WaitForIdle();
            Assert.Equal(4, device.UploadDiagnostics.Transfers);
            Assert.Equal(4, device.UploadDiagnostics.Submissions);
            Assert.True(device.UploadDiagnostics.PeakResidentBytes <= UploadArenaLimits.MaximumResidentBytes);
        }
        finally
        {
            release.Set(); gate.Signal(1); Assert.True(signal.Join(5000)); Assert.Null(signalingFailure);
            foreach (var buffer in buffers) buffer.Dispose();
        }
        device.WaitForIdle();
        Assert.Equal(0, device.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(0, device.Diagnostics.Debug.ErrorCount);
    }

    [Theory]
    [InlineData((int)SchedulingOperation.AllocateUpload)]
    [InlineData((int)SchedulingOperation.MapUpload)]
    public void StaticAllocationAndMapFailurePreserveExistingGeometry(int point)
    {
        using var fixture = new Fixture(false);
        using var good = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        fixture.Device.SchedulingFailureInjection = op =>
        { if (op == (SchedulingOperation)point) throw new InvalidOperationException("Injected static staging failure."); };
        int bytes = point == (int)SchedulingOperation.AllocateUpload ? 8 * 1024 * 1024 : 36;
        Assert.Throws<InvalidOperationException>(() => fixture.Device.CreateInitializedStaticBuffer(new((ulong)bytes, StaticGraphicsBufferUsage.Vertex), new byte[bytes]));
        fixture.Device.SchedulingFailureInjection = null;
        Assert.NotEmpty(fixture.Capture(good));
        fixture.Device.WaitForIdle();
        Assert.Equal(0, fixture.Device.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(0, fixture.Device.Diagnostics.Health.SubmissionFaultCount);
    }

    [Theory]
    [InlineData((int)SchedulingOperation.CloseUpload)]
    [InlineData((int)SchedulingOperation.ExecuteUpload)]
    [InlineData((int)SchedulingOperation.SignalUpload)]
    public void StaticSubmissionFailureKeepsStickyOwnershipThroughShutdown(int point)
    {
        using var fixture = new Fixture(false);
        var buffer = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        fixture.Device.SchedulingFailureInjection = op =>
        { if (op == (SchedulingOperation)point) throw new InvalidOperationException("Injected static submission failure."); };
        Assert.Throws<InvalidOperationException>(() => fixture.Device.WaitForIdle());
        buffer.Dispose();
        Assert.True(fixture.Device.Diagnostics.Health.LiveResourceCount > 0);
        Assert.Throws<GraphicsDeviceException>(() => fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices()));
        fixture.Device.SchedulingFailureInjection = null;
        fixture.Device.Dispose();
        Assert.Equal(0, fixture.Device.UploadDiagnostics.Slots);
        Assert.Equal(0UL, fixture.Device.UploadDiagnostics.ResidentBytes);
        Assert.Equal(0, Field<GraphicsResourceRetirement>(fixture.Device, "_resources").LiveCount);
    }

    [Fact]
    public void LastDrawAndPendingCopySurviveWrapperDisposalAndDeviceShutdown()
    {
        using var fixture = new Fixture(false);
        var buffer = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        fixture.Device.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            fixture.BindSolidPipeline(context); context.SetVertexBuffer(buffer, 12); context.Draw(3);
            buffer.Dispose();
        });
        fixture.Device.WaitForIdle();
        Assert.Equal(0, fixture.Device.Diagnostics.Health.PendingRetirementCount);
        var pending = fixture.Device.CreateInitializedStaticBuffer(new(36, StaticGraphicsBufferUsage.Vertex), Fixture.Vertices());
        fixture.Device.Dispose();
        pending.Dispose();
        Assert.Equal(0, fixture.Device.UploadDiagnostics.Slots);
        Assert.Equal(0, Field<GraphicsResourceRetirement>(fixture.Device, "_resources").LiveCount);
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
