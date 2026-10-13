using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ArtilleryAimPresentationTests
{
    [Theory]
    [InlineData(FireMissionStatus.Acquiring, true)]
    [InlineData(FireMissionStatus.Firing, true)]
    [InlineData(FireMissionStatus.WaitingReload, true)]
    [InlineData(FireMissionStatus.Cancelled, false)]
    [InlineData(FireMissionStatus.Complete, false)]
    public void OwnAuthorizedMissionSuppliesAimWithoutReadingForeignMissionOrChangingState(FireMissionStatus status, bool aimed)
    {
        var simulation = new SimulationCoordinator(seed: 2026);
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots, viewingFaction: new(1)));
        var mission = new FireMissionState(new(100, 0, 600), default, 2, 0, status, default, default, default, default);
        EntityId own = Add(1);
        EntityId foreign = Add(2);
        simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Assert.True(snapshots.TryReadLatest(out var snapshot));
        var ownInstance = Assert.Single(snapshot.Instances.ToArray(), x => x.Entity == own && x.UnitFeature.IsSpecified);
        var foreignInstance = Assert.Single(snapshot.Instances.ToArray(), x => x.Entity == foreign && x.UnitFeature.IsSpecified);
        Assert.Equal(aimed ? MathF.Atan2(100, 600) : 0, ownInstance.UnitFeature.AimYawRadians, precision: 5);
        Assert.Equal(aimed ? MathF.Atan2(360 - 1.4f, MathF.Sqrt(100 * 100 + 600 * 600)) : 0,
            ownInstance.MechanicalPose.Elevation, precision: 5);
        Assert.Equal(0, foreignInstance.UnitFeature.AimYawRadians);
        Assert.Equal(0, foreignInstance.MechanicalPose.Elevation);
        Assert.Equal(mission, simulation.Entities.GetComponent<FireMissionState>(own));
        Assert.Equal(mission, simulation.Entities.GetComponent<FireMissionState>(foreign));

        EntityId Add(uint faction)
        {
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(new(0, 1.4f, 0), Quaternion.Identity, new(3.8f, 3.2f, 7.5f)));
            simulation.Entities.AddComponent(entity, new VisualIdentity(205));
            simulation.Entities.AddComponent(entity, new UnitIdentity(UnitIds.MobileArtillery, new(1)));
            simulation.Entities.AddComponent(entity, new IntelligenceSignature(new(faction), entity.Index));
            simulation.Entities.AddComponent(entity, new ArtilleryCapability(DirectorateContent.WeaponIds.MobileArtillery));
            simulation.Entities.AddComponent(entity, mission);
            return entity;
        }
    }
}
