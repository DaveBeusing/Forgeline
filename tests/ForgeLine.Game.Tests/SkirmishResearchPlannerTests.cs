using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishResearchPlannerTests
{
    [Fact]
    public void PaidResearchUsesOneRequestAndSkipsUnusableLogisticsCoordination()
    {
        var fixture = new Fixture();
        fixture.Simulation.RunTicks(430, TestContext.Current.CancellationToken);
        foreach (TechnologyId id in new[] { TechnologyIds.IndustrialStandardization, TechnologyIds.MechanizedSystems, TechnologyIds.SensorFusion })
            Assert.True(TechnologyStateQueries.IsCompleted(fixture.Simulation.Entities, new PlayerId(1), id));
        Assert.False(TechnologyStateQueries.IsCompleted(fixture.Simulation.Entities, new PlayerId(1), TechnologyIds.LogisticsCoordination));
        Assert.Equal(760, fixture.Stock.GetQuantity(fixture.Inventory, ResourceIds.Steel));
        Assert.Equal(860, fixture.Stock.GetQuantity(fixture.Inventory, ResourceIds.Electronics));
        Assert.False(TechnologyStateQueries.TryGetActiveResearch(fixture.Simulation.Entities, new PlayerId(1), out _, out _));
    }

    [Fact]
    public void PriorityAndProductionReserveDoNotCreateOrReserveAnUnaffordableWish()
    {
        var fixture = new Fixture { Priority = true };
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(SkirmishResearchReason.Priority, fixture.Planner.Capture(fixture.Controller).Reason);
        Assert.False(TechnologyStateQueries.TryGetActiveResearch(fixture.Simulation.Entities, new PlayerId(1), out _, out _));
        fixture.Priority = false;
        fixture.Stock.Remove(fixture.Inventory, ResourceIds.Steel, 900);
        fixture.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        Assert.Equal(SkirmishResearchReason.ProductionReserve, fixture.Planner.Capture(fixture.Controller).Reason);
        Assert.False(TechnologyStateQueries.TryGetActiveResearch(fixture.Simulation.Entities, new PlayerId(1), out _, out _));
        Assert.Equal(100, fixture.Stock.GetAvailableQuantity(fixture.Inventory, ResourceIds.Steel));
        Assert.Equal(1000, fixture.Stock.GetAvailableQuantity(fixture.Inventory, ResourceIds.Electronics));
    }

    [Fact]
    public void PowerPauseCancelAndForeignInfrastructureKeepNormalResearchRules()
    {
        var fixture = new Fixture();
        fixture.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.True(TechnologyStateQueries.TryGetActiveResearch(fixture.Simulation.Entities, new PlayerId(1), out var entity, out var active));
        Assert.Equal(5u, active.ProgressTicks);
        fixture.Simulation.Entities.SetComponent(fixture.Core, new PowerConsumer(10, enabled: false));
        fixture.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        Assert.Equal(active.ProgressTicks, fixture.Simulation.Entities.GetComponent<TechnologyResearchRequest>(entity).ProgressTicks);
        fixture.Priority = true;
        fixture.Simulation.SubmitCommand(PlayerTechnologyActionCommand.Cancel(new PlayerId(2), entity, fixture.Simulation.CurrentTick), fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();
        Assert.True(fixture.Simulation.Entities.IsAlive(entity));
        fixture.Simulation.SubmitCommand(PlayerTechnologyActionCommand.Cancel(new PlayerId(1), entity, fixture.Simulation.CurrentTick), fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();
        Assert.False(fixture.Simulation.Entities.IsAlive(entity));
        Assert.Equal(920, fixture.Stock.GetQuantity(fixture.Inventory, ResourceIds.Steel));
        fixture.Priority = false;
        fixture.Simulation.Entities.SetComponent(fixture.Core, new CompletedBuilding(BuildingIds.CommandCore, new PlayerId(2), fixture.Simulation.CurrentTick));
        fixture.Simulation.AdvanceOneTick();
        Assert.False(TechnologyStateQueries.TryGetActiveResearch(fixture.Simulation.Entities, new PlayerId(1), out _, out _));
    }

    private sealed class Fixture
    {
        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Stock { get; } = new();
        public InventoryId Inventory { get; }
        public EntityId Controller { get; }
        public EntityId Core { get; }
        public SkirmishResearchPlanner Planner { get; }
        public bool Priority { get; set; }
        private readonly List<EntityId> _buildings = new();
        public Fixture()
        {
            Inventory = Stock.CreateInventory(new InventorySpecification(10000));
            Stock.Add(Inventory, ResourceIds.Steel, 1000);
            Stock.Add(Inventory, ResourceIds.Electronics, 1000);
            foreach (BuildingId id in new[] { BuildingIds.CommandCore, BuildingIds.VehicleFactory, BuildingIds.Radar })
            {
                var building = Simulation.Entities.CreateEntity();
                _buildings.Add(building);
                Simulation.Entities.AddComponent(building, new CompletedBuilding(id, new PlayerId(1), SimulationTick.Zero));
                Simulation.Entities.AddComponent(building, new PowerConsumer(10, allocatedPower: 10, state: PowerOperationalState.Powered));
                Simulation.Entities.AddComponent(building, new InventoryStorage(Inventory));
            }
            Core = _buildings[0];
            Controller = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Controller, new SkirmishOpponentController(new PlayerId(1), new FactionId(1), Core, Vector3.Zero));
            var definitions = DirectorateTechnologyDefinitions.CreateCatalog();
            Planner = new(definitions, DirectorateContent.CreateUnitCatalog(), Stock);
            Simulation.RegisterSystem(new TechnologyResearchSystem(definitions, Stock));
            Simulation.RegisterSystem(new Decision(this));
        }
        private sealed class Decision(Fixture fixture) : ISimulationSystem
        {
            public SimulationPhase Phase => SimulationPhase.AiDecisions;
            public void Execute(SimulationContext context) => fixture.Planner.Evaluate(context, fixture.Controller,
                context.Entities.GetComponent<SkirmishOpponentController>(fixture.Controller), fixture._buildings, fixture.Priority, true);
        }
    }
}
