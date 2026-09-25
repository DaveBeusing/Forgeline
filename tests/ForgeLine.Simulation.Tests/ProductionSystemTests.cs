using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class ProductionSystemTests
{
    private static readonly PowerNetworkId ProductionNetwork = new(1);

    [Fact]
    public void InitialCatalogsExposeIndustrialResourcesAndRecipes()
    {
        ResourceCatalog resources = InitialResourceDefinitions.CreateCatalog();
        ProductionRecipeCatalog recipes = InitialProductionRecipes.CreateCatalog();

        Assert.Equal(7, resources.Count);
        Assert.False(resources[ResourceIds.Steel].IsExtractable);
        Assert.False(resources[ResourceIds.Fuel].IsExtractable);
        Assert.False(resources[ResourceIds.Electronics].IsExtractable);
        Assert.False(resources[ResourceIds.Ammunition].IsExtractable);

        Assert.Equal(4, recipes.Count);
        Assert.Equal(
            ResourceIds.FerrousOre,
            Assert.Single(recipes[RecipeIds.Steel].Inputs).ResourceId);
        Assert.Equal(
            ResourceIds.Steel,
            Assert.Single(recipes[RecipeIds.Steel].Outputs).ResourceId);
        Assert.Equal(
            ResourceIds.Volatiles,
            Assert.Single(recipes[RecipeIds.Fuel].Inputs).ResourceId);
        Assert.Equal(
            ResourceIds.Fuel,
            Assert.Single(recipes[RecipeIds.Fuel].Outputs).ResourceId);
        Assert.Equal(
            ResourceIds.Silicates,
            Assert.Single(recipes[RecipeIds.Electronics].Inputs).ResourceId);
        Assert.Equal(
            ResourceIds.Electronics,
            Assert.Single(recipes[RecipeIds.Electronics].Outputs).ResourceId);
        Assert.Equal(
            ResourceIds.Ammunition,
            Assert.Single(recipes[RecipeIds.Ammunition].Outputs).ResourceId);
        Assert.Contains(
            recipes[RecipeIds.Ammunition].Inputs,
            ingredient => ingredient.ResourceId == ResourceIds.Steel);
        Assert.Contains(
            recipes[RecipeIds.Ammunition].Inputs,
            ingredient => ingredient.ResourceId == ResourceIds.Electronics);
    }

    [Fact]
    public void NormalProductionConsumesReservedInputAndProducesOutput()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 100.0);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        uint duration =
            scenario.Recipes[RecipeIds.Steel].DurationTicks;
        scenario.Simulation.RunTicks(
            duration,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            90.0,
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            10.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));
        Assert.Equal(
            0.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));

        ProductionFacility facility =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(1UL, facility.CompletedCycles);
        Assert.Equal(10.0, facility.TotalOutputQuantity);
        Assert.Equal(ProductionStatus.Idle, facility.Status);
        Assert.Equal(1, scenario.System.Metrics.CompletedCycles);
    }

    [Fact]
    public void ProductionConservesConfiguredQuantitiesAndDoesNotDuplicateOutput()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 100.0);

        double before =
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre) +
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        uint duration =
            scenario.Recipes[RecipeIds.Steel].DurationTicks;
        scenario.Simulation.RunTicks(
            duration,
            TestContext.Current.CancellationToken);

        double after =
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre) +
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel);

        Assert.Equal(before, after);
        Assert.Equal(
            10.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));

        scenario.Simulation.RunTicks(
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            10.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));
        Assert.Equal(1, scenario.System.Metrics.CompletedCycles);
    }

    [Fact]
    public void MissingInputReportsNoInputWithoutChangingInventories()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        scenario.Simulation.AdvanceOneTick();

        ProductionFacility facility =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(ProductionStatus.NoInput, facility.Status);
        Assert.Equal(ProductionBlockReason.NoInput, facility.BlockReason);
        Assert.Equal(0U, facility.ProgressTicks);
        Assert.Equal(
            0.0,
            scenario.Inventories.GetTotalQuantity(
                scenario.InputInventory));
        Assert.Equal(
            0.0,
            scenario.Inventories.GetTotalQuantity(
                scenario.OutputInventory));
    }

    [Fact]
    public void MissingInputRecoversWhenResourceBecomesAvailable()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            ProductionStatus.NoInput,
            scenario.Simulation.Entities
                .GetComponent<ProductionFacility>(scenario.Facility)
                .Status);

        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 10.0);

        scenario.Simulation.AdvanceOneTick();

        ProductionFacility recovered =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(ProductionStatus.Running, recovered.Status);
        Assert.Equal(ProductionBlockReason.None, recovered.BlockReason);
        Assert.Equal(1U, recovered.ProgressTicks);
        Assert.Equal(
            10.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void MissingPowerReportsNoPowerBeforeInputReservation()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing,
            addGenerator: false);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        scenario.Simulation.AdvanceOneTick();

        ProductionFacility facility =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(ProductionStatus.NoPower, facility.Status);
        Assert.Equal(ProductionBlockReason.NoPower, facility.BlockReason);
        Assert.Equal(0U, facility.ProgressTicks);
        Assert.Equal(
            0.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void PowerLossFreezesActiveCycleAndRecoveryResumesIt()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        scenario.Simulation.AdvanceOneTick();

        ProductionFacility running =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);
        Assert.Equal(1U, running.ProgressTicks);
        Assert.True(running.InputsReserved);

        PowerGenerator generator =
            scenario.Simulation.Entities.GetComponent<PowerGenerator>(
                scenario.Generator);
        scenario.Simulation.Entities.SetComponent(
            scenario.Generator,
            generator.WithEnabled(false));

        scenario.Simulation.AdvanceOneTick();

        ProductionFacility blocked =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(ProductionStatus.NoPower, blocked.Status);
        Assert.Equal(ProductionBlockReason.NoPower, blocked.BlockReason);
        Assert.Equal(1U, blocked.ProgressTicks);
        Assert.True(blocked.InputsReserved);

        generator =
            scenario.Simulation.Entities.GetComponent<PowerGenerator>(
                scenario.Generator);
        scenario.Simulation.Entities.SetComponent(
            scenario.Generator,
            generator.WithEnabled(true));

        scenario.Simulation.AdvanceOneTick();

        ProductionFacility recovered =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(ProductionStatus.Running, recovered.Status);
        Assert.Equal(ProductionBlockReason.None, recovered.BlockReason);
        Assert.Equal(2U, recovered.ProgressTicks);
    }

    [Fact]
    public void FullOutputBlocksCompletedCycleWithoutConsumingInput()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing,
            outputCapacity: 10.0);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);

        Assert.True(
            scenario.Inventories.Add(
                scenario.OutputInventory,
                ResourceIds.Fuel,
                10.0).Succeeded);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        uint duration =
            scenario.Recipes[RecipeIds.Steel].DurationTicks;
        scenario.Simulation.RunTicks(
            duration,
            TestContext.Current.CancellationToken);

        ProductionFacility blocked =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(ProductionStatus.OutputFull, blocked.Status);
        Assert.Equal(
            ProductionBlockReason.OutputFull,
            blocked.BlockReason);
        Assert.Equal(duration, blocked.ProgressTicks);
        Assert.True(blocked.InputsReserved);
        Assert.Equal(
            20.0,
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            10.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));

        Assert.True(
            scenario.Inventories.Remove(
                scenario.OutputInventory,
                ResourceIds.Fuel,
                10.0).Succeeded);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            10.0,
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            10.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));
        Assert.Equal(
            0.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void PauseAndResumeRetainReservationAndProgress()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);

        var queue = new QueueProductionCommand(
            scenario.Facility,
            RecipeIds.Steel,
            new SimulationTick(0));

        scenario.Simulation.SubmitCommand(
            queue,
            new SimulationTick(1));
        scenario.Simulation.AdvanceOneTick();

        EntityId requestEntity = queue.RequestEntity;
        ProductionFacility beforePause =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.True(beforePause.InputsReserved);
        Assert.Equal(1U, beforePause.ProgressTicks);

        var pause = new SetProductionRequestPausedCommand(
            requestEntity,
            paused: true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.SubmitCommand(
            pause,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        ProductionFacility paused =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.True(pause.Accepted);
        Assert.Equal(ProductionStatus.Paused, paused.Status);
        Assert.Equal(1U, paused.ProgressTicks);
        Assert.Equal(
            10.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));

        var resume = new SetProductionRequestPausedCommand(
            requestEntity,
            paused: false,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.SubmitCommand(
            resume,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        ProductionFacility resumed =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.True(resume.Accepted);
        Assert.Equal(ProductionStatus.Running, resumed.Status);
        Assert.Equal(2U, resumed.ProgressTicks);
    }

    [Fact]
    public void CancellationReleasesReservedInputs()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);

        var queue = new QueueProductionCommand(
            scenario.Facility,
            RecipeIds.Steel,
            SimulationTick.Zero);
        scenario.Simulation.SubmitCommand(
            queue,
            new SimulationTick(1));
        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            10.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));

        var cancel = new CancelProductionRequestCommand(
            queue.RequestEntity,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.SubmitCommand(
            cancel,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        ProductionFacility facility =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.True(cancel.Accepted);
        Assert.False(
            scenario.Simulation.Entities.IsAlive(queue.RequestEntity));
        Assert.Equal(
            0.0,
            scenario.Inventories.GetReservedQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            20.0,
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre));
        Assert.False(facility.ActiveRequest.IsValid);
        Assert.Equal(1, scenario.System.Metrics.CancelledRequests);
    }

    [Fact]
    public void DesiredStockStopsAtTargetAndResumesAfterStockFalls()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 100.0);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.DesiredStock,
            ResourceIds.Steel,
            desiredStockQuantity: 20.0);

        uint duration =
            scenario.Recipes[RecipeIds.Steel].DurationTicks;
        scenario.Simulation.RunTicks(
            duration * 2UL,
            TestContext.Current.CancellationToken);
        scenario.Simulation.AdvanceOneTick();

        ProductionFacility stopped =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(
            20.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));
        Assert.Equal(ProductionStatus.Idle, stopped.Status);
        Assert.Equal(
            ProductionBlockReason.DesiredStockReached,
            stopped.BlockReason);

        Assert.True(
            scenario.Inventories.Remove(
                scenario.OutputInventory,
                ResourceIds.Steel,
                10.0).Succeeded);

        scenario.Simulation.RunTicks(
            duration,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            20.0,
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel));
        Assert.Equal(3, scenario.System.Metrics.CompletedCycles);
    }

    [Fact]
    public void HigherPriorityRequestWinsDeterministically()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing |
            ProductionCapability.FuelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);
        AddInput(
            scenario,
            ResourceIds.Volatiles,
            quantity: 20.0);

        var low = new QueueProductionCommand(
            scenario.Facility,
            RecipeIds.Steel,
            SimulationTick.Zero,
            ProductionPriority.Low);
        var high = new QueueProductionCommand(
            scenario.Facility,
            RecipeIds.Fuel,
            SimulationTick.Zero,
            ProductionPriority.High);

        scenario.Simulation.SubmitCommand(
            low,
            new SimulationTick(1));
        scenario.Simulation.SubmitCommand(
            high,
            new SimulationTick(1));
        scenario.Simulation.AdvanceOneTick();

        ProductionFacility facility =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        Assert.Equal(RecipeIds.Fuel, facility.ActiveRecipe);
        Assert.Equal(high.RequestEntity, facility.ActiveRequest);
        Assert.Equal(1U, facility.ProgressTicks);
    }

    [Fact]
    public void ProductionReadModelExposesBottleneckAndThroughput()
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 20.0);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.OneShot);

        scenario.Simulation.AdvanceOneTick();

        ProductionFacilityReadModel running =
            Assert.Single(scenario.System.Facilities);

        Assert.Equal(scenario.Facility, running.Entity);
        Assert.Equal(RecipeIds.Steel, running.ActiveRecipe);
        Assert.Equal(ProductionStatus.Running, running.Status);
        Assert.Equal(ProductionBlockReason.None, running.BlockReason);
        Assert.True(running.InputAvailable);
        Assert.True(running.OutputCapacityAvailable);
        Assert.Equal(PowerOperationalState.Powered, running.PowerState);
        Assert.True(running.Progress > 0.0);

        uint duration =
            scenario.Recipes[RecipeIds.Steel].DurationTicks;
        scenario.Simulation.RunTicks(
            duration - 1UL,
            TestContext.Current.CancellationToken);

        ProductionFacilityReadModel complete =
            Assert.Single(scenario.System.Facilities);

        Assert.Equal(1UL, complete.CompletedCycles);
        Assert.Equal(10.0, complete.TotalOutputQuantity);
        Assert.True(complete.AverageOutputPerSecond > 0.0);
    }

    [Fact]
    public void IdenticalInitialStateProducesIdenticalProductionResult()
    {
        ProductionResult first = RunDeterministicScenario(seed: 12345);
        ProductionResult second = RunDeterministicScenario(seed: 12345);

        Assert.Equal(first, second);
    }

    [Fact]
    public void HeadlessExtractionPowerAndProductionFormIndustrialLoop()
    {
        var inventories = new InventoryStore();
        var recipes = InitialProductionRecipes.CreateCatalog();
        var power = new PowerNetworkSystem();
        var extraction = new ResourceExtractionSystem(
            inventories: inventories);
        var production = new ProductionSystem(
            recipes,
            inventories);
        var simulation = new SimulationCoordinator(seed: 777);

        simulation.RegisterSystem(power);
        simulation.RegisterSystem(production);
        simulation.RegisterSystem(extraction);

        EntityId generator = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            generator,
            new PowerNetworkMembership(ProductionNetwork));
        simulation.Entities.AddComponent(
            generator,
            new PowerGenerator(100.0));

        InventoryId rawInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    100.0,
                    [ResourceIds.FerrousOre]));
        EntityId rawStorage = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            rawStorage,
            new InventoryStorage(rawInventory));

        EntityId deposit = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            deposit,
            new ResourceDeposit(
                ResourceIds.FerrousOre,
                new AxisAlignedBounds(
                    Vector3.Zero,
                    new Vector3(2.0f, 1.0f, 2.0f)),
                totalQuantity: 100.0,
                baseExtractionRatePerSecond: 10.0));

        EntityId extractor = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            extractor,
            new ResourceExtractor(
                deposit,
                ResourceIds.FerrousOre,
                maximumExtractionRatePerSecond: 10.0,
                outputInventory: rawStorage));
        simulation.Entities.AddComponent(
            extractor,
            new PowerNetworkMembership(ProductionNetwork));
        simulation.Entities.AddComponent(
            extractor,
            new PowerConsumer(
                demand: 10.0,
                PowerPriority.Industrial));

        InventoryId processedInventory =
            inventories.CreateInventory(
                new InventorySpecification(100.0));

        EntityId facility = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            facility,
            new PowerNetworkMembership(ProductionNetwork));
        simulation.Entities.AddComponent(
            facility,
            new PowerConsumer(
                demand: 20.0,
                PowerPriority.Industrial));
        simulation.Entities.AddComponent(
            facility,
            new ProductionFacility(
                rawInventory,
                processedInventory,
                ProductionCapability.SteelProcessing,
                SimulationTick.Zero));

        simulation.SubmitCommand(
            new QueueProductionCommand(
                facility,
                RecipeIds.Steel,
                SimulationTick.Zero,
                mode: ProductionRequestMode.Repeat),
            new SimulationTick(1));

        simulation.RunTicks(
            64,
            TestContext.Current.CancellationToken);

        Assert.True(
            inventories.GetQuantity(
                processedInventory,
                ResourceIds.Steel) >= 10.0);
        Assert.True(
            simulation.Entities.GetComponent<ResourceDeposit>(deposit)
                .RemainingQuantity < 100.0);
        Assert.True(production.Metrics.CompletedCycles >= 1);
        Assert.Equal(
            PowerOperationalState.Powered,
            simulation.Entities.GetComponent<PowerConsumer>(facility).State);
    }

    private static ProductionResult RunDeterministicScenario(ulong seed)
    {
        ProductionScenario scenario = CreateScenario(
            ProductionCapability.SteelProcessing |
            ProductionCapability.FuelProcessing,
            seed: seed);
        AddInput(
            scenario,
            ResourceIds.FerrousOre,
            quantity: 100.0);
        AddInput(
            scenario,
            ResourceIds.Volatiles,
            quantity: 100.0);

        Queue(
            scenario,
            RecipeIds.Steel,
            ProductionRequestMode.Repeat,
            priority: ProductionPriority.Normal);
        Queue(
            scenario,
            RecipeIds.Fuel,
            ProductionRequestMode.Repeat,
            priority: ProductionPriority.High);

        scenario.Simulation.RunTicks(
            120,
            TestContext.Current.CancellationToken);

        ProductionFacility facility =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                scenario.Facility);

        return new ProductionResult(
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.FerrousOre),
            scenario.Inventories.GetQuantity(
                scenario.InputInventory,
                ResourceIds.Volatiles),
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Steel),
            scenario.Inventories.GetQuantity(
                scenario.OutputInventory,
                ResourceIds.Fuel),
            facility.CompletedCycles,
            facility.ActiveRecipe,
            facility.ProgressTicks,
            facility.Status);
    }

    private static ProductionScenario CreateScenario(
        ProductionCapability capabilities,
        bool addGenerator = true,
        double outputCapacity = 1_000.0,
        ulong seed = 1)
    {
        var inventories = new InventoryStore();
        ProductionRecipeCatalog recipes =
            InitialProductionRecipes.CreateCatalog();
        var power = new PowerNetworkSystem();
        var production = new ProductionSystem(
            recipes,
            inventories);
        var simulation = new SimulationCoordinator(seed: seed);

        simulation.RegisterSystem(power);
        simulation.RegisterSystem(production);

        EntityId generator = EntityId.Invalid;
        if (addGenerator)
        {
            generator = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                generator,
                new PowerNetworkMembership(ProductionNetwork));
            simulation.Entities.AddComponent(
                generator,
                new PowerGenerator(10_000.0));
        }

        InventoryId inputInventory =
            inventories.CreateInventory(
                new InventorySpecification(10_000.0));
        InventoryId outputInventory =
            inventories.CreateInventory(
                new InventorySpecification(outputCapacity));

        EntityId facility = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            facility,
            new PowerNetworkMembership(ProductionNetwork));
        simulation.Entities.AddComponent(
            facility,
            new PowerConsumer(
                demand: 10.0,
                PowerPriority.Industrial));
        simulation.Entities.AddComponent(
            facility,
            new ProductionFacility(
                inputInventory,
                outputInventory,
                capabilities,
                SimulationTick.Zero));

        return new ProductionScenario(
            simulation,
            inventories,
            recipes,
            production,
            facility,
            generator,
            inputInventory,
            outputInventory);
    }

    private static void AddInput(
        ProductionScenario scenario,
        ResourceId resourceId,
        double quantity)
    {
        Assert.True(
            scenario.Inventories.Add(
                scenario.InputInventory,
                resourceId,
                quantity).Succeeded);
    }

    private static void Queue(
        ProductionScenario scenario,
        RecipeId recipeId,
        ProductionRequestMode mode,
        ResourceId desiredStockResourceId = default,
        double desiredStockQuantity = 0.0,
        ProductionPriority priority = ProductionPriority.Normal)
    {
        SimulationTick targetTick =
            scenario.Simulation.CurrentTick.Next();
        scenario.Simulation.SubmitCommand(
            new QueueProductionCommand(
                scenario.Facility,
                recipeId,
                scenario.Simulation.CurrentTick,
                priority,
                mode,
                desiredStockResourceId,
                desiredStockQuantity),
            targetTick);
    }

    private sealed record ProductionScenario(
        SimulationCoordinator Simulation,
        InventoryStore Inventories,
        ProductionRecipeCatalog Recipes,
        ProductionSystem System,
        EntityId Facility,
        EntityId Generator,
        InventoryId InputInventory,
        InventoryId OutputInventory);

    private readonly record struct ProductionResult(
        double FerrousOre,
        double Volatiles,
        double Steel,
        double Fuel,
        ulong CompletedCycles,
        RecipeId ActiveRecipe,
        uint ProgressTicks,
        ProductionStatus Status);
}
