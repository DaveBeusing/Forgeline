using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationDiagnosticReplanTests
{
    [Fact]
    public void PhysicalReplanReportsTheActualReasonRequestAndEventTickWithoutReplacingTheGoal()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = runtime.Simulation.Entities;
        EntityId truck = default;
        foreach (var entity in entities.Query<UnitIdentity, ControllableEntity>())
            if (entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck &&
                entities.GetComponent<ControllableEntity>(entity).Owner == new PlayerId(1)) truck = entity;
        var pose = entities.GetComponent<WorldTransform>(truck);
        Vector3 goal = pose.Position + new Vector3(-35, 0, 30);
        entities.AddComponent(truck, new MovementOrder(new PlayerId(1), goal, SimulationTick.Zero, SimulationTick.Zero));
        var obstacle = entities.CreateEntity();
        entities.AddComponent(obstacle, new WorldTransform(pose.Position, Quaternion.Identity, new(100, 20, 100)));
        entities.AddComponent(obstacle, new SpatialPresence(new(50, 10, 50), new SpatialEntryMetadata(0, 1, SpatialMobility.Mobile)));
        entities.AddComponent(obstacle, GroundMovementState.Stationary());
        runtime.Services.GroundMovement.SetNavigationDiagnosticObservations(true);
        var observed = new List<NavigationDiagnosticReplan>();
        runtime.Simulation.RegisterTickObserver(new Observer(context =>
        {
            var frame = runtime.Services.GroundMovement.CaptureNavigationDiagnostics(runtime, context, new PlayerId(1), [truck]);
            if (frame is { Vehicles.Count: > 0 } current && current.Vehicles[0].Facts.LastReplan is { } replan) observed.Add(replan);
        }));
        runtime.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        Assert.True(observed.Count > 0, $"actors={string.Join(';', runtime.Services.GroundMovement.CaptureNavigationState().Actors)}; movement={entities.GetComponent<GroundMovementState>(truck)}; route={entities.HasComponent<NavigationRouteState>(truck)}; pending={entities.HasComponent<NavigationPendingPath>(truck)}");
        Assert.All(observed, replan =>
        {
            Assert.Equal(goal, replan.OriginalOrder.WorldTarget);
            Assert.Equal(truck, replan.Vehicle);
            Assert.Equal(NavigationBlockReason.MovingVehicle, replan.Reason);
            Assert.NotEqual(replan.PreviousRequestId, replan.RequestId);
            Assert.True(replan.RequestId > 0);
            Assert.True(replan.ActorReplans > 0);
            Assert.InRange(replan.Source.SourceTick.Value, 1UL, 200UL);
            Assert.Equal(runtime.Services.Navigation.World.Version, replan.Source.Revision.Geometry);
        });
    }

    private sealed class Observer(Action<SimulationContext> capture) : ISimulationTickObserver
    {
        public void OnTickCompleted(SimulationContext context) => capture(context);
    }
}
