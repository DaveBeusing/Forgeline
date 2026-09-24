using System.Numerics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class LinearMotionSystemTests
{
    [Theory]
    [InlineData(20, 0.5f)]
    [InlineData(10, 1.0f)]
    public void MotionUsesConfiguredFixedTickDuration(
        int ticksPerSecond,
        float expectedX)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        var entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            WorldTransform.Identity);
        simulation.Entities.AddComponent(
            entity,
            new LinearVelocity(new Vector3(10.0f, 0.0f, 0.0f)));
        simulation.RegisterSystem(new LinearMotionSystem());

        simulation.AdvanceOneTick();

        Assert.True(
            simulation.Entities.TryGetComponent(
                entity,
                out WorldTransform transform));
        Assert.InRange(transform.Position.X, expectedX - 0.0001f, expectedX + 0.0001f);
    }

    [Fact]
    public void VisualIdentityContainsNoGraphicsObjectReferences()
    {
        var visual = new VisualIdentity(7);

        Assert.Equal(7U, visual.VisualId);
        Assert.Equal(VisualVisibilityMask.World, visual.Visibility);
    }
}
