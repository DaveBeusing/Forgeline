using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class IndustrialEffectPresetTests
{
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
