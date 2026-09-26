using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct SkirmishStartingStock(
    double FerrousOre,
    double Volatiles,
    double Silicates,
    double Steel,
    double Fuel,
    double Electronics,
    double Ammunition)
{
    public static SkirmishStartingStock Standard =>
        new(
            FerrousOre: 1_400.0,
            Volatiles: 900.0,
            Silicates: 900.0,
            Steel: 280.0,
            Fuel: 320.0,
            Electronics: 160.0,
            Ammunition: 420.0);
}

public readonly record struct SkirmishStartingBase(
    PlayerId Player,
    FactionId Faction,
    EntityId CommandCore,
    EntityId Controller,
    InventoryId StartingInventory,
    IReadOnlyList<EntityId> StartingUnits);

public static class SkirmishStartingBaseFactory
{
    private static readonly PowerNetworkId DefaultPowerNetwork = new(1);

    public static SkirmishStartingBase Create(
        EntityRegistry entities,
        InventoryStore inventories,
        UnitFactory unitFactory,
        TerrainWorld terrain,
        BattlefieldStartPosition start,
        SkirmishStartingStock? startingStock = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(unitFactory);
        ArgumentNullException.ThrowIfNull(terrain);

        if (!start.Player.IsSpecified)
        {
            throw new ArgumentException(
                "Skirmish starts require a valid player.",
                nameof(start));
        }

        if (start.Player.Value > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                "Skirmish player identifiers must fit in FactionId.");
        }

        FactionId faction =
            new((uint)start.Player.Value);
        Vector3 commandCorePosition =
            SampleTerrain(
                terrain,
                start.CommandCorePosition,
                heightOffset: 6.0f);

        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity: 12_000.0));
        SeedStartingInventory(
            inventories,
            inventory,
            startingStock ??
            SkirmishStartingStock.Standard);

        EntityId commandCore =
            entities.CreateEntity();
        Vector3 coreScale =
            new(20.0f, 12.0f, 20.0f);
        var transform =
            new WorldTransform(
                commandCorePosition,
                Quaternion.Identity,
                coreScale);

        entities.AddComponent(
            commandCore,
            transform);
        entities.AddComponent(
            commandCore,
            new VisualIdentity(101));
        entities.AddComponent(
            commandCore,
            new ControllableEntity(
                start.Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            commandCore,
            new SpatialPresence(
                coreScale * 0.5f,
                new SpatialEntryMetadata(
                    start.Player.Value,
                    (ulong)ControllableEntityCategory.Building,
                    SpatialMobility.Static)));
        entities.AddComponent(
            commandCore,
            new InventoryStorage(inventory));
        entities.AddComponent(
            commandCore,
            new PowerNetworkMembership(
                DefaultPowerNetwork));
        entities.AddComponent(
            commandCore,
            new PowerConsumer(
                demand: 20.0,
                priority: PowerPriority.Critical));
        entities.AddComponent(
            commandCore,
            new CommandFacility());
        entities.AddComponent(
            commandCore,
            new CompletedBuilding(
                BuildingIds.CommandCore,
                start.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            commandCore,
            new IntelligenceSignature(
                faction,
                checked(
                    0x7000_0000u +
                    (uint)start.Player.Value)));

        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();

        EntityId engineer =
            unitFactory.Create(
                units[UnitIds.CombatEngineer],
                SampleTerrain(
                    terrain,
                    start.Position +
                    new Vector3(30.0f, 0.0f, -20.0f),
                    heightOffset: 1.5f),
                start.Player);
        EntityId cargoTruck =
            unitFactory.Create(
                units[UnitIds.CargoTruck],
                SampleTerrain(
                    terrain,
                    start.Position +
                    new Vector3(35.0f, 0.0f, 20.0f),
                    heightOffset: 2.0f),
                start.Player);

        EntityId controller =
            entities.CreateEntity();
        entities.AddComponent(
            controller,
            new SkirmishOpponentController(
                start.Player,
                faction,
                commandCore,
                commandCorePosition));
        entities.AddComponent(
            controller,
            SkirmishOpponentState.Initial);

        return new SkirmishStartingBase(
            start.Player,
            faction,
            commandCore,
            controller,
            inventory,
            [engineer, cargoTruck]);
    }

    private static void SeedStartingInventory(
        InventoryStore inventories,
        InventoryId inventory,
        in SkirmishStartingStock stock)
    {
        Add(
            inventories,
            inventory,
            ResourceIds.FerrousOre,
            stock.FerrousOre);
        Add(
            inventories,
            inventory,
            ResourceIds.Volatiles,
            stock.Volatiles);
        Add(
            inventories,
            inventory,
            ResourceIds.Silicates,
            stock.Silicates);
        Add(
            inventories,
            inventory,
            ResourceIds.Steel,
            stock.Steel);
        Add(
            inventories,
            inventory,
            ResourceIds.Fuel,
            stock.Fuel);
        Add(
            inventories,
            inventory,
            ResourceIds.Electronics,
            stock.Electronics);
        Add(
            inventories,
            inventory,
            ResourceIds.Ammunition,
            stock.Ammunition);
    }

    private static void Add(
        InventoryStore inventories,
        InventoryId inventory,
        ResourceId resource,
        double quantity)
    {
        if (!double.IsFinite(quantity) ||
            quantity < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity));
        }

        if (quantity == 0.0)
        {
            return;
        }

        InventoryOperationResult result =
            inventories.Add(
                inventory,
                resource,
                quantity);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to seed skirmish inventory {inventory} with resource {resource}: {result.Failure}.");
        }
    }

    private static Vector3 SampleTerrain(
        TerrainWorld terrain,
        Vector3 requested,
        float heightOffset)
    {
        if (!terrain.TrySampleHeight(
                requested.X,
                requested.Z,
                out float height))
        {
            throw new InvalidOperationException(
                $"Skirmish start ({requested.X}, {requested.Z}) lies outside terrain.");
        }

        return new Vector3(
            requested.X,
            height + heightOffset,
            requested.Z);
    }
}
