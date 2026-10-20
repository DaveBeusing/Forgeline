using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;

namespace ForgeLine.Presentation.Tests.RenderSubmission;

public sealed class SubmissionReferenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void DistributedRootsKeepReferenceUploadOrderAcrossCameras(int count)
    {
        using var graphics = new FakeGraphicsDevice { CaptureUploads = true };
        using var referenceGraphics = new FakeGraphicsDevice { CaptureUploads = true };
        using var renderer = new SimpleInstanceRenderer(graphics);
        using var reference = new SimpleInstanceRenderer(referenceGraphics, clusterCulling: false);
        var camera = new RtsCamera();
        var random = new Random(7241);
        var instances = Enumerable.Range(0, count).Select(i => Root(i,
            camera.Target + new Vector3(random.Next(-600, 600), 0, random.Next(-600, 600)))).ToArray();
        var world = CreateWorld(instances);
        foreach (float distance in new[] { 120f, 420f, 900f })
        {
            camera.ApplyState(camera.CaptureState() with { Distance = distance });
            renderer.Render(new FakeGraphicsCommandContext(), camera, world, .5f);
            reference.Render(new FakeGraphicsCommandContext(), camera, world, .5f);
            Assert.Equal(reference.LastDiagnostics.VisibleInstances, renderer.LastDiagnostics.VisibleInstances);
            Assert.Equal(reference.SubmissionMetrics.SubmittedInstances, renderer.SubmissionMetrics.SubmittedInstances);
            if (renderer.SubmissionMetrics.SubmittedInstances > 0)
                Assert.Equal(referenceGraphics.Buffers[^1].LastUpload, graphics.Buffers[^1].LastUpload);
        }
    }

    internal static RenderInstance Root(int index, Vector3 position) => new(new EntityId((uint)index + 1, 1),
        new(position, Quaternion.Identity, Vector3.One), new(1), RenderMaterialHandle.Default,
        RenderVisibilityMask.World, (uint)index + 1);

    internal static RenderWorld CreateWorld(RenderInstance[] instances)
    {
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), instances.Length, instances));
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        return world;
    }
}
