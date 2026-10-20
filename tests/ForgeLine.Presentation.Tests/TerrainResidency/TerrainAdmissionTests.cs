using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation.TerrainResidency;
using Xunit;
using static ForgeLine.Presentation.Tests.TerrainResidency.TerrainPreparationTests;

namespace ForgeLine.Presentation.Tests.TerrainResidency;

public sealed class TerrainAdmissionTests
{
    [Fact]
    public void BoundedStepsNeverPublishPartialMainOrShadowCoverage()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(), residency: new(true, 25),
            preparation: new() { PrefetchEnabled = false });
        var context = new TerrainTestContext(); var camera = new RtsCamera();
        var light = new GraphicsDirectionalShadowFrame(Matrix4x4.CreateLookAt(new(0, 1000, 0), Vector3.Zero, Vector3.UnitZ) *
            Matrix4x4.CreateOrthographic(4096, 4096, 1, 5000), Vector3.Zero, 2048, 5000);
        int steps = 0;
        while (true)
        {
            long before = renderer.WorkMetrics.GeometryUploadBytes;
            bool ready = renderer.PrepareStep(camera, context.Width, context.Height, light, 1); steps++;
            Assert.InRange(renderer.WorkMetrics.GeometryUploadBytes - before, 0, 33 * 33 * 48);
            if (ready) break;
            Assert.False(renderer.IsPrepared);
            Assert.Throws<InvalidOperationException>(() => renderer.Render(context, camera));
            Assert.Throws<InvalidOperationException>(() => renderer.RenderShadow(context, light, 1000));
            Assert.True(steps < 30);
        }
        Assert.Equal(25, steps); Assert.True(renderer.IsPrepared);
        Assert.Equal(25, renderer.WorkMetrics.RequiredChunks);
        Assert.Equal(25, renderer.RenderShadow(context, light, 1000));
        renderer.Render(context, camera); Assert.True(renderer.LastDiagnostics.VisibleChunks > 0);
    }

    [Fact]
    public void AChangedPlanAbandonsIncompleteCoverageWithoutPublishingHoles()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(), residency: new(true, 25));
        var camera = new RtsCamera(new() { InitialDistance = 900 }); var context = new TerrainTestContext();
        Assert.False(renderer.PrepareStep(camera, context.Width, context.Height, maximumNewChunks: 1));
        camera.ApplyState(camera.CaptureState() with { Target = new(128, 0, 128), Distance = 12 });
        renderer.Prepare(camera, context.Width, context.Height); renderer.Render(context, camera);
        Assert.True(renderer.IsPrepared); Assert.Equal(1, renderer.LastDiagnostics.VisibleChunks);
    }

    [Fact]
    public void EqualAspectResizeStillRequiresTheNewViewportPreparation()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(1), residency: new(true, 9));
        var camera = new RtsCamera(); var context = new TerrainTestContext();
        renderer.Prepare(camera, context.Width, context.Height);
        context.Width *= 2; context.Height *= 2;
        Assert.Throws<InvalidOperationException>(() => renderer.Render(context, camera));
        renderer.Prepare(camera, context.Width, context.Height); renderer.Render(context, camera);
    }

    [Fact]
    public void PrefetchIsBoundedCpuWorkAndDoesNotPublishOptionalGpuChunks()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(3), residency: new(true, 4));
        var camera = new RtsCamera(new() { InitialTarget = new(128, 0, 128), InitialDistance = 12 });
        renderer.Prepare(camera, 1600, 900); int gpuBuffers = device.Inner.Buffers.Count;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            renderer.Prepare(camera, 1600, 900);
            Assert.InRange(renderer.WorkMetrics.PendingCpuChunks, 0, 2);
            return renderer.WorkMetrics.PendingCpuChunks == 0;
        }, TimeSpan.FromSeconds(10)));
        Assert.True(renderer.WorkMetrics.PreparedChunks > 1);
        Assert.Equal(gpuBuffers, device.Inner.Buffers.Count); Assert.Equal(1, renderer.WorkMetrics.ResidentChunks);
    }

    [Fact]
    public void EvictionVisitsOneCandidateAndAllBudgetsStayBoundedAlongAPath()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(6), residency: new(true, 1),
            preparation: new() { PrefetchEnabled = false, MaximumPreparedChunks = 4 });
        var camera = new RtsCamera(new() { InitialDistance = 12 }); var context = new TerrainTestContext();
        for (int x = -5; x <= 5; x++)
        {
            camera.ApplyState(camera.CaptureState() with { Target = new(x * 256 + 128, 0, 128) });
            renderer.Prepare(camera, context.Width, context.Height); renderer.Render(context, camera);
            Assert.Equal(1, renderer.WorkMetrics.ResidentChunks);
            Assert.InRange(renderer.WorkMetrics.PreparedChunks, 0, 4);
            Assert.InRange(renderer.WorkMetrics.PreparedBytes, 0, 32 * 1024 * 1024);
            Assert.Equal(5720, renderer.WorkMetrics.ResidentControlBytes);
        }
        Assert.Equal(renderer.LastDiagnostics.ChunkEvictions, renderer.WorkMetrics.EvictionCandidateVisits);
        Assert.Equal(10, renderer.WorkMetrics.EvictionCandidateVisits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentByteBudgetsRejectBeforeGpuMutation(bool geometry)
    {
        using var device = new TerrainTestDevice();
        long indices = new TerrainIndexPatterns(33, 8 * 1024 * 1024).Bytes;
        var policy = geometry ? new TerrainPreparationSettings { MaximumGeometryBytes = indices + 33 * 33 * 48 - 1 } :
            new TerrainPreparationSettings { MaximumControlBytes = 5719 };
        using var renderer = new TerrainRenderer(device, FlatWorld(), residency: new(true, 25), preparation: policy);
        int buffers = device.Inner.Buffers.Count, textures = device.Textures.Count;
        Assert.Throws<InvalidOperationException>(() => renderer.Prepare(new(new() { InitialTarget = new(128, 0, 128), InitialDistance = 12 }), 1600, 900));
        Assert.Equal(buffers, device.Inner.Buffers.Count); Assert.Equal(textures, device.Textures.Count);
    }
}

[Collection("Terrain preparation allocations")]
public sealed class TerrainWarmPreparationTests
{
    [Fact]
    public void StablePreparedCameraAllocatesNoBytes()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(1), residency: new(true, 9), preparation: new() { PrefetchEnabled = false });
        var camera = new RtsCamera();
        for (int i = 0; i < 64; i++) renderer.Prepare(camera, 1600, 900);
        long builds = renderer.WorkMetrics.MeshBuilds;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) renderer.Prepare(camera, 1600, 900);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.Equal(builds, renderer.WorkMetrics.MeshBuilds);
        before = GC.GetAllocatedBytesForCurrentThread(); byte[] control = new byte[4096];
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before >= 4096); GC.KeepAlive(control);
    }
}
[CollectionDefinition("Terrain preparation allocations", DisableParallelization = true)]
public sealed class TerrainPreparationAllocationGroup;
