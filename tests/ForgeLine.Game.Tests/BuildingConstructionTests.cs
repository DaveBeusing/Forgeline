using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BuildingConstructionTests
{
    private static readonly PlayerId Player = new(1);

    [Fact]
    public void InitialBuildingCatalogExposesStableEconomicStructures()
    {
        BuildingDefinitionCatalog catalog =
            InitialBuildingDefinitions.CreateCatalog();

        Assert.Equal(5, catalog.Count);
        Assert.Equal(
            "building.command_core",
            catalog[BuildingIds.CommandCore].Key);
        Assert.Equal(
            "building.power_plant",
            catalog[BuildingIds.PowerPlant].Key);
        Assert.Equal(
            "building.extractor",
            catalog[BuildingIds.Extractor].Key);
        Assert.Equal(
            "building.storage_depot",
            catalog[BuildingIds.StorageDepot].Key);
        Assert.Equal(
            "building.smelter",
            catalog[BuildingIds.Smelter].Key);

        BuildingFootprint footprint =
            catalog[BuildingIds.Smelter].Footprint;

        Assert.Equal(
            new Vector3(9.0f, 5.0f, 8.0f),
            footprint.GetHalfExtents(BuildingOrientation.North));
        Assert.Equal(
            new Vector3(8.0f, 5.0f, 9.0f),
            footprint.GetHalfExtents(BuildingOrientation.East));
    }

    [Fact]
    public void PreviewRejectsTerrainThatExceedsBuildingSlopeLimit()
    {
        var definitions = InitialBuildingDefinitions.CreateCatalog();
        var terrain = new SteepTerrainQuery();
        var spatialIndex = new SpatialGridIndex(
            new SpatialGridSettings
            {
                World = new WorldGridSettings(),
                CellSizeMeters = 16.0f
            });
        var simulation = new SimulationCoordinator();
        var placement = new BuildingPlacementService(
            definitions,
            terrain,
            spatialIndex);

        BuildingPlacementPreview preview =
            placement.CreatePreview(
                simulation.Entities,
                Player,
                BuildingIds.PowerPlant,
                new Vector3(64.0f, 0.0f, 64.0f),
                BuildingOrientation.North);

        Assert.False(preview.IsValid);
        Assert.Equal(
            BuildingPlacementFailureReason.SlopeTooSteep,
            preview.Failure);
    }

    [Fact]
    public void PreviewReportsBuildableAreaFailureWithoutChangingSimulation()
    {
        TestWorld test = CreateTestWorld(
            buildableArea: new RejectingBuildableAreaQuery());
        int before = test.Simulation.Entities.EntityCount;

        BuildingPlacementPreview preview =
            test.Placement.CreatePreview(
                test.Simulation.Entities,
                Player,
                BuildingIds.PowerPlant,
                new Vector3(64.0f, 0.0f, 64.0f),
                BuildingOrientation.North);

        Assert.False(preview.IsValid);
        Assert.Equal(
            BuildingPlacementFailureReason.OutsideBuildableArea,
            preview.Failure);
        Assert.Equal(before, test.Simulation.Entities.EntityCount);
    }

    [Fact]
    public void AuthoritativeCommandRejectsPlacementThatBecameObstructedAfterPreview()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: true);
        Vector3 position = new(64.0f, 0.0f, 64.0f);

        BuildingPlacementPreview preview =
            test.Placement.CreatePreview(
                test.Simulation.Entities,
                Player,
                BuildingIds.PowerPlant,
                position,
                BuildingOrientation.North);

        Assert.True(preview.IsValid);

        EntityId blocker = test.Simulation.Entities.CreateEntity();
        var blockerTransform = new WorldTransform(
            preview.Bounds.Center,
            Quaternion.Identity,
            Vector3.One);
        var blockerPresence = new SpatialPresence(
            new Vector3(2.0f, 2.0f, 2.0f),
            new SpatialEntryMetadata(
                Player.Value,
                (ulong)ControllableEntityCategory.Building,
                SpatialMobility.Static));
        test.Simulation.Entities.AddComponent(blocker, blockerTransform);
        test.Simulation.Entities.AddComponent(blocker, blockerPresence);
        test.SpatialIndex.Insert(
            blockerPresence.CreateEntry(blocker, blockerTransform));

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.PowerPlant,
            position);
        test.Simulation.AdvanceOneTick();

        Assert.Equal(
            BuildCommandRejectionReason.PlacementInvalid,
            test.Commands.Metrics.LastRejection);
        Assert.Equal(
            BuildingPlacementFailureReason.Obstructed,
            test.Commands.Metrics.LastPlacementFailure);
        Assert.Equal(0, CountSites(test));
        AssertAllReservationsZero(test, inventoryEntity);
    }

    [Fact]
    public void InsufficientResourcesRejectWithoutLeakingReservations()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: false);

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.PowerPlant,
            new Vector3(64.0f, 0.0f, 64.0f));
        test.Simulation.AdvanceOneTick();

        Assert.Equal(
            BuildCommandRejectionReason.InsufficientResources,
            test.Commands.Metrics.LastRejection);
        Assert.Equal(0, CountSites(test));
        AssertAllReservationsZero(test, inventoryEntity);

        InventoryId inventoryId = GetInventoryId(test, inventoryEntity);
        Assert.Equal(
            10_000.0,
            test.Inventories.GetQuantity(
                inventoryId,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void ConcurrentPlacementCommandsAcceptOnlyOneFootprint()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: true);
        Vector3 position = new(64.0f, 0.0f, 64.0f);

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.PowerPlant,
            position);
        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.PowerPlant,
            position);

        test.Simulation.AdvanceOneTick();

        Assert.Equal(1, test.Commands.Metrics.AcceptedCommands);
        Assert.Equal(1, test.Commands.Metrics.RejectedCommands);
        Assert.Equal(1, CountSites(test));

        InventoryId inventoryId = GetInventoryId(test, inventoryEntity);
        BuildingDefinition definition =
            test.Definitions[BuildingIds.PowerPlant];

        foreach (BuildingResourceCost cost in definition.Costs)
        {
            Assert.Equal(
                cost.Quantity,
                test.Inventories.GetReservedQuantity(
                    inventoryId,
                    cost.ResourceId));
        }
    }

    [Fact]
    public void CancellationReleasesReservedResourcesAndRemovesOccupancy()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: true);
        Vector3 position = new(64.0f, 0.0f, 64.0f);

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.StorageDepot,
            position);
        test.Simulation.AdvanceOneTick();

        EntityId site = SingleSite(test);
        Assert.True(test.SpatialIndex.Contains(site));

        var cancel = new CancelConstructionCommand(
            Player,
            site,
            test.Simulation.CurrentTick);
        test.Simulation.SubmitCommand(
            cancel,
            NextTick(test),
            new SimulationCommandSource(Player.Value));
        test.Simulation.AdvanceOneTick();

        Assert.True(cancel.Accepted);
        Assert.False(test.Simulation.Entities.IsAlive(site));
        Assert.False(test.SpatialIndex.Contains(site));
        Assert.Equal(0, CountSites(test));
        AssertAllReservationsZero(test, inventoryEntity);

        InventoryId inventoryId = GetInventoryId(test, inventoryEntity);
        BuildingDefinition definition =
            test.Definitions[BuildingIds.StorageDepot];

        foreach (BuildingResourceCost cost in definition.Costs)
        {
            Assert.Equal(
                10_000.0,
                test.Inventories.GetQuantity(
                    inventoryId,
                    cost.ResourceId));
        }
    }

    [Fact]
    public void CompletionConsumesReservedResourcesAndActivatesCapabilitiesExactlyOnce()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: true);
        BuildingDefinition definition =
            test.Definitions[BuildingIds.PowerPlant];

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.PowerPlant,
            new Vector3(64.0f, 0.0f, 64.0f));
        test.Simulation.AdvanceOneTick();

        EntityId site = SingleSite(test);
        test.Simulation.RunTicks(
            definition.ConstructionTicks - 1,
            TestContext.Current.CancellationToken);

        Assert.False(
            test.Simulation.Entities.HasComponent<ConstructionSite>(site));
        Assert.True(
            test.Simulation.Entities.HasComponent<CompletedBuilding>(site));
        Assert.True(
            test.Simulation.Entities.HasComponent<PowerGenerator>(site));
        Assert.True(
            test.Simulation.Entities.HasComponent<PowerNetworkMembership>(site));

        InventoryId inventoryId = GetInventoryId(test, inventoryEntity);
        foreach (BuildingResourceCost cost in definition.Costs)
        {
            Assert.Equal(
                10_000.0 - cost.Quantity,
                test.Inventories.GetQuantity(
                    inventoryId,
                    cost.ResourceId));
            Assert.Equal(
                0.0,
                test.Inventories.GetReservedQuantity(
                    inventoryId,
                    cost.ResourceId));
        }

        double ferrousAfterCompletion =
            test.Inventories.GetQuantity(
                inventoryId,
                ResourceIds.FerrousOre);

        test.Simulation.RunTicks(
            25,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ferrousAfterCompletion,
            test.Inventories.GetQuantity(
                inventoryId,
                ResourceIds.FerrousOre));
        Assert.Equal(1, test.Construction.Metrics.CompletedBuildings);
    }

    [Fact]
    public void ExtractorCompletionBindsDepositStorageAndPowerContracts()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: true);
        Vector3 position = new(96.0f, 0.0f, 96.0f);
        EntityId deposit = test.Simulation.Entities.CreateEntity();
        test.Simulation.Entities.AddComponent(
            deposit,
            new ResourceDeposit(
                ResourceIds.FerrousOre,
                new AxisAlignedBounds(
                    new Vector3(88.0f, -1.0f, 88.0f),
                    new Vector3(104.0f, 1.0f, 104.0f)),
                1_000.0,
                10.0));

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.Extractor,
            position);
        test.Simulation.AdvanceOneTick();

        EntityId site = SingleSite(test);
        ConstructionSite construction =
            test.Simulation.Entities.GetComponent<ConstructionSite>(site);

        Assert.Equal(deposit, construction.ResourceDeposit);
        Assert.Equal(
            ResourceIds.FerrousOre,
            construction.ExtractedResourceId);

        BuildingDefinition definition =
            test.Definitions[BuildingIds.Extractor];
        test.Simulation.RunTicks(
            definition.ConstructionTicks - 1,
            TestContext.Current.CancellationToken);

        ResourceExtractor extractor =
            test.Simulation.Entities.GetComponent<ResourceExtractor>(site);
        InventoryStorage storage =
            test.Simulation.Entities.GetComponent<InventoryStorage>(site);

        Assert.Equal(deposit, extractor.Deposit);
        Assert.Equal(ResourceIds.FerrousOre, extractor.ResourceId);
        Assert.Equal(site, extractor.OutputInventory);
        Assert.True(test.Inventories.Contains(storage.InventoryId));
        Assert.True(
            test.Simulation.Entities.HasComponent<PowerConsumer>(site));
    }

    [Fact]
    public void HeadlessConstructionRunsThroughFixedTicksWithoutPresentation()
    {
        TestWorld test = CreateTestWorld();
        EntityId inventoryEntity =
            AddFundedInventory(test, includeAllCosts: true);

        SubmitBuild(
            test,
            inventoryEntity,
            BuildingIds.StorageDepot,
            new Vector3(64.0f, 0.0f, 64.0f));

        ulong executed = test.Simulation.RunTicks(
            256,
            TestContext.Current.CancellationToken);

        Assert.Equal(256UL, executed);
        Assert.Equal(0, CountSites(test));
        Assert.Equal(1, test.Construction.Metrics.CompletedBuildings);

        EntityId completed = SingleCompletedBuilding(test);
        Assert.True(
            test.Simulation.Entities.HasComponent<InventoryStorage>(completed));
        Assert.True(
            test.Simulation.Entities.HasComponent<StorageDepot>(completed));
    }

    private static TestWorld CreateTestWorld(
        IBuildableAreaQuery? buildableArea = null)
    {
        var definitions = InitialBuildingDefinitions.CreateCatalog();
        var terrain = new FlatTerrainQuery();
        var spatialIndex = new SpatialGridIndex(
            new SpatialGridSettings
            {
                World = new WorldGridSettings(),
                CellSizeMeters = 16.0f
            });
        var inventories = new InventoryStore();
        var placement = new BuildingPlacementService(
            definitions,
            terrain,
            spatialIndex,
            buildableArea);
        var commands = new BuildingCommandProcessingSystem(
            definitions,
            placement,
            inventories,
            spatialIndex);
        var construction = new BuildingConstructionSystem(
            definitions,
            inventories,
            spatialIndex);
        var simulation = new SimulationCoordinator();

        simulation.RegisterSystem(commands);
        simulation.RegisterSystem(construction);

        return new TestWorld(
            simulation,
            definitions,
            inventories,
            spatialIndex,
            placement,
            commands,
            construction);
    }

    private static EntityId AddFundedInventory(
        TestWorld test,
        bool includeAllCosts)
    {
        EntityId entity = test.Simulation.Entities.CreateEntity();
        InventoryId inventoryId =
            test.Inventories.CreateInventory(
                new InventorySpecification(50_000.0));
        test.Simulation.Entities.AddComponent(
            entity,
            new InventoryStorage(inventoryId));
        test.Simulation.Entities.AddComponent(
            entity,
            new StorageDepot(
                inventoryId,
                new FactionId((uint)Player.Value)));

        Assert.True(
            test.Inventories.Add(
                inventoryId,
                ResourceIds.FerrousOre,
                10_000.0).Succeeded);

        if (includeAllCosts)
        {
            Assert.True(
                test.Inventories.Add(
                    inventoryId,
                    ResourceIds.Silicates,
                    10_000.0).Succeeded);
            Assert.True(
                test.Inventories.Add(
                    inventoryId,
                    ResourceIds.Volatiles,
                    10_000.0).Succeeded);
        }

        return entity;
    }

    private static BuildCommand SubmitBuild(
        TestWorld test,
        EntityId inventoryEntity,
        BuildingId buildingId,
        Vector3 position)
    {
        var command = new BuildCommand(
            Player,
            buildingId,
            position,
            BuildingOrientation.North,
            inventoryEntity,
            test.Simulation.CurrentTick);

        test.Simulation.SubmitCommand(
            command,
            NextTick(test),
            new SimulationCommandSource(Player.Value));

        return command;
    }

    private static SimulationTick NextTick(TestWorld test) =>
        new(checked(test.Simulation.CurrentTick.Value + 1));

    private static InventoryId GetInventoryId(
        TestWorld test,
        EntityId inventoryEntity) =>
        test.Simulation.Entities
            .GetComponent<InventoryStorage>(inventoryEntity)
            .InventoryId;

    private static int CountSites(TestWorld test)
    {
        int count = 0;
        foreach (EntityId unused in
                 test.Simulation.Entities.Query<ConstructionSite>())
        {
            _ = unused;
            count++;
        }

        return count;
    }

    private static EntityId SingleSite(TestWorld test)
    {
        EntityId found = EntityId.Invalid;

        foreach (EntityId entity in
                 test.Simulation.Entities.Query<ConstructionSite>())
        {
            Assert.False(found.IsValid);
            found = entity;
        }

        Assert.True(found.IsValid);
        return found;
    }

    private static EntityId SingleCompletedBuilding(TestWorld test)
    {
        EntityId found = EntityId.Invalid;

        foreach (EntityId entity in
                 test.Simulation.Entities.Query<CompletedBuilding>())
        {
            Assert.False(found.IsValid);
            found = entity;
        }

        Assert.True(found.IsValid);
        return found;
    }

    private static void AssertAllReservationsZero(
        TestWorld test,
        EntityId inventoryEntity)
    {
        InventoryId inventoryId =
            GetInventoryId(test, inventoryEntity);

        Assert.Equal(
            0.0,
            test.Inventories.GetReservedQuantity(
                inventoryId,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            test.Inventories.GetReservedQuantity(
                inventoryId,
                ResourceIds.Silicates));
        Assert.Equal(
            0.0,
            test.Inventories.GetReservedQuantity(
                inventoryId,
                ResourceIds.Volatiles));
    }

    private sealed class FlatTerrainQuery : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds { get; } =
            new(
                Vector3.Zero,
                new Vector3(256.0f, 20.0f, 256.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            bool contained =
                worldX >= WorldBounds.Minimum.X &&
                worldX <= WorldBounds.Maximum.X &&
                worldZ >= WorldBounds.Minimum.Z &&
                worldZ <= WorldBounds.Maximum.Z;

            height = 0.0f;
            return contained;
        }

        public bool TrySampleNormal(
            float worldX,
            float worldZ,
            out Vector3 normal)
        {
            bool contained =
                worldX >= WorldBounds.Minimum.X &&
                worldX <= WorldBounds.Maximum.X &&
                worldZ >= WorldBounds.Minimum.Z &&
                worldZ <= WorldBounds.Maximum.Z;

            normal = Vector3.UnitY;
            return contained;
        }
    }

    private sealed class SteepTerrainQuery : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds { get; } =
            new(
                Vector3.Zero,
                new Vector3(256.0f, 20.0f, 256.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            height = 0.0f;
            return worldX >= 0.0f &&
                   worldX <= 256.0f &&
                   worldZ >= 0.0f &&
                   worldZ <= 256.0f;
        }

        public bool TrySampleNormal(
            float worldX,
            float worldZ,
            out Vector3 normal)
        {
            normal = Vector3.Normalize(new Vector3(1.0f, 1.0f, 0.0f));
            return worldX >= 0.0f &&
                   worldX <= 256.0f &&
                   worldZ >= 0.0f &&
                   worldZ <= 256.0f;
        }
    }

    private sealed class RejectingBuildableAreaQuery : IBuildableAreaQuery
    {
        public bool IsBuildable(
            PlayerId issuer,
            in AxisAlignedBounds footprintBounds)
        {
            _ = issuer;
            _ = footprintBounds;
            return false;
        }
    }

    private sealed record TestWorld(
        SimulationCoordinator Simulation,
        BuildingDefinitionCatalog Definitions,
        InventoryStore Inventories,
        SpatialGridIndex SpatialIndex,
        BuildingPlacementService Placement,
        BuildingCommandProcessingSystem Commands,
        BuildingConstructionSystem Construction);
}
