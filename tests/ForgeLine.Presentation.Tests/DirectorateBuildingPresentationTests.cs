using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DirectorateBuildingPresentationTests
{
    [Fact]
    public void CatalogBindsRequiredFamiliesLodsCollisionAndStrategicSymbols()
    {
        BuildingId[] buildings =
        [
            BuildingIds.CommandCore,
            BuildingIds.Extractor,
            BuildingIds.Smelter,
            BuildingIds.ElectronicsPlant,
            BuildingIds.Refinery,
            BuildingIds.VehicleFactory,
            BuildingIds.StorageDepot,
            BuildingIds.SupplyDepot,
            BuildingIds.PowerPlant,
            BuildingIds.LogisticsHub,
            BuildingIds.AmmunitionPlant,
            BuildingIds.Barracks,
            BuildingIds.Radar
        ];

        foreach (BuildingId building in
                 buildings)
        {
            BuildingPresentationDefinition definition =
                BuildingPresentationCatalog.Get(
                    building);

            Assert.StartsWith(
                "building.directorate.",
                definition.MeshAssetId);
            Assert.Equal(
                definition.MeshAssetId +
                ".lod1",
                definition.Lod1AssetId);
            Assert.Equal(
                definition.MeshAssetId +
                ".lod2",
                definition.Lod2AssetId);
            Assert.Equal(
                BuildingPresentationCatalog.CollisionAssetId,
                definition.CollisionAssetId);
            Assert.StartsWith(
                "material.directorate.symbol.building.",
                definition.StrategicSymbolAssetId);
        }

        Assert.Equal(
            "building.directorate.fuel_refinery",
            BuildingPresentationCatalog.Get(
                BuildingIds.Refinery).MeshAssetId);
    }

    [Fact]
    public void ConstructionProgressExtractsFoundationFrameAndShellStates()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId foundation =
            AddConstruction(
                simulation,
                BuildingIds.CommandCore,
                progressTicks: 0);
        EntityId frame =
            AddConstruction(
                simulation,
                BuildingIds.Smelter,
                progressTicks: 45);
        EntityId shell =
            AddConstruction(
                simulation,
                BuildingIds.VehicleFactory,
                progressTicks: 80);

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        Assert.Equal(
            BuildingPresentationState.ConstructionFoundation,
            StateFor(
                snapshot,
                foundation));
        Assert.Equal(
            BuildingPresentationState.ConstructionFrame,
            StateFor(
                snapshot,
                frame));
        Assert.Equal(
            BuildingPresentationState.ConstructionShell,
            StateFor(
                snapshot,
                shell));
    }

    [Fact]
    public void AuthoritativePowerHealthAndWreckStateDrivePresentation()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId unpowered =
            AddCompleted(
                simulation,
                BuildingIds.PowerPlant);
        simulation.Entities.AddComponent(
            unpowered,
            new PowerGenerator(
                100.0,
                enabled: false));

        EntityId damaged =
            AddCompleted(
                simulation,
                BuildingIds.CommandCore);
        simulation.Entities.AddComponent(
            damaged,
            new HealthState(
                50.0,
                100.0));

        EntityId critical =
            AddCompleted(
                simulation,
                BuildingIds.SupplyDepot);
        simulation.Entities.AddComponent(
            critical,
            new HealthState(
                20.0,
                100.0));

        EntityId wreck =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            wreck);
        simulation.Entities.AddComponent(
            wreck,
            new BuildingWreckPresentationIdentity(
                BuildingIds.VehicleFactory,
                new PlayerId(
                    1)));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        Assert.Equal(
            BuildingPresentationState.Unpowered,
            StateFor(
                snapshot,
                unpowered));
        Assert.Equal(
            BuildingPresentationState.Damaged,
            StateFor(
                snapshot,
                damaged));
        Assert.Equal(
            BuildingPresentationState.Critical,
            StateFor(
                snapshot,
                critical));
        Assert.Equal(
            BuildingPresentationState.Destroyed,
            StateFor(
                snapshot,
                wreck));
    }

    [Fact]
    public void InfrastructureLifecycleMapsToRoadAndBridgePresentation()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId road =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            road);
        simulation.Entities.AddComponent(
            road,
            new InfrastructurePresentationIdentity(
                InfrastructurePresentationKind.RoadSegment,
                "road.test"));

        EntityId bridge =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            bridge);
        simulation.Entities.AddComponent(
            bridge,
            new InfrastructurePresentationIdentity(
                InfrastructurePresentationKind.RoadBridge,
                "crossing.north_bridge"));
        simulation.Entities.AddComponent(
            bridge,
            new StrategicInfrastructureState(
                StrategicInfrastructureOperationalState.Restoring,
                10));

        EntityId disabled =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            disabled);
        simulation.Entities.AddComponent(
            disabled,
            new InfrastructurePresentationIdentity(
                InfrastructurePresentationKind.RoadBridge,
                "bridge.disabled"));
        simulation.Entities.AddComponent(
            disabled,
            StrategicInfrastructureState.Disabled);

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        RenderInstance roadInstance =
            InstanceFor(
                snapshot,
                road);
        RenderInstance bridgeInstance =
            InstanceFor(
                snapshot,
                bridge);
        RenderInstance disabledInstance =
            InstanceFor(
                snapshot,
                disabled);

        Assert.Equal(
            InfrastructurePresentationState.Operational,
            roadInstance.InfrastructureFeature.State);
        Assert.Equal(
            InfrastructurePresentationState.Restoring,
            bridgeInstance.InfrastructureFeature.State);
        Assert.Equal(
            InfrastructurePresentationState.Disabled,
            disabledInstance.InfrastructureFeature.State);
        Assert.Equal(
            "infrastructure.directorate.bridge.road.damaged",
            InfrastructurePresentationCatalog.ResolveMeshAssetId(
                bridgeInstance.InfrastructureFeature));
        Assert.Equal(
            "infrastructure.directorate.bridge.road.destroyed",
            InfrastructurePresentationCatalog.ResolveMeshAssetId(
                disabledInstance.InfrastructureFeature));
        Assert.Equal(
            "infrastructure.directorate.road.shoulder",
            InfrastructurePresentationCatalog.ResolveMeshAssetId(
                new InfrastructureFeaturePresentationMetadata(
                    InfrastructurePresentationKind.RoadShoulder,
                    InfrastructurePresentationState.Operational)));
        Assert.Equal(
            "infrastructure.directorate.road.curve_short",
            InfrastructurePresentationCatalog.ResolveMeshAssetId(
                new InfrastructureFeaturePresentationMetadata(
                    InfrastructurePresentationKind.RoadCurveShort,
                    InfrastructurePresentationState.Operational)));
        Assert.Equal(
            "infrastructure.directorate.road.junction_t",
            InfrastructurePresentationCatalog.ResolveMeshAssetId(
                new InfrastructureFeaturePresentationMetadata(
                    InfrastructurePresentationKind.RoadJunctionT,
                    InfrastructurePresentationState.Operational)));
        Assert.Equal(
            InfrastructurePresentationCatalog.RoadShoulderMaterialAssetId,
            InfrastructurePresentationCatalog.ResolveMaterialAssetId(
                new InfrastructureFeaturePresentationMetadata(
                    InfrastructurePresentationKind.RoadShoulder,
                    InfrastructurePresentationState.Operational)));
        Assert.Equal(
            InfrastructurePresentationCatalog.RoadDamagedMaterialAssetId,
            InfrastructurePresentationCatalog.ResolveMaterialAssetId(
                new InfrastructureFeaturePresentationMetadata(
                    InfrastructurePresentationKind.RoadSegment,
                    InfrastructurePresentationState.Disabled)));
    }

    [Fact]
    public void CriticalOperationalStatesUseGeometryAttachmentsNotOnlyTint()
    {
        Assert.Equal(
            BuildingPresentationCatalog.UnpoweredStateAssetId,
            BuildingPresentationCatalog.ResolveStateAttachmentAssetId(
                new BuildingFeaturePresentationMetadata(
                    BuildingIds.PowerPlant,
                    BuildingPresentationState.Unpowered)));
        Assert.Equal(
            BuildingPresentationCatalog.DamagedStateAssetId,
            BuildingPresentationCatalog.ResolveStateAttachmentAssetId(
                new BuildingFeaturePresentationMetadata(
                    BuildingIds.CommandCore,
                    BuildingPresentationState.Damaged)));
        Assert.Equal(
            BuildingPresentationCatalog.CriticalStateAssetId,
            BuildingPresentationCatalog.ResolveStateAttachmentAssetId(
                new BuildingFeaturePresentationMetadata(
                    BuildingIds.CommandCore,
                    BuildingPresentationState.Critical)));
        Assert.Equal(
            BuildingPresentationCatalog.DestroyedAssetId,
            BuildingPresentationCatalog.ResolveMeshAssetId(
                new BuildingFeaturePresentationMetadata(
                    BuildingIds.CommandCore,
                    BuildingPresentationState.Destroyed),
                BuildingAssetLod.Lod0));
    }

    private static EntityId AddConstruction(
        SimulationCoordinator simulation,
        BuildingId building,
        uint progressTicks)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            entity);
        simulation.Entities.AddComponent(
            entity,
            new ConstructionSite(
                building,
                new PlayerId(
                    1),
                new InventoryId(
                    1),
                SimulationTick.Zero,
                requiredTicks: 100,
                progressTicks));

        return entity;
    }

    private static EntityId AddCompleted(
        SimulationCoordinator simulation,
        BuildingId building)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            entity);
        simulation.Entities.AddComponent(
            entity,
            new CompletedBuilding(
                building,
                new PlayerId(
                    1),
                SimulationTick.Zero));

        return entity;
    }

    private static void AddRenderable(
        SimulationCoordinator simulation,
        EntityId entity)
    {
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                new Vector3(
                    16.0f,
                    10.0f,
                    16.0f)));
        simulation.Entities.AddComponent(
            entity,
            new VisualIdentity(
                101));
    }

    private static BuildingPresentationState StateFor(
        PresentationSnapshot snapshot,
        EntityId entity) =>
        InstanceFor(
            snapshot,
            entity).BuildingFeature.State;

    private static RenderInstance InstanceFor(
        PresentationSnapshot snapshot,
        EntityId entity) =>
        snapshot.Instances
            .ToArray()
            .Single(
                instance =>
                    instance.Entity ==
                    entity);
}
