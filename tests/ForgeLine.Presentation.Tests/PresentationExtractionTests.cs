using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PresentationExtractionTests
{
    [Fact]
    public void ExtractedSnapshotOwnsCopiedTransformState()
    {
        var buffer = new PresentationSnapshotBuffer();
        var extractor = new PresentationExtractor(buffer);
        var simulation = new SimulationCoordinator();
        simulation.RegisterTickObserver(extractor);

        var entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                new Vector3(1.0f, 2.0f, 3.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(entity, new VisualIdentity(1));

        simulation.AdvanceOneTick();

        Assert.True(buffer.TryReadLatest(out PresentationSnapshot snapshot));
        Assert.Equal(1, snapshot.InstanceCount);
        Assert.Equal(
            new Vector3(1.0f, 2.0f, 3.0f),
            snapshot.Instances[0].Transform.Position);

        simulation.Entities.SetComponent(
            entity,
            new WorldTransform(
                new Vector3(99.0f, 2.0f, 3.0f),
                Quaternion.Identity,
                Vector3.One));

        Assert.Equal(
            new Vector3(1.0f, 2.0f, 3.0f),
            snapshot.Instances[0].Transform.Position);
    }

    [Fact]
    public void RenderWorldInterpolatesMatchingEntitiesAcrossTicks()
    {
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        var entity = new ForgeLine.Core.EntityId(5, 1);

        buffer.Publish(
            Snapshot(
                1,
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        Vector3.Zero,
                        Quaternion.Identity,
                        Vector3.One),
                    new RenderMeshHandle(1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityFlags.World)));
        Assert.True(world.Update(buffer));

        buffer.Publish(
            Snapshot(
                2,
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        new Vector3(10.0f, 0.0f, 0.0f),
                        Quaternion.Identity,
                        Vector3.One),
                    new RenderMeshHandle(1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityFlags.World)));
        Assert.True(world.Update(buffer));

        RenderInstance interpolated =
            world.GetInterpolatedInstance(0, 0.25f);

        Assert.InRange(
            interpolated.Transform.Position.X,
            2.4999f,
            2.5001f);
    }

    [Fact]
    public void NewEntityWithoutPreviousStateUsesCurrentTransform()
    {
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();

        buffer.Publish(Snapshot(1));
        Assert.True(world.Update(buffer));

        var entity = new ForgeLine.Core.EntityId(10, 1);
        buffer.Publish(
            Snapshot(
                2,
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        new Vector3(7.0f, 0.0f, 0.0f),
                        Quaternion.Identity,
                        Vector3.One),
                    new RenderMeshHandle(1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityFlags.World)));
        Assert.True(world.Update(buffer));

        Assert.Equal(
            7.0f,
            world.GetInterpolatedInstance(0, 0.5f)
                .Transform.Position.X);
    }

    [Theory]
    [InlineData(-1.0, 0.0f)]
    [InlineData(0.025, 0.5f)]
    [InlineData(0.100, 1.0f)]
    public void RenderAlphaIsClampedToTickInterval(
        double accumulatedSeconds,
        float expected)
    {
        float alpha = RenderInterpolation.CalculateAlpha(
            TimeSpan.FromSeconds(accumulatedSeconds),
            TimeSpan.FromMilliseconds(50));

        Assert.InRange(alpha, expected - 0.0001f, expected + 0.0001f);
    }

    private static PresentationSnapshot Snapshot(
        ulong tick,
        params RenderInstance[] instances) =>
        new(
            new SimulationTick(tick),
            TimeSpan.FromMilliseconds(50),
            instances.Length,
            instances);
}
