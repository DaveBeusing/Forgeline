using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectablePresentationExtractionTests
{
    [Fact]
    public void ExtractorCopiesControllableMetadataIntoSnapshot()
    {
        var buffer = new PresentationSnapshotBuffer();
        var simulation = new SimulationCoordinator();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer));

        var owner = new PlayerId(17);
        var entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new VisualIdentity(1));
        simulation.Entities.AddComponent(
            entity,
            new ControllableEntity(
                owner,
                ControllableEntityCategory.Logistics));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        Assert.Equal(1, snapshot.InstanceCount);
        Assert.Equal(entity, snapshot.Instances[0].Entity);
        Assert.True(snapshot.Instances[0].Selectable.IsSelectable);
        Assert.Equal(owner, snapshot.Instances[0].Selectable.Owner);
        Assert.Equal(
            ControllableEntityCategory.Logistics,
            snapshot.Instances[0].Selectable.Category);
    }
}
