using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationWaypointOwnershipTests
{
    [Theory]
    [InlineData(MovementOrderKind.Strategic)]
    [InlineData(MovementOrderKind.LocalApproach)]
    public void CoincidentNewOrderIsNotMistakenForOwnedWaypoint(MovementOrderKind kind)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 128, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 128, new float[17 * 17]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(world));
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(navigation);
        EntityId unit = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(unit, new WorldTransform(new Vector3(10, 0.5f, 10), Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(unit, GroundMovement.CreateDefault(0.5f));
        simulation.Entities.AddComponent(unit, GroundMovementState.Stationary());
        simulation.Entities.AddComponent(unit, new NavigationAgent(NavigationMovementClass.Wheeled));
        simulation.Entities.AddComponent(unit, new MovementOrder(new PlayerId(1), new Vector3(106, 0, 10),
            SimulationTick.Zero, new SimulationTick(1)));
        simulation.RunTicks(2, TestContext.Current.CancellationToken);
        NavigationRouteState completed = simulation.Entities.GetComponent<NavigationRouteState>(unit);
        Assert.NotNull(completed.SourceRequest);
        Assert.True(completed.SourceRequest.Value.RequestId > 0);
        Assert.Equal(unit, completed.SourceRequest.Value.Requester);
        Assert.Equal(completed.OriginalOrder.WorldTarget, completed.SourceRequest.Value.Destination);
        Assert.Equal(Quaternion.Identity, completed.StartRotation);
        MovementOrder waypoint = simulation.Entities.GetComponent<MovementOrder>(unit);
        // Same position and accepted tick are not a sufficient ownership token.
        MovementOrder replacement = waypoint with
        {
            Kind = kind,
            SubmittedAtTick = kind == MovementOrderKind.Strategic ? simulation.CurrentTick : waypoint.SubmittedAtTick,
            TargetHeadingRadians = kind == MovementOrderKind.LocalApproach ? MathF.PI / 2 : null
        };
        simulation.Entities.SetComponent(unit, replacement);
        navigation.UpdateWorld(new NavigationWorld(world.Grid, world.SectorSettings, new NavigationVersion(2)));
        simulation.AdvanceOneTick();
        if (kind == MovementOrderKind.LocalApproach)
        {
            Assert.Equal(replacement, simulation.Entities.GetComponent<MovementOrder>(unit));
            Assert.False(simulation.Entities.HasComponent<NavigationRouteState>(unit));
        }
        else
        {
            Assert.Equal(replacement, simulation.Entities.GetComponent<NavigationPendingPath>(unit).OriginalOrder);
        }
    }
}
