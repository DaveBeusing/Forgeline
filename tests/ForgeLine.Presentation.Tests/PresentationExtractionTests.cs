using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PresentationExtractionTests
{
    [Fact]
    public async Task OwnedExtractionRemainsImmutableDuringConcurrentPublication()
    {
        var buffer = new PresentationSnapshotBuffer();
        var simulation = new SimulationCoordinator();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer));
        var entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.One, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, new VisualIdentity(1));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var retained));
        Task producer = Task.Run(() =>
        {
            for (uint tick = 2; tick <= 200; tick++)
            {
                simulation.Entities.SetComponent(entity, new WorldTransform(new Vector3(tick, 1, 1), Quaternion.Identity, Vector3.One));
                if (tick % 10 == 0)
                {
                    var added = simulation.Entities.CreateEntity();
                    simulation.Entities.AddComponent(added, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
                    simulation.Entities.AddComponent(added, new VisualIdentity(1));
                }
                simulation.AdvanceOneTick();
            }
        }, TestContext.Current.CancellationToken);
        Task reader = Task.Run(() =>
        {
            while (!producer.IsCompleted)
            {
                if (buffer.TryReadLatest(out var snapshot))
                    Assert.Equal((float)snapshot.Tick.Value, snapshot.Instances[0].Transform.Position.X);
                Assert.Equal(Vector3.One, retained.Instances[0].Transform.Position);
                Thread.Yield();
            }
        }, TestContext.Current.CancellationToken);
        await Task.WhenAll(producer, reader);
        Assert.Equal(Vector3.One, retained.Instances[0].Transform.Position);
    }

    [Fact]
    public void RetainedExtractedSnapshotSurvivesLaterTicksAndPopulationGrowth()
    {
        var buffer = new PresentationSnapshotBuffer();
        var extractor = new PresentationExtractor(buffer);
        var simulation = new SimulationCoordinator();
        simulation.RegisterTickObserver(extractor);
        var first = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(first, new WorldTransform(Vector3.One, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(first, new VisualIdentity(1));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var retained));
        for (int i = 0; i < 128; i++)
        {
            var entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
        }
        simulation.Entities.SetComponent(first, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var latest));
        Assert.Equal(129, latest.InstanceCount);
        Assert.Equal(1, retained.InstanceCount);
        Assert.Equal(Vector3.One, retained.Instances[0].Transform.Position);
    }

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
                    RenderVisibilityMask.World)));
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
                    RenderVisibilityMask.World)));
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
                    RenderVisibilityMask.World)));
        Assert.True(world.Update(buffer));

        Assert.Equal(
            7.0f,
            world.GetInterpolatedInstance(0, 0.5f)
                .Transform.Position.X);
    }

    [Fact]
    public async Task SnapshotHandoffSupportsIndependentProducerAndConsumerRates()
    {
        const ulong finalTick = 2_000;
        var buffer = new PresentationSnapshotBuffer();

        Task producer = Task.Run(
            () =>
            {
                for (ulong tick = 1; tick <= finalTick; tick++)
                {
                    buffer.Publish(Snapshot(tick));
                }
            },
            TestContext.Current.CancellationToken);

        ulong observedTick = 0;
        Task consumer = Task.Run(
            () =>
            {
                while (observedTick < finalTick)
                {
                    if (buffer.TryReadLatest(out PresentationSnapshot snapshot))
                    {
                        Assert.True(snapshot.Tick.Value >= observedTick);
                        observedTick = snapshot.Tick.Value;
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            },
            TestContext.Current.CancellationToken);

        await Task.WhenAll(producer, consumer);

        Assert.Equal(finalTick, observedTick);
        Assert.True(buffer.TryReadLatest(out PresentationSnapshot latest));
        Assert.Equal(finalTick, latest.Tick.Value);
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
