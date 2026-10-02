using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class UnitProductionPriorityCommandTests
{
    [Fact]
    public void PriorityCommandUpdatesOwnedRequestAndRejectsForeignOwner()
    {
        var simulation =
            new SimulationCoordinator();
        PlayerId owner =
            new(1);
        PlayerId foreign =
            new(2);

        EntityId facility =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            facility,
            new UnitProductionFacility(
                new InventoryId(1),
                UnitProductionCapability.Vehicle,
                owner,
                Vector3.Zero,
                SimulationTick.Zero));

        EntityId request =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            request,
            new UnitProductionRequest(
                facility,
                UnitIds.ScoutVehicle,
                ProductionPriority.Normal,
                SimulationTick.Zero));

        var owned =
            new SetUnitProductionRequestPriorityCommand(
                owner,
                request,
                ProductionPriority.High,
                SimulationTick.Zero);
        owned.Execute(
            new SimulationContextAccessor(
                simulation).Context);

        Assert.True(owned.Accepted);
        Assert.Equal(
            ProductionPriority.High,
            simulation.Entities
                .GetComponent<UnitProductionRequest>(
                    request)
                .Priority);

        var rejected =
            new SetUnitProductionRequestPriorityCommand(
                foreign,
                request,
                ProductionPriority.Critical,
                SimulationTick.Zero);
        rejected.Execute(
            new SimulationContextAccessor(
                simulation).Context);

        Assert.False(rejected.Accepted);
        Assert.Equal(
            ProductionPriority.High,
            simulation.Entities
                .GetComponent<UnitProductionRequest>(
                    request)
                .Priority);
    }

    private sealed class SimulationContextAccessor : ISimulationSystem
    {
        public SimulationContextAccessor(
            SimulationCoordinator simulation)
        {
            simulation.RegisterSystem(this);
            simulation.AdvanceOneTick();
        }

        public SimulationPhase Phase =>
            SimulationPhase.AiDecisions;

        public SimulationContext Context { get; private set; } = null!;

        public void Execute(
            SimulationContext context)
        {
            Context = context;
        }
    }
}
