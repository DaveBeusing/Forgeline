using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class IndustrialEffectPresetTests
{
    [Fact]
    public void RealProductionEmitsSteamStopsWithoutPowerAndLeavesSimulationIdentical()
    {
        var first = CreateFactory();
        var second = CreateFactory();
        var buffer = new PresentationSnapshotBuffer();
        first.Simulation.RegisterTickObserver(new PresentationExtractor(buffer));
        for (int i = 0; i < 16; i++)
        {
            first.Simulation.AdvanceOneTick(); second.Simulation.AdvanceOneTick();
            Assert.Equal(first.Simulation.Entities.GetComponent<ProductionFacility>(first.Facility),
                second.Simulation.Entities.GetComponent<ProductionFacility>(second.Facility));
            Assert.Equal(first.Inventories.GetQuantity(first.Output, ResourceIds.Steel), second.Inventories.GetQuantity(second.Output, ResourceIds.Steel));
            Assert.Equal(first.Simulation.Entities.EntityCount, second.Simulation.Entities.EntityCount);
        }
        Assert.True(buffer.TryReadLatest(out var running));
        var steam = Assert.Single(running.Instances.ToArray(), x => x.VfxFeature.Kind == VfxEffectKind.IndustrialSteam);
        Assert.True(steam.VfxFeature.Continuous);
        first.Simulation.Entities.RemoveComponent<PowerGenerator>(first.Generator);
        first.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var stopped));
        Assert.DoesNotContain(stopped.Instances.ToArray(), x => x.VfxFeature.Kind == VfxEffectKind.IndustrialSteam);
    }

    private static (SimulationCoordinator Simulation, InventoryStore Inventories, InventoryId Output, EntityId Facility, EntityId Generator) CreateFactory()
    {
        var simulation = new SimulationCoordinator(seed: 12345);
        var inventories = new InventoryStore();
        simulation.RegisterSystem(new PowerNetworkSystem());
        simulation.RegisterSystem(new ProductionSystem(InitialProductionRecipes.CreateCatalog(), inventories));
        var network = new PowerNetworkId(1);
        var generator = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(generator, new PowerNetworkMembership(network));
        simulation.Entities.AddComponent(generator, new PowerGenerator(1000));
        var input = inventories.CreateInventory(new InventorySpecification(10000));
        var output = inventories.CreateInventory(new InventorySpecification(10000));
        Assert.True(inventories.Add(input, ResourceIds.FerrousOre, 1000).Succeeded);
        var facility = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(facility, new WorldTransform(Vector3.Zero, Quaternion.Identity, new(24, 16, 24)));
        simulation.Entities.AddComponent(facility, new VisualIdentity(1));
        simulation.Entities.AddComponent(facility, new CompletedBuilding(BuildingIds.Smelter, new PlayerId(1), SimulationTick.Zero));
        simulation.Entities.AddComponent(facility, new PowerNetworkMembership(network));
        simulation.Entities.AddComponent(facility, new PowerConsumer(10, PowerPriority.Industrial));
        simulation.Entities.AddComponent(facility, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
        simulation.SubmitCommand(new QueueProductionCommand(facility, RecipeIds.Steel, simulation.CurrentTick,
            ProductionPriority.Normal, ProductionRequestMode.Repeat), simulation.CurrentTick.Next());
        return (simulation, inventories, output, facility, generator);
    }

    [Fact]
    public void PresetsRequireWorkingOperationalStateAndReuseValidatedAssets()
    {
        foreach (var id in new[] { BuildingIds.Smelter, BuildingIds.Refinery, BuildingIds.AmmunitionPlant, BuildingIds.VehicleFactory })
        {
            foreach (var state in Enum.GetValues<BuildingPresentationState>())
            {
                var building = new BuildingFeaturePresentationMetadata(id, state);
                Assert.False(IndustrialEffectPreset.TryResolve(building, false, out _));
                bool admitted = IndustrialEffectPreset.TryResolve(building, true, out var preset);
                Assert.Equal(state == BuildingPresentationState.Operational, admitted);
                if (!admitted) continue;
                var source = VfxPresentationCatalog.Get(preset.Effect);
                source.Validate();
                Assert.True(ParticleEmitterDescriptor.TryResolve(preset.Effect, out var descriptor));
                descriptor.Validate();
                Assert.Equal(6, descriptor.Count);
                Assert.True(descriptor.RadiusMeters <= .35f);
            }
        }
        Assert.False(IndustrialEffectPreset.TryResolve(new(BuildingIds.CommandCore, BuildingPresentationState.Operational), true, out _));
    }

    [Fact]
    public void ThousandsOfDamagedUnitsCannotGrowSnapshotEffectsWithoutBound()
    {
        var simulation = new SimulationCoordinator();
        var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer));
        for (int i = 0; i < 2000; i++)
        {
            var entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(entity, new VisualIdentity(1));
            simulation.Entities.AddComponent(entity, new UnitIdentity(UnitIds.MainBattleTank, DirectorateContent.FactionId));
            simulation.Entities.AddComponent(entity, new ForgeLine.Combat.HealthState(100, 560));
        }
        int count = simulation.Entities.EntityCount;
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var first));
        Assert.Equal(count, simulation.Entities.EntityCount);
        Assert.Equal(PresentationExtractor.MaximumSnapshotEffects, first.VfxMetrics.PublishedEffects);
        Assert.Equal(6000 - PresentationExtractor.MaximumSnapshotEffects, first.VfxMetrics.DiscardedSnapshotEffects);
        var saved = first.Instances.ToArray();
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var second));
        Assert.Equal(first.VfxMetrics.PublishedEffects, second.VfxMetrics.PublishedEffects);
        Assert.Equal(saved, first.Instances.ToArray());
        Assert.Equal(count, simulation.Entities.EntityCount);
    }
}
