using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
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

        Vector3 rallyPoint =
            new(320.0f, 0.0f, 220.0f);
        scenario.Simulation.Entities.AddComponent(
            facility,
            new UnitProductionRallyPoint(
                rallyPoint,
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
        Assert.Equal(
            rallyPoint,
            units.RallyPoint);

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

        interaction.SetSelection(
            [scenario.West.CommandCore, engineer]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions?.Logistics);
        Assert.Null(snapshot.PlayerActions?.Supply);

        interaction.SetSelection(
            [scenario.East.CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions?.Logistics);
        Assert.Null(snapshot.PlayerActions?.Supply);
    }

    [Fact]
    public void TacticalActionsExposeOwnedStateAndCurrentIdentifiedTargets()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4305);
        var buffer =
            RegisterExtraction(
                scenario,
                out PresentationInteractionState interaction,
                out _);
        EntityId engineer =
            scenario.West.StartingUnits[0];
        EntityId enemy =
            scenario.East.StartingUnits[0];
        WorldTransform engineerTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    engineer);

        scenario.Simulation.Entities.SetComponent(
            enemy,
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    enemy) with
            {
                Position =
                    engineerTransform.Position +
                    new Vector3(20.0f, 0.0f, 0.0f)
            });

        interaction.SetSelection([engineer]);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        PlayerTacticalActionReadModel tactical =
            Assert.IsType<PlayerTacticalActionReadModel>(
                snapshot.PlayerActions?.Tactical);

        Assert.Equal(1, tactical.RequestedSelectionCount);
        Assert.Equal(1, tactical.CombatEligibleCount);
        PlayerTacticalTargetReadModel target =
            Assert.Single(
                tactical.Targets,
                candidate =>
                    candidate.Entity ==
                    enemy);
        Assert.Equal(
            IntelligenceState.Identified,
            target.State);
        Assert.True(
            target.CompatibleUnitCount > 0);
        Assert.Equal(
            IntelligenceContactKey.FromEntity(
                enemy),
            target.ContactKey);

        EntityId artillery =
            scenario.UnitFactory.Create(
                scenario.Services.UnitDefinitions[
                    UnitIds.MobileArtillery],
                engineerTransform.Position +
                    new Vector3(10.0f, 0.0f, 0.0f),
                scenario.West.Player);
        interaction.SetSelection([artillery]);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(out snapshot));
        tactical =
            Assert.IsType<PlayerTacticalActionReadModel>(
                snapshot.PlayerActions?.Tactical);
        PlayerArtilleryActionReadModel artilleryState =
            Assert.Single(tactical.Artillery);

        Assert.Equal(artillery, artilleryState.Entity);
        Assert.True(artilleryState.AmmunitionCapacity > 0.0);
        Assert.InRange(
            artilleryState.AmmunitionQuantity,
            0.0,
            artilleryState.AmmunitionCapacity);
        Assert.Equal(90.0f, artilleryState.MinimumRangeMeters);
        Assert.Equal(700.0f, artilleryState.MaximumRangeMeters);

        interaction.SetSelection(
            [engineer, scenario.East.StartingUnits[1]]);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(out snapshot));
        tactical =
            Assert.IsType<PlayerTacticalActionReadModel>(
                snapshot.PlayerActions?.Tactical);
        Assert.Equal(
            2,
            tactical.RequestedSelectionCount);
        Assert.Single(tactical.SelectedEntities);
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
                scenario.BattlefieldRuntime.MatchStateEntity,
                intelligence:
                    scenario.Intelligence,
                weapons:
                    scenario.Services.Weapons,
                artilleryWeapons:
                    scenario.Services.ArtilleryWeapons);

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
