using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PlayerActionReadModelTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void ConstructionActionsMirrorCatalogAndOwnCapturedInventoryValues()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4301);
        var buffer =
            RegisterExtraction(
                scenario,
                out PresentationInteractionState interaction,
                out _);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        PlayerActionSnapshot actions =
            Assert.IsType<PlayerActionSnapshot>(
                snapshot.PlayerActions);

        Assert.Equal(
            scenario.Services.BuildingDefinitions.Count,
            actions.Construction.Count);
        Assert.Equal(
            snapshot.SessionId,
            actions.SessionId);
        Assert.Equal(
            snapshot.Tick,
            actions.Tick);

        PlayerConstructionActionReadModel powerPlant =
            Assert.Single(
                actions.Construction,
                action =>
                    action.BuildingId ==
                    BuildingIds.PowerPlant);
        PlayerActionResourceAmount ferrous =
            Assert.Single(
                powerPlant.Costs,
                cost =>
                    cost.ResourceId ==
                    ResourceIds.FerrousOre);
        double captured =
            ferrous.AvailableQuantity;

        Assert.True(
            scenario.Inventories.Remove(
                scenario.West.StartingInventory,
                ResourceIds.FerrousOre,
                1.0).Succeeded);

        Assert.Equal(
            captured,
            ferrous.AvailableQuantity);
        Assert.NotEqual(
            captured,
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.FerrousOre));

        interaction.SetSelection(
            [scenario.West.CommandCore]);
    }

    [Fact]
    public void ProductionActionsUseSelectedFacilityInventoryAndRejectMixedOrForeignSelection()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4302);
        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        InventoryId output =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        EntityId facility =
            scenario.Simulation.Entities.CreateEntity();

        scenario.Simulation.Entities.AddComponent(
            facility,
            new ControllableEntity(
                LocalPlayer,
                ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            facility,
            new ProductionFacility(
                input,
                output,
                ProductionCapability.SteelProcessing,
                scenario.Simulation.CurrentTick));

        var buffer =
            RegisterExtraction(
                scenario,
                out PresentationInteractionState interaction,
                out _);
        interaction.SetSelection([facility]);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        PlayerProductionFacilityActionReadModel production =
            Assert.IsType<PlayerProductionFacilityActionReadModel>(
                snapshot.PlayerActions?.Production);
        PlayerProductionRecipeActionReadModel steel =
            Assert.Single(production.Recipes);

        Assert.Equal(
            RecipeIds.Steel,
            steel.RecipeId);
        Assert.False(steel.HasInputs);
        Assert.Equal(
            0.0,
            Assert.Single(steel.Inputs).AvailableQuantity);
        Assert.True(
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.FerrousOre) >
            0.0);

        Assert.True(
            scenario.Inventories.Add(
                input,
                ResourceIds.FerrousOre,
                100.0).Succeeded);
        Assert.Equal(
            0.0,
            Assert.Single(steel.Inputs).AvailableQuantity);

        interaction.SetSelection(
            [facility, scenario.West.CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.Null(
            snapshot.PlayerActions?.Production);

        scenario.Simulation.Entities.SetComponent(
            facility,
            new ControllableEntity(
                new PlayerId(2),
                ControllableEntityCategory.Building));
        interaction.SetSelection([facility]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.Null(
            snapshot.PlayerActions?.Production);
    }

    [Fact]
    public void UnitProductionActionsCoverSupportedCatalogAndExposeRealCosts()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4303);
        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(10_000.0));
        EntityId facility =
            scenario.Simulation.Entities.CreateEntity();

        scenario.Simulation.Entities.AddComponent(
            facility,
            new ControllableEntity(
                LocalPlayer,
                ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            facility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.All,
                LocalPlayer,
                System.Numerics.Vector3.Zero,
                scenario.Simulation.CurrentTick));

        var buffer =
            RegisterExtraction(
                scenario,
                out PresentationInteractionState interaction,
                out _);
        interaction.SetSelection([facility]);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        PlayerUnitProductionFacilityActionReadModel units =
            Assert.IsType<PlayerUnitProductionFacilityActionReadModel>(
                snapshot.PlayerActions?.UnitProduction);

        Assert.Equal(
            scenario.Services.UnitDefinitions.Count,
            units.Units.Count);

        PlayerUnitProductionActionReadModel tank =
            Assert.Single(
                units.Units,
                action =>
                    action.UnitId ==
                    UnitIds.MainBattleTank);
        Assert.Contains(
            tank.Costs,
            cost =>
                cost.ResourceId ==
                ResourceIds.Fuel);
        Assert.Contains(
            tank.Costs,
            cost =>
                cost.ResourceId ==
                ResourceIds.Ammunition);
        Assert.False(tank.HasInputs);
    }

    [Fact]
    public void LogisticsAndSupplyActionsExposeOwnedAuthoritativeState()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4304);
        var buffer =
            RegisterExtraction(
                scenario,
                out PresentationInteractionState interaction,
                out _);

        EntityId policyEntity =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            policyEntity,
            new LogisticsStockPolicy(
                scenario.West.CommandCore,
                ResourceIds.Fuel,
                50.0,
                100.0,
                150.0,
                LogisticsStockPriority.High));

        interaction.SetSelection(
            [scenario.West.CommandCore]);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        PlayerLogisticsActionReadModel logistics =
            Assert.IsType<PlayerLogisticsActionReadModel>(
                snapshot.PlayerActions?.Logistics);
        PlayerStockPolicyActionReadModel fuel =
            Assert.Single(
                logistics.Policies,
                policy =>
                    policy.ResourceId ==
                    ResourceIds.Fuel);

        Assert.True(fuel.HasPolicy);
        Assert.Equal(policyEntity, fuel.PolicyEntity);
        Assert.Equal(100.0, fuel.DesiredTarget);
        Assert.True(fuel.CurrentQuantity > 0.0);

        EntityId cargoTruck =
            scenario.West.StartingUnits[1];
        interaction.SetSelection([cargoTruck]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.NotNull(
            snapshot.PlayerActions?.Logistics?.Cargo);

        EntityId engineer =
            scenario.West.StartingUnits[0];
        interaction.SetSelection([engineer]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));

        PlayerSupplyActionReadModel supply =
            Assert.IsType<PlayerSupplyActionReadModel>(
                snapshot.PlayerActions?.Supply);
        Assert.Equal(engineer, supply.Entity);
        Assert.InRange(
            supply.FuelFraction,
            0.0,
            1.0);
        Assert.InRange(
            supply.AmmunitionFraction,
            0.0,
            1.0);
    }

    private static PresentationSnapshotBuffer RegisterExtraction(
        VerticalSliceScenario scenario,
        out PresentationInteractionState interaction,
        out PlayerCommandGateway gateway)
    {
        var buffer =
            new PresentationSnapshotBuffer();
        interaction =
            new PresentationInteractionState();
        gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity);

        scenario.Simulation.RegisterTickObserver(
            gateway);
        scenario.Simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer,
                new PresentationExtractionContext(
                    scenario,
                    LocalPlayer,
                    interaction,
                    gateway)));

        return buffer;
    }

    private static VerticalSliceScenario CreateScenario(
        ulong seed)
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        return VerticalSliceScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
}
