using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BuildingCombatInitializationTests
{
    private static readonly PlayerId Owner = new(7);

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(7u)]
    [InlineData(8u)]
    [InlineData(9u)]
    [InlineData(10u)]
    [InlineData(11u)]
    [InlineData(12u)]
    [InlineData(13u)]
    public void EveryCatalogBuildingReceivesTheOwnedStructureCombatContract(uint buildingValue)
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[new BuildingId(buildingValue)];
        EntityId building = CreateCompletedBuilding(entities, definition);

        BuildingCombatInitialization.Initialize(entities, building, definition);

        Assert.True(double.IsFinite(definition.MaximumHealth));
        Assert.True(definition.MaximumHealth > 0.0);
        Assert.Equal(HealthState.Full(definition.MaximumHealth), entities.GetComponent<HealthState>(building));
        Assert.Equal(new ControllableEntity(Owner, ControllableEntityCategory.Building),
            entities.GetComponent<ControllableEntity>(building));
        Assert.Equal(new FactionId(7), entities.GetComponent<Combatant>(building).Faction);
        Assert.Equal(TargetClass.Structure, entities.GetComponent<Targetable>(building).Class);
        Assert.Equal(definition.TargetPriority, entities.GetComponent<TargetPriority>(building).Value);
        Assert.Equal(definition.Footprint.GetHalfExtents(BuildingOrientation.North),
            entities.GetComponent<CombatHitbox>(building).HalfExtents);
        Assert.False(entities.HasComponent<ArmorState>(building));
    }

    [Theory]
    [InlineData(123.0)]
    [InlineData(0.0)]
    public void ReinitializationPreservesDamagedAndDepletedHealth(double remainingHealth)
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.Smelter];
        EntityId building = CreateCompletedBuilding(entities, definition);
        BuildingCombatInitialization.Initialize(entities, building, definition);
        var damaged = new HealthState(remainingHealth, definition.MaximumHealth);
        entities.SetComponent(building, damaged);

        BuildingCombatInitialization.Initialize(entities, building,
            definition with { MaximumHealth = definition.MaximumHealth * 2.0 });
        BuildingCombatInitialization.Initialize(entities, building, definition);

        Assert.Equal(damaged, entities.GetComponent<HealthState>(building));
        Assert.Equal(remainingHealth == 0.0, entities.GetComponent<HealthState>(building).IsDepleted);
    }

    [Fact]
    public void DeclaredArmorAttachesAndReinitializationPreservesExistingArmor()
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.PowerPlant] with
        {
            ArmorProfileId = DirectorateContent.ArmorIds.LightVehicle
        };
        EntityId building = CreateCompletedBuilding(entities, definition);

        BuildingCombatInitialization.Initialize(entities, building, definition);
        Assert.Equal(definition.ArmorProfileId, entities.GetComponent<ArmorState>(building).ProfileId);

        entities.SetComponent(building, new ArmorState(DirectorateContent.ArmorIds.MainBattleTank));
        BuildingCombatInitialization.Initialize(entities, building, definition);
        BuildingCombatInitialization.Initialize(entities, building, definition with { ArmorProfileId = default });

        Assert.Equal(DirectorateContent.ArmorIds.MainBattleTank,
            entities.GetComponent<ArmorState>(building).ProfileId);
    }

    [Fact]
    public void HitboxUsesThePlacedFootprintAndPreservesAnExistingCombatContract()
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.Smelter];
        EntityId building = CreateCompletedBuilding(entities, definition);
        Vector3 placedExtents = definition.Footprint.GetHalfExtents(BuildingOrientation.East);
        entities.AddComponent(building, new SpatialPresence(placedExtents,
            new SpatialEntryMetadata(Owner.Value, (ulong)ControllableEntityCategory.Building, SpatialMobility.Static)));

        BuildingCombatInitialization.Initialize(entities, building, definition);
        Assert.Equal(placedExtents, entities.GetComponent<CombatHitbox>(building).HalfExtents);

        var existingHitbox = new CombatHitbox(new Vector3(3, 4, 5));
        entities.SetComponent(building, existingHitbox);
        entities.SetComponent(building, new TargetPriority(42));
        BuildingCombatInitialization.Initialize(entities, building, definition);

        Assert.Equal(existingHitbox, entities.GetComponent<CombatHitbox>(building));
        Assert.Equal(42, entities.GetComponent<TargetPriority>(building).Value);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("category")]
    [InlineData("faction")]
    [InlineData("class")]
    public void InconsistentExistingOwnershipOrCombatIdentityIsRejected(string inconsistency)
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.PowerPlant];
        EntityId building = CreateCompletedBuilding(entities, definition);
        switch (inconsistency)
        {
            case "owner":
                entities.AddComponent(building, new ControllableEntity(new PlayerId(8), ControllableEntityCategory.Building));
                break;
            case "category":
                entities.AddComponent(building, new ControllableEntity(Owner, ControllableEntityCategory.Unit));
                break;
            case "faction":
                entities.AddComponent(building, new Combatant(new FactionId(8)));
                break;
            case "class":
                entities.AddComponent(building, new Targetable(TargetClass.LightVehicle));
                break;
        }

        Assert.Throws<InvalidOperationException>(() =>
            BuildingCombatInitialization.Initialize(entities, building, definition));
        Assert.False(entities.HasComponent<HealthState>(building));
    }

    [Theory]
    [InlineData("unfinished")]
    [InlineData("unowned")]
    [InlineData("owner_overflow")]
    [InlineData("wrong_definition")]
    public void OnlyCompletedStructuresWithAnUnambiguousOwnerCanBeInitialized(string invalidState)
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.PowerPlant];
        EntityId building = CreateCompletedBuilding(entities, definition);
        switch (invalidState)
        {
            case "unfinished":
                entities.AddComponent(building, new ConstructionSite(definition.Id, Owner, new InventoryId(1),
                    SimulationTick.Zero, definition.ConstructionTicks, 0));
                break;
            case "unowned":
                entities.SetComponent(building, new CompletedBuilding(definition.Id, default, SimulationTick.Zero));
                break;
            case "owner_overflow":
                entities.SetComponent(building,
                    new CompletedBuilding(definition.Id, new PlayerId((ulong)uint.MaxValue + 1UL), SimulationTick.Zero));
                break;
            case "wrong_definition":
                definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.Radar];
                break;
        }

        Assert.Throws<InvalidOperationException>(() =>
            BuildingCombatInitialization.Initialize(entities, building, definition));
        Assert.False(entities.HasComponent<HealthState>(building));
        Assert.False(entities.HasComponent<Targetable>(building));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void BuildingDefinitionRejectsInvalidDurability(double maximumHealth)
    {
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.PowerPlant] with
        {
            MaximumHealth = maximumHealth
        };
        Assert.Throws<InvalidOperationException>(definition.Validate);
    }

    [Fact]
    public void StartingCoreUsesTheCompositionCombatDefinition()
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay);
        BuildingDefinition coreDefinition = settings.Composition.Content.Buildings[BuildingIds.CommandCore] with
        {
            MaximumHealth = 2037.0,
            TargetPriority = 63,
            ArmorProfileId = DirectorateContent.ArmorIds.LightVehicle
        };
        var buildings = new BuildingDefinitionCatalog(settings.Composition.Content.Buildings.Definitions
            .Select(definition => definition.Id == BuildingIds.CommandCore ? coreDefinition : definition));
        settings = settings with
        {
            Composition = settings.Composition with
            {
                Content = settings.Composition.Content with { Buildings = buildings }
            }
        };
        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);

        foreach (SkirmishStartingBase start in runtime.Initialization.Bases.Values)
        {
            EntityRegistry entities = runtime.Simulation.Entities;
            EntityId core = start.CommandCore;
            Assert.Equal(HealthState.Full(2037.0), entities.GetComponent<HealthState>(core));
            Assert.Equal(63, entities.GetComponent<TargetPriority>(core).Value);
            Assert.Equal(coreDefinition.ArmorProfileId, entities.GetComponent<ArmorState>(core).ProfileId);
            Assert.Equal(start.Faction, entities.GetComponent<Combatant>(core).Faction);
            Assert.Equal(TargetClass.Structure, entities.GetComponent<Targetable>(core).Class);
            Assert.Equal(entities.GetComponent<SpatialPresence>(core).HalfExtents,
                entities.GetComponent<CombatHitbox>(core).HalfExtents);
        }
    }

    [Fact]
    public void MatchInitializationAttachesCombatToUninitializedStartingIndustry()
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay);
        MatchComposition standard = settings.Composition;
        BuildingDefinition storage = standard.Content.Buildings[BuildingIds.StorageDepot] with
        {
            MaximumHealth = 987.0,
            TargetPriority = 17,
            ArmorProfileId = DirectorateContent.ArmorIds.LightVehicle
        };
        var buildings = new BuildingDefinitionCatalog(standard.Content.Buildings.Definitions
            .Select(definition => definition.Id == storage.Id ? storage : definition));
        var initialIndustry = new List<(EntityId Entity, PlayerId Owner)>();
        Vector3 placedExtents = storage.Footprint.GetHalfExtents(BuildingOrientation.East);
        settings = settings with
        {
            Composition = standard with
            {
                Content = standard.Content with { Buildings = buildings },
                CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
                {
                    SkirmishStartingBase starting = standard.CreateStartingBase(
                        entities, inventories, factory, terrain, start, content, stock);
                    Vector3 position = start.CommandCorePosition + new Vector3(0, 0, 80);
                    Assert.True(terrain.TrySampleHeight(position.X, position.Z, out float height));
                    position.Y = height + placedExtents.Y;
                    EntityId industry = entities.CreateEntity();
                    entities.AddComponent(industry, new WorldTransform(position, Quaternion.Identity, Vector3.One));
                    entities.AddComponent(industry, new SpatialPresence(placedExtents,
                        new SpatialEntryMetadata(start.Player.Value,
                            (ulong)ControllableEntityCategory.Building, SpatialMobility.Static)));
                    entities.AddComponent(industry, new CompletedBuilding(storage.Id, start.Player, SimulationTick.Zero));
                    Assert.False(entities.HasComponent<HealthState>(industry));
                    Assert.False(entities.HasComponent<Combatant>(industry));
                    initialIndustry.Add((industry, start.Player));
                    return starting;
                }
            }
        };

        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);

        Assert.Equal(settings.Participants.Count, initialIndustry.Count);
        foreach ((EntityId industry, PlayerId owner) in initialIndustry)
        {
            EntityRegistry entities = runtime.Simulation.Entities;
            Assert.Equal(HealthState.Full(storage.MaximumHealth), entities.GetComponent<HealthState>(industry));
            Assert.Equal(new ControllableEntity(owner, ControllableEntityCategory.Building),
                entities.GetComponent<ControllableEntity>(industry));
            Assert.Equal(new FactionId((uint)owner.Value), entities.GetComponent<Combatant>(industry).Faction);
            Assert.Equal(TargetClass.Structure, entities.GetComponent<Targetable>(industry).Class);
            Assert.Equal(storage.TargetPriority, entities.GetComponent<TargetPriority>(industry).Value);
            Assert.Equal(storage.ArmorProfileId, entities.GetComponent<ArmorState>(industry).ProfileId);
            Assert.Equal(placedExtents, entities.GetComponent<CombatHitbox>(industry).HalfExtents);
        }
    }

    [Fact]
    public void ReattachingCommandCoreObjectivePreservesIdentityAndDamagedHealth()
    {
        var entities = new EntityRegistry();
        BuildingDefinition definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.CommandCore];
        EntityId core = CreateCompletedBuilding(entities, definition);
        entities.AddComponent(core, new CommandFacility());
        var objective = new BattlefieldObjectiveDefinition("test.core", Owner, Vector3.Zero);
        EntityId first = MatchObjectiveSystem.AttachCommandCoreObjective(entities, objective, core, definition);
        var damaged = new HealthState(420.0, definition.MaximumHealth);
        entities.SetComponent(core, damaged);

        EntityId second = MatchObjectiveSystem.AttachCommandCoreObjective(entities, objective, core, definition);

        Assert.Equal(first, second);
        Assert.Equal(1, entities.GetComponentCount<CommandCoreObjective>());
        Assert.Equal(damaged, entities.GetComponent<HealthState>(core));
    }

    private static EntityId CreateCompletedBuilding(EntityRegistry entities, BuildingDefinition definition)
    {
        EntityId building = entities.CreateEntity();
        entities.AddComponent(building, new CompletedBuilding(definition.Id, Owner, SimulationTick.Zero));
        return building;
    }
}
