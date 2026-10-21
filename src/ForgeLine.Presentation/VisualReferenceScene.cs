using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum VisualReferenceSceneKind { DaylightTerrain, IndustrialBase, FormationConvoy, LogisticsCorridor, DamageDestruction, BattlefieldCombat, MechanicalIdle, MechanicalMoving, MechanicalFiring, MechanicalLoading, MechanicalUnpowered, MechanicalBroken, DirectorateUnits, DirectorateBuildingStates, CommandCoreProduction, MainBattleTankProduction, MainBattleTankFormation, EnvironmentKitYard, EnvironmentKitBridge, EnvironmentKitRepeated, ExtractorProduction, SmelterProduction, ElectronicsPlantProduction, FuelRefineryProduction, AmmunitionPlantProduction, StorageDepotProduction, SupplyDepotProduction, LogisticsHubProduction, LogisticsLoadingProduction, LogisticsTransitProduction, LogisticsUnloadingProduction, LogisticsSustainedProduction, VehicleFactoryProduction, PowerPlantProduction, BarracksProduction, RadarProduction, OperationalBuildingsRepeated, InfantryRifleProduction, InfantryEngineerProduction, InfantryRepeated, InfantryBattleProduction, BattlefieldVariantProduction, ScoutVehicleProduction, ArtilleryProduction, CargoTruckProduction, SupplyTruckProduction, CombatSupportRepeated, MaterialFoundation, EffectsBarrage, IndustrialEffects, InfantrySkinning, InfantrySkinningRepeated, InfantrySkinningEngineer, CommandCoreSingle, CommandCoreRepeated }

// Authored presentation acceptance states, not economic/combat simulations or gameplay presets.
public sealed record VisualReferenceScene(VisualReferenceSceneKind Kind, Vector3 Target,
    PresentationSnapshot Snapshot, EntityId HiddenEnemy, string Fingerprint)
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };
    public const ulong Seed = 2026;
    public const int FixedTick = 32;
    public const int FixtureVersion = 7;

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, bool includeHiddenEnemy = true, RuntimeAssetCatalog? runtimeAssets = null, double? mechanicalSeconds = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (mechanicalSeconds is double seconds && (!double.IsFinite(seconds) || seconds is < 0 or > RuntimeAnimationData.MaximumDuration ||
            kind is not (VisualReferenceSceneKind.MainBattleTankProduction or VisualReferenceSceneKind.MainBattleTankFormation or VisualReferenceSceneKind.InfantrySkinning or VisualReferenceSceneKind.InfantrySkinningRepeated or VisualReferenceSceneKind.InfantrySkinningEngineer)))
            throw new ArgumentOutOfRangeException(nameof(mechanicalSeconds));
        ArgumentNullException.ThrowIfNull(terrain);
        if (kind is VisualReferenceSceneKind.CommandCoreSingle or VisualReferenceSceneKind.CommandCoreRepeated)
            return CommandCoreReferenceScene.Create(kind, terrain, runtimeAssets);
        if (kind is VisualReferenceSceneKind.InfantrySkinning or VisualReferenceSceneKind.InfantrySkinningRepeated or VisualReferenceSceneKind.InfantrySkinningEngineer)
            return InfantrySkinReferenceScene.Create(kind, terrain, mechanicalSeconds ?? .375);
        if (kind is VisualReferenceSceneKind.EffectsBarrage or VisualReferenceSceneKind.IndustrialEffects)
            return EffectReferenceScene.Create(kind, terrain, includeHiddenEnemy, runtimeAssets);
        if (kind == VisualReferenceSceneKind.MaterialFoundation)
        {
            var materialSnapshot = new PresentationSnapshot(new SimulationTick(FixedTick), TimeSpan.FromMilliseconds(50), 0, ReadOnlySpan<RenderInstance>.Empty);
            var materialTarget = new Vector3(650, 0, 520);
            string materialFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, Seed, target = materialTarget, materials = MaterialReferenceRenderer.Materials }, FingerprintOptions)));
            return new(kind, materialTarget, materialSnapshot, default, materialFingerprint);
        }
        if (kind is >= VisualReferenceSceneKind.ScoutVehicleProduction and <= VisualReferenceSceneKind.CombatSupportRepeated)
            return CombatSupportReferenceScene.Create(kind, terrain);
        if (kind is VisualReferenceSceneKind.EnvironmentKitYard or VisualReferenceSceneKind.EnvironmentKitBridge or VisualReferenceSceneKind.EnvironmentKitRepeated)
            return CreateKitReview(kind);
        if (kind is VisualReferenceSceneKind.LogisticsLoadingProduction or VisualReferenceSceneKind.LogisticsTransitProduction or VisualReferenceSceneKind.LogisticsUnloadingProduction or VisualReferenceSceneKind.LogisticsSustainedProduction)
            return LogisticsTrafficReferenceScene.Create(kind, terrain, runtimeAssets);
        BuildingId operationalReview = kind switch
        {
            VisualReferenceSceneKind.VehicleFactoryProduction => BuildingIds.VehicleFactory,
            VisualReferenceSceneKind.PowerPlantProduction => BuildingIds.PowerPlant,
            VisualReferenceSceneKind.BarracksProduction => BuildingIds.Barracks,
            VisualReferenceSceneKind.RadarProduction => BuildingIds.Radar,
            _ => default
        };
        bool operationalGallery = operationalReview.IsSpecified || kind == VisualReferenceSceneKind.OperationalBuildingsRepeated;
        BuildingId industrialBuilding = kind switch
        {
            VisualReferenceSceneKind.ExtractorProduction => BuildingIds.Extractor,
            VisualReferenceSceneKind.SmelterProduction => BuildingIds.Smelter,
            VisualReferenceSceneKind.ElectronicsPlantProduction => BuildingIds.ElectronicsPlant,
            VisualReferenceSceneKind.FuelRefineryProduction => BuildingIds.Refinery,
            VisualReferenceSceneKind.AmmunitionPlantProduction => BuildingIds.AmmunitionPlant,
            VisualReferenceSceneKind.StorageDepotProduction => BuildingIds.StorageDepot,
            VisualReferenceSceneKind.SupplyDepotProduction => BuildingIds.SupplyDepot,
            VisualReferenceSceneKind.LogisticsHubProduction => BuildingIds.LogisticsHub,
            _ => default
        };
        bool logisticsReview = kind is VisualReferenceSceneKind.StorageDepotProduction or VisualReferenceSceneKind.SupplyDepotProduction or VisualReferenceSceneKind.LogisticsHubProduction;
        bool infantryReview = kind is VisualReferenceSceneKind.InfantryRifleProduction or VisualReferenceSceneKind.InfantryEngineerProduction or VisualReferenceSceneKind.InfantryRepeated or VisualReferenceSceneKind.InfantryBattleProduction;
        bool battlefieldReview = kind == VisualReferenceSceneKind.BattlefieldVariantProduction;
        var battlefield = CentralDivideBattlefield.Create();
        Vector3 target = kind == VisualReferenceSceneKind.LogisticsCorridor
            ? battlefield.Crossings[0].Position : new(650, 0, 520);
        target.Y = Height(target.X, target.Z);
        var simulation = new SimulationCoordinator(seed: Seed);
        var store = new FactionIntelligenceStore(new() { CellSizeMeters = 32 });
        var snapshots = new PresentationSnapshotBuffer();
        var tankReview = new List<EntityId>();
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(store));
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots, store, new(1), terrain.WorldBounds));
        // A real sensor/intelligence/extraction boundary filters the authored foreign units.
        EntityId sensor = Add(new(0, 0, 0), Vector3.One, renderable: false);
        simulation.Entities.AddComponent(sensor, new VisualSensorState(new(1), rangeMeters: 130));
        foreach (var item in battlefield.WorldObjects)
        {
            // Keep authored review plots clear of unrelated dressing.
            if ((operationalGallery || infantryReview || battlefieldReview) && MathF.Abs(item.Position.X - target.X) <= 64 + item.Scale.X * .5f &&
                MathF.Abs(item.Position.Z - target.Z) <= 64 + item.Scale.Z * .5f) continue;
            Vector3 relative = item.Position - target;
            if (industrialBuilding.IsSpecified &&
                MathF.Abs(relative.X) <= 44 + item.Scale.X * .5f &&
                MathF.Abs(relative.Z) <= 44 + item.Scale.Z * .5f)
                continue;
            if (kind == VisualReferenceSceneKind.CommandCoreProduction &&
                MathF.Abs(relative.X) <= 44 + item.Scale.X * .5f &&
                MathF.Abs(relative.Z) <= 22 + item.Scale.Z * .5f)
                continue;
            float clearWidth = kind == VisualReferenceSceneKind.MainBattleTankFormation ? 164 : 24;
            float clearDepth = kind == VisualReferenceSceneKind.MainBattleTankFormation ? 140 : 28;
            if (kind is VisualReferenceSceneKind.MainBattleTankProduction or VisualReferenceSceneKind.MainBattleTankFormation &&
                MathF.Abs(relative.X) <= clearWidth + item.Scale.X * .5f &&
                MathF.Abs(relative.Z) <= clearDepth + item.Scale.Z * .5f)
                continue;
            float groundOffset = item.Kind switch
            {
                WorldPresentationKind.Decal => .04f,
                WorldPresentationKind.Vegetation => 0,
                _ => item.Scale.Y * .5f
            };
            EntityId entity = Add(new(item.Position.X - target.X, groundOffset, item.Position.Z - target.Z), item.Scale, yaw: item.RotationDegrees * MathF.PI / 180);
            simulation.Entities.AddComponent(entity, new WorldPresentationIdentity(item.Visual, item.Kind));
        }
        foreach (var item in battlefield.Resources)
        {
            if (industrialBuilding.IsSpecified || operationalGallery || infantryReview || battlefieldReview) continue;
            if (kind is VisualReferenceSceneKind.CommandCoreProduction or VisualReferenceSceneKind.MainBattleTankProduction or VisualReferenceSceneKind.MainBattleTankFormation) continue;
            EntityId deposit = Add(new(item.Center.X - target.X, 0, item.Center.Z - target.Z), new(item.HalfExtents.X * 2, 8, item.HalfExtents.Z * 2));
            simulation.Entities.AddComponent(deposit, WorldPresentationIdentity.ForResource(item.ResourceId));
            simulation.Entities.AddComponent(deposit, new ResourceDeposit(item.ResourceId,
                new(item.Center - item.HalfExtents, item.Center + item.HalfExtents), item.TotalQuantity, item.ExtractionRatePerSecond));
        }
        if (!operationalGallery && !infantryReview && !battlefieldReview && kind is not (VisualReferenceSceneKind.DaylightTerrain or VisualReferenceSceneKind.MainBattleTankProduction or VisualReferenceSceneKind.MainBattleTankFormation))
        {
            for (int i = 0; i < 18; i++)
            {
                EntityId prop = Add(new((i % 6 - 2.5f) * 16, 0, 70 + (i / 6) * 12), new(3, 5, 3));
                simulation.Entities.AddComponent(prop, new WorldPresentationIdentity(i % 2 == 0 ? WorldVisualId.VegetationConifer : WorldVisualId.PropRock,
                    i % 2 == 0 ? WorldPresentationKind.Vegetation : WorldPresentationKind.Prop));
            }
        }
        if (industrialBuilding.IsSpecified)
        {
            var footprint = DirectorateContent.CreateBuildingCatalog()[industrialBuilding].Footprint;
            for (int i = 0; i < 9; i++)
            {
                float spacing = logisticsReview ? 22 : 28;
                EntityId building = Add(new((i % 3 - 1) * spacing, footprint.Height * .5f, (i / 3 - 1) * spacing),
                    new(footprint.Width, footprint.Height, footprint.Depth));
                if (i < 3)
                    simulation.Entities.AddComponent(building, new ConstructionSite(industrialBuilding,
                        new(1), new(1), SimulationTick.Zero, 100, (uint)(i * 40)));
                else if (i == 8)
                    simulation.Entities.AddComponent(building, new BuildingWreckPresentationIdentity(industrialBuilding, new(1)));
                else
                {
                    simulation.Entities.AddComponent(building, new CompletedBuilding(industrialBuilding, new(1), SimulationTick.Zero));
                    if (i == 3 && !logisticsReview) simulation.Entities.AddComponent(building, new ProductionFacility(
                        new(1), new(2), ProductionCapability.SteelProcessing, SimulationTick.Zero));
                    if (i == 5) simulation.Entities.AddComponent(building, new PowerConsumer(20));
                    if (i is 6 or 7) simulation.Entities.AddComponent(building, new HealthState(i == 6 ? 50 : 20, 100));
                }
                if (i != 8) simulation.Entities.AddComponent(building, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            }
        }
        if (kind == VisualReferenceSceneKind.IndustrialBase)
        {
            BuildingId[] buildings = [BuildingIds.CommandCore, BuildingIds.PowerPlant, BuildingIds.Smelter,
                BuildingIds.ElectronicsPlant, BuildingIds.Refinery, BuildingIds.VehicleFactory,
                BuildingIds.StorageDepot, BuildingIds.SupplyDepot, BuildingIds.LogisticsHub,
                BuildingIds.AmmunitionPlant, BuildingIds.Barracks, BuildingIds.Radar, BuildingIds.Extractor];
            for (int i = 0; i < buildings.Length; i++)
            {
                EntityId building = Add(new((i % 4 - 1.5f) * 27, 0, (i / 4 - 1.5f) * 27), new(16, 10, 16));
                simulation.Entities.AddComponent(building, new CompletedBuilding(buildings[i], new(1), SimulationTick.Zero));
                simulation.Entities.AddComponent(building, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            }
        }
        if (kind == VisualReferenceSceneKind.DirectorateUnits)
        {
            UnitId[] units = [UnitIds.RifleSquad, UnitIds.CombatEngineer, UnitIds.ScoutVehicle,
                UnitIds.MainBattleTank, UnitIds.MobileArtillery, UnitIds.CargoTruck, UnitIds.SupplyTruck];
            var definitions = DirectorateContent.CreateUnitCatalog();
            for (int i = 0; i < units.Length; i++)
            {
                var definition = definitions[units[i]];
                EntityId unit = Add(new((i % 4 - 1.5f) * 14, definition.Movement.HeightOffset,
                    (i / 4 - .5f) * 20), definition.VisualScale);
                simulation.Entities.AddComponent(unit, new UnitIdentity(units[i], new(1)));
                simulation.Entities.AddComponent(unit, new ControllableEntity(new(1), ControllableEntityCategory.Unit));
            }
        }
        if (kind is VisualReferenceSceneKind.MainBattleTankProduction or VisualReferenceSceneKind.MainBattleTankFormation)
        {
            var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.MainBattleTank];
            int count = kind == VisualReferenceSceneKind.MainBattleTankFormation ? 1000 : 8;
            int columns = count == 1000 ? 40 : 4;
            for (int i = 0; i < count; i++)
            {
                EntityId tank = Add(new((i % columns - (columns - 1) * .5f) * 8,
                    definition.Movement.HeightOffset, (i / columns - (count / columns - 1) * .5f) * 11), definition.VisualScale);
                tankReview.Add(tank);
                if (count == 8 && i == 7)
                    simulation.Entities.AddComponent(tank, new UnitWreckPresentationIdentity(UnitIds.MainBattleTank, new(1)));
                else
                {
                    simulation.Entities.AddComponent(tank, new UnitIdentity(UnitIds.MainBattleTank, new(1)));
                    simulation.Entities.AddComponent(tank, new ControllableEntity(new(1), ControllableEntityCategory.Unit));
                    if (count == 8 && i is 5 or 6) simulation.Entities.AddComponent(tank, new HealthState(i == 5 ? 50 : 20, 100));
                }
            }
            if (count == 8)
            {
                foreach (var pair in new[] { (UnitIds.ScoutVehicle, -12f), (UnitIds.MobileArtillery, 12f) })
                {
                    var other = DirectorateContent.CreateUnitCatalog()[pair.Item1];
                    EntityId unit = Add(new(pair.Item2, other.Movement.HeightOffset, 20), other.VisualScale);
                    simulation.Entities.AddComponent(unit, new UnitIdentity(pair.Item1, new(1)));
                }
            }
        }
        if (kind == VisualReferenceSceneKind.DirectorateBuildingStates)
        {
            BuildingId[] buildings = [BuildingIds.CommandCore, BuildingIds.VehicleFactory, BuildingIds.PowerPlant];
            var definitions = DirectorateContent.CreateBuildingCatalog();
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < buildings.Length; column++)
            {
                var footprint = definitions[buildings[column]].Footprint;
                EntityId building = Add(new((column - 1) * 27, footprint.Height * .5f, (row - 1) * 27),
                    new(footprint.Width, footprint.Height, footprint.Depth));
                simulation.Entities.AddComponent(building, new CompletedBuilding(buildings[column], new(1), SimulationTick.Zero));
                simulation.Entities.AddComponent(building, new ControllableEntity(new(1), ControllableEntityCategory.Building));
                if (row == 1) simulation.Entities.AddComponent(building, new PowerConsumer(100));
                if (row == 2) simulation.Entities.AddComponent(building, new HealthState(20, 100));
            }
        }
        if (operationalGallery)
        {
            BuildingId[] families = [BuildingIds.VehicleFactory, BuildingIds.PowerPlant, BuildingIds.Barracks, BuildingIds.Radar];
            var definitions = DirectorateContent.CreateBuildingCatalog();
            bool repeated = kind == VisualReferenceSceneKind.OperationalBuildingsRepeated;
            for (int i = 0; i < (repeated ? 1000 : 9); i++)
            {
                BuildingId id = repeated ? families[i % families.Length] : operationalReview;
                bool producer = id == BuildingIds.VehicleFactory || id == BuildingIds.Barracks;
                // Idle is meaningful only for the existing production-capable families.
                if (!repeated && i == 4 && !producer) continue;
                var footprint = definitions[id].Footprint;
                Vector3 offset = repeated ? new((i % 40 - 19.5f) * 28, footprint.Height * .5f, (i / 40 - 12) * 28)
                    : new((i % 3 - 1) * 32, footprint.Height * .5f, (i / 3 - 1) * 32);
                EntityId building = Add(offset, new(footprint.Width, footprint.Height, footprint.Depth));
                if (!repeated && i < 3)
                    simulation.Entities.AddComponent(building, new ConstructionSite(id,
                        new(1), new(1), SimulationTick.Zero, 100, (uint)(i * 40)));
                else if (!repeated && i == 8)
                    simulation.Entities.AddComponent(building, new BuildingWreckPresentationIdentity(id, new(1)));
                else
                {
                    simulation.Entities.AddComponent(building, new CompletedBuilding(id, new(1), SimulationTick.Zero));
                    if (!repeated && i == 4)
                        simulation.Entities.AddComponent(building, new UnitProductionFacility(new(1),
                            producer && id == BuildingIds.Barracks ? UnitProductionCapability.Infantry : UnitProductionCapability.Vehicle,
                            new(1), definitions[id].UnitSpawnOffset, SimulationTick.Zero));
                    if (!repeated && i == 5) simulation.Entities.AddComponent(building, new PowerConsumer(20));
                    if (!repeated && i is 6 or 7) simulation.Entities.AddComponent(building, new HealthState(i == 6 ? 50 : 20, 100));
                }
                if (repeated || i != 8)
                    simulation.Entities.AddComponent(building, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            }
        }
        if (kind == VisualReferenceSceneKind.CommandCoreProduction)
        {
            var footprint = DirectorateContent.CreateBuildingCatalog()[BuildingIds.CommandCore].Footprint;
            for (int i = 0; i < 8; i++)
            {
                EntityId building = Add(new((i % 4 - 1.5f) * 22, footprint.Height * .5f, (i / 4 - .5f) * 22),
                    new(footprint.Width, footprint.Height, footprint.Depth));
                if (i < 3)
                    simulation.Entities.AddComponent(building, new ConstructionSite(BuildingIds.CommandCore,
                        new(1), new(1), SimulationTick.Zero, 100, (uint)(i * 40)));
                else if (i == 7)
                    simulation.Entities.AddComponent(building, new BuildingWreckPresentationIdentity(BuildingIds.CommandCore, new(1)));
                else
                {
                    simulation.Entities.AddComponent(building, new CompletedBuilding(BuildingIds.CommandCore, new(1), SimulationTick.Zero));
                    if (i == 4) simulation.Entities.AddComponent(building, new PowerConsumer(20));
                    if (i >= 5) simulation.Entities.AddComponent(building, new HealthState(i == 5 ? 50 : 20, 100));
                }
                if (i != 7)
                    simulation.Entities.AddComponent(building, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            }
        }
        if (kind is VisualReferenceSceneKind.FormationConvoy or VisualReferenceSceneKind.LogisticsCorridor)
        {
            for (int i = 0; i < 15; i++)
                Unit(i % 3 == 0 ? UnitIds.CargoTruck : UnitIds.MainBattleTank, new((i % 3 - 1) * 10, 0, (i / 3 - 2) * 14));
            for (int i = -3; i <= 3; i++)
            {
                bool bridge = kind == VisualReferenceSceneKind.LogisticsCorridor && i == 0;
                EntityId road = Add(new(0, .08f, i * 20), bridge ? new(18, 4, 42) : new(12, .2f, 20));
                simulation.Entities.AddComponent(road, new InfrastructurePresentationIdentity(
                    bridge ? InfrastructurePresentationKind.RoadBridge : InfrastructurePresentationKind.RoadSegment, $"reference-road-{i}"));
            }
        }
        if (kind is VisualReferenceSceneKind.DamageDestruction or VisualReferenceSceneKind.BattlefieldCombat)
        {
            for (int i = 0; i < 9; i++)
            {
                EntityId unit = Unit(i % 3 == 0 ? UnitIds.RifleSquad : UnitIds.MainBattleTank, new((i % 3 - 1) * 14, 0, (i / 3 - 1) * 14));
                if (i % 3 == 0) simulation.Entities.AddComponent(unit, new HealthState(25, 100));
                if (i == 8) simulation.Entities.AddComponent(unit, new UnitWreckPresentationIdentity(UnitIds.MainBattleTank, new(1)));
            }
            EntityId building = Add(new(-24, 0, -32), new(16, 10, 16));
            simulation.Entities.AddComponent(building, new CompletedBuilding(BuildingIds.VehicleFactory, new(1), SimulationTick.Zero));
            simulation.Entities.AddComponent(building, new HealthState(20, 100));
            simulation.Entities.AddComponent(building, new ControllableEntity(new(1),ControllableEntityCategory.Building));
        }
        if (infantryReview)
        {
            bool repeated = kind == VisualReferenceSceneKind.InfantryRepeated;
            for (int i = 0; i < (repeated ? 1000 : 12); i++)
            {
                UnitId family = kind == VisualReferenceSceneKind.InfantryEngineerProduction ||
                    kind is VisualReferenceSceneKind.InfantryRepeated or VisualReferenceSceneKind.InfantryBattleProduction && i % 2 != 0
                    ? UnitIds.CombatEngineer : UnitIds.RifleSquad;
                Vector3 offset = repeated ? new((i % 40 - 19.5f) * 3.2f, 0, (i / 40 - 12) * 3.2f)
                    : new((i % 4 - 1.5f) * 5, 0, (i / 4 - 1) * 8);
                EntityId soldier = Unit(family, offset);
                if (!repeated && i % 4 is 1 or 2)
                    simulation.Entities.AddComponent(soldier, new HealthState(i % 4 == 1 ? 50 : 20, 100));
                if (!repeated && i % 4 == 3)
                    {
                    simulation.Entities.AddComponent(soldier, new UnitWreckPresentationIdentity(family, new(1)));
                    simulation.Entities.RemoveComponent<ControllableEntity>(soldier);
                }
            }
        }
        if (battlefieldReview)
        {
            UnitId[] wreckFamilies = [UnitIds.RifleSquad, UnitIds.CombatEngineer, UnitIds.ScoutVehicle,
                UnitIds.MobileArtillery, UnitIds.CargoTruck, UnitIds.SupplyTruck];
            for (int i = 0; i < wreckFamilies.Length; i++)
            {
                EntityId wreck = Unit(wreckFamilies[i], new((i % 3 - 1) * 14, 0, (i / 3 - .5f) * 16));
                simulation.Entities.AddComponent(wreck, new UnitWreckPresentationIdentity(wreckFamilies[i], new(1)));
                simulation.Entities.RemoveComponent<ControllableEntity>(wreck);
            }
            for (int i = 0; i < 4; i++)
            {
                EntityId road = Add(new((i % 2 - .5f) * 28, i < 2 ? .08f : 2, 28 + i / 2 * 22),
                    i < 2 ? new(12, .2f, 20) : new(18, 4, 20));
                simulation.Entities.AddComponent(road, new InfrastructurePresentationIdentity(
                    i < 2 ? InfrastructurePresentationKind.RoadSegment : InfrastructurePresentationKind.RoadBridge, $"reference-damage-{i}"));
                simulation.Entities.AddComponent(road, new StrategicInfrastructureState(
                    i % 2 == 0 ? StrategicInfrastructureOperationalState.Restoring : StrategicInfrastructureOperationalState.Disabled, 0));
            }
            foreach (var visual in new[] { WorldVisualId.PropRubble, WorldVisualId.PropScorchedBarrier, WorldVisualId.PropBurstPipe })
            {
                EntityId prop = Add(new(((int)visual % 3 - 1) * 12, 1, -24), new(8, 2, 6));
                simulation.Entities.AddComponent(prop, new WorldPresentationIdentity(visual, WorldPresentationKind.Prop));
            }
        }
        bool mechanical = kind is >= VisualReferenceSceneKind.MechanicalIdle and <= VisualReferenceSceneKind.MechanicalBroken;
        if (mechanical)
        {
            UnitId[] units = [UnitIds.MainBattleTank, UnitIds.ScoutVehicle, UnitIds.CargoTruck, UnitIds.SupplyTruck];
            for (int i = 0; i < units.Length; i++)
                Unit(units[i], new((i - 1.5f) * 9, .75f, 8));
            EntityId factory = Add(new(0, 3.9f, -12), new(16, 10, 16));
            simulation.Entities.AddComponent(factory, new CompletedBuilding(BuildingIds.VehicleFactory, new(1), SimulationTick.Zero));
        }        EntityId hidden = default;
        if (kind is VisualReferenceSceneKind.BattlefieldCombat or VisualReferenceSceneKind.InfantryBattleProduction)
        {
            EntityId contact = Unit(kind == VisualReferenceSceneKind.InfantryBattleProduction ? UnitIds.RifleSquad : UnitIds.MainBattleTank, new(55, 0, 10), foreign: true);
            simulation.Entities.AddComponent(contact, new IntelligenceSignature(new(2), identityKey: 42));
            if (includeHiddenEnemy)
            {
                hidden = Unit(kind == VisualReferenceSceneKind.InfantryBattleProduction ? UnitIds.CombatEngineer : UnitIds.MobileArtillery, new(300, 0, 0), foreign: true);
                simulation.Entities.AddComponent(hidden, new IntelligenceSignature(new(2), identityKey: 43));
            }
        }
        for (int tick = 0; tick < FixedTick; tick++) simulation.AdvanceOneTick();
        if (!snapshots.TryReadLatest(out var extracted)) throw new InvalidOperationException("Reference snapshot was not published.");
        if (hidden.IsValid && extracted.Instances.ToArray().Any(x => x.Entity == hidden))
            throw new InvalidOperationException("Reference extraction exposed a hidden enemy.");
        var experience = new PlayerExperienceSnapshot(new(1), extracted.Tick, PlayerMatchStatus.Active, default,
            new(100, 80, 90, 200, 150, 75, 100), new(default, 500, 300, 300, 0, 0, 0),
            new(extracted.Intelligence!.Cells.Count, extracted.Intelligence.Cells.Count(x => x.State == IntelligenceState.Visible), extracted.Intelligence.Contacts.Count),
            default, PlayerAlertState.None, 0, 0, PlayerCommandFeedback.None, default);
        var visible = extracted.Instances.ToArray().ToList();
        if (kind is VisualReferenceSceneKind.BattlefieldCombat or VisualReferenceSceneKind.InfantryBattleProduction)
        {
            // Authored effects at known visible positions; no events or positions from hidden units.
            VfxEffectKind[] effects = [kind == VisualReferenceSceneKind.InfantryBattleProduction ? VfxEffectKind.MuzzleInfantry : VfxEffectKind.MuzzleTankCannon, VfxEffectKind.ImpactMetal,
                VfxEffectKind.ImpactDirt, VfxEffectKind.ExplosionBuilding, VfxEffectKind.DestructionSparkEmission];
            for (int i = 0; i < effects.Length; i++)
            {
                var definition = VfxPresentationCatalog.Get(effects[i]);
                Vector3 position = target + new Vector3(i * 10 - 20, 2, 18);
                position.Y = Height(position.X, position.Z) + 2;
                visible.Add(new(new((uint)i + 1_000_000, 1), new(position, Quaternion.Identity, definition.BaseScale),
                    new(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default, VfxFeature: new(effects[i])));
            }
        }
        if (tankReview.Count > 0)
        {
            for (int i = 0; i < visible.Count; i++)
            {
                RenderInstance item = visible[i];
                int review = tankReview.IndexOf(item.Entity);
                if (review < 0 || item.UnitFeature.IsWreck) continue;
                bool formation = kind == VisualReferenceSceneKind.MainBattleTankFormation;
                visible[i] = item with
                {
                    MechanicalPose = new(true, formation || review == 2 ? 1.7 : 0, mechanicalSeconds ?? 2, 1.6,
                        review == 3 && !formation ? 1.55 : -1, review is 1 or 3 && !formation ? .2f : 0, 0),
                    UnitFeature = item.UnitFeature with { AimYawRadians = !formation && review is 1 or 3 ? .6f : 0 }
                };
            }
        }
        if (infantryReview || battlefieldReview)
        {
            for (int i = 0; i < visible.Count; i++)
            {
                RenderInstance item = visible[i];
                if (infantryReview && item.UnitFeature.IsSpecified && !item.UnitFeature.IsWreck)
                    visible[i] = item with { MechanicalPose = new(true, i % 3 == 1 ? .35 : 0, 1.6, 1.6,
                        i % 3 == 2 ? 1.55 : -1, 0, 0) };
            }
        }
        if (mechanical)
        {
            for (int i = 0; i < visible.Count; i++)
            {
                RenderInstance item = visible[i];
                if (!item.UnitFeature.IsSpecified && !item.BuildingFeature.IsSpecified) continue;
                bool broken = kind == VisualReferenceSceneKind.MechanicalBroken;
                bool powered = kind != VisualReferenceSceneKind.MechanicalUnpowered && !broken;
                visible[i] = item with
                {
                    MechanicalPose = new(true, kind == VisualReferenceSceneKind.MechanicalMoving ? 1.7 : 0,
                        powered ? 2 : 0, 1.6, kind == VisualReferenceSceneKind.MechanicalFiring ? 1.55 : -1,
                        kind == VisualReferenceSceneKind.MechanicalFiring ? .2f : 0,
                        kind == VisualReferenceSceneKind.MechanicalLoading ? 1 : 0),
                    UnitFeature = item.UnitFeature.IsSpecified ? item.UnitFeature with
                    {
                        AimYawRadians = kind == VisualReferenceSceneKind.MechanicalFiring ? .6f : 0,
                        DamageState = broken ? UnitPresentationDamageState.Critical : UnitPresentationDamageState.Intact
                    } : item.UnitFeature,
                    BuildingFeature = item.BuildingFeature.IsSpecified ? item.BuildingFeature with
                    {
                        State = broken ? BuildingPresentationState.Critical : powered ? BuildingPresentationState.Operational : BuildingPresentationState.Unpowered
                    } : item.BuildingFeature
                };
            }
        }        var snapshot = new PresentationSnapshot(extracted.Tick, extracted.TickDuration, extracted.SimulationEntityCount,
            visible.ToArray(), extracted.Intelligence, new(1), experience with
            {
                Selection = PlayerSelectionSummary.Empty with
                {
                    Count = Math.Min(3, visible.Count(x => x.Selectable.Owner == new PlayerId(1))),
                    Kind = industrialBuilding.IsSpecified || operationalGallery || kind is VisualReferenceSceneKind.IndustrialBase or VisualReferenceSceneKind.CommandCoreProduction
                        ? PlayerSelectionKind.Building : PlayerSelectionKind.Unit,
                    DisplayName = kind.ToString()
                }
            }, playerActions: new(new(1), extracted.Tick,
                [new(BuildingIds.PowerPlant, "Power Plant", [], true), new(BuildingIds.VehicleFactory, "Vehicle Factory", [], true)],
                0, null, null), vfxMetrics: extracted.VfxMetrics);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = FixtureVersion, kind, target, seed = Seed, tick = snapshot.Tick.Value,
            instances = snapshot.Instances.ToArray(), cells = snapshot.Intelligence!.Cells, contacts = snapshot.Intelligence.Contacts,
            snapshot.PlayerExperience, snapshot.PlayerActions
        }, FingerprintOptions)));
        return new(kind, target, snapshot, hidden, fingerprint);

        float Height(float x, float z) => terrain.TrySampleHeight(x, z, out float height) ? height : throw new InvalidOperationException("Reference point lies outside terrain.");
        EntityId Add(Vector3 offset, Vector3 scale, bool renderable = true, float yaw = 0)
        {
            Vector3 position = target + offset;
            position.Y = Height(position.X, position.Z) + offset.Y;
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), scale));
            if (renderable) simulation.Entities.AddComponent(entity, new VisualIdentity(1));
            return entity;
        }
        EntityId Unit(UnitId id, Vector3 offset, bool foreign = false)
        {
            var definition = DirectorateContent.CreateUnitCatalog()[id];
            bool physicalReview = infantryReview || battlefieldReview;
            if (physicalReview) offset.Y += definition.Movement.HeightOffset;
            EntityId unit = Add(offset, physicalReview ? definition.VisualScale : id == UnitIds.RifleSquad ? new(2, 2, 2) : new(4, 3, 7));
            simulation.Entities.AddComponent(unit, new UnitIdentity(id, new(foreign ? 2u : 1u)));
            if (physicalReview) simulation.Entities.AddComponent(unit, definition.Movement);
            simulation.Entities.AddComponent(unit, new ControllableEntity(new(foreign ? 2UL : 1UL), ControllableEntityCategory.Unit));
            return unit;
        }
    }
    private static VisualReferenceScene CreateKitReview(VisualReferenceSceneKind kind)
    {
        using var scenario = CentralDivideScenario.Create(seed: Seed);
        var snapshots = new PresentationSnapshotBuffer();
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(new PresentationExtractor(snapshots,
            new PresentationExtractionContext(scenario, new(1), new PresentationInteractionState(), gateway)));
        scenario.Simulation.RunTicks((int)FixedTick);
        if (!snapshots.TryReadLatest(out var snapshot)) throw new InvalidOperationException("Kit snapshot was not published.");
        Vector3 target = kind == VisualReferenceSceneKind.EnvironmentKitBridge ? new(1536, 0, 920) : new(855, 0, 303);
        if (!scenario.Terrain.TrySampleHeight(target.X, target.Z, out float ground))
            throw new InvalidOperationException("Kit review point lies outside terrain.");
        target.Y = ground;
        if (kind == VisualReferenceSceneKind.EnvironmentKitRepeated)
        {
            (WorldVisualId Visual, Vector3 Scale)[] families =
            [
                (WorldVisualId.PropRockSlab, new(9,3,6)), (WorldVisualId.PropRockSpire, new(4,7,4)),
                (WorldVisualId.PropBarrierModule, new(4,1.2f,.9f)), (WorldVisualId.PropPipeElbow, new(3,1,3)),
                (WorldVisualId.PropLoadingGantry, new(8,6,4)), (WorldVisualId.PropRoadEdge, new(8,.4f,1.4f)),
                (WorldVisualId.PropBridgeApron, new(12,.3f,8)), (WorldVisualId.PropLoadingApron, new(8,.45f,8)),
                (WorldVisualId.PropServiceFence, new(8,2.4f,.35f)), (WorldVisualId.VegetationBroadleaf, new(7,12,7))
            ];
            var instances = snapshot.Instances.ToArray().ToList();
            for (int i = 0; i < 1000; i++)
            {
                var family = families[i % families.Length];
                Vector3 position = target + new Vector3((i % 40 - 19.5f) * 14, 0, (i / 40 - 12) * 14);
                if (!scenario.Terrain.TrySampleHeight(position.X, position.Z, out float height))
                    throw new InvalidOperationException("Repeated kit point lies outside terrain.");
                bool plant = family.Visual == WorldVisualId.VegetationBroadleaf;
                position.Y = height + (plant ? 0 : family.Scale.Y * .5f);
                instances.Add(new(new(0xD000_0000U + (uint)i, 1), new(position, Quaternion.Identity, family.Scale),
                    new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                    WorldFeature: new(family.Visual, plant ? WorldPresentationKind.Vegetation : WorldPresentationKind.Prop, default, false)));
            }
            snapshot = new(snapshot.Tick, snapshot.TickDuration, snapshot.SimulationEntityCount, instances.ToArray(),
                snapshot.Intelligence, new(1), snapshot.PlayerExperience, playerActions: snapshot.PlayerActions, vfxMetrics: snapshot.VfxMetrics);
        }
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = FixtureVersion, kind, target, seed = Seed, tick = snapshot.Tick.Value,
            instances = snapshot.Instances.ToArray(), snapshot.Intelligence, snapshot.PlayerExperience,
            actions = snapshot.PlayerActions is null ? null : new
            {
                snapshot.PlayerActions.Tick, snapshot.PlayerActions.Construction,
                snapshot.PlayerActions.PendingCommandCount, snapshot.PlayerActions.Production,
                snapshot.PlayerActions.UnitProduction, snapshot.PlayerActions.Logistics,
                snapshot.PlayerActions.Supply, snapshot.PlayerActions.Tactical, snapshot.PlayerActions.Technology
            }
        }, FingerprintOptions)));
        return new(kind, target, snapshot, default, fingerprint);
    }

}
