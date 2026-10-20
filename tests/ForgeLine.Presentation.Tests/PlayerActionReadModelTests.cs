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
    public void CargoOrderShowsCatalogMaterialAndTripQuantityWithOwnedEndpoints()
    {
        using var scenario = CreateScenario(4311);
        var buffer = RegisterExtraction(scenario,out var interaction,out _);
        scenario.Simulation.AdvanceOneTick();
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(LocalPlayer);
        var core = entities.GetComponent<WorldTransform>(side.CommandCore);
        var destination = entities.CreateEntity();
        var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(500));
        entities.AddComponent(destination,new ControllableEntity(LocalPlayer,ControllableEntityCategory.Building));
        entities.AddComponent(destination,new WorldTransform(core.Position + new Vector3(100,0,0),Quaternion.Identity,Vector3.One));
        entities.AddComponent(destination,new InventoryStorage(inventory));
        var target = scenario.Logistics.AddNode(destination,core.Position + new Vector3(100,0,0),
            ForgeLine.Logistics.LogisticsNodeKind.StorageDepot,ForgeLine.Logistics.LogisticsNodeCapabilities.CargoDestination);
        Assert.True(scenario.Logistics.TryGetNodeForEntity(side.CommandCore,out var source));
        scenario.Logistics.AddEdge(source,target,ForgeLine.Logistics.LogisticsTransportMode.GroundRoad,100,1,100);
        var truck = side.StartingUnits.First(unit => entities.HasComponent<CargoTransport>(unit));
        Assert.True(scenario.CargoTransport.TryAssignOrder(entities,truck,
            new CargoTransportOrder(source,target,ResourceIds.Steel,50,scenario.Simulation.CurrentTick),scenario.Simulation.CurrentTick));
        interaction.SetSelection([truck]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var cargo = Assert.IsType<PlayerCargoStatusReadModel>(snapshot.PlayerActions!.Logistics!.Cargo);
        Assert.Equal(ResourceIds.Steel,cargo.Resource);
        Assert.Equal(scenario.Services.Resources[ResourceIds.Steel].DisplayName,cargo.ResourceName);
        Assert.Equal(50,cargo.RequestedQuantity);
        Assert.Equal(side.CommandCore,cargo.Source);
        Assert.Equal(destination,cargo.Destination);
        Assert.Equal(scenario.Services.BuildingDefinitions[BuildingIds.CommandCore].DisplayName,cargo.SourceName);
    }

    [Fact]
    public void SupplyCargoAndDriveTankAreSeparateAndSupplyIsNotRegionalCargo()
    {
        using var scenario = CreateScenario(4310);
        var side = scenario.GetBase(LocalPlayer);
        var core = scenario.Simulation.Entities.GetComponent<WorldTransform>(side.CommandCore);
        var truck = scenario.UnitFactory.Create(scenario.Services.UnitDefinitions[UnitIds.SupplyTruck],core.Position + new Vector3(45,0,45),LocalPlayer);
        var supplyCargo = scenario.Simulation.Entities.GetComponent<SupplyTruck>(truck);
        var drive = scenario.Simulation.Entities.GetComponent<UnitFuelState>(truck);
        Assert.NotEqual(supplyCargo.InventoryId,drive.InventoryId);
        var buffer = RegisterExtraction(scenario,out var interaction,out _);
        interaction.SetSelection([truck]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var supply = Assert.IsType<PlayerSupplyActionReadModel>(snapshot.PlayerActions?.Supply);
        Assert.True(supply.IsSupplyTruck);
        Assert.Equal(scenario.Inventories.GetQuantity(supplyCargo.InventoryId,ResourceIds.Fuel),supply.CargoFuel);
        Assert.Equal(scenario.Inventories.GetQuantity(supplyCargo.InventoryId,ResourceIds.Ammunition),supply.CargoAmmunition);
        Assert.Equal(scenario.Inventories.GetQuantity(drive.InventoryId,ResourceIds.Fuel),supply.DriveFuelQuantity);
        interaction.SetSelection([side.CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out snapshot));
        Assert.Equal(side.StartingUnits.Count(unit => scenario.Simulation.Entities.HasComponent<CargoTransport>(unit)),snapshot.PlayerActions!.Logistics!.CargoFleet.Total);
        var coreSupply = Assert.IsType<PlayerSupplyActionReadModel>(snapshot.PlayerActions.Supply);
        Assert.True(coreSupply.IsStaticProvider);
        Assert.True(coreSupply.HasLocalProviderInventory);
        Assert.Equal(scenario.Inventories.GetQuantity(side.StartingInventory,ResourceIds.Fuel),coreSupply.LocalProviderFuel);
        Assert.Equal(scenario.Inventories.GetQuantity(side.StartingInventory,ResourceIds.Ammunition),coreSupply.LocalProviderAmmunition);
    }

    [Fact]
    public void ConstructionActionsMirrorCatalogAndOwnCapturedInventoryValues()
    {
        using MatchRuntime scenario =
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
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                ResourceIds.FerrousOre,
                1.0).Succeeded);

        Assert.Equal(
            captured,
            ferrous.AvailableQuantity);
        Assert.NotEqual(
            captured,
            scenario.Inventories.GetQuantity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                ResourceIds.FerrousOre));

        interaction.SetSelection(
            [scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore]);
    }

    [Fact]
    public void ProductionActionsUseSelectedFacilityInventoryAndRejectMixedOrForeignSelection()
    {
        using MatchRuntime scenario =
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
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
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
            [facility, scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore]);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitProductionActionsCoverSupportedCatalogAndExposeRealCosts(bool changedCatalog)
    {
        using MatchRuntime scenario =
            CreateScenario(4303, changedCatalog);
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

        var logistics = Assert.IsType<PlayerLogisticsActionReadModel>(snapshot.PlayerActions?.Logistics);
        var scout = Assert.Single(logistics.Requirements, r => r.Kind == 2 && r.Id == UnitIds.ScoutVehicle.Value);
        var proposal = ProductionStockProposal.Create(logistics, scout, 1);
        Assert.True(proposal.FitsCapacity);
        foreach (var cost in units.Units.Single(u => u.UnitId == UnitIds.ScoutVehicle).Costs)
            Assert.Equal(cost.RequiredQuantity, Assert.Single(proposal.Thresholds, t => t.Resource == cost.ResourceId).Target);
        Assert.Contains(proposal.Thresholds, t => t.Resource == ResourceIds.Steel && t.Target == (changedCatalog ? 91 : 70));
        Assert.Contains(proposal.Thresholds, t => t.Resource == ResourceIds.Electronics && t.Target == 24);
        Assert.Contains(proposal.Thresholds, t => t.Resource == ResourceIds.Fuel && t.Target == 60);
        Assert.Contains(proposal.Thresholds, t => t.Resource == ResourceIds.Ammunition && t.Target == 40);
        Assert.All(logistics.Policies, p => Assert.Equal(10, p.DesiredTarget));
    }

    [Fact]
    public void ActiveDistributionRequestsAreCopiedOnlyForTheCurrentOwnedSelection()
    {
        using MatchRuntime scenario = CreateScenario(5304);
        var buffer = RegisterExtraction(scenario, out PresentationInteractionState interaction, out _);
        var side = scenario.GetBase(new ForgeLine.Game.PlayerId(1));
        double stock = scenario.Inventories.GetQuantity(side.StartingInventory, ResourceIds.Fuel);
        EntityId policyEntity = scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(policyEntity,
            new LogisticsStockPolicy(side.CommandCore, ResourceIds.Fuel,
                stock + 50, stock + 100, stock + 150));

        interaction.SetSelection([side.CommandCore]);
        for (int i = 0; i < 3; i++) scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out PresentationSnapshot snapshot));

        var logistics = Assert.IsType<PlayerLogisticsActionReadModel>(snapshot.PlayerActions?.Logistics);
        var observed = scenario.AutomatedDistribution.LastDebugSnapshot;
        Assert.Equal(snapshot.Tick, observed.Tick);
        var live = observed.Requests.Where(r => r.PolicyEntity == policyEntity &&
            r.State != LogisticsTransportRequestState.Completed).ToArray();
        Assert.NotEmpty(live);
        Assert.Equal(live.Length, logistics.TotalOrderCount);
        Assert.Equal(live.Length, logistics.Orders.Count);
        foreach (var order in logistics.Orders)
        {
            var authoritative = Assert.Single(live, r => r.RequestId == order.RequestId);
            Assert.Equal(authoritative.RequestedQuantity, order.RequestedQuantity);
            Assert.Equal(authoritative.State, order.State);
            Assert.Equal(ResourceIds.Fuel, order.ResourceId);
            Assert.Equal(side.CommandCore, order.Destination);
        }

        interaction.SetSelection([scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions?.Logistics);
    }

    [Fact]
    public void LogisticsAndSupplyActionsExposeOwnedAuthoritativeState()
    {
        using MatchRuntime scenario =
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
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                ResourceIds.Fuel,
                50.0,
                100.0,
                150.0,
                LogisticsStockPriority.High));

        interaction.SetSelection(
            [scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore]);
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
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[1];
        interaction.SetSelection([cargoTruck]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.NotNull(
            snapshot.PlayerActions?.Logistics?.Cargo);
        Assert.Equal(snapshot.PlayerExperience!.Value.Selection.Work,snapshot.PlayerActions!.Logistics!.Service);


        EntityId engineer =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        scenario.Simulation.Entities.SetComponent(
            engineer,
            new UnitSupplyPriority(
                BattlefieldSupplyPriority.Critical));

        WorldTransform commandCoreTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);
        WorldTransform engineerTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    engineer);
        scenario.Simulation.Entities.SetComponent(
            engineer,
            engineerTransform with
            {
                Position =
                    commandCoreTransform.Position +
                    new Vector3(5.0f, 0.0f, 0.0f)
            });

        HealthState engineerHealth =
            scenario.Simulation.Entities
                .GetComponent<HealthState>(
                    engineer);
        scenario.Simulation.Entities.SetComponent(
            engineer,
            new HealthState(
                Math.Max(
                    1.0,
                    engineerHealth.Maximum - 20.0),
                engineerHealth.Maximum));
        scenario.Simulation.Entities.SetComponent(
            engineer,
            new SuppressionState(
                0.5,
                SuppressionLevel.Suppressed,
                scenario.Simulation.CurrentTick,
                scenario.Simulation.CurrentTick));
        scenario.Simulation.Entities.AddComponent(
            engineer,
            new RetreatRecoveryState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                RetreatRecoveryReason.RepairAndSupply,
                commandCoreTransform.Position,
                scenario.Simulation.CurrentTick));

        interaction.SetSelection([engineer]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));

        PlayerSupplyActionReadModel supply =
            Assert.IsType<PlayerSupplyActionReadModel>(
                snapshot.PlayerActions?.Supply);
        Assert.Equal(engineer, supply.Entity);
        Assert.Equal(
            BattlefieldSupplyPriority.Critical,
            supply.Priority);
        Assert.InRange(
            supply.FuelFraction,
            0.0,
            1.0);
        Assert.InRange(
            supply.AmmunitionFraction,
            0.0,
            1.0);

        PlayerTacticalActionReadModel tactical =
            Assert.IsType<PlayerTacticalActionReadModel>(
                snapshot.PlayerActions?.Tactical);
        Assert.Equal(1, tactical.SuppressedCount);
        Assert.Equal(0, tactical.PinnedCount);
        Assert.Equal(1, tactical.RepairingCount);
        Assert.Equal(
            RetreatRecoveryReason.RepairAndSupply,
            tactical.RetreatReason);
        Assert.Equal(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
            tactical.RetreatProvider);

        interaction.SetSelection(
            [scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore, engineer]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions?.Logistics);
        Assert.Null(snapshot.PlayerActions?.Supply);

        interaction.SetSelection(
            [scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            buffer.TryReadLatest(out snapshot));
        Assert.Null(snapshot.PlayerActions?.Logistics);
        Assert.Null(snapshot.PlayerActions?.Supply);
    }

    [Fact]
    public void TacticalActionsExposeOwnedStateAndCurrentIdentifiedTargets()
    {
        using MatchRuntime scenario =
            CreateScenario(4305);
        var buffer =
            RegisterExtraction(
                scenario,
                out PresentationInteractionState interaction,
                out _);
        EntityId engineer =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId enemy =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];
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
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
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
            [engineer, scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[1]]);
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


    [Fact]
    public void TechnologyActionsExposeImmutableAuthoritativeProgressionState()
    {
        using MatchRuntime scenario =
            CreateScenario(4306);
        EntityId generator = scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(generator, new PowerGenerator(40.0));
        scenario.Simulation.Entities.AddComponent(generator,
            scenario.Simulation.Entities.GetComponent<PowerNetworkMembership>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore));
        Assert.True(
            scenario.Inventories.Add(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                ResourceIds.Steel,
                200.0).Succeeded);
        Assert.True(
            scenario.Inventories.Add(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                ResourceIds.Electronics,
                100.0).Succeeded);

        var buffer =
            RegisterExtraction(
                scenario,
                out _,
                out _);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));
        PlayerTechnologyActionReadModel industrial =
            Assert.Single(
                snapshot.PlayerActions!.Technology,
                technology =>
                    technology.TechnologyId ==
                    TechnologyIds.IndustrialStandardization);

        Assert.Equal(
            TechnologyDomain.Industry,
            industrial.Domain);
        Assert.Equal(
            TechnologyPhase.IndustrialFoundation,
            industrial.Phase);
        Assert.Equal(
            BuildingIds.CommandCore,
            industrial.RequiredFacility);
        Assert.Equal(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
            industrial.Facility);
        Assert.Equal(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
            industrial.SourceInventory);
        Assert.Equal(
            PlayerTechnologyState.Available,
            industrial.State);
        PlayerActionResourceAmount capturedSteel =
            Assert.Single(
                industrial.Costs,
                cost =>
                    cost.ResourceId ==
                    ResourceIds.Steel);
        double capturedQuantity =
            capturedSteel.AvailableQuantity;

        Assert.True(
            scenario.Inventories.Remove(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                ResourceIds.Steel,
                1.0).Succeeded);
        Assert.Equal(
            capturedQuantity,
            capturedSteel.AvailableQuantity);

        PlayerTechnologyActionCommand command =
            PlayerTechnologyActionCommand.Start(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                TechnologyIds.IndustrialStandardization,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                scenario.Simulation.CurrentTick,
                scenario.Services.TechnologyDefinitions);
        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value));
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot researchingSnapshot));
        PlayerTechnologyActionReadModel researching =
            Assert.Single(
                researchingSnapshot.PlayerActions!.Technology,
                technology =>
                    technology.TechnologyId ==
                    TechnologyIds.IndustrialStandardization);

        Assert.Equal(
            PlayerTechnologyState.Researching,
            researching.State);
        Assert.True(
            researching.Progress >
            0.0);
        Assert.True(
            researching.ActiveRequest.IsValid);
        Assert.Equal(
            PlayerTechnologyState.Available,
            industrial.State);
    }

    private static PresentationSnapshotBuffer RegisterExtraction(
        MatchRuntime scenario,
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
                    scenario.Services.ArtilleryWeapons,
                technologies:
                    scenario.Services.TechnologyDefinitions);

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

    private static MatchRuntime CreateScenario(
        ulong seed, bool changedCatalog = false)
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    CentralDivideScenario.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        if (changedCatalog)
        {
            var content = runtime.Composition.Content;
            var units = new UnitDefinitionCatalog(content.Units.Definitions.Select(unit => unit.Id == UnitIds.ScoutVehicle
                ? unit with { Costs = unit.Costs.Select(cost => cost.ResourceId == ResourceIds.Steel ? new UnitResourceCost(cost.ResourceId,91) : cost).ToArray() }
                : unit));
            runtime = runtime with { Composition = runtime.Composition with { Content = content with { Units = units } } };
        }
        return CentralDivideScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
}
