using ForgeLine.Game;
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

    [Fact]
    public void MovingWindBoundsMatchUnculledUploadsAcrossAlphaAndTime()
    {
        using var graphics = new FakeGraphicsDevice { CaptureUploads = true };
        using var referenceGraphics = new FakeGraphicsDevice { CaptureUploads = true };
        using var renderer = new SimpleInstanceRenderer(graphics);
        using var reference = new SimpleInstanceRenderer(referenceGraphics, clusterCulling: false);
        var camera = new RtsCamera(); var random = new Random(8321);
        var old = Enumerable.Range(0, 1000).Select(i => Root(i, new(random.Next(-500, 500), 0, random.Next(-500, 500))) with {
            WorldFeature = i % 5 == 0 ? new(WorldVisualId.VegetationConifer, ForgeLine.Game.WorldPresentationKind.Vegetation, default, false) : default
        }).ToArray();
        var current = old.Select((root, i) => root with { Transform = root.Transform with {
            Position = root.Transform.Position + new Vector3(i % 2 == 0 ? -200 : 200, 0, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * .1f), Scale = new(2 + i % 8, 5, 2) } }).ToArray();
        var buffer = new PresentationSnapshotBuffer(); var world = new RenderWorld();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), old.Length, old)); world.Update(buffer);
        buffer.Publish(new(new SimulationTick(2), TimeSpan.FromMilliseconds(50), current.Length, current)); world.Update(buffer);
        foreach (float distance in new[] { 120f, 420f, 900f })
        foreach (float alpha in new[] { 0f, .125f, .5f, .875f, 1f })
        {
            camera.ApplyState(camera.CaptureState() with { Distance = distance });
            renderer.Render(new FakeGraphicsCommandContext(), camera, world, alpha, alpha + .25);
            reference.Render(new FakeGraphicsCommandContext(), camera, world, alpha, alpha + .25);
            Assert.Equal(reference.LastDiagnostics.VisibleInstances, renderer.LastDiagnostics.VisibleInstances);
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
