using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests.SnapshotPublication;

public sealed class ProducerScratchTests
{
    [Fact]
    public void FilteredCaptureNeverTransfersScratchAndShrinksAfterOversizedPopulation()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(store));
        var buffer = new PresentationSnapshotBuffer();
        var extractor = new PresentationExtractor(buffer, store, new FactionId(1));
        simulation.RegisterTickObserver(extractor);
        var own = Add(1);
        var enemy = Add(2);
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var retained));
        Assert.Single(retained.Instances.ToArray());
        Assert.Equal(own, retained.Instances[0].Entity);
        for (int i = 2; i < 64; i++)
        {
            simulation.Entities.SetComponent(own, new WorldTransform(new Vector3(i, 0, 0), Quaternion.Identity, Vector3.One));
            simulation.AdvanceOneTick();
            Assert.Equal(1, retained.Instances[0].Transform.Position.X);
            Assert.True(buffer.TryReadLatest(out var current));
            Assert.Equal(i, current.Instances[0].Transform.Position.X);
            Assert.DoesNotContain(current.Instances.ToArray(), value => value.Entity == enemy);
        }
        var extra = Enumerable.Range(0, 17_000).Select(_ => Add(1)).ToArray();
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var large));
        Assert.Equal(17_001, large.InstanceCount);
        foreach (var entity in extra) simulation.Entities.DestroyEntity(entity);
        simulation.AdvanceOneTick();
        Assert.InRange(extractor.CaptureScratchRetainedBytes, 0,
            (long)PresentationExtractor.MaximumRetainedCaptureInstances * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderInstance>());
        Assert.Equal(17_001, large.InstanceCount);
        Assert.Equal(1, retained.Instances[0].Transform.Position.X);
        simulation.Entities.DestroyEntity(own);
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var empty));
        Assert.Equal(0, empty.InstanceCount);

        EntityId Add(uint faction)
        {
            var entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.UnitX, Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
            simulation.Entities.AddComponent(entity, new IntelligenceSignature(new(faction), entity.Index));
            return entity;
        }
    }
}
