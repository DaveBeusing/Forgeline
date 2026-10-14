using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RenderSnapshotIndexTests
{
    [Fact]
    public void SweptClustersNeverRejectVisibleInterpolatedActorsDuringPanAndRotation()
    {
        var camera = new RtsCamera();
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        var first = Enumerable.Range(1, 96).Select(i => Instance((uint)i, new Vector3(i * 5 - 240, 0, 0), new Vector3(2, 8, 20))).ToArray();
        Publish(first, 1);
        var second = first.Select(i => i with { Transform = i.Transform with { Position = i.Transform.Position + new Vector3(160, 0, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) } }).ToArray();
        Publish(second, 2);
        foreach (float alpha in new[] { 0, .25f, .5f, .75f, 1 })
        {
            var frustum = ViewFrustum.FromViewProjection(camera.GetMatrices(1600, 900).ViewProjection);
            for (int i = 0; i < world.InstanceCount; i++)
                if (frustum.Intersects(PresentationBounds.ResolveAxisAlignedBounds(world.GetInterpolatedInstance(i, alpha))))
                    Assert.True(world.ClusterVisible(i / 32 * 32, frustum));
        }
        void Publish(RenderInstance[] instances, uint tick)
        {
            buffer.Publish(new(new SimulationTick(tick), TimeSpan.FromMilliseconds(50), instances.Length, instances));
            Assert.True(world.Update(buffer));
        }
    }

    [Fact]
    public void PreviousMatchesRespectGenerationRemovalAndInsertion()
    {
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 2,
            new[] { Instance(1, Vector3.Zero, Vector3.One), Instance(3, Vector3.Zero, Vector3.One) }));
        world.Update(buffer);
        buffer.Publish(new(new SimulationTick(2), TimeSpan.FromMilliseconds(50), 3,
            new[] { Instance(1, new Vector3(10, 0, 0), Vector3.One), Instance(2, new Vector3(20, 0, 0), Vector3.One),
                Instance(3, new Vector3(30, 0, 0), Vector3.One) with { Entity = new(3, 2) } }));
        world.Update(buffer);
        Assert.Equal(5, world.GetInterpolatedInstance(0, .5f).Transform.Position.X);
        Assert.Equal(20, world.GetInterpolatedInstance(1, .5f).Transform.Position.X);
        Assert.Equal(30, world.GetInterpolatedInstance(2, .5f).Transform.Position.X);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) _ = world.GetInterpolatedInstance(i % 3, .5f);
        Assert.Equal(bytes, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void OffscreenClusterRejectsAndInvalidBoundsFailOpen()
    {
        var camera = new RtsCamera();
        var frustum = ViewFrustum.FromViewProjection(camera.GetMatrices(1600, 900).ViewProjection);
        var index = new RenderSnapshotIndex();
        var instances = Enumerable.Range(1, 32).Select(i => Instance((uint)i, new Vector3(1_000_000), Vector3.One)).ToArray();
        index.Rebuild(new(new SimulationTick(1), TimeSpan.Zero, 32, instances), null);
        Assert.False(index.IsVisible(0, frustum));
        instances[12] = instances[12] with { Transform = instances[12].Transform with { Position = new(float.NaN) } };
        index.Rebuild(new(new SimulationTick(2), TimeSpan.Zero, 32, instances), null);
        Assert.True(index.IsVisible(0, frustum));
    }

    private static RenderInstance Instance(uint id, Vector3 position, Vector3 scale) =>
        new(new EntityId(id, 1), new(position, Quaternion.Identity, scale), new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
}
