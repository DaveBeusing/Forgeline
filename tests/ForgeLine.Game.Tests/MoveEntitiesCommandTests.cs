using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MoveEntitiesCommandTests
{
    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly PlayerId ForeignPlayer = new(2);

    [Fact]
    public void MovementOrderExecutesOnlyOnScheduledTick()
    {
        var simulation = new SimulationCoordinator();
        EntityId entity = CreateControllableEntity(
            simulation,
            LocalPlayer,
            Vector3.Zero);

        var command = new MoveEntitiesCommand(
            LocalPlayer,
            [entity],
            new Vector3(25.0f, 3.0f, -10.0f),
            SimulationTick.Zero);

        simulation.SubmitCommand(
            command,
            new SimulationTick(2),
            new SimulationCommandSource(LocalPlayer.Value));

        simulation.AdvanceOneTick();

        Assert.False(
            simulation.Entities.HasComponent<MovementOrder>(entity));

        simulation.AdvanceOneTick();

        Assert.True(
            simulation.Entities.TryGetComponent(
                entity,
                out MovementOrder order));
        Assert.Equal(new SimulationTick(2), order.AcceptedAtTick);
        Assert.Equal(SimulationTick.Zero, order.SubmittedAtTick);
        Assert.Equal(LocalPlayer, order.Issuer);
        Assert.Equal(
            new Vector3(25.0f, 3.0f, -10.0f),
            order.WorldTarget);
        Assert.Equal(1, command.AcceptedTargetCount);
        Assert.Equal(0, command.RejectedTargetCount);
        Assert.Equal(new SimulationTick(2), command.ExecutedAtTick);
    }

    [Fact]
    public void MovementOrderRejectsStaleAndForeignTargets()
    {
        var simulation = new SimulationCoordinator();

        EntityId stale = CreateControllableEntity(
            simulation,
            LocalPlayer,
            Vector3.Zero);
        Assert.True(simulation.Entities.DestroyEntity(stale));

        EntityId replacement = CreateControllableEntity(
            simulation,
            LocalPlayer,
            Vector3.One);
        EntityId foreign = CreateControllableEntity(
            simulation,
            ForeignPlayer,
            new Vector3(2.0f, 0.0f, 0.0f));
        EntityId accepted = CreateControllableEntity(
            simulation,
            LocalPlayer,
            new Vector3(3.0f, 0.0f, 0.0f));

        Assert.Equal(stale.Index, replacement.Index);
        Assert.NotEqual(stale.Generation, replacement.Generation);

        var command = new MoveEntitiesCommand(
            LocalPlayer,
            [stale, foreign, accepted],
            new Vector3(100.0f, 0.0f, 100.0f),
            SimulationTick.Zero);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));
        simulation.AdvanceOneTick();

        Assert.Equal(1, command.AcceptedTargetCount);
        Assert.Equal(2, command.RejectedTargetCount);
        Assert.False(
            simulation.Entities.HasComponent<MovementOrder>(replacement));
        Assert.False(
            simulation.Entities.HasComponent<MovementOrder>(foreign));
        Assert.True(
            simulation.Entities.HasComponent<MovementOrder>(accepted));
    }

    [Fact]
    public void MovementOrderRejectsSelectedBuildings()
    {
        var simulation = new SimulationCoordinator();
        EntityId building =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            building,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            building,
            new ControllableEntity(
                LocalPlayer,
                ControllableEntityCategory.Building));

        var command =
            new MoveEntitiesCommand(
                LocalPlayer,
                [building],
                new Vector3(64.0f, 0.0f, 64.0f),
                SimulationTick.Zero);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(
                LocalPlayer.Value));
        simulation.AdvanceOneTick();

        Assert.Equal(
            0,
            command.AcceptedTargetCount);
        Assert.Equal(
            1,
            command.RejectedTargetCount);
        Assert.False(
            simulation.Entities.HasComponent<MovementOrder>(
                building));
    }

    [Fact]
    public void MovementCommandNeverChangesTransformDirectly()
    {
        var simulation = new SimulationCoordinator();
        var initialPosition = new Vector3(4.0f, 2.0f, -7.0f);
        EntityId entity = CreateControllableEntity(
            simulation,
            LocalPlayer,
            initialPosition);

        var command = new MoveEntitiesCommand(
            LocalPlayer,
            [entity],
            new Vector3(500.0f, 0.0f, 500.0f),
            SimulationTick.Zero);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));
        simulation.AdvanceOneTick();

        Assert.True(
            simulation.Entities.TryGetComponent(
                entity,
                out WorldTransform transform));
        Assert.Equal(initialPosition, transform.Position);
        Assert.True(
            simulation.Entities.HasComponent<MovementOrder>(entity));
    }

    private static EntityId CreateControllableEntity(
        SimulationCoordinator simulation,
        PlayerId owner,
        Vector3 position)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new ControllableEntity(
                owner,
                ControllableEntityCategory.Unit));
        return entity;
    }
}
