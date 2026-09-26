using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

public static class DirectorateContent
{
    public static readonly FactionId FactionId = new(1);

    public static class WeaponIds
    {
        public static readonly WeaponId Rifle = new(20_001);
        public static readonly WeaponId EngineerCarbine = new(20_002);
        public static readonly WeaponId ScoutAutocannon = new(20_003);
        public static readonly WeaponId MainBattleCannon = new(20_004);
        public static readonly WeaponId MobileArtillery = new(21_001);
    }

    public static class ArmorIds
    {
        public static readonly ArmorProfileId Infantry = new(20_001);
        public static readonly ArmorProfileId LightVehicle = new(20_002);
        public static readonly ArmorProfileId MainBattleTank = new(20_003);
        public static readonly ArmorProfileId MobileArtillery = new(20_004);
        public static readonly ArmorProfileId LogisticsVehicle = new(20_005);
    }

    public static FactionContentDefinition CreateFactionDefinition() =>
        new()
        {
            Id = FactionId,
            Key = "faction.directorate",
            ContentNamespace = "directorate",
            DisplayName = "Directorate",
            Buildings =
            [
                new(BuildingIds.CommandCore, ContentAvailabilityTier.Bootstrap),
                new(BuildingIds.PowerPlant, ContentAvailabilityTier.Bootstrap),
                new(BuildingIds.Extractor, ContentAvailabilityTier.Bootstrap),
                new(BuildingIds.StorageDepot, ContentAvailabilityTier.Bootstrap),
                new(BuildingIds.Smelter, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.Refinery, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.ElectronicsPlant, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.LogisticsHub, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.Barracks, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.VehicleFactory, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.AmmunitionPlant, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.SupplyDepot, ContentAvailabilityTier.IndustrialFoundation),
                new(BuildingIds.Radar, ContentAvailabilityTier.IndustrialFoundation)
            ],
            Units =
            [
                new(UnitIds.RifleSquad, ContentAvailabilityTier.IndustrialFoundation),
                new(UnitIds.CombatEngineer, ContentAvailabilityTier.IndustrialFoundation),
                new(UnitIds.ScoutVehicle, ContentAvailabilityTier.IndustrialFoundation),
                new(UnitIds.CargoTruck, ContentAvailabilityTier.IndustrialFoundation),
                new(UnitIds.SupplyTruck, ContentAvailabilityTier.IndustrialFoundation),
                new(UnitIds.MainBattleTank, ContentAvailabilityTier.MechanizedWarfare),
                new(UnitIds.MobileArtillery, ContentAvailabilityTier.MechanizedWarfare)
            ]
        };

    public static BuildingDefinitionCatalog CreateBuildingCatalog() =>
        InitialBuildingDefinitions.CreateCatalog();

    public static UnitDefinitionCatalog CreateUnitCatalog() =>
        new(
        [
            CreateRifleSquad(),
            CreateCombatEngineer(),
            CreateScoutVehicle(),
            CreateMainBattleTank(),
            CreateMobileArtillery(),
            CreateCargoTruck(),
            CreateSupplyTruck()
        ]);

    public static WeaponCatalog CreateWeaponCatalog()
    {
        var catalog = new WeaponCatalog();

        catalog.Add(
            new WeaponDefinition(
                WeaponIds.Rifle,
                rangeMeters: 115.0f,
                fireIntervalTicks: 5,
                ammunitionPerShot: 1.0,
                new DamagePayload(8.0),
                WeaponDeliveryModel.Hitscan,
                magazineSize: 8,
                reloadTicks: 18,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.Infantry |
                        TargetClassMask.LightVehicle,
                        penetration: 20.0)));

        catalog.Add(
            new WeaponDefinition(
                WeaponIds.EngineerCarbine,
                rangeMeters: 95.0f,
                fireIntervalTicks: 6,
                ammunitionPerShot: 1.0,
                new DamagePayload(7.0),
                WeaponDeliveryModel.Hitscan,
                magazineSize: 7,
                reloadTicks: 18,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.Infantry |
                        TargetClassMask.LightVehicle,
                        penetration: 18.0)));

        catalog.Add(
            new WeaponDefinition(
                WeaponIds.ScoutAutocannon,
                rangeMeters: 180.0f,
                fireIntervalTicks: 4,
                ammunitionPerShot: 1.0,
                new DamagePayload(14.0),
                WeaponDeliveryModel.Hitscan,
                magazineSize: 12,
                reloadTicks: 20,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.Infantry |
                        TargetClassMask.LightVehicle |
                        TargetClassMask.ArmoredVehicle,
                        penetration: 48.0)));

        catalog.Add(
            new WeaponDefinition(
                WeaponIds.MainBattleCannon,
                rangeMeters: 260.0f,
                fireIntervalTicks: 28,
                ammunitionPerShot: 1.0,
                new DamagePayload(115.0),
                WeaponDeliveryModel.PhysicalProjectile,
                magazineSize: 1,
                reloadTicks: 20,
                projectileSpeedMetersPerSecond: 190.0f,
                projectileRadiusMeters: 0.2f,
                projectileLifetimeTicks: 80,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.LightVehicle |
                        TargetClassMask.ArmoredVehicle |
                        TargetClassMask.Structure,
                        penetration: 185.0)));

        return catalog;
    }

    public static ArtilleryWeaponCatalog CreateArtilleryWeaponCatalog()
    {
        var catalog = new ArtilleryWeaponCatalog();

        catalog.Add(
            new ArtilleryWeaponDefinition(
                WeaponIds.MobileArtillery,
                minimumRangeMeters: 90.0f,
                maximumRangeMeters: 700.0f,
                fireIntervalTicks: 42,
                acquisitionTicks: 12,
                ammunitionPerShot: 1.0,
                new DamagePayload(125.0),
                areaRadiusMeters: 28.0f,
                minimumDamageFraction: 0.25,
                projectileSpeedMetersPerSecond: 155.0f,
                apexHeightMeters: 90.0f,
                dispersionRadiusMeters: 8.0f,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.All,
                        penetration: 125.0)));

        return catalog;
    }

    public static ArmorCatalog CreateArmorCatalog()
    {
        var catalog = new ArmorCatalog();

        catalog.Add(
            new ArmorProfileDefinition(
                ArmorIds.Infantry,
                front: 12.0,
                side: 10.0,
                rear: 8.0,
                top: 5.0));
        catalog.Add(
            new ArmorProfileDefinition(
                ArmorIds.LightVehicle,
                front: 45.0,
                side: 30.0,
                rear: 20.0,
                top: 15.0));
        catalog.Add(
            new ArmorProfileDefinition(
                ArmorIds.MainBattleTank,
                front: 175.0,
                side: 105.0,
                rear: 70.0,
                top: 45.0));
        catalog.Add(
            new ArmorProfileDefinition(
                ArmorIds.MobileArtillery,
                front: 65.0,
                side: 45.0,
                rear: 30.0,
                top: 22.0));
        catalog.Add(
            new ArmorProfileDefinition(
                ArmorIds.LogisticsVehicle,
                front: 28.0,
                side: 20.0,
                rear: 16.0,
                top: 12.0));

        return catalog;
    }

    private static UnitDefinition CreateRifleSquad() =>
        new()
        {
            Id = UnitIds.RifleSquad,
            Faction = FactionId,
            Key = "directorate.unit.rifle_squad",
            DisplayName = "Rifle Squad",
            RequiredProductionCapability = UnitProductionCapability.Infantry,
            ProductionTicks = 90,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 24.0),
                new UnitResourceCost(ResourceIds.Electronics, 6.0),
                new UnitResourceCost(ResourceIds.Fuel, 5.0),
                new UnitResourceCost(ResourceIds.Ammunition, 30.0)
            ],
            MovementClass = NavigationMovementClass.Infantry,
            Movement =
                new GroundMovement(
                    maximumSpeed: 7.2f,
                    acceleration: 8.0f,
                    deceleration: 10.0f,
                    turnRateRadiansPerSecond: MathF.PI * 1.5f,
                    radius: 0.9f,
                    stopRadius: 0.6f,
                    separationRadius: 2.4f,
                    obstacleLookAhead: 4.0f,
                    maximumSlopeDegrees: 45.0f,
                    heightOffset: 0.9f),
            VisualScale = new Vector3(1.8f, 1.8f, 1.8f),
            VisualId = 201,
            TargetClass = TargetClass.Infantry,
            MaximumHealth = 95.0,
            ArmorProfileId = ArmorIds.Infantry,
            WeaponId = WeaponIds.Rifle,
            VisualSensorRangeMeters = 150.0f,
            FuelCapacity = 10.0,
            AmmunitionCapacity = 60.0,
            FuelConsumptionPerMeter = 0.005,
            InitialFuelFraction = 0.5,
            InitialAmmunitionFraction = 0.5,
            TargetPriority = 40
        };

    private static UnitDefinition CreateCombatEngineer() =>
        new()
        {
            Id = UnitIds.CombatEngineer,
            Faction = FactionId,
            Key = "directorate.unit.combat_engineer",
            DisplayName = "Combat Engineer",
            RequiredProductionCapability = UnitProductionCapability.Infantry,
            ProductionTicks = 100,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 28.0),
                new UnitResourceCost(ResourceIds.Electronics, 12.0),
                new UnitResourceCost(ResourceIds.Fuel, 4.0),
                new UnitResourceCost(ResourceIds.Ammunition, 20.0)
            ],
            MovementClass = NavigationMovementClass.Infantry,
            Movement =
                new GroundMovement(
                    maximumSpeed: 6.8f,
                    acceleration: 7.5f,
                    deceleration: 10.0f,
                    turnRateRadiansPerSecond: MathF.PI * 1.5f,
                    radius: 0.9f,
                    stopRadius: 0.6f,
                    separationRadius: 2.4f,
                    obstacleLookAhead: 4.0f,
                    maximumSlopeDegrees: 45.0f,
                    heightOffset: 0.9f),
            VisualScale = new Vector3(1.8f, 1.8f, 1.8f),
            VisualId = 202,
            TargetClass = TargetClass.Infantry,
            MaximumHealth = 90.0,
            ArmorProfileId = ArmorIds.Infantry,
            WeaponId = WeaponIds.EngineerCarbine,
            VisualSensorRangeMeters = 165.0f,
            FuelCapacity = 8.0,
            AmmunitionCapacity = 40.0,
            FuelConsumptionPerMeter = 0.004,
            InitialFuelFraction = 0.5,
            InitialAmmunitionFraction = 0.5,
            TargetPriority = 35
        };

    private static UnitDefinition CreateScoutVehicle() =>
        new()
        {
            Id = UnitIds.ScoutVehicle,
            Faction = FactionId,
            Key = "directorate.unit.scout_vehicle",
            DisplayName = "Scout Vehicle",
            RequiredProductionCapability = UnitProductionCapability.Vehicle,
            ProductionTicks = 120,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 70.0),
                new UnitResourceCost(ResourceIds.Electronics, 24.0),
                new UnitResourceCost(ResourceIds.Fuel, 60.0),
                new UnitResourceCost(ResourceIds.Ammunition, 40.0)
            ],
            MovementClass = NavigationMovementClass.Wheeled,
            Movement =
                new GroundMovement(
                    maximumSpeed: 18.0f,
                    acceleration: 9.0f,
                    deceleration: 12.0f,
                    turnRateRadiansPerSecond: 2.8f,
                    radius: 1.7f,
                    stopRadius: 0.9f,
                    separationRadius: 5.0f,
                    obstacleLookAhead: 9.0f,
                    maximumSlopeDegrees: 24.0f,
                    heightOffset: 1.2f),
            VisualScale = new Vector3(3.0f, 2.4f, 5.2f),
            VisualId = 203,
            TargetClass = TargetClass.LightVehicle,
            MaximumHealth = 170.0,
            ArmorProfileId = ArmorIds.LightVehicle,
            WeaponId = WeaponIds.ScoutAutocannon,
            VisualSensorRangeMeters = 320.0f,
            RadarDetectionRangeMeters = 380.0f,
            RadarIdentificationRangeMeters = 130.0f,
            RadarUpdateIntervalTicks = 2,
            FuelCapacity = 100.0,
            AmmunitionCapacity = 80.0,
            FuelConsumptionPerMeter = 0.075,
            InitialFuelFraction = 0.6,
            InitialAmmunitionFraction = 0.5,
            SupplyPriority = BattlefieldSupplyPriority.High,
            TargetPriority = 55
        };

    private static UnitDefinition CreateMainBattleTank() =>
        new()
        {
            Id = UnitIds.MainBattleTank,
            Faction = FactionId,
            Key = "directorate.unit.main_battle_tank",
            DisplayName = "Main Battle Tank",
            RequiredProductionCapability = UnitProductionCapability.Vehicle,
            ProductionTicks = 220,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 210.0),
                new UnitResourceCost(ResourceIds.Electronics, 65.0),
                new UnitResourceCost(ResourceIds.Fuel, 108.0),
                new UnitResourceCost(ResourceIds.Ammunition, 20.0)
            ],
            MovementClass = NavigationMovementClass.Tracked,
            Movement =
                new GroundMovement(
                    maximumSpeed: 10.5f,
                    acceleration: 5.0f,
                    deceleration: 8.0f,
                    turnRateRadiansPerSecond: 1.65f,
                    radius: 2.4f,
                    stopRadius: 1.2f,
                    separationRadius: 6.5f,
                    obstacleLookAhead: 10.0f,
                    maximumSlopeDegrees: 35.0f,
                    heightOffset: 1.5f),
            VisualScale = new Vector3(4.0f, 3.0f, 7.2f),
            VisualId = 204,
            TargetClass = TargetClass.ArmoredVehicle,
            MaximumHealth = 560.0,
            ArmorProfileId = ArmorIds.MainBattleTank,
            WeaponId = WeaponIds.MainBattleCannon,
            VisualSensorRangeMeters = 250.0f,
            FuelCapacity = 180.0,
            AmmunitionCapacity = 40.0,
            FuelConsumptionPerMeter = 0.18,
            InitialFuelFraction = 0.6,
            InitialAmmunitionFraction = 0.5,
            SupplyPriority = BattlefieldSupplyPriority.High,
            TargetPriority = 75
        };

    private static UnitDefinition CreateMobileArtillery() =>
        new()
        {
            Id = UnitIds.MobileArtillery,
            Faction = FactionId,
            Key = "directorate.unit.mobile_artillery",
            DisplayName = "Mobile Artillery",
            RequiredProductionCapability = UnitProductionCapability.Vehicle,
            ProductionTicks = 200,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 150.0),
                new UnitResourceCost(ResourceIds.Electronics, 55.0),
                new UnitResourceCost(ResourceIds.Fuel, 90.0),
                new UnitResourceCost(ResourceIds.Ammunition, 18.0)
            ],
            MovementClass = NavigationMovementClass.Tracked,
            Movement =
                new GroundMovement(
                    maximumSpeed: 9.0f,
                    acceleration: 4.5f,
                    deceleration: 7.0f,
                    turnRateRadiansPerSecond: 1.55f,
                    radius: 2.2f,
                    stopRadius: 1.2f,
                    separationRadius: 6.0f,
                    obstacleLookAhead: 9.0f,
                    maximumSlopeDegrees: 32.0f,
                    heightOffset: 1.4f),
            VisualScale = new Vector3(3.8f, 3.2f, 7.5f),
            VisualId = 205,
            TargetClass = TargetClass.ArmoredVehicle,
            MaximumHealth = 310.0,
            ArmorProfileId = ArmorIds.MobileArtillery,
            ArtilleryWeaponId = WeaponIds.MobileArtillery,
            VisualSensorRangeMeters = 210.0f,
            FuelCapacity = 150.0,
            AmmunitionCapacity = 36.0,
            FuelConsumptionPerMeter = 0.15,
            InitialFuelFraction = 0.6,
            InitialAmmunitionFraction = 0.5,
            SupplyPriority = BattlefieldSupplyPriority.High,
            TargetPriority = 80
        };

    private static UnitDefinition CreateCargoTruck() =>
        new()
        {
            Id = UnitIds.CargoTruck,
            Faction = FactionId,
            Key = "directorate.unit.cargo_truck",
            DisplayName = "Cargo Truck",
            RequiredProductionCapability = UnitProductionCapability.Logistics,
            ProductionTicks = 100,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 55.0),
                new UnitResourceCost(ResourceIds.Electronics, 12.0),
                new UnitResourceCost(ResourceIds.Fuel, 120.0)
            ],
            MovementClass = NavigationMovementClass.Wheeled,
            Movement =
                new GroundMovement(
                    maximumSpeed: 14.5f,
                    acceleration: 7.0f,
                    deceleration: 10.0f,
                    turnRateRadiansPerSecond: 2.5f,
                    radius: 1.9f,
                    stopRadius: 1.0f,
                    separationRadius: 5.5f,
                    obstacleLookAhead: 8.0f,
                    maximumSlopeDegrees: 24.0f,
                    heightOffset: 1.1f),
            VisualScale = new Vector3(3.2f, 2.4f, 6.4f),
            VisualId = 206,
            TargetClass = TargetClass.LightVehicle,
            MaximumHealth = 145.0,
            ArmorProfileId = ArmorIds.LogisticsVehicle,
            VisualSensorRangeMeters = 170.0f,
            FuelCapacity = 120.0,
            AmmunitionCapacity = 1.0,
            FuelConsumptionPerMeter = 0.085,
            InitialFuelFraction = 1.0,
            InitialAmmunitionFraction = 0.0,
            CargoCapacity = 220.0,
            AutomaticResupply = true,
            TargetPriority = 30
        };

    private static UnitDefinition CreateSupplyTruck() =>
        new()
        {
            Id = UnitIds.SupplyTruck,
            Faction = FactionId,
            Key = "directorate.unit.supply_truck",
            DisplayName = "Supply Truck",
            RequiredProductionCapability = UnitProductionCapability.Logistics,
            ProductionTicks = 115,
            Costs =
            [
                new UnitResourceCost(ResourceIds.Steel, 65.0),
                new UnitResourceCost(ResourceIds.Electronics, 18.0),
                new UnitResourceCost(ResourceIds.Fuel, 120.0)
            ],
            MovementClass = NavigationMovementClass.Wheeled,
            Movement =
                new GroundMovement(
                    maximumSpeed: 13.0f,
                    acceleration: 6.5f,
                    deceleration: 10.0f,
                    turnRateRadiansPerSecond: 2.35f,
                    radius: 2.0f,
                    stopRadius: 1.0f,
                    separationRadius: 5.8f,
                    obstacleLookAhead: 8.0f,
                    maximumSlopeDegrees: 24.0f,
                    heightOffset: 1.1f),
            VisualScale = new Vector3(3.4f, 2.5f, 6.8f),
            VisualId = 207,
            TargetClass = TargetClass.LightVehicle,
            MaximumHealth = 155.0,
            ArmorProfileId = ArmorIds.LogisticsVehicle,
            VisualSensorRangeMeters = 180.0f,
            FuelCapacity = 120.0,
            AmmunitionCapacity = 1.0,
            FuelConsumptionPerMeter = 0.09,
            InitialFuelFraction = 1.0,
            InitialAmmunitionFraction = 0.0,
            CargoCapacity = 240.0,
            IsSupplyTruck = true,
            SupplyFuelTarget = 110.0,
            SupplyAmmunitionTarget = 120.0,
            SupplyLoadRangeMeters = 12.0f,
            SupplyRangeMeters = 18.0f,
            AutomaticResupply = false,
            SupplyPriority = BattlefieldSupplyPriority.Critical,
            TargetPriority = 35
        };
}
