using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationDiagnosticOwnershipTests
{
    [Fact]
    public void NewOwnerDoesNotInheritDiagnosticAccessToThePreviousOwnersRouteOrRequest()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = runtime.Simulation.Entities;
        EntityId truck = default;
        foreach (var entity in entities.Query<UnitIdentity, ControllableEntity>())
            if (entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck &&
                entities.GetComponent<ControllableEntity>(entity).Owner == new PlayerId(1)) truck = entity;
        var pose = entities.GetComponent<WorldTransform>(truck);
        entities.AddComponent(truck, new MovementOrder(new PlayerId(1), pose.Position + new Vector3(-30, 0, 20),
            SimulationTick.Zero, SimulationTick.Zero));
        NavigationDiagnosticSnapshot? captured = null;
        runtime.Simulation.RegisterTickObserver(new Observer(context =>
            captured = runtime.Services.GroundMovement.CaptureNavigationDiagnostics(runtime, context, new PlayerId(2), [truck])));
        runtime.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.True(entities.HasComponent<NavigationRouteState>(truck));
        entities.SetComponent(truck, new ControllableEntity(new PlayerId(2), ControllableEntityCategory.Logistics));
        runtime.Simulation.AdvanceOneTick();
        var vehicle = Assert.Single(Assert.IsType<NavigationDiagnosticSnapshot>(captured).Vehicles);
        Assert.Equal(new PlayerId(2), vehicle.Facts.Owner);
        Assert.Null(vehicle.Facts.OriginalOrder);
        Assert.Null(vehicle.Facts.RequestId);
        Assert.Null(vehicle.Facts.CommandGeneration);
        Assert.Null(vehicle.Facts.SearchStatus);
        Assert.Null(vehicle.Facts.ActualMotion);
        Assert.Empty(vehicle.Route);
    }

    private sealed class Observer(Action<SimulationContext> capture) : ISimulationTickObserver
    {
        public void OnTickCompleted(SimulationContext context) => capture(context);
    }
}
