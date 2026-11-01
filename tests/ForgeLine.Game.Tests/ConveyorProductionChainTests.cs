using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorProductionChainTests
{
    // Aggregate accounts include the fixture's 200,000 Steel construction stock.
    // 1e-7 covers accumulated binary rounding; individual transfers remain bounded by their actual tick budget.
    private const double Tolerance = 1e-7;
    private static readonly ResourceId[] Resources =
    [ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Steel,
        ResourceIds.Electronics, ResourceIds.Ammunition, ResourceIds.Fuel, ResourceIds.Volatiles];

    [Theory]
    [InlineData(17UL, 20, false)]
    [InlineData(917UL, 30, false)]
    [InlineData(17UL, 20, true)]
    [InlineData(917UL, 30, true)]
    public void ExtractionAndOriginalRecipesBalanceEveryResourceThroughPaidConveyors(
        ulong seed, int ticksPerSecond, bool electronics)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        ResourceId raw = electronics ? ResourceIds.Silicates : ResourceIds.FerrousOre;
        ResourceId product = electronics ? ResourceIds.Electronics : ResourceIds.Steel;
        RecipeId recipeId = electronics ? RecipeIds.Electronics : RecipeIds.Steel;
        BuildingId building = electronics ? BuildingIds.ElectronicsPlant : BuildingIds.Smelter;
        EntityId extractor = fixture.AddBuilding(BuildingIds.Extractor, new Vector3(24, 0, 112));
        EntityId deposit = fixture.Entities.CreateEntity();
        var faction = new FactionId(checked((uint)fixture.Player.Value));
        fixture.Entities.AddComponent(deposit, new ResourceDeposit(raw,
            new AxisAlignedBounds(new Vector3(16, -2, 104), new Vector3(32, 2, 120)),
            totalQuantity: 40, baseExtractionRatePerSecond: 7, owner: faction));
        fixture.Entities.AddComponent(extractor, new ResourceExtractor(deposit, raw, 7, faction,
            outputInventory: extractor));
        var extraction = new ResourceExtractionSystem(inventories: fixture.Inventories);
        fixture.Simulation.RegisterSystem(extraction);
        EntityId factory = fixture.AddProduction(building, recipeId, new Vector3(55, 0, 112));
        EntityId destination = fixture.AddStorage(new Vector3(87, 0, 112));
        Dictionary<ResourceId, double> initial = Totals(fixture.Inventories);
        double payerBefore = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        EntityId[] ingress = fixture.Connect(extractor, factory);
        EntityId[] egress = fixture.Connect(factory, destination);
        double constructionSteel = payerBefore - fixture.Stock(fixture.Payer, ResourceIds.Steel);
        Assert.True(constructionSteel > 0);
        Queue(fixture, factory, recipeId, 2);

        bool sawRawBuffer = false, sawProductBuffer = false, sawReservation = false;
        for (int tick = 0; tick < 400; tick++)
        {
            fixture.Step();
            ProductionFacility production = fixture.Entities.GetComponent<ProductionFacility>(factory);
            double extracted = 40 - fixture.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity;
            Near(extracted, extraction.Metrics.TotalExtractedQuantity);
            AssertRecipeAccounts(fixture.Inventories, initial, fixture.Recipes[recipeId],
                production.CompletedCycles, constructionSteel, raw, extracted);
            sawRawBuffer |= ingress.Any(entity => fixture.Stock(entity, raw) > 0);
            sawProductBuffer |= egress.Any(entity => fixture.Stock(entity, product) > 0);
            sawReservation |= fixture.Inventories.GetReservedQuantity(production.InputInventory, raw) > 0;
            Assert.All(fixture.Carriers, entity =>
                Assert.InRange(fixture.Inventories.GetTotalQuantity(fixture.Buffer(entity)),
                    0, fixture.Flow(entity).Capacity + Tolerance));
        }

        Assert.Equal(2UL, fixture.Entities.GetComponent<ProductionFacility>(factory).CompletedCycles);
        Near(40, extraction.Metrics.TotalExtractedQuantity);
        Near(20, fixture.Stock(destination, product));
        Near(20, fixture.Stock(factory, raw));
        Near(0, fixture.Stock(extractor, raw));
        Near(0, fixture.Inventories.GetQuantity(fixture.Output(factory), product));
        Assert.All(fixture.Carriers, entity => Near(0, fixture.Inventories.GetTotalQuantity(fixture.Buffer(entity))));
        Assert.True(sawRawBuffer && sawProductBuffer && sawReservation);
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void ManufacturedAmmunitionReachesAFieldUnitThroughADepotAndPhysicalSupplyTruck(
        ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        var navigationWorld = NavigationWorld.Build(fixture.Terrain,
            gridSettings: new NavigationGridSettings { CellSizeMeters = 4 },
            sectorSettings: new NavigationSectorSettings { SectorSizeCells = 4 });
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(navigationWorld));
        fixture.Simulation.RegisterSystem(navigation);
        fixture.Simulation.RegisterSystem(new GroundMovementSystem(fixture.Terrain, fixture.Spatial));
        fixture.Simulation.RegisterSystem(fixture.Cargo);
        var supply = new BattlefieldSupplySystem(fixture.Inventories,
            new VehicleDockingService(fixture.Terrain, fixture.Spatial, navigation));
        fixture.Simulation.RegisterSystem(supply);

        EntityId source = fixture.AddStorage(new Vector3(31, 0, 96));
        EntityId factory = fixture.AddProduction(BuildingIds.AmmunitionPlant, RecipeIds.Ammunition,
            new Vector3(63, 0, 96));
        // Cargo and supply share the authored dock queue; its approach must clear the plant footprint.
        EntityId depot = fixture.AddBuilding(BuildingIds.SupplyDepot, new Vector3(128, 0, 96));
        fixture.Entities.AddComponent(depot, new SupplyDepot(fixture.Inventory(depot), fixture.Player));
        fixture.AddLoadingDock(source);
        fixture.AddLoadingDock(depot);
        double payerBefore = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        EntityId[] ingress = fixture.Connect(source, factory);
        EntityId[] egress = fixture.Connect(factory, depot);
        double constructionSteel = payerBefore - fixture.Stock(fixture.Payer, ResourceIds.Steel);
        fixture.Give(source, ResourceIds.Steel, 24);
        fixture.Give(source, ResourceIds.Electronics, 8);
        fixture.Give(source, ResourceIds.Fuel, 10);
        fixture.AddRoad(source, depot);

        var units = new UnitFactory(fixture.Entities, fixture.Inventories, fixture.Cargo);
        EntityId cargoTruck = units.Create(fixture.Units[UnitIds.CargoTruck], new Vector3(16, 0, 132), fixture.Player);
        EntityId supplyTruck = SupplyTruckFactory.Create(fixture.Entities, fixture.Inventories,
            new Vector3(156, 0, 144), fixture.Player, fixture.Cargo, fuelTarget: 10, ammunitionTarget: 40);
        EntityId recipient = units.Create(fixture.Units[UnitIds.RifleSquad], new Vector3(180, 0, 180), fixture.Player);
        AmmunitionState ammunition = fixture.Entities.GetComponent<AmmunitionState>(recipient);
        Dictionary<ResourceId, double> initial = Totals(fixture.Inventories);
        initial[ResourceIds.Steel] += constructionSteel;
        const double spentAmmunition = 10;
        Assert.True(AmmunitionConsumption.TryConsume(fixture.Inventories, ammunition, spentAmmunition));
        double recipientBefore = fixture.Inventories.GetQuantity(ammunition.InventoryId, ResourceIds.Ammunition);
        double fuelConsumed = 0, truckDistance = 0;
        bool sawIngredients = false, sawAmmunition = false;
        Vector3 priorSupplyPosition = fixture.Entities.GetComponent<WorldTransform>(supplyTruck).Position;
        Queue(fixture, factory, RecipeIds.Ammunition, 4);
        var fuelPolicy = PlayerLogisticsActionCommand.SetStockPolicy(fixture.Player, depot, ResourceIds.Fuel,
            10, 10, 10, LogisticsStockPriority.Normal, true, fixture.Simulation.CurrentTick);
        Submit(fixture.Simulation, fuelPolicy);

        void StepAndBalance()
        {
            var observed = fixture.Find<UnitFuelState>().Select(entity =>
                (Entity: entity, Fuel: fixture.Entities.GetComponent<UnitFuelState>(entity),
                    Available: fixture.Inventories.GetAvailableQuantity(
                        fixture.Entities.GetComponent<UnitFuelState>(entity).InventoryId, ResourceIds.Fuel))).ToArray();
            fixture.Step();
            foreach (var unit in observed)
            {
                Vector3 position = fixture.Entities.GetComponent<WorldTransform>(unit.Entity).Position;
                if (unit.Fuel.HasObservedPosition)
                    fuelConsumed += Math.Min(unit.Available,
                        HorizontalDistance(unit.Fuel.ObservedPosition, position) * unit.Fuel.ConsumptionPerMeter);
            }
            Vector3 supplyPosition = fixture.Entities.GetComponent<WorldTransform>(supplyTruck).Position;
            truckDistance += HorizontalDistance(priorSupplyPosition, supplyPosition);
            priorSupplyPosition = supplyPosition;
            sawIngredients |= ingress.Any(entity => fixture.Stock(entity, ResourceIds.Steel) > 0 ||
                fixture.Stock(entity, ResourceIds.Electronics) > 0);
            sawAmmunition |= egress.Any(entity => fixture.Stock(entity, ResourceIds.Ammunition) > 0);
            AssertRecipeAccounts(fixture.Inventories, initial, fixture.Recipes[RecipeIds.Ammunition],
                fixture.Entities.GetComponent<ProductionFacility>(factory).CompletedCycles,
                constructionSteel, spentAmmunition: spentAmmunition, consumedFuel: fuelConsumed);
            Assert.All(fixture.Carriers, entity => Near(0, fixture.Stock(entity, ResourceIds.Fuel)));
        }

        RunUntil(() => fixture.Stock(depot, ResourceIds.Ammunition) >= 40 - Tolerance &&
            fixture.Cargo.Metrics.CompletedOrderCount == 1, StepAndBalance, 2_000);
        Assert.True(fuelPolicy.Accepted);
        Near(10, fixture.Cargo.Metrics.DeliveredQuantity);
        Near(0, fixture.Cargo.Metrics.LostQuantity);
        Near(10, fixture.Stock(depot, ResourceIds.Fuel));
        Near(recipientBefore, fixture.Inventories.GetQuantity(ammunition.InventoryId, ResourceIds.Ammunition));

        // The original cargo unit also accepts one ammunition unit. Park it clear of the
        // supply route before loading, so this delivery has one intended field recipient.
        Vector3 cargoParking = new(208, 0, 48);
        var parkCargo = new MoveEntitiesCommand(fixture.Player, [cargoTruck], cargoParking,
            fixture.Simulation.CurrentTick);
        Submit(fixture.Simulation, parkCargo);
        RunUntil(() => HorizontalDistance(fixture.Entities.GetComponent<WorldTransform>(cargoTruck).Position,
            cargoParking) < 1.1, StepAndBalance, 1_000);
        Assert.Equal(1, parkCargo.AcceptedTargetCount);

        var load = new ResupplyCommand(fixture.Player, [supplyTruck], fixture.Simulation.CurrentTick);
        Submit(fixture.Simulation, load);
        InventoryId truckInventory = fixture.Entities.GetComponent<SupplyTruck>(supplyTruck).InventoryId;
        RunUntil(() => fixture.Inventories.GetQuantity(truckInventory, ResourceIds.Ammunition) >= 40 - Tolerance &&
            !fixture.Entities.HasComponent<SupplyLoadingOrder>(supplyTruck), StepAndBalance, 2_000, () =>
                $"accepted={load.AcceptedTargetCount}, truck={fixture.Entities.GetComponent<WorldTransform>(supplyTruck)}, " +
                $"movement={fixture.Entities.GetComponent<GroundMovementState>(supplyTruck)}, " +
                $"loading={fixture.Entities.TryGetComponent(supplyTruck, out SupplyLoadingOrder loading)} {loading}, " +
                $"visit={fixture.Entities.TryGetComponent(supplyTruck, out VehicleDockVisit visit)} {visit}, " +
                $"lane={fixture.Entities.TryGetComponent(depot, out VehicleQueueLane lane)} {lane}, " +
                $"holding={fixture.Entities.TryGetComponent(supplyTruck, out VehicleQueueHolding holding)} {holding}, " +
                $"truckAmmo={fixture.Inventories.GetQuantity(truckInventory, ResourceIds.Ammunition)}, " +
                $"truckFuel={fixture.Inventories.GetQuantity(truckInventory, ResourceIds.Fuel)}, " +
                $"depotAmmo={fixture.Stock(depot, ResourceIds.Ammunition)}, depotFuel={fixture.Stock(depot, ResourceIds.Fuel)}, " +
                $"supply={supply.Metrics}");
        Assert.Equal(1, load.AcceptedTargetCount);
        double loaded = fixture.Inventories.GetQuantity(truckInventory, ResourceIds.Ammunition);
        Near(40, loaded);
        Near(0, fixture.Stock(depot, ResourceIds.Ammunition));
        Near(recipientBefore, fixture.Inventories.GetQuantity(ammunition.InventoryId, ResourceIds.Ammunition));

        var move = new MoveEntitiesCommand(fixture.Player, [supplyTruck], new Vector3(180, 0, 176),
            fixture.Simulation.CurrentTick);
        Submit(fixture.Simulation, move);
        RunUntil(() => fixture.Inventories.GetQuantity(ammunition.InventoryId, ResourceIds.Ammunition) >=
            recipientBefore + loaded - Tolerance, StepAndBalance, 2_000);
        Assert.Equal(1, move.AcceptedTargetCount);
        Assert.True(truckDistance > 60);
        Near(0, fixture.Inventories.GetQuantity(truckInventory, ResourceIds.Ammunition));
        Near(40, fixture.Inventories.GetQuantity(ammunition.InventoryId, ResourceIds.Ammunition) - recipientBefore);
        Assert.Equal(4UL, fixture.Entities.GetComponent<ProductionFacility>(factory).CompletedCycles);
        Assert.True(sawIngredients && sawAmmunition);
        Assert.True(fuelConsumed > 0);
        Assert.False(fixture.Entities.HasComponent<CargoTransportOrder>(cargoTruck));
    }

    [Fact]
    public void NormalGameplayStartPaysForAnOreToSteelStorageChain()
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 17) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false)
        };
        using var runtime = CentralDivideScenario.Create(settings, cancellationToken: TestContext.Current.CancellationToken);
        var player = new PlayerId(1);
        EntityId core = runtime.GetBase(player).CommandCore;
        Dictionary<ResourceId, double> initial = Totals(runtime.Inventories);
        var construction = Resources.ToDictionary(resource => resource, _ => 0.0);

        EntityId Build(BuildingId id, Vector3 position, BuildingOrientation orientation = BuildingOrientation.North)
        {
            var command = new BuildCommand(player, id, position, orientation, core, runtime.Simulation.CurrentTick);
            Submit(runtime.Simulation, command);
            runtime.Simulation.AdvanceOneTick();
            Assert.True(runtime.Services.BuildingCommands.TryGetLastResult(player, out var result));
            Assert.True(result.Accepted, result.ToString());
            foreach (BuildingResourceCost cost in runtime.Services.BuildingDefinitions[id].Costs)
                construction[cost.ResourceId] += cost.Quantity;
            RunUntil(() => runtime.Simulation.Entities.HasComponent<CompletedBuilding>(result.CreatedSite),
                runtime.Simulation.AdvanceOneTick, 1_000);
            RemovePolicies(result.CreatedSite);
            return result.CreatedSite;
        }

        void RemovePolicies(EntityId target)
        {
            foreach (EntityId policy in Find<LogisticsStockPolicy>(runtime.Simulation.Entities))
                if (runtime.Simulation.Entities.GetComponent<LogisticsStockPolicy>(policy).TargetEntity == target)
                    Submit(runtime.Simulation, PlayerLogisticsActionCommand.RemoveStockPolicy(player, policy,
                        runtime.Simulation.CurrentTick));
            runtime.Simulation.AdvanceOneTick();
        }

        void Connect(EntityId source, EntityId destination)
        {
            IReadOnlyList<ConveyorBuildingPort> ports = ConveyorConstructionService.CapturePorts(runtime.Simulation.Entities, player);
            var start = Assert.Single(ports, port => port.Building == source && !port.Input &&
                (!port.Adaptable || port.Key == "east.primary"));
            var end = Assert.Single(ports, port => port.Building == destination && port.Input &&
                (!port.Adaptable || port.Key == "west.primary"));
            var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
                start.Position - Vector3.UnitY * 5.8f, end.Position - Vector3.UnitY * 5.8f,
                BuildingOrientation.North, 0, true, SourcePortBuilding: source, DestinationPortBuilding: destination);
            var preview = runtime.Services.Conveyors.Preview(runtime.Simulation.Entities, player, request, core);
            Assert.True(preview.IsValid, preview.Failure.ToString());
            var command = new ConveyorConstructionCommand(runtime.Services.Conveyors, player, request, core,
                runtime.Simulation.CurrentTick);
            Submit(runtime.Simulation, command);
            runtime.Simulation.AdvanceOneTick();
            Assert.True(command.Accepted, command.Result.ToString());
            construction[ResourceIds.Steel] += preview.Steel;
        }

        _ = Build(BuildingIds.PowerPlant, new Vector3(362, 0, 1464));
        EntityId source = Build(BuildingIds.StorageDepot, new Vector3(401, 0, 1600));
        EntityId smelter = Build(BuildingIds.Smelter, new Vector3(433, 0, 1600));
        Connect(source, smelter);
        EntityId destination = Build(BuildingIds.StorageDepot, new Vector3(470, 0, 1623), BuildingOrientation.West);
        Connect(smelter, destination);
        InventoryId sourceInventory = runtime.Simulation.Entities.GetComponent<InventoryStorage>(source).InventoryId;
        InventoryId destinationInventory = runtime.Simulation.Entities.GetComponent<InventoryStorage>(destination).InventoryId;
        var feed = PlayerLogisticsActionCommand.SetStockPolicy(player, source, ResourceIds.FerrousOre,
            20, 20, 20, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick);
        Submit(runtime.Simulation, feed);
        RunUntil(() => runtime.Inventories.GetQuantity(sourceInventory, ResourceIds.FerrousOre) >= 20 - Tolerance,
            runtime.Simulation.AdvanceOneTick, 2_000);
        Assert.True(feed.Accepted);
        Near(0, runtime.Inventories.GetQuantity(destinationInventory, ResourceIds.Steel));
        RemovePolicies(source);
        for (int cycle = 0; cycle < 2; cycle++)
            Submit(runtime.Simulation, PlayerProductionActionCommand.Queue(player, smelter, RecipeIds.Steel,
                runtime.Simulation.CurrentTick));
        bool sawCargo = false;
        RunUntil(() => runtime.Inventories.GetQuantity(destinationInventory, ResourceIds.Steel) >= 20 - Tolerance, () =>
        {
            runtime.Simulation.AdvanceOneTick();
            sawCargo |= Find<ConveyorFlowState>(runtime.Simulation.Entities).Any(entity =>
                runtime.Inventories.GetTotalQuantity(runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(entity).Buffer) > 0);
        }, 500);
        Assert.True(sawCargo);
        Assert.Equal(2UL, runtime.Simulation.Entities.GetComponent<ProductionFacility>(smelter).CompletedCycles);
        Near(20, runtime.Inventories.GetQuantity(destinationInventory, ResourceIds.Steel));
        Near(20, runtime.CargoTransport.Metrics.DeliveredQuantity);
        Assert.True(construction[ResourceIds.Steel] > 0);
        ProductionRecipeDefinition recipe = runtime.Services.ProductionRecipes[RecipeIds.Steel];
        // This Ore-to-Steel scenario measures solids; regional vehicle propulsion
        // consumes Fuel independently of the recipe and construction ledgers.
        foreach (ResourceId resource in Resources.Where(resource =>
                     resource != ResourceIds.Fuel && resource != ResourceIds.Volatiles))
        {
            double consumed = recipe.Inputs.Where(input => input.ResourceId == resource).Sum(input => input.Quantity) * 2;
            double produced = recipe.Outputs.Where(output => output.ResourceId == resource).Sum(output => output.Quantity) * 2;
            Near(initial[resource] - construction[resource] - consumed + produced,
                Total(runtime.Inventories, resource));
        }
    }

    private static void Queue(ConveyorTestFixture fixture, EntityId facility, RecipeId recipe, int cycles)
    {
        for (int cycle = 0; cycle < cycles; cycle++)
            Submit(fixture.Simulation, PlayerProductionActionCommand.Queue(fixture.Player, facility, recipe,
                fixture.Simulation.CurrentTick));
    }

    private static void Submit(SimulationCoordinator simulation, ISimulationCommand command) =>
        simulation.SubmitCommand(command, new SimulationTick(simulation.CurrentTick.Value + 1));

    private static void RunUntil(Func<bool> condition, Action step, int maximumTicks, Func<string>? diagnostic = null)
    {
        for (int tick = 0; tick < maximumTicks && !condition(); tick++) step();
        Assert.True(condition(), $"Condition did not hold after {maximumTicks} ticks. {diagnostic?.Invoke()}");
    }

    private static EntityId[] Find<T>(EntityRegistry entities) where T : struct
    {
        var result = new List<EntityId>();
        foreach (EntityId entity in entities.Query<T>(QueryIterationOrder.StableByEntityIndex)) result.Add(entity);
        return result.ToArray();
    }

    private static Dictionary<ResourceId, double> Totals(InventoryStore inventories) =>
        Resources.ToDictionary(resource => resource, resource => Total(inventories, resource));

    private static double Total(InventoryStore inventories, ResourceId resource) =>
        inventories.CaptureSnapshot().Inventories.Sum(inventory =>
            inventory.Resources.Where(item => item.ResourceId == resource).Sum(item => item.Quantity));

    private static void AssertRecipeAccounts(InventoryStore inventories, Dictionary<ResourceId, double> initial,
        ProductionRecipeDefinition recipe, ulong completedCycles, double constructionSteel,
        ResourceId extractedResource = default, double extractedQuantity = 0,
        double spentAmmunition = 0, double consumedFuel = 0)
    {
        foreach (ResourceId resource in Resources)
        {
            double consumed = recipe.Inputs.Where(input => input.ResourceId == resource).Sum(input => input.Quantity) * completedCycles;
            double produced = recipe.Outputs.Where(output => output.ResourceId == resource).Sum(output => output.Quantity) * completedCycles;
            double expected = initial[resource] + (resource == extractedResource ? extractedQuantity : 0) - consumed + produced;
            if (resource == ResourceIds.Steel) expected -= constructionSteel;
            if (resource == ResourceIds.Ammunition) expected -= spentAmmunition;
            if (resource == ResourceIds.Fuel) expected -= consumedFuel;
            Near(expected, Total(inventories, resource));
        }
    }

    private static double HorizontalDistance(Vector3 first, Vector3 second)
    {
        double x = (double)first.X - second.X, z = (double)first.Z - second.Z;
        return Math.Sqrt(x * x + z * z);
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
}
