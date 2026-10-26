using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class AutomaticProcessingPersistenceTests
{
    [Theory]
    [InlineData(ProductionCapability.SteelProcessing, 1u)]
    [InlineData(ProductionCapability.FuelProcessing, 2u)]
    [InlineData(ProductionCapability.ElectronicsProcessing, 3u)]
    public void UnorderedProcessingContinuesAcrossSaveAndReplay(ProductionCapability capability, uint recipeId)
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 7284);
        var basis = settings.Composition;
        var recipe = basis.Content.Recipes[new RecipeId(recipeId)];
        var ingredient = Assert.Single(recipe.Inputs);
        var output = Assert.Single(recipe.Outputs);
        EntityId processor = default;
        var composition = basis with
        {
            Key = "automatic-processing-recovery-" + recipeId,
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                var result = basis.CreateStartingBase(entities, inventories, factory, terrain, start, content, stock);
                if (start.Player != new PlayerId(1)) return result;
                processor = entities.CreateEntity();
                var inputInventory = inventories.CreateInventory(new InventorySpecification(1000));
                var outputInventory = inventories.CreateInventory(new InventorySpecification(1000));
                Assert.True(inventories.Add(inputInventory, ingredient.ResourceId, ingredient.Quantity * 3).Succeeded);
                entities.AddComponent(processor, new WorldTransform(start.CommandCorePosition + new Vector3(80, 0, 80), Quaternion.Identity, Vector3.One));
                entities.AddComponent(processor, new ControllableEntity(start.Player, ControllableEntityCategory.Building));
                entities.AddComponent(processor, new CompletedBuilding(capability switch { ProductionCapability.SteelProcessing => BuildingIds.Smelter, ProductionCapability.FuelProcessing => BuildingIds.Refinery, _ => BuildingIds.ElectronicsPlant }, start.Player, SimulationTick.Zero));
                entities.AddComponent(processor, new ProductionFacility(inputInventory, outputInventory, capability, SimulationTick.Zero));
                entities.AddComponent(processor, new PowerNetworkMembership(new PowerNetworkId(123)));
                entities.AddComponent(processor, new PowerConsumer(10, PowerPriority.Industrial));
                entities.AddComponent(processor, new PowerGenerator(100));
                return result;
            }
        };
        settings = settings with { Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
        using var original = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        original.Simulation.RunTicks(Math.Max(1u, recipe.DurationTicks / 2), TestContext.Current.CancellationToken);
        var inFlight = original.Simulation.Entities.GetComponent<ProductionFacility>(processor);
        Assert.True(inFlight.InputsReserved);
        Assert.True(inFlight.ProgressTicks > 0);
        MatchComposition Resolve(string key) => key == composition.Key ? composition : throw new InvalidOperationException(key);
        var save = MatchPersistenceService.CaptureSave(original);
        using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(save)), Resolve);
        using var replay = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(original))), Resolve);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
        foreach (var runtime in new[] { original, restored, replay })
        {
            runtime.Simulation.RunTicks(recipe.DurationTicks * 3, TestContext.Current.CancellationToken);
            var facility = runtime.Simulation.Entities.GetComponent<ProductionFacility>(processor);
            Assert.Equal(3UL, facility.CompletedCycles);
            Assert.Equal(output.Quantity * 3, runtime.Inventories.GetQuantity(facility.OutputInventory, output.ResourceId));
            int requestCount = 0;
            foreach (var entity in runtime.Simulation.Entities.Query<ProductionRequest>())
                if (runtime.Simulation.Entities.GetComponent<ProductionRequest>(entity).Facility == processor) requestCount++;
            Assert.Equal(1, requestCount);
        }
        string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
    }
}
