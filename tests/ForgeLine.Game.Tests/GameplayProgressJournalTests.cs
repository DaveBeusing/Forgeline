using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GameplayProgressJournalTests
{
    [Fact]
    public void FullIdentityOwnershipAndCompletedTickGateProtectRetainedEvidence()
    {
        var simulation = new SimulationCoordinator();
        var journal = new GameplayProgressJournal(simulation.Entities, simulation.SessionId);
        simulation.RegisterTickObserver(journal);
        var owner = new PlayerId(1);
        var actor = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(actor, new ControllableEntity(owner, ControllableEntityCategory.Unit));
        journal.Record(new(1), owner, GameplayProgressKind.CargoUnloaded, actor, actor, actor, 1, ResourceIds.Fuel, 10);
        Assert.Empty(journal.Capture(owner, new(1)));
        simulation.AdvanceOneTick();
        var proof = Assert.Single(journal.Capture(owner, simulation.CurrentTick));
        Assert.Same(journal.Capture(owner, simulation.CurrentTick), journal.Capture(owner, simulation.CurrentTick));
        Assert.Empty(journal.Capture(new PlayerId(2), simulation.CurrentTick));
        journal.Record(simulation.CurrentTick, owner, proof.Kind, actor, actor, actor, 1, ResourceIds.Fuel, 10);
        Assert.Equal(proof, Assert.Single(journal.Capture(owner, simulation.CurrentTick)));
        journal.Record(simulation.CurrentTick, new PlayerId(2), proof.Kind, actor, actor, actor, 2, ResourceIds.Fuel, 10);
        Assert.Empty(journal.Capture(new PlayerId(2), simulation.CurrentTick));
        var foreign = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(foreign, new ControllableEntity(new PlayerId(2), ControllableEntityCategory.Unit));
        journal.Record(simulation.CurrentTick, owner, proof.Kind, foreign, foreign, actor, 2, ResourceIds.Fuel, 10);
        Assert.Equal(proof, Assert.Single(journal.Capture(owner, simulation.CurrentTick)));
        simulation.Entities.DestroyEntity(foreign);
        simulation.Entities.DestroyEntity(actor);
        var replacement = simulation.Entities.CreateEntity();
        Assert.Equal(actor.Index, replacement.Index); Assert.NotEqual(actor.Generation, replacement.Generation);
        simulation.Entities.AddComponent(replacement, new ControllableEntity(owner, ControllableEntityCategory.Unit));
        journal.Record(simulation.CurrentTick, owner, proof.Kind, actor, actor, replacement, 2, ResourceIds.Fuel, 10);
        Assert.Equal(proof, Assert.Single(journal.Capture(owner, simulation.CurrentTick)));
        Assert.Empty(journal.Capture(owner, new(simulation.CurrentTick.Value + 1)));
    }

    [Fact]
    public void FuelAndAmmunitionMustComeFromTheSameActualLoadingVisit()
    {
        var simulation = new SimulationCoordinator();
        var journal = new GameplayProgressJournal(simulation.Entities, simulation.SessionId);
        simulation.RegisterTickObserver(journal);
        var owner = new PlayerId(1);
        var truck = simulation.Entities.CreateEntity();
        var depot = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(truck, new ControllableEntity(owner, ControllableEntityCategory.Unit));
        simulation.Entities.AddComponent(depot, new ControllableEntity(owner, ControllableEntityCategory.Building));
        journal.Record(new(1), owner, GameplayProgressKind.SupplyLoadedFuel, truck, depot, truck, 10, ResourceIds.Fuel, 20, BuildingIds.SupplyDepot.Value);
        journal.Record(new(1), owner, GameplayProgressKind.SupplyLoadedAmmunition, truck, depot, truck, 11, ResourceIds.Ammunition, 20, BuildingIds.SupplyDepot.Value);
        simulation.AdvanceOneTick();
        Assert.DoesNotContain(journal.Capture(owner, simulation.CurrentTick), x => x.Kind == GameplayProgressKind.SupplyLoadCompleted);
        journal.Record(simulation.CurrentTick, owner, GameplayProgressKind.SupplyLoadedFuel, truck, depot, truck, 11, ResourceIds.Fuel, 20, BuildingIds.SupplyDepot.Value);
        Assert.Contains(journal.Capture(owner, simulation.CurrentTick), x => x.Kind == GameplayProgressKind.SupplyLoadCompleted && x.OrderIdentity == 11);
    }
}
