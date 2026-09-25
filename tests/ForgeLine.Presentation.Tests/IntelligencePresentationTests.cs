using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class IntelligencePresentationTests
{
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Fact]
    public void EnemyRenderInstanceDisappearsWhenIdentificationIsLost()
    {
        var simulation = new SimulationCoordinator();
        var intelligenceStore =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 16.0f
                });
        var intelligence =
            new BattlefieldIntelligenceSystem(
                intelligenceStore);
        var buffer =
            new PresentationSnapshotBuffer();

        simulation.RegisterSystem(intelligence);
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer,
                intelligenceStore,
                BlueFaction,
                new AxisAlignedBounds(
                    new Vector3(-64.0f, -1.0f, -64.0f),
                    new Vector3(64.0f, 1.0f, 64.0f))));

        EntityId sensor =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            sensor,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            sensor,
            new VisualSensorState(
                BlueFaction,
                rangeMeters: 50.0f));

        EntityId enemy =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            enemy,
            new WorldTransform(
                new Vector3(10.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            enemy,
            new VisualIdentity(1));
        simulation.Entities.AddComponent(
            enemy,
            new IntelligenceSignature(
                RedFaction,
                identityKey: 42));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot visible));
        Assert.Equal(
            1,
            visible.InstanceCount);
        Assert.Equal(
            enemy,
            visible.Instances[0].Entity);
        Assert.NotNull(
            visible.Intelligence);
        Assert.Contains(
            visible.Intelligence!.Cells,
            cell =>
                cell.State ==
                IntelligenceState.Visible);

        Assert.True(
            simulation.Entities.DestroyEntity(
                sensor));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot hidden));
        Assert.Equal(
            0,
            hidden.InstanceCount);
        Assert.NotNull(
            hidden.Intelligence);
        Assert.Contains(
            hidden.Intelligence!.Cells,
            cell =>
                cell.State ==
                IntelligenceState.Explored);
        Assert.Contains(
            hidden.Intelligence.Contacts,
            contact =>
                contact.ContactKey ==
                    IntelligenceContactKey.FromEntity(enemy) &&
                !contact.IsCurrent);
    }
}
