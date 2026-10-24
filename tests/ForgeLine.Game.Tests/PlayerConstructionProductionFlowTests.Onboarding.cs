using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed partial class PlayerConstructionProductionFlowTests
{
    [Fact]
    public void StandardOpeningRecordsRealProductionDepotLoadingAndMobileResupply()
    {
        using var scenario = CreateScenario(4133);
        var player = new PlayerId(1);
        var meter = new GameplayTelemetryCollector(scenario);
        scenario.Simulation.RegisterTickObserver(new JourneyTelemetryObserver(meter));
        var core = scenario.GetBase(player);
        Assert.Equal(2, FindOwnedUnits(scenario, UnitIds.CargoTruck).Length);
        Assert.Empty(FindOwnedUnits(scenario, UnitIds.SupplyTruck));
        Assert.Equal(0, scenario.CountBuildings(player, BuildingIds.VehicleFactory));
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);
        foreach (var id in new[] { BuildingIds.PowerPlant, BuildingIds.PowerPlant })
            BuildPlayerBuilding(scenario, gateway, id, FindOpenPlacement(scenario, id));
        Vector3 oreSite = FindOpeningOreSite(scenario);
        BuildPlayerBuilding(scenario, gateway, BuildingIds.Extractor, oreSite);
        foreach (var id in new[] { BuildingIds.Smelter, BuildingIds.VehicleFactory, BuildingIds.SupplyDepot })
            BuildPlayerBuilding(scenario, gateway, id, FindOpenPlacement(scenario, id));
        scenario.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        var smelter = FindCompletedBuilding(scenario, BuildingIds.Smelter);
        var factory = FindCompletedBuilding(scenario, BuildingIds.VehicleFactory);
        var depot = FindCompletedBuilding(scenario, BuildingIds.SupplyDepot);
        Assert.Contains(scenario.Progress.Capture(player, scenario.Simulation.CurrentTick), x => x.Kind == GameplayProgressKind.Extraction);
        double retainedSteel = scenario.Inventories.GetQuantity(core.StartingInventory, ResourceIds.Steel);
        SubmitStockPolicy(scenario, gateway, core.CommandCore, ResourceIds.Steel, retainedSteel, retainedSteel, retainedSteel);
        var scoutDefinition = scenario.Services.UnitDefinitions[UnitIds.ScoutVehicle];
        var truckDefinition = scenario.Services.UnitDefinitions[UnitIds.SupplyTruck];
        var costs = scoutDefinition.Costs.Concat(truckDefinition.Costs).GroupBy(x => x.ResourceId)
            .Select(x => (Resource: x.Key, Quantity: x.Sum(c => c.Quantity))).ToArray();
        double steel = costs.Single(x => x.Resource == ResourceIds.Steel).Quantity;
        var recipe = scenario.Services.ProductionRecipes[RecipeIds.Steel];
        double cycles = Math.Ceiling(steel / recipe.Outputs.Single(x => x.ResourceId == ResourceIds.Steel).Quantity);
        foreach (var input in recipe.Inputs)
            SubmitStockPolicy(scenario, gateway, smelter, input.ResourceId, input.Quantity * cycles, input.Quantity * cycles, input.Quantity * cycles);
        Assert.Equal(PlayerCommandFeedbackState.Accepted, DispatchAction(scenario, gateway,
            PlayerActionRequest.QueueProduction(smelter, RecipeIds.Steel, ProductionPriority.High,
                ProductionRequestMode.DesiredStock, ResourceIds.Steel, steel)).State);
        QueueUnit(scenario, gateway, factory, UnitIds.ScoutVehicle);
        scenario.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        foreach (var cost in costs) SubmitStockPolicy(scenario, gateway, factory, cost.Resource, 1, cost.Quantity, cost.Quantity);
        RunUntil(scenario, () => FindOwnedUnits(scenario, UnitIds.ScoutVehicle).Length == 1, 12_000);
        foreach (var cost in costs) SubmitStockPolicy(scenario, gateway, factory, cost.Resource, 0, cost.Quantity, cost.Quantity);
        QueueUnit(scenario, gateway, factory, UnitIds.SupplyTruck);
        RunUntil(scenario, () => FindOwnedUnits(scenario, UnitIds.SupplyTruck).Length == 1, 8_000);
        var scout = Assert.Single(FindOwnedUnits(scenario, UnitIds.ScoutVehicle));
        var truck = Assert.Single(FindOwnedUnits(scenario, UnitIds.SupplyTruck));
        var cargo = scenario.Simulation.Entities.GetComponent<SupplyTruck>(truck);
        foreach (var item in new[] { (ResourceIds.Fuel, cargo.FuelTarget), (ResourceIds.Ammunition, cargo.AmmunitionTarget) })
            SubmitStockPolicy(scenario, gateway, depot, item.Item1, item.Item2, item.Item2, item.Item2);
        var storage = scenario.Simulation.Entities.GetComponent<InventoryStorage>(depot);
        RunUntil(scenario, () => scenario.Inventories.GetQuantity(storage.InventoryId, ResourceIds.Fuel) >= cargo.FuelTarget &&
            scenario.Inventories.GetQuantity(storage.InventoryId, ResourceIds.Ammunition) >= cargo.AmmunitionTarget, 6_000);
        var truckTransform = scenario.Simulation.Entities.GetComponent<WorldTransform>(truck);
        var depotTransform = scenario.Simulation.Entities.GetComponent<WorldTransform>(depot);
        Assert.True(VehicleDockPose.TryCreate(scenario.Simulation.Entities.GetComponent<BuildingLoadingDock>(depot), depotTransform, truckTransform, out var pose));
        var forward = new Vector3(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
        var outward = new Vector3(-forward.Z, 0, forward.X);
        if (Vector3.Dot(outward, pose.Service - depotTransform.Position) < 0) outward = -outward;
        Vector3 staging = pose.Service + outward * 12;
        Move(truck, staging);
        Assert.True(gateway.SubmitResupply(player, truck, scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick(); while (gateway.Results.TryRead(out _)) { }
        RunUntil(scenario, () => scenario.Progress.Capture(player, scenario.Simulation.CurrentTick)
            .Any(x => x.Kind == GameplayProgressKind.SupplyLoadCompleted && x.Source == depot && x.Target == truck), 2_000);
        Vector3 field = staging + outward * 160;
        Move(scout, field);
        Move(truck, field + forward * 8);
        Assert.True(gateway.SubmitResupply(player, scout, scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick(); while (gateway.Results.TryRead(out _)) { }
        RunUntil(scenario, () => scenario.Progress.Capture(player, scenario.Simulation.CurrentTick)
            .Any(x => x.Kind == GameplayProgressKind.FieldFuelTransferred && x.Actor == truck && x.Target == scout), 2_000);
        var evidence = scenario.Progress.Capture(player, scenario.Simulation.CurrentTick);
        foreach (var kind in new[] { GameplayProgressKind.Extraction, GameplayProgressKind.OrePolicyApplied,
            GameplayProgressKind.CargoLoaded, GameplayProgressKind.CargoUnloaded, GameplayProgressKind.SteelCompleted,
            GameplayProgressKind.ScoutProduced, GameplayProgressKind.SupplyTruckProduced, GameplayProgressKind.SupplyLoadCompleted,
            GameplayProgressKind.FieldFuelTransferred, GameplayProgressKind.DisruptionResolved })
            Assert.Contains(evidence, x => x.Kind == kind);
        Assert.Contains(evidence, x => x.Kind == GameplayProgressKind.CargoUnloaded && x.Target == smelter && x.Resource == ResourceIds.FerrousOre);
        Assert.All(evidence, x => { Assert.Equal(player, x.Player); Assert.Equal(scenario.Simulation.SessionId, x.SessionId); Assert.True(x.Quantity > 0); });
        Assert.Same(evidence, scenario.Progress.Capture(player, scenario.Simulation.CurrentTick));
        Assert.DoesNotContain(scenario.Progress.Capture(new PlayerId(2), scenario.Simulation.CurrentTick), x => x.Player == player);
        scenario.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        Assert.Contains(scenario.Progress.Capture(player, scenario.Simulation.CurrentTick), x => x.Kind == GameplayProgressKind.SupplyLoadCompleted);

        string? measurementPath = Environment.GetEnvironmentVariable("FORGELINE_GAMEPLAY_JOURNEY_REPORT");
        if (!string.IsNullOrWhiteSpace(measurementPath))
            File.WriteAllText(measurementPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                Profile = "Gameplay", Seed = 4133, Participants = "player:1 human / player:2 human",
                Scenario = scenario.Battlefield.Metadata.Key, Tick = scenario.Simulation.CurrentTick.Value,
                AuthoritativeHash = MatchAuthoritativeSnapshot.Capture(scenario).ComputeSha256(),
                Evidence = scenario.Progress.CaptureFirst(player, scenario.Simulation.CurrentTick),
                Telemetry = meter.Capture()
            }));

        void Move(EntityId unit, Vector3 target)
        {
            Assert.True(gateway.SubmitMovement(player, [unit], target, scenario.Simulation.CurrentTick, FormationTemplate.Compact).Accepted);
            scenario.Simulation.AdvanceOneTick(); while (gateway.Results.TryRead(out _)) { }
            RunUntil(scenario, () => Vector3.DistanceSquared(scenario.Simulation.Entities.GetComponent<WorldTransform>(unit).Position, target) <= 16, 2_000);
        }
    }
    [Fact]
    public void ActualExtractionEvidenceSurvivesSkippedReadsAndSaveReplayWithNewSession()
    {
        using var scenario = CreateScenario(4134);
        var player = new PlayerId(1);
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);
        scenario.Simulation.AdvanceOneTick();
        Assert.Empty(scenario.Progress.Capture(player, scenario.Simulation.CurrentTick));
        BuildPlayerBuilding(scenario, gateway, BuildingIds.PowerPlant, FindOpenPlacement(scenario, BuildingIds.PowerPlant));
        BuildPlayerBuilding(scenario, gateway, BuildingIds.Extractor, FindOpeningOreSite(scenario));
        scenario.Simulation.RunTicks(150, TestContext.Current.CancellationToken);
        var coreEntity = scenario.GetBase(player).CommandCore;
        var research = PlayerTechnologyActionCommand.Start(player, TechnologyIds.IndustrialStandardization,
            coreEntity, coreEntity, scenario.Simulation.CurrentTick, scenario.Services.TechnologyDefinitions);
        scenario.Simulation.SubmitCommand(research, scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();
        Assert.DoesNotContain(scenario.Progress.Capture(player, scenario.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted);
        scenario.Simulation.RunTicks(120, TestContext.Current.CancellationToken);
        var researchProof = Assert.Single(scenario.Progress.CaptureFirst(player, scenario.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted);
        var before = scenario.Progress.Capture(player, scenario.Simulation.CurrentTick);
        var extracted = Assert.Single(before, x => x.Kind == GameplayProgressKind.Extraction);
        scenario.Simulation.RunTicks(50, TestContext.Current.CancellationToken);
        Assert.Equal(extracted, Assert.Single(before, x => x.Kind == GameplayProgressKind.Extraction));
        Assert.Contains(scenario.Progress.Capture(player, scenario.Simulation.CurrentTick), x => x.Kind == GameplayProgressKind.Extraction && x.Identity >= extracted.Identity);
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(scenario)));
        using var restored = MatchPersistenceService.Restore(save);
        Assert.NotEqual(scenario.Simulation.SessionId, restored.Simulation.SessionId);
        var recovered = Assert.Single(restored.Progress.Capture(player, restored.Simulation.CurrentTick), x => x.Kind == GameplayProgressKind.Extraction);
        Assert.Equal(restored.Simulation.SessionId, recovered.SessionId);
        Assert.Equal(extracted.Actor, recovered.Actor);
        Assert.True(recovered.Quantity > 0);
        Assert.Empty(restored.Progress.Capture(new PlayerId(2), restored.Simulation.CurrentTick));
        Assert.Equal(researchProof with { SessionId = restored.Simulation.SessionId },
            Assert.Single(restored.Progress.CaptureFirst(player, restored.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted));
        using var playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(scenario))));
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(scenario).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        Assert.Equal(researchProof with { SessionId = playback.Simulation.SessionId },
            Assert.Single(playback.Progress.CaptureFirst(player, playback.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted));
    }

    private sealed class JourneyTelemetryObserver(GameplayTelemetryCollector meter) : ForgeLine.Simulation.ISimulationTickObserver
    {
        public void OnTickCompleted(ForgeLine.Simulation.SimulationContext context) => meter.Observe();
    }

    private static Vector3 FindOpeningOreSite(MatchRuntime scenario)
    {
        var player = new PlayerId(1);
        var core = scenario.Simulation.Entities.GetComponent<WorldTransform>(scenario.GetBase(player).CommandCore).Position;
        var deposits = new List<EntityId>();
        foreach (var entity in scenario.Simulation.Entities.Query<ResourceDeposit>()) deposits.Add(entity);
        deposits.Sort((a,b) => Vector3.DistanceSquared(scenario.Simulation.Entities.GetComponent<ResourceDeposit>(a).Bounds.Center, core)
            .CompareTo(Vector3.DistanceSquared(scenario.Simulation.Entities.GetComponent<ResourceDeposit>(b).Bounds.Center, core)));
        foreach (var entity in deposits)
        {
            var deposit = scenario.Simulation.Entities.GetComponent<ResourceDeposit>(entity);
            if (deposit.ResourceId != ResourceIds.FerrousOre) continue;
            var preview = scenario.Services.BuildingPlacement.CreatePreview(scenario.Simulation.Entities, player,
                BuildingIds.Extractor, deposit.Bounds.Center, BuildingOrientation.North);
            if (preview.IsValid) return preview.GroundPosition;
        }
        throw new InvalidOperationException("No valid local standard-start Ore site.");
    }

}
