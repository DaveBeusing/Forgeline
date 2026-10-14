using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CombatDecalExtractionTests
{
    [Fact]
    public void SampledVehicleTracksRequireContiguousMotionAndRemainBounded()
    {
        var simulation = new SimulationCoordinator();
        var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer));
        EntityId vehicle = Add(simulation, Vector3.Zero);
        simulation.Entities.AddComponent(vehicle, new UnitIdentity(UnitIds.MainBattleTank, DirectorateContent.FactionId));
        for (int tick = 0; tick < 32; tick++)
        {
            simulation.Entities.SetComponent(vehicle, new WorldTransform(new(0, 0, tick), Quaternion.Identity, new(8, 4, 12)));
            simulation.AdvanceOneTick();
        }
        Assert.True(buffer.TryReadLatest(out var moving));
        Assert.Contains(moving.Instances.ToArray(), x => x.DecalFeature.Kind == DecalKind.TrackedVehicle);
        ulong spawned = moving.DecalMetrics.Spawned;
        simulation.RunTicks(16, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var idle));
        Assert.Equal(spawned, idle.DecalMetrics.Spawned);
        simulation.Entities.SetComponent(vehicle, new WorldTransform(new(0, 0, 1000), Quaternion.Identity, new(8, 4, 12)));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var teleported));
        Assert.Equal(spawned, teleported.DecalMetrics.Spawned);
        Assert.InRange(teleported.DecalMetrics.Active, 1, 16);
        Assert.Equal(1, simulation.Entities.EntityCount);
    }

    [Fact]
    public void CopiedImpactCreatesTerrainMarkWithoutChangingAuthoritativeState()
    {
        var simulation = new SimulationCoordinator();
        var buffer = new PresentationSnapshotBuffer();
        var extractor = new PresentationExtractor(buffer);
        simulation.RegisterTickObserver(extractor);
        EntityId target = Add(simulation, Vector3.Zero);
        var impact = new CombatEvent(SimulationTick.Zero, CombatEventType.Impact, default, target, default,
            DirectorateContent.WeaponIds.MobileArtillery, Vector3.Zero, 0);
        extractor.QueueImpactDecal(simulation.Entities, impact, TargetClass.Structure);
        int count = simulation.Entities.EntityCount;
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var mark = Assert.Single(snapshot.Instances.ToArray(), x => x.DecalFeature.IsSpecified);
        Assert.Equal(DecalKind.Crater, mark.DecalFeature.Kind);
        Assert.Equal(.06f, mark.Transform.Position.Y);
        Assert.False(mark.Selectable.IsSelectable);
        Assert.False(mark.WorldFeature.Inspectable);
        Assert.Equal(1, snapshot.DecalMetrics.Published);
        Assert.Equal(count, simulation.Entities.EntityCount);
        Assert.Equal(Vector3.Zero, simulation.Entities.GetComponent<WorldTransform>(target).Position);
    }

    [Fact]
    public void ReceiverMarksFollowCopiedMotionAndRetireWhenVisualAuthorizationIsLost()
    {
        var simulation = new SimulationCoordinator();
        var buffer = new PresentationSnapshotBuffer();
        var extractor = new PresentationExtractor(buffer);
        simulation.RegisterTickObserver(extractor);
        EntityId source = Add(simulation, new(20, 0, 0));
        EntityId target = Add(simulation, Vector3.Zero);
        var impact = new CombatEvent(SimulationTick.Zero, CombatEventType.Impact, source, target, default,
            DirectorateContent.WeaponIds.Rifle, Vector3.Zero, 0);
        extractor.QueueImpactDecal(simulation.Entities, impact, TargetClass.ArmoredVehicle);
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var first));
        var mark = Assert.Single(first.Instances.ToArray(), x => x.DecalFeature.IsSpecified);
        Assert.Equal(DecalSurface.Receiver, mark.DecalFeature.Surface);
        simulation.Entities.SetComponent(target, new WorldTransform(new(300, 0, 0), Quaternion.Identity, new(8, 4, 12)));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var moved));
        var next = Assert.Single(moved.Instances.ToArray(), x => x.DecalFeature.IsSpecified);
        Assert.Equal(mark.Entity, next.Entity);
        Assert.Equal(300, next.Transform.Position.X - mark.Transform.Position.X, .001f);
        simulation.Entities.SetComponent(target, new VisualIdentity(1, VisualVisibilityMask.None));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.DoesNotContain(hidden.Instances.ToArray(), x => x.DecalFeature.IsSpecified);
        Assert.Equal(1UL, hidden.DecalMetrics.Retired);
        Assert.Equal(2, simulation.Entities.EntityCount);
    }

    private static EntityId Add(SimulationCoordinator simulation, Vector3 position)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, new(8, 4, 12)));
        simulation.Entities.AddComponent(entity, new VisualIdentity(1));
        return entity;
    }
}
