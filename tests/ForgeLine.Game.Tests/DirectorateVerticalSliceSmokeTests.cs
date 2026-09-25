using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DirectorateVerticalSliceSmokeTests
{
    private static readonly PlayerId Player = new(1);

    [Fact]
    public void HeadlessVerticalSliceLoadsConstructsAndComposesCompleteRoster()
    {
        FactionContentDefinition faction =
            DirectorateContent.CreateFactionDefinition();
        BuildingDefinitionCatalog buildings =
            DirectorateContent.CreateBuildingCatalog();
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        ResourceCatalog resources =
            InitialResourceDefinitions.CreateCatalog();
        ProductionRecipeCatalog recipes =
            InitialProductionRecipes.CreateCatalog();
        WeaponCatalog weapons =
            DirectorateContent.CreateWeaponCatalog();
        ArmorCatalog armor =
            DirectorateContent.CreateArmorCatalog();
        ArtilleryWeaponCatalog artillery =
            DirectorateContent.CreateArtilleryWeaponCatalog();

        GameContentValidator.ValidateDirectorate(
            faction,
            resources,
            buildings,
            units,
            recipes,
            weapons,
            armor,
            artillery);

        var terrain = new FlatTerrainQuery();
        var spatialIndex =
            new SpatialGridIndex(
                new SpatialGridSettings
                {
                    World = new WorldGridSettings(),
                    CellSizeMeters = 16.0f
                });
        var inventories = new InventoryStore();
        var placement =
            new BuildingPlacementService(
                buildings,
                terrain,
                spatialIndex);
        var buildingCommands =
            new BuildingCommandProcessingSystem(
                buildings,
                placement,
                inventories,
                spatialIndex);
        var construction =
            new BuildingConstructionSystem(
                buildings,
                inventories,
                spatialIndex);
        var simulation = new SimulationCoordinator();

        simulation.RegisterSystem(buildingCommands);
        simulation.RegisterSystem(construction);

        EntityId source =
            CreateFundedConstructionInventory(
                simulation,
                inventories);

        BuildingId[] requiredBuildings =
        [
            BuildingIds.CommandCore,
            BuildingIds.PowerPlant,
            BuildingIds.Extractor,
            BuildingIds.StorageDepot,
            BuildingIds.Smelter,
            BuildingIds.Refinery,
            BuildingIds.ElectronicsPlant,
            BuildingIds.LogisticsHub,
            BuildingIds.Barracks,
            BuildingIds.VehicleFactory,
            BuildingIds.AmmunitionPlant,
            BuildingIds.SupplyDepot,
            BuildingIds.Radar
        ];

        var completedBuildings =
            new Dictionary<BuildingId, EntityId>();

        for (int index = 0; index < requiredBuildings.Length; index++)
        {
            BuildingId buildingId = requiredBuildings[index];
            Vector3 position =
                new(
                    30.0f + (index % 5) * 45.0f,
                    0.0f,
                    30.0f + (index / 5) * 55.0f);

            if (buildingId == BuildingIds.Extractor)
            {
                _ = ResourceDepositSpawner.Place(
                    simulation.Entities,
                    new ResourceDepositPlacement(
                        ResourceIds.FerrousOre,
                        new AxisAlignedBounds(
                            position - new Vector3(12.0f, 1.0f, 12.0f),
                            position + new Vector3(12.0f, 1.0f, 12.0f)),
                        totalQuantity: 10_000.0,
                        baseExtractionRatePerSecond: 10.0));
            }

            var command =
                new BuildCommand(
                    Player,
                    buildingId,
                    position,
                    BuildingOrientation.North,
                    source,
                    simulation.CurrentTick);

            simulation.SubmitCommand(
                command,
                simulation.CurrentTick.Next(),
                new SimulationCommandSource(Player.Value));
            simulation.AdvanceOneTick();

            Assert.Equal(
                BuildCommandRejectionReason.None,
                buildingCommands.Metrics.LastRejection);

            EntityId site =
                buildingCommands.Metrics.LastCreatedSite;
            Assert.True(site.IsValid);

            BuildingDefinition definition =
                buildings[buildingId];

            if (definition.ConstructionTicks > 1)
            {
                simulation.RunTicks(
                    definition.ConstructionTicks - 1,
                    TestContext.Current.CancellationToken);
            }

            Assert.True(
                simulation.Entities.HasComponent<CompletedBuilding>(site));

            AssertPrimaryBuildingCapability(
                simulation,
                site,
                definition);

            completedBuildings.Add(
                buildingId,
                site);
        }

        Assert.Equal(
            requiredBuildings.Length,
            completedBuildings.Count);

        var logisticsNetwork = new LogisticsNetwork();
        var cargo =
            new CargoTransportSystem(
                logisticsNetwork,
                inventories);
        var unitFactory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargo);
        var unitProduction =
            new UnitProductionSystem(
                units,
                inventories,
                unitFactory);
        simulation.RegisterSystem(unitProduction);

        EntityId barracks =
            completedBuildings[BuildingIds.Barracks];
        EntityId vehicleFactory =
            completedBuildings[BuildingIds.VehicleFactory];

        FullyPowerFacility(
            simulation,
            barracks);
        FullyPowerFacility(
            simulation,
            vehicleFactory);

        FundUnitProductionFacility(
            simulation,
            inventories,
            barracks,
            units.Definitions.Where(
                definition =>
                    definition.RequiredProductionCapability ==
                    UnitProductionCapability.Infantry));
        FundUnitProductionFacility(
            simulation,
            inventories,
            vehicleFactory,
            units.Definitions.Where(
                definition =>
                    definition.RequiredProductionCapability !=
                    UnitProductionCapability.Infantry));

        uint totalProductionTicks = 0;
        foreach (UnitDefinition definition in units.Definitions)
        {
            EntityId facility =
                definition.RequiredProductionCapability ==
                UnitProductionCapability.Infantry
                    ? barracks
                    : vehicleFactory;

            simulation.SubmitCommand(
                new QueueUnitProductionCommand(
                    Player,
                    facility,
                    definition.Id,
                    simulation.CurrentTick),
                simulation.CurrentTick.Next(),
                new SimulationCommandSource(Player.Value));

            totalProductionTicks =
                checked(totalProductionTicks + definition.ProductionTicks);
        }

        simulation.RunTicks(
            checked((int)totalProductionTicks + 2),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            7UL,
            unitProduction.Metrics.CompletedUnits);

        var createdUnits =
            new Dictionary<UnitId, EntityId>();

        foreach (EntityId entity in
                 simulation.Entities.Query<UnitIdentity>())
        {
            UnitIdentity identity =
                simulation.Entities.GetComponent<UnitIdentity>(entity);

            createdUnits.Add(
                identity.UnitId,
                entity);

            Assert.True(
                simulation.Entities.HasComponent<GroundMovement>(entity));
            Assert.True(
                simulation.Entities.HasComponent<UnitFuelState>(entity));
            Assert.True(
                simulation.Entities.HasComponent<AmmunitionState>(entity));
            Assert.True(
                simulation.Entities.HasComponent<Combatant>(entity));
            Assert.True(
                simulation.Entities.HasComponent<IntelligenceSignature>(entity));
        }

        Assert.Equal(7, createdUnits.Count);
        Assert.True(
            simulation.Entities.HasComponent<WeaponState>(
                createdUnits[UnitIds.RifleSquad]));
        Assert.True(
            simulation.Entities.HasComponent<WeaponState>(
                createdUnits[UnitIds.CombatEngineer]));
        Assert.True(
            simulation.Entities.HasComponent<RadarSensorState>(
                createdUnits[UnitIds.ScoutVehicle]));
        Assert.True(
            simulation.Entities.HasComponent<ArmorState>(
                createdUnits[UnitIds.MainBattleTank]));
        Assert.True(
            simulation.Entities.HasComponent<ArtilleryCapability>(
                createdUnits[UnitIds.MobileArtillery]));
        Assert.True(
            simulation.Entities.HasComponent<CargoTransport>(
                createdUnits[UnitIds.CargoTruck]));
        Assert.True(
            simulation.Entities.HasComponent<SupplyTruck>(
                createdUnits[UnitIds.SupplyTruck]));

        VerifyBattlefieldResupply(
            simulation,
            inventories,
            completedBuildings[BuildingIds.SupplyDepot],
            createdUnits[UnitIds.SupplyTruck],
            createdUnits[UnitIds.MainBattleTank]);
    }

    private static void FullyPowerFacility(
        SimulationCoordinator simulation,
        EntityId facility)
    {
        PowerConsumer consumer =
            simulation.Entities.GetComponent<PowerConsumer>(facility);

        simulation.Entities.SetComponent(
            facility,
            new PowerConsumer(
                consumer.Demand,
                consumer.Priority,
                enabled: true,
                allocatedPower: consumer.Demand,
                state: PowerOperationalState.Powered));
    }

    private static void FundUnitProductionFacility(
        SimulationCoordinator simulation,
        InventoryStore inventories,
        EntityId facility,
        IEnumerable<UnitDefinition> definitions)
    {
        UnitProductionFacility production =
            simulation.Entities.GetComponent<UnitProductionFacility>(
                facility);

        foreach (UnitDefinition definition in definitions)
        {
            foreach (UnitResourceCost cost in definition.Costs)
            {
                Assert.True(
                    inventories.Add(
                        production.InputInventory,
                        cost.ResourceId,
                        cost.Quantity).Succeeded);
            }
        }
    }

    private static void VerifyBattlefieldResupply(
        SimulationCoordinator simulation,
        InventoryStore inventories,
        EntityId supplyDepotEntity,
        EntityId supplyTruckEntity,
        EntityId tankEntity)
    {
        SupplyDepot depot =
            simulation.Entities.GetComponent<SupplyDepot>(
                supplyDepotEntity);
        WorldTransform depotTransform =
            simulation.Entities.GetComponent<WorldTransform>(
                supplyDepotEntity);

        Assert.True(
            inventories.Add(
                depot.InventoryId,
                ResourceIds.Fuel,
                300.0).Succeeded);
        Assert.True(
            inventories.Add(
                depot.InventoryId,
                ResourceIds.Ammunition,
                300.0).Succeeded);

        simulation.Entities.SetComponent(
            supplyTruckEntity,
            simulation.Entities.GetComponent<WorldTransform>(
                supplyTruckEntity) with
            {
                Position = depotTransform.Position
            });

        var supply =
            new BattlefieldSupplySystem(inventories);
        simulation.RegisterSystem(supply);
        simulation.AdvanceOneTick();

        SupplyTruck supplyTruck =
            simulation.Entities.GetComponent<SupplyTruck>(
                supplyTruckEntity);

        Assert.True(
            inventories.GetQuantity(
                supplyTruck.InventoryId,
                ResourceIds.Fuel) > 0.0);
        Assert.True(
            inventories.GetQuantity(
                supplyTruck.InventoryId,
                ResourceIds.Ammunition) > 0.0);

        UnitFuelState tankFuel =
            simulation.Entities.GetComponent<UnitFuelState>(
                tankEntity);
        AmmunitionState tankAmmunition =
            simulation.Entities.GetComponent<AmmunitionState>(
                tankEntity);

        double fuelBefore =
            inventories.GetQuantity(
                tankFuel.InventoryId,
                ResourceIds.Fuel);
        double ammunitionBefore =
            inventories.GetQuantity(
                tankAmmunition.InventoryId,
                ResourceIds.Ammunition);

        Vector3 resupplyPosition =
            depotTransform.Position +
            new Vector3(30.0f, 0.0f, 0.0f);

        simulation.Entities.SetComponent(
            supplyTruckEntity,
            simulation.Entities.GetComponent<WorldTransform>(
                supplyTruckEntity) with
            {
                Position = resupplyPosition
            });
        simulation.Entities.SetComponent(
            tankEntity,
            simulation.Entities.GetComponent<WorldTransform>(
                tankEntity) with
            {
                Position = resupplyPosition
            });

        simulation.AdvanceOneTick();

        Assert.True(
            inventories.GetQuantity(
                tankFuel.InventoryId,
                ResourceIds.Fuel) >
            fuelBefore);
        Assert.True(
            inventories.GetQuantity(
                tankAmmunition.InventoryId,
                ResourceIds.Ammunition) >
            ammunitionBefore);
    }

    private static EntityId CreateFundedConstructionInventory(
        SimulationCoordinator simulation,
        InventoryStore inventories)
    {
        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(100_000.0));

        ResourceId[] resources =
        [
            ResourceIds.FerrousOre,
            ResourceIds.Silicates,
            ResourceIds.Volatiles,
            ResourceIds.Steel,
            ResourceIds.Fuel,
            ResourceIds.Electronics,
            ResourceIds.Ammunition
        ];

        foreach (ResourceId resource in resources)
        {
            Assert.True(
                inventories.Add(
                    inventory,
                    resource,
                    10_000.0).Succeeded);
        }

        EntityId source =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            source,
            new InventoryStorage(inventory));
        simulation.Entities.AddComponent(
            source,
            new StorageDepot(
                inventory,
                DirectorateContent.FactionId));

        return source;
    }

    private static void AssertPrimaryBuildingCapability(
        SimulationCoordinator simulation,
        EntityId entity,
        BuildingDefinition definition)
    {
        if (definition.Capabilities.HasFlag(BuildingCapability.Command))
        {
            Assert.True(
                simulation.Entities.HasComponent<CommandFacility>(entity));
        }

        if (definition.Capabilities.HasFlag(
                BuildingCapability.PowerGeneration))
        {
            Assert.True(
                simulation.Entities.HasComponent<PowerGenerator>(entity));
        }

        if (definition.Capabilities.HasFlag(
                BuildingCapability.PowerConsumption))
        {
            Assert.True(
                simulation.Entities.HasComponent<PowerConsumer>(entity));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Extraction))
        {
            Assert.True(
                simulation.Entities.HasComponent<ResourceExtractor>(entity));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Storage))
        {
            Assert.True(
                simulation.Entities.HasComponent<InventoryStorage>(entity));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Processing))
        {
            Assert.True(
                simulation.Entities.HasComponent<ProductionFacility>(entity));
        }

        if (definition.Capabilities.HasFlag(
                BuildingCapability.UnitProduction))
        {
            Assert.True(
                simulation.Entities.HasComponent<UnitProductionFacility>(
                    entity));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Radar))
        {
            Assert.True(
                simulation.Entities.HasComponent<RadarSensorState>(entity));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Supply))
        {
            Assert.True(
                simulation.Entities.HasComponent<SupplyProvider>(entity));
        }

        Assert.True(
            simulation.Entities.HasComponent<IntelligenceSignature>(entity));
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
}
