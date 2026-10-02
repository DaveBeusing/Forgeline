using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DirectorateContentTests
{
    private static readonly PlayerId DirectoratePlayer = new(1);

    [Fact]
    public void CompleteDirectorateCatalogPassesCrossCatalogValidation()
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

        Assert.Equal(13, buildings.Count);
        Assert.Equal(7, units.Count);
        Assert.Equal(
            DirectorateContent.FactionId,
            faction.Id);
        Assert.Equal(
            "directorate",
            faction.ContentNamespace);
    }

    [Fact]
    public void ProductionMenusFollowVerticalSliceAvailability()
    {
        FactionContentDefinition faction =
            DirectorateContent.CreateFactionDefinition();
        BuildingDefinitionCatalog buildings =
            DirectorateContent.CreateBuildingCatalog();
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();

        IReadOnlyList<UnitId> barracks =
            faction.GetUnitProductionMenu(
                buildings[BuildingIds.Barracks],
                units,
                ContentAvailabilityTier.IndustrialFoundation);
        IReadOnlyList<UnitId> industrialVehicles =
            faction.GetUnitProductionMenu(
                buildings[BuildingIds.VehicleFactory],
                units,
                ContentAvailabilityTier.IndustrialFoundation);
        IReadOnlyList<UnitId> mechanizedVehicles =
            faction.GetUnitProductionMenu(
                buildings[BuildingIds.VehicleFactory],
                units,
                ContentAvailabilityTier.MechanizedWarfare);

        Assert.Equal(
            new[] { UnitIds.RifleSquad, UnitIds.CombatEngineer },
            barracks);
        Assert.Equal(
            new[]
            {
                UnitIds.ScoutVehicle,
                UnitIds.CargoTruck,
                UnitIds.SupplyTruck
            },
            industrialVehicles);
        Assert.Contains(
            UnitIds.MainBattleTank,
            mechanizedVehicles);
        Assert.Contains(
            UnitIds.MobileArtillery,
            mechanizedVehicles);
    }

    [Fact]
    public void LogisticsTransportsKeepCriticalFuelPriority()
    {
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();

        Assert.Equal(
            BattlefieldSupplyPriority.Critical,
            units[UnitIds.CargoTruck].SupplyPriority);
        Assert.Equal(
            BattlefieldSupplyPriority.Critical,
            units[UnitIds.SupplyTruck].SupplyPriority);
    }

    [Fact]
    public void GenericUnitFactoryComposesDirectorateUnitsFromExistingSystems()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo =
            new CargoTransportSystem(
                network,
                inventories);
        var factory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargo);
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();

        EntityId tank =
            factory.Create(
                units[UnitIds.MainBattleTank],
                Vector3.Zero,
                DirectoratePlayer);
        EntityId artillery =
            factory.Create(
                units[UnitIds.MobileArtillery],
                new Vector3(10.0f, 0.0f, 0.0f),
                DirectoratePlayer);
        EntityId supplyTruck =
            factory.Create(
                units[UnitIds.SupplyTruck],
                new Vector3(20.0f, 0.0f, 0.0f),
                DirectoratePlayer);

        Assert.True(
            simulation.Entities.HasComponent<GroundMovement>(tank));
        Assert.True(
            simulation.Entities.HasComponent<NavigationAgent>(tank));
        Assert.True(
            simulation.Entities.HasComponent<WeaponState>(tank));
        Assert.True(
            simulation.Entities.HasComponent<ArmorState>(tank));
        Assert.True(
            simulation.Entities.HasComponent<VisualSensorState>(tank));
        Assert.True(
            simulation.Entities.HasComponent<UnitFuelState>(tank));
        Assert.True(
            simulation.Entities.HasComponent<AmmunitionState>(tank));
        Assert.True(
            simulation.Entities.HasComponent<AutomaticResupplyPolicy>(tank));
        AutomaticResupplyPolicy tankResupply =
            simulation.Entities.GetComponent<AutomaticResupplyPolicy>(tank);
        Assert.True(tankResupply.Enabled);
        Assert.Equal(0.2, tankResupply.FuelThreshold);
        Assert.Equal(0.2, tankResupply.AmmunitionThreshold);

        Assert.True(
            simulation.Entities.HasComponent<ArtilleryCapability>(artillery));
        Assert.False(
            simulation.Entities.HasComponent<WeaponState>(artillery));

        Assert.True(
            simulation.Entities.HasComponent<CargoTransport>(supplyTruck));
        Assert.True(
            simulation.Entities.HasComponent<SupplyTruck>(supplyTruck));
        Assert.True(
            simulation.Entities.HasComponent<SupplyProvider>(supplyTruck));

        UnitFuelState tankFuel =
            simulation.Entities.GetComponent<UnitFuelState>(tank);
        AmmunitionState tankAmmo =
            simulation.Entities.GetComponent<AmmunitionState>(tank);

        Assert.Equal(
            108.0,
            inventories.GetQuantity(
                tankFuel.InventoryId,
                ResourceIds.Fuel),
            precision: 6);
        Assert.Equal(
            20.0,
            inventories.GetQuantity(
                tankAmmo.InventoryId,
                ResourceIds.Ammunition),
            precision: 6);
    }

    [Fact]
    public void VerticalSliceUnitGameplayFootprintsRemainSaneAndIndependentFromRenderLod()
    {
        var simulation =
            new SimulationCoordinator();
        var inventories =
            new InventoryStore();
        var cargo =
            new CargoTransportSystem(
                new LogisticsNetwork(),
                inventories);
        var factory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargo);
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        UnitId[] ids =
        [
            UnitIds.RifleSquad,
            UnitIds.ScoutVehicle,
            UnitIds.MainBattleTank,
            UnitIds.MobileArtillery,
            UnitIds.CargoTruck,
            UnitIds.SupplyTruck
        ];

        for (int index = 0;
             index < ids.Length;
             index++)
        {
            UnitDefinition definition =
                units[ids[index]];
            EntityId entity =
                factory.Create(
                    definition,
                    new Vector3(
                        index * 20.0f,
                        0.0f,
                        0.0f),
                    DirectoratePlayer);

            CombatHitbox hitbox =
                simulation.Entities.GetComponent<CombatHitbox>(
                    entity);
            GroundMovement movement =
                simulation.Entities.GetComponent<GroundMovement>(
                    entity);
            WorldTransform transform =
                simulation.Entities.GetComponent<WorldTransform>(
                    entity);

            Assert.Equal(
                definition.VisualScale,
                transform.Scale);
            Assert.Equal(
                definition.VisualScale * 0.5f,
                hitbox.HalfExtents);
            Assert.True(
                movement.Radius > 0.0f);
            Assert.True(
                movement.Radius <=
                MathF.Max(
                    hitbox.HalfExtents.X,
                    hitbox.HalfExtents.Z));
        }
    }

    [Fact]
    public void UnitProductionConsumesPhysicalInputsAndSpawnsConfiguredUnit()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo =
            new CargoTransportSystem(
                network,
                inventories);
        var factory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargo);
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        var production =
            new UnitProductionSystem(
                units,
                inventories,
                factory);

        simulation.RegisterSystem(production);

        UnitDefinition tank =
            units[UnitIds.MainBattleTank];

        InventoryId input =
            inventories.CreateInventory(
                new InventorySpecification(2_000.0));

        foreach (UnitResourceCost cost in tank.Costs)
        {
            Assert.True(
                inventories.Add(
                    input,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }

        EntityId facility =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            facility,
            new WorldTransform(
                new Vector3(50.0f, 0.0f, 50.0f),
                Quaternion.Identity,
                new Vector3(20.0f, 10.0f, 20.0f)));
        simulation.Entities.AddComponent(
            facility,
            new PowerConsumer(
                demand: 40.0,
                allocatedPower: 40.0,
                state: PowerOperationalState.Powered));
        simulation.Entities.AddComponent(
            facility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                DirectoratePlayer,
                new Vector3(0.0f, 0.0f, 18.0f),
                SimulationTick.Zero));

        var command =
            new QueueUnitProductionCommand(
                DirectoratePlayer,
                facility,
                UnitIds.MainBattleTank,
                simulation.CurrentTick);

        simulation.SubmitCommand(
            command,
            simulation.CurrentTick.Next());

        simulation.RunTicks(
            checked((ulong)tank.ProductionTicks + 1UL),
            TestContext.Current.CancellationToken);

        Assert.True(command.Accepted);
        Assert.False(
            simulation.Entities.IsAlive(
                command.RequestEntity));

        EntityId produced = EntityId.Invalid;
        foreach (EntityId entity in
                 simulation.Entities.Query<UnitIdentity>())
        {
            UnitIdentity identity =
                simulation.Entities.GetComponent<UnitIdentity>(entity);
            if (identity.UnitId == UnitIds.MainBattleTank)
            {
                produced = entity;
                break;
            }
        }

        Assert.True(produced.IsValid);
        Assert.True(
            simulation.Entities.HasComponent<WeaponState>(produced));
        Assert.True(
            simulation.Entities.HasComponent<UnitFuelState>(produced));

        foreach (UnitResourceCost cost in tank.Costs)
        {
            Assert.Equal(
                0.0,
                inventories.GetQuantity(
                    input,
                    cost.ResourceId),
                precision: 6);
        }

        Assert.Equal(
            1L,
            production.Metrics.CompletedUnits);
    }

    [Fact]
    public void UnitProductionRallyPointRoutesProducedUnitThroughMovementOrder()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo =
            new CargoTransportSystem(
                network,
                inventories);
        var factory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargo);
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        var production =
            new UnitProductionSystem(
                units,
                inventories,
                factory);

        simulation.RegisterSystem(production);

        UnitDefinition scout =
            units[UnitIds.ScoutVehicle];
        InventoryId input =
            inventories.CreateInventory(
                new InventorySpecification(2_000.0));

        foreach (UnitResourceCost cost in scout.Costs)
        {
            Assert.True(
                inventories.Add(
                    input,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }

        EntityId facility =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            facility,
            new WorldTransform(
                new Vector3(50.0f, 0.0f, 50.0f),
                Quaternion.Identity,
                new Vector3(20.0f, 10.0f, 20.0f)));
        simulation.Entities.AddComponent(
            facility,
            new PowerConsumer(
                demand: 40.0,
                allocatedPower: 40.0,
                state: PowerOperationalState.Powered));
        simulation.Entities.AddComponent(
            facility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                DirectoratePlayer,
                new Vector3(0.0f, 0.0f, 18.0f),
                SimulationTick.Zero));

        var rallyPoint =
            new Vector3(180.0f, 0.0f, 125.0f);
        var rallyCommand =
            new SetUnitProductionRallyPointCommand(
                DirectoratePlayer,
                facility,
                rallyPoint,
                simulation.CurrentTick);

        simulation.SubmitCommand(
            rallyCommand,
            simulation.CurrentTick.Next(),
            new SimulationCommandSource(
                DirectoratePlayer.Value));
        simulation.AdvanceOneTick();

        Assert.True(rallyCommand.Accepted);

        var queueCommand =
            new QueueUnitProductionCommand(
                DirectoratePlayer,
                facility,
                scout.Id,
                simulation.CurrentTick);
        simulation.SubmitCommand(
            queueCommand,
            simulation.CurrentTick.Next(),
            new SimulationCommandSource(
                DirectoratePlayer.Value));

        simulation.RunTicks(
            checked((ulong)scout.ProductionTicks + 1UL),
            TestContext.Current.CancellationToken);

        EntityId produced = EntityId.Invalid;
        foreach (EntityId entity in
                 simulation.Entities.Query<UnitIdentity>())
        {
            UnitIdentity identity =
                simulation.Entities.GetComponent<UnitIdentity>(
                    entity);

            if (identity.UnitId == scout.Id)
            {
                produced = entity;
                break;
            }
        }

        Assert.True(produced.IsValid);

        MovementOrder movement =
            simulation.Entities.GetComponent<MovementOrder>(
                produced);
        Assert.Equal(
            DirectoratePlayer,
            movement.Issuer);
        Assert.Equal(
            rallyPoint,
            movement.WorldTarget);

        UnitProductionFacilityReadModel facilityState =
            Assert.Single(
                production.Facilities,
                state =>
                    state.Entity ==
                    facility);
        Assert.Equal(
            rallyPoint,
            facilityState.RallyPoint);
    }

    [Fact]
    public void UnitProductionStopsWithoutPowerAndResumesWhenPowerReturns()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo =
            new CargoTransportSystem(
                network,
                inventories);
        var factory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargo);
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        var production =
            new UnitProductionSystem(
                units,
                inventories,
                factory);
        simulation.RegisterSystem(production);

        UnitDefinition rifle =
            units[UnitIds.RifleSquad];
        InventoryId input =
            inventories.CreateInventory(
                new InventorySpecification(1_000.0));

        foreach (UnitResourceCost cost in rifle.Costs)
        {
            Assert.True(
                inventories.Add(
                    input,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }

        EntityId barracks =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            barracks,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            barracks,
            new PowerConsumer(
                demand: 20.0));
        simulation.Entities.AddComponent(
            barracks,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Infantry,
                DirectoratePlayer,
                new Vector3(0.0f, 0.0f, 10.0f),
                SimulationTick.Zero));

        simulation.SubmitCommand(
            new QueueUnitProductionCommand(
                DirectoratePlayer,
                barracks,
                UnitIds.RifleSquad,
                SimulationTick.Zero),
            new SimulationTick(1));

        simulation.RunTicks(
            10,
            TestContext.Current.CancellationToken);

        UnitProductionFacility blocked =
            simulation.Entities.GetComponent<UnitProductionFacility>(
                barracks);

        Assert.Equal(
            UnitProductionStatus.NoPower,
            blocked.Status);
        Assert.Equal(0U, blocked.ProgressTicks);

        simulation.Entities.SetComponent(
            barracks,
            new PowerConsumer(
                demand: 20.0,
                allocatedPower: 20.0,
                state: PowerOperationalState.Powered));

        simulation.RunTicks(
            checked((ulong)rifle.ProductionTicks),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            1L,
            production.Metrics.CompletedUnits);
    }

    [Fact]
    public void UnitProductionBuildingRegistersAsLogisticsDestination()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var registration =
            new BuildingLogisticsRegistrationSystem(
                network);
        simulation.RegisterSystem(registration);

        InventoryId input =
            inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        EntityId factory =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            factory,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                DirectoratePlayer,
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                DirectoratePlayer,
                new Vector3(0.0f, 0.0f, 10.0f),
                SimulationTick.Zero));

        simulation.AdvanceOneTick();

        Assert.True(
            network.TryGetNodeForEntity(
                factory,
                out LogisticsNodeId nodeId));
        Assert.True(
            network.TryGetNode(
                nodeId,
                out LogisticsNode node));
        Assert.True(
            node.Capabilities.HasFlag(
                LogisticsNodeCapabilities.CargoDestination));
        Assert.True(
            node.Capabilities.HasFlag(
                LogisticsNodeCapabilities.Processing));
        Assert.False(
            node.Capabilities.HasFlag(
                LogisticsNodeCapabilities.CargoSource));
    }
}
