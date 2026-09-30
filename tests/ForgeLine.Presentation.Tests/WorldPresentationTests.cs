using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldPresentationTests
{
    [Fact]
    public void CentralDivideTerrainProfileDefinesAllInitialMaterialSlots()
    {
        TerrainPresentationProfile profile =
            TerrainPresentationProfile.CreateCentralDivide();

        foreach (TerrainMaterialSlot slot in
                 Enum.GetValues<TerrainMaterialSlot>())
        {
            TerrainMaterialDefinition material =
                profile.GetMaterial(slot);

            Assert.False(
                string.IsNullOrWhiteSpace(
                    material.AssetId));
        }

        Vector4 field =
            profile.SampleBaseColor(
                new Vector3(300.0f, 6.0f, 300.0f),
                Vector3.UnitY);
        Vector4 road =
            profile.SampleBaseColor(
                new Vector3(1_536.0f, 6.0f, 1_536.0f),
                Vector3.UnitY);

        Assert.NotEqual(field, road);
    }

    [Fact]
    public void MissingTerrainSlotUsesDeterministicFallback()
    {
        var profile =
            new TerrainPresentationProfile(
                [
                    new TerrainMaterialDefinition(
                        TerrainMaterialSlot.GrassGround,
                        "material.world.terrain.grass_ground",
                        new Vector3(0.2f, 0.3f, 0.15f),
                        0.9f,
                        0.0f)
                ]);

        TerrainMaterialDefinition fallback =
            profile.GetMaterial(
                TerrainMaterialSlot.Concrete);

        Assert.Equal(
            "material.world.terrain.dirt",
            fallback.AssetId);
    }

    [Fact]
    public void WorldCatalogProvidesLodsAndStrategicDepositSymbols()
    {
        WorldFeaturePresentationMetadata feature =
            new(
                WorldVisualId.ResourceRareElements,
                WorldPresentationKind.ResourceDeposit,
                ResourceDepositPresentationState.Untouched,
                Inspectable: true);

        WorldPresentationDefinition definition =
            WorldPresentationCatalog.Get(
                feature.Visual);

        Assert.Equal(
            "material.world.symbol.resource.rare_elements",
            definition.StrategicSymbolAssetId);
        Assert.Equal(
            WorldAssetLod.High,
            WorldPresentationCatalog.SelectLod(
                feature,
                definition.ReducedLodDistance - 1.0f));
        Assert.Equal(
            WorldAssetLod.Reduced,
            WorldPresentationCatalog.SelectLod(
                feature,
                definition.ReducedLodDistance));
    }

    [Fact]
    public void ExtractorPublishesUntouchedActiveAndDepletedDepositStates()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId untouched =
            AddDeposit(
                simulation,
                ResourceDeposit.Restore(
                    ResourceIds.FerrousOre,
                    BoundsAt(0.0f),
                    1_000.0,
                    1_000.0,
                    10.0));

        EntityId active =
            AddDeposit(
                simulation,
                ResourceDeposit.Restore(
                    ResourceIds.Silicates,
                    BoundsAt(20.0f),
                    1_000.0,
                    650.0,
                    10.0));

        EntityId depleted =
            AddDeposit(
                simulation,
                ResourceDeposit.Restore(
                    ResourceIds.RareElements,
                    BoundsAt(40.0f),
                    1_000.0,
                    0.0,
                    10.0));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        Assert.Equal(
            ResourceDepositPresentationState.Untouched,
            StateFor(snapshot, untouched));
        Assert.Equal(
            ResourceDepositPresentationState.Active,
            StateFor(snapshot, active));
        Assert.Equal(
            ResourceDepositPresentationState.Depleted,
            StateFor(snapshot, depleted));
    }

    private static EntityId AddDeposit(
        SimulationCoordinator simulation,
        ResourceDeposit deposit)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();

        simulation.Entities.AddComponent(
            entity,
            deposit);
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                deposit.Bounds.Center,
                Quaternion.Identity,
                new Vector3(12.0f, 4.0f, 12.0f)));
        WorldPresentationIdentity world =
            WorldPresentationIdentity.ForResource(
                deposit.ResourceId);
        simulation.Entities.AddComponent(
            entity,
            new VisualIdentity(
                checked(4_000u + (uint)world.Visual)));
        simulation.Entities.AddComponent(
            entity,
            world);

        return entity;
    }

    private static ResourceDepositPresentationState StateFor(
        PresentationSnapshot snapshot,
        EntityId entity) =>
        snapshot.Instances
            .ToArray()
            .Single(
                instance =>
                    instance.Entity == entity)
            .WorldFeature
            .ResourceState;

    private static AxisAlignedBounds BoundsAt(
        float x) =>
        new(
            new Vector3(
                x - 5.0f,
                0.0f,
                -5.0f),
            new Vector3(
                x + 5.0f,
                5.0f,
                5.0f));
}
