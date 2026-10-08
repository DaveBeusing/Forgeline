using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyCapabilityGateTests
{
    [Fact]
    public void CombatEngineerProductionRequiresFieldEngineeringUnlock()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 4411);
        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    10_000.0));
        EntityId facility =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            facility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Infantry,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                scenario.Simulation.CurrentTick));

        Assert.False(
            TechnologyStateQueries.IsCapabilityUnlocked(
                scenario.Simulation.Entities,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                TechnologyCapabilityIds.FieldEngineering));

        EntityId blockedRequest =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            blockedRequest,
            new UnitProductionRequest(
                facility,
                UnitIds.CombatEngineer,
                ProductionPriority.Normal,
                scenario.Simulation.CurrentTick));

        scenario.Simulation.AdvanceOneTick();

        Assert.False(
            scenario.Simulation.Entities.IsAlive(
                blockedRequest));

        EntityId unlockEntity =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            unlockEntity,
            new TechnologyCapabilityUnlock(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                TechnologyCapabilityIds.FieldEngineering,
                TechnologyIds.IndustrialStandardization,
                scenario.Simulation.CurrentTick));

        EntityId allowedRequest =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            allowedRequest,
            new UnitProductionRequest(
                facility,
                UnitIds.CombatEngineer,
                ProductionPriority.Normal,
                scenario.Simulation.CurrentTick));

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            scenario.Simulation.Entities.IsAlive(
                allowedRequest));
        UnitProductionFacility state =
            scenario.Simulation.Entities
                .GetComponent<UnitProductionFacility>(
                    facility);
        Assert.Equal(
            allowedRequest,
            state.ActiveRequest);
        Assert.Equal(
            UnitIds.CombatEngineer,
            state.ActiveUnit);
    }

    private static MatchRuntime CreateScenario(
        ulong seed)
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    CentralDivideScenario
                        .CreateDefaultParticipants(
                            westComputerControlled: false,
                            eastComputerControlled: false)
            };

        return CentralDivideScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
}
