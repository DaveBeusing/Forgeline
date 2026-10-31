using System.Numerics;
using ForgeLine.Presentation.TerrainResidency;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests.TerrainResidency;

public sealed class TerrainLifetimeTests
{
    [Fact]
    public void RetainedLruEvictsCorrectSortedSlotsAfterOutOfOrderTouches()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, TerrainPreparationTests.FlatWorld(4), residency: new(true, 4),
            preparation: new() { PrefetchEnabled = false });
        var camera = new RtsCamera(new() { InitialDistance = 12 }); var context = new TerrainTestContext();
        foreach (float x in new[] { 128f, 384, 640, 896, 384, 1152, 128, 640 })
        {
            camera.ApplyState(camera.CaptureState() with { Target = new(x, 0, 128) });
            renderer.Prepare(camera, 1600, 900); renderer.Render(context, camera);
            Assert.Equal(1, renderer.LastDiagnostics.VisibleChunks); Assert.InRange(renderer.WorkMetrics.ResidentChunks, 1, 4);
        }
        Assert.Equal(3, renderer.LastDiagnostics.ChunkEvictions); Assert.Equal(3, renderer.WorkMetrics.EvictionCandidateVisits);
        Assert.Equal(5, renderer.WorkMetrics.MeshBuilds); Assert.Equal(7, renderer.LastDiagnostics.ChunkUploads);
    }

    [Fact]
    public async Task DisposalObservesCancelledCpuWorkAndCannotPublishIntoTheNextMap()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new TerrainPreparationWorker();
        var oldSource = new TerrainPreparedSource(TerrainPreparationTests.FlatWorld(0), new(), new([]), new());
        try
        {
            worker.Start([0], (index, token) =>
            {
                entered.SetResult(true); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested();
                return oldSource.Build(index, token);
            });
            Assert.True(await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            worker.Dispose();
        }
        Assert.False(worker.Busy); Assert.Equal(0, worker.PendingCount); Assert.Null(worker.Harvest());
        var changed = new TerrainWorld(new(), [new(new(0, 0), new TerrainHeightfield(65, 256, Enumerable.Repeat(37f, 65 * 65).ToArray()))]);
        var fresh = new TerrainPreparedSource(changed, new(), new([]), new()).Build(0, CancellationToken.None);
        Assert.All(fresh.Vertices, v => Assert.Equal(37, v.Position.Y));
    }

    [Fact]
    public void AbortedFramePreservesPreparedCoverageAndNextFrameCanRecord()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, TerrainPreparationTests.FlatWorld(), residency: new(true, 25));
        var camera = new RtsCamera(); renderer.Prepare(camera, 1600, 900);
        long uploads = renderer.WorkMetrics.GeometryUploadBytes;
        Assert.Throws<InvalidOperationException>(() => device.RenderFrame(default, context =>
        {
            renderer.Render(context, camera); throw new InvalidOperationException("Abort after terrain commands.");
        }));
        Assert.False(device.IsRecording); Assert.True(renderer.IsPrepared);
        renderer.Prepare(camera, 1600, 900); device.RenderFrame(default, context => renderer.Render(context, camera));
        Assert.Equal(uploads, renderer.WorkMetrics.GeometryUploadBytes);
    }

    [Fact]
    public void WholeMapTraversalPlateausAtSeparateCpuAndGpuCaps()
    {
        using var device = new TerrainTestDevice();
        var policy = new TerrainPreparationSettings { PrefetchEnabled = false, MaximumPreparedChunks = 4 };
        using var renderer = new TerrainRenderer(device, TerrainPreparationTests.FlatWorld(), residency: new(true, 1), preparation: policy);
        var camera = new RtsCamera(new() { InitialDistance = 12 });
        var points = Enumerable.Range(-2, 5).SelectMany(z => Enumerable.Range(-2, 5).Select(x => new Vector3(x * 256 + 128, 0, z * 256 + 128))).ToArray();
        long geometry = 0, control = 0, cache = 0;
        for (int lap = 0; lap < 3; lap++)
        {
            foreach (var point in points)
            {
                camera.ApplyState(camera.CaptureState() with { Target = point }); renderer.Prepare(camera, 1600, 900);
                Assert.True(renderer.IsPrepared); Assert.Equal(1, renderer.WorkMetrics.ResidentChunks);
                Assert.InRange(renderer.WorkMetrics.PreparedChunks, 1, 4);
                Assert.InRange(renderer.WorkMetrics.PreparedBytes, 1, policy.MaximumPreparedBytes);
            }
            if (lap == 0)
            { geometry = renderer.WorkMetrics.ResidentGeometryBytes; control = renderer.WorkMetrics.ResidentControlBytes; cache = renderer.WorkMetrics.PreparedBytes; }
            else
            {
                Assert.Equal(geometry, renderer.WorkMetrics.ResidentGeometryBytes);
                Assert.Equal(control, renderer.WorkMetrics.ResidentControlBytes); Assert.Equal(cache, renderer.WorkMetrics.PreparedBytes);
            }
        }
        Assert.Equal(75, renderer.WorkMetrics.MeshBuilds); Assert.Equal(74, renderer.WorkMetrics.EvictionCandidateVisits);
    }
}
