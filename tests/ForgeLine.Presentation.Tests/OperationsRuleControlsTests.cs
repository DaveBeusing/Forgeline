using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsRuleControlsTests
{
    [Fact]
    public void ExtractionCopiesOnlyOneOwnedFacilityRulesAndRealRecipeInputs()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        var input = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
        var output = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
        EntityId facility = entities.CreateEntity();
        entities.AddComponent(facility, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Building));
        entities.AddComponent(facility, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
        entities.AddComponent(facility, new PowerNetworkMembership(new PowerNetworkId(100001)));
        entities.AddComponent(facility, new PowerGenerator(100));
        entities.AddComponent(facility, new PowerConsumer(1));
        var interaction = new PresentationInteractionState();
        interaction.SetSelection([facility]);
        interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var definition = new OperationsRuleDefinition(71, new PlayerId(1), facility,
            OperationsRuleTemplate.EnsureInputStockPolicy, ResourceIds.FerrousOre, 0, 100, 150, LogisticsStockPriority.Normal);
        scenario.Simulation.SubmitCommand(PlayerProductionActionCommand.Queue(new PlayerId(1), facility,
            RecipeIds.Steel, SimulationTick.Zero, mode: ProductionRequestMode.Repeat), new SimulationTick(1));
        scenario.Simulation.SubmitCommand(new OperationsRuleCommand(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, definition, 0, true, SimulationTick.Zero), new SimulationTick(1));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var selected = Assert.IsType<OperationsRuleSelectionSnapshot>(snapshot.PlayerActions!.OperationsRules);
        Assert.Equal(facility, selected.Facility);
        Assert.Equal(1000, selected.InputCapacity);
        Assert.Equal(RecipeIds.Steel, selected.ContextRecipe);
        Assert.Single(selected.Inputs, amount => amount.ResourceId == ResourceIds.FerrousOre);
        Assert.Equal(1u, Assert.Single(selected.Rules).Revision);
        scenario.Simulation.SubmitCommand(new OperationsRuleCommand(scenario.OperationsRules,
            OperationsRuleOperation.SetEnabled, definition, 1, false, scenario.Simulation.CurrentTick), scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out snapshot));
        Assert.False(Assert.Single(snapshot.PlayerActions!.OperationsRules!.Rules).Enabled);
        Assert.True(Assert.Single(selected.Rules).Enabled);
        Assert.Equal(1u, Assert.Single(selected.Rules).Revision);
        interaction.SetSelection([facility, scenario.GetBase(new PlayerId(2)).CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions!.OperationsRules);
    }
}
