using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SplitBasinRuntimeTests
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void NormalPaidIndustryConveyorsPhysicalFuelAndActiveRecoveryWorkOnEachStart(ulong owner)
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 4133,
            enableDebugCapture: true, composition: ScenarioRegistry.Default.Resolve(SplitBasinBattlefield.CompositionKey)) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
        using var runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var player = new PlayerId(owner);
        var core = runtime.GetBase(player);
        Assert.Equal(SkirmishStartingStock.Standard, settings.Scenario.StartingStock);
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands, runtime.BattlefieldRuntime.MatchStateEntity);
        runtime.Simulation.RegisterTickObserver(gateway);
        Vector3 home = runtime.Simulation.Entities.GetComponent<WorldTransform>(core.CommandCore).Position;
        var buildings = new Dictionary<BuildingId, EntityId>();
        float X(float x) => owner == 1 ? x : 2048 - x;
        for (int i = 0; i < 3; i++) Build(BuildingIds.PowerPlant, new(X(140 + i * 80), 0, 840));
        foreach (var raw in new[] { ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Volatiles })
        {
            var deposit = runtime.Battlefield.Resources.Where(x => !x.Contested && x.ResourceId == raw)
                .OrderBy(x => Vector3.DistanceSquared(x.Center, home)).First();
            var extractor = Build(BuildingIds.Extractor, deposit.Center);
            Assert.True(runtime.Simulation.Entities.HasComponent<ResourceExtractor>(extractor));
        }
        foreach (var id in new[] { BuildingIds.Smelter, BuildingIds.ElectronicsPlant, BuildingIds.AmmunitionPlant, BuildingIds.SupplyDepot })
            buildings[id] = id == BuildingIds.AmmunitionPlant ? Build(id, new(owner == 1 ? 240 : 1808, 0, 983.5f), BuildingOrientation.West)
                : id == BuildingIds.SupplyDepot ? Build(id, new(owner == 1 ? 240 : 1808, 0, 1064.5f), BuildingOrientation.West)
                : id == BuildingIds.Smelter ? Build(id, new(X(520), 0, 970))
                : Build(id, new(X(560), 0, 1140));
        EntityId smelter = buildings[BuildingIds.Smelter];
        EntityId electronics = buildings[BuildingIds.ElectronicsPlant];
        EntityId ammo = buildings[BuildingIds.AmmunitionPlant];
        EntityId depot = buildings[BuildingIds.SupplyDepot];
        // A paid conveyor exports real manufactured Ammunition; Fuel remains physical truck cargo.
        var ports = ConveyorConstructionService.CapturePorts(runtime.Simulation.Entities, player);
        var output = Assert.Single(ports, x => x.Building == ammo && !x.Input);
        var inputPort = Assert.Single(ports, x => x.Building == depot && x.Input);
        var conveyorRequest = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, inputPort.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, true, SourcePortBuilding: ammo, DestinationPortBuilding: depot);
        var plan = runtime.Services.Conveyors.Preview(runtime.Simulation.Entities, player, conveyorRequest, core.CommandCore);
        Assert.True(plan.IsValid, plan.Failure.ToString());
        double steelBefore = runtime.Inventories.GetQuantity(core.StartingInventory, ResourceIds.Steel);
        var conveyor = new ConveyorConstructionCommand(runtime.Services.Conveyors, player, conveyorRequest, core.CommandCore, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(conveyor, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick();
        Assert.True(conveyor.Accepted, conveyor.Result.ToString());
        Assert.Equal(steelBefore - plan.Steel, runtime.Inventories.GetQuantity(core.StartingInventory, ResourceIds.Steel), 8);
        foreach (var raw in new[] { ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Steel, ResourceIds.Electronics, ResourceIds.Ammunition })
        {
            double retain = runtime.Inventories.GetQuantity(core.StartingInventory, raw);
            Policy(core.CommandCore, raw, retain);
        }
        foreach (var tuple in new[] { (smelter, RecipeIds.Steel), (electronics, RecipeIds.Electronics), (ammo, RecipeIds.Ammunition) })
        {
            var recipe = runtime.Services.ProductionRecipes[tuple.Item2];
            foreach (var input in recipe.Inputs) Policy(tuple.Item1, input.ResourceId, input.Quantity * 4);
            Action(PlayerActionRequest.QueueProduction(tuple.Item1, tuple.Item2, ProductionPriority.Normal,
                ProductionRequestMode.DesiredStock, recipe.Outputs[0].ResourceId, 40));
        }

        Policy(depot, ResourceIds.Fuel, 60);
        Policy(depot, ResourceIds.Ammunition, 40);
        var storage = runtime.Simulation.Entities.GetComponent<InventoryStorage>(depot).InventoryId;
        Until(() => runtime.Inventories.GetQuantity(storage, ResourceIds.Fuel) >= 60 &&
            runtime.Inventories.GetQuantity(storage, ResourceIds.Ammunition) >= 10, 16000);
        foreach (var factory in new[] { smelter, electronics, ammo })
            Assert.True(runtime.Simulation.Entities.GetComponent<ProductionFacility>(factory).CompletedCycles > 0);
        Assert.Contains(runtime.Progress.Capture(player, runtime.Simulation.CurrentTick), x => x.Kind == GameplayProgressKind.Extraction);
        Assert.True(runtime.AutomatedDistribution.Metrics.CompletedRequestCount > 0);
        Assert.True(runtime.Simulation.Entities.GetComponentCount<ConveyorFlowState>() > 0);
        AssertRoundTrip(runtime);

        var bridge = runtime.BattlefieldRuntime.CrossingEntities["crossing.basin_bridge"];
        var edge = runtime.BattlefieldRuntime.RoadEdges["basin.bridge"];
        var disable = new DisableStrategicInfrastructureCommand(bridge, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(disable, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick(); Assert.True(disable.Accepted);
        Assert.True(runtime.Logistics.TryGetEdge(edge, out var disabled)); Assert.False(disabled.Enabled);
        var restore = new RestoreStrategicInfrastructureCommand(bridge, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(restore, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick(); Assert.True(restore.Accepted);
        Until(() => runtime.Simulation.Entities.GetComponent<StrategicInfrastructureState>(bridge).State == StrategicInfrastructureOperationalState.Operational, 100);
        Assert.True(runtime.Logistics.TryGetEdge(edge, out var recovered)); Assert.True(recovered.Enabled);
        // Existing host infrastructure commands deliberately remain outside the player replay codec.
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.CaptureSave(runtime));

        EntityId Build(BuildingId id, Vector3? requested = null, BuildingOrientation? fixedOrientation = null)
        {
            Vector3 location = default;
            BuildingOrientation orientation = default;
            bool found = false;
            for (int radius = requested.HasValue ? 0 : 2; radius <= (requested.HasValue ? 0 : 8) && !found; radius++)
            for (int z = -radius; z <= radius && !found; z++)
            for (int x = -radius; x <= radius && !found; x++)
            {
                if (radius > 0 && Math.Abs(x) != radius && Math.Abs(z) != radius) continue;
                var candidate = requested ?? home + new Vector3(x * 40, 0, z * 40);
                orientation = fixedOrientation ?? runtime.Services.BuildingDefinitions[id].LoadingDock?.OrientationToward(home - candidate) ?? BuildingOrientation.North;
                var preview = runtime.Services.BuildingPlacement.CreatePreview(runtime.Simulation.Entities, player, id, candidate, orientation);
                if (preview.IsValid) { location = preview.GroundPosition; found = true; }
            }
            Assert.True(found, $"No {id} placement for {player}");
            Assert.True(gateway.SubmitBuild(player, id, location, orientation, core.CommandCore, runtime.Simulation.CurrentTick).Accepted);
            runtime.Simulation.AdvanceOneTick(); Drain();
            runtime.Simulation.RunTicks(runtime.Services.BuildingDefinitions[id].ConstructionTicks + 1, TestContext.Current.CancellationToken);
            foreach (var entity in runtime.Simulation.Entities.Query<CompletedBuilding>())
            {
                var building = runtime.Simulation.Entities.GetComponent<CompletedBuilding>(entity);
                if (building.Owner == player && building.BuildingId == id && Vector3.DistanceSquared(runtime.Simulation.Entities.GetComponent<WorldTransform>(entity).Position, location) < 400) return entity;
            }
            throw new InvalidOperationException("Paid building did not complete.");
        }
        void Action(PlayerActionRequest request)
        {
            Assert.True(PlayerActionRequestDispatcher.TryDispatch(request, player, gateway, runtime.Simulation.CurrentTick, out var receipt));
            Assert.True(receipt.Accepted); runtime.Simulation.AdvanceOneTick(); Drain();
        }
        void Drain()
        {
            Assert.True(gateway.Results.TryRead(out var result));
            Assert.Equal(PlayerCommandFeedbackState.Accepted, result.State);
            while (gateway.Results.TryRead(out result)) Assert.Equal(PlayerCommandFeedbackState.Accepted, result.State);
        }
        void Policy(EntityId target, ResourceId resource, double amount) => Action(PlayerActionRequest.SetStockPolicy(target, resource, amount, amount, amount, LogisticsStockPriority.High, true));
        void Until(Func<bool> predicate, int limit)
        {
            for (int i = 0; i < limit && !predicate(); i++) runtime.Simulation.AdvanceOneTick();
            if (!predicate())
            {
                var details = new List<string>();
                foreach (var entity in runtime.Simulation.Entities.Query<ProductionFacility>())
                {
                    var p = runtime.Simulation.Entities.GetComponent<ProductionFacility>(entity);
                    details.Add($"{entity}: block={p.BlockReason}, cycles={p.CompletedCycles}; output={string.Join(';', runtime.Services.Resources.Definitions.Select(r => r.Key + ':' + runtime.Inventories.GetQuantity(p.OutputInventory, r.Id)))}; input={string.Join(';', runtime.Services.Resources.Definitions.Select(r => r.Key + ':' + runtime.Inventories.GetQuantity(p.InputInventory, r.Id)))}");
                }
                foreach (var e in runtime.Simulation.Entities.Query<ResourceExtractor>())
                    details.Add("Extractor: " + System.Text.Json.JsonSerializer.Serialize(runtime.Simulation.Entities.GetComponent<ResourceExtractor>(e)) + " power=" + System.Text.Json.JsonSerializer.Serialize(runtime.Simulation.Entities.GetComponent<PowerConsumer>(e)));
                details.Add(System.Text.Json.JsonSerializer.Serialize(runtime.AutomatedDistribution.LastDebugSnapshot));
                Assert.Fail($"Opening {player} stalled at tick {runtime.Simulation.CurrentTick}; Fuel={runtime.Inventories.GetQuantity(storage, ResourceIds.Fuel)}; Ammo={runtime.Inventories.GetQuantity(storage, ResourceIds.Ammunition)}; {string.Join(Environment.NewLine, details)}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingOpponentsUseBothStartsAndRepeatExactlyWithSwappedConfigurations(bool swapped)
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026,
            enableDebugCapture: true, composition: ScenarioRegistry.Default.Resolve(SplitBasinBattlefield.CompositionKey));
        if (swapped) settings = settings with { Scenario = settings.Scenario with
        {
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>
            { [1] = settings.Scenario.OpponentConfigurations[2], [2] = settings.Scenario.OpponentConfigurations[1] }
        }};
        using var first = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        using var second = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        first.Simulation.RunTicks(2000, TestContext.Current.CancellationToken);
        second.Simulation.RunTicks(2000, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(first).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(second).ComputeSha256());
        foreach (ulong owner in new ulong[] { 1, 2 })
        {
            Assert.True(first.CountBuildings(new PlayerId(owner), BuildingIds.PowerPlant) > 0);
            Assert.True(first.CountBuildings(new PlayerId(owner), BuildingIds.Extractor) > 0);
            Assert.True(first.CountBuildings(new PlayerId(owner), BuildingIds.Smelter) > 0);
        }
    }

    private static void AssertRoundTrip(MatchRuntime runtime)
    {
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)));
        using var restored = MatchPersistenceService.Restore(save);
        using var playback = MatchPersistenceService.PlayReplay(replay);
        Assert.Equal(ScenarioRegistry.SplitBasinDescriptor.Identity, restored.RuntimeSettings.Composition.Identity);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        foreach (ulong ticks in new ulong[] { 1, 7, 24 })
        {
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            Assert.Equal(MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        }
        foreach (int schema in new[] { 6, 7 })
        {
            var missing = save with { SchemaVersion = schema, Configuration = save.Configuration with { Identity = null } };
            Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
                Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(missing))).Reason);
        }
        var wrong = save with { Configuration = save.Configuration with { Identity = ScenarioRegistry.CentralDivideDescriptor.Identity } };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(wrong)).Reason);
    }
}
