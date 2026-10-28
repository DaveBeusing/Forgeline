using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class RadarPowerTests
{
    private static readonly PlayerId Player = new(1);
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);
    private static readonly Vector3 RadarPosition = new(64.0f, 0.0f, 64.0f);
    private static readonly Vector3 ContactPosition = new(314.0f, 0.0f, 64.0f);

    [Fact]
    public void CompletedRadarWithoutGenerationDoesNotObserveEnemies()
    {
        TestWorld test = CreateWorld();
        EntityId radar = Build(test, BuildingIds.Radar, RadarPosition);
        EntityId target = AddTarget(test, ContactPosition);

        test.Simulation.RunTicks(12, TestContext.Current.CancellationToken);

        Assert.Equal(PowerOperationalState.Offline,
            test.Simulation.Entities.GetComponent<PowerConsumer>(radar).State);
        Assert.False(test.Store.TryGetContact(BlueFaction,
            IntelligenceContactKey.FromEntity(target), out _));
        Assert.Equal(0UL, test.Intelligence.Metrics.TotalSensorScans);
        Assert.Empty(test.Intelligence.DebugSensors);
    }

    [Fact]
    public void GeneratorOutageRetainsLastKnownKnowledgeAndRecoversOnScheduledScan()
    {
        TestWorld test = CreateWorld();
        EntityId generator = Build(test, BuildingIds.PowerPlant, new(128.0f, 0.0f, 64.0f));
        EntityId radar = Build(test, BuildingIds.Radar, RadarPosition);
        EntityId target = AddTarget(test, ContactPosition);
        AdvanceToScan(test, radar);
        IntelligenceContact before = Contact(test, target);
        Assert.True(before.IsCurrent);
        Assert.Equal(0U, before.IdentityKey);

        AdvanceBeforeScan(test, radar);
        PowerGenerator generation = test.Simulation.Entities.GetComponent<PowerGenerator>(generator);
        test.Simulation.Entities.SetComponent(generator, generation.WithEnabled(false));
        test.Simulation.AdvanceOneTick();

        // Sensors precedes Infrastructure: this tick uses the last completed allocation.
        Assert.True(Contact(test, target).IsCurrent);
        Assert.Equal(PowerOperationalState.Offline,
            test.Simulation.Entities.GetComponent<PowerConsumer>(radar).State);
        IntelligenceContact lastPoweredObservation = Contact(test, target);
        ulong scansAtOutage = test.Intelligence.Metrics.TotalSensorScans;
        test.Simulation.Entities.SetComponent(target,
            new WorldTransform(ContactPosition + new Vector3(50.0f, 0.0f, 0.0f),
                Quaternion.Identity, Vector3.One));
        EntityId newcomer = AddTarget(test, ContactPosition + new Vector3(0.0f, 0.0f, 50.0f));

        test.Simulation.RunTicks(12, TestContext.Current.CancellationToken);

        IntelligenceContact stale = Contact(test, target);
        Assert.False(stale.IsCurrent);
        Assert.Equal(lastPoweredObservation.LastSeenTick, stale.LastSeenTick);
        Assert.Equal(lastPoweredObservation.LastKnownPosition, stale.LastKnownPosition);
        Assert.Equal(scansAtOutage, test.Intelligence.Metrics.TotalSensorScans);
        Assert.False(test.Store.TryGetContact(BlueFaction,
            IntelligenceContactKey.FromEntity(newcomer), out _));

        test.Simulation.Entities.SetComponent(generator, generation.WithEnabled(true));
        test.Simulation.AdvanceOneTick();
        Assert.Equal(scansAtOutage, test.Intelligence.Metrics.TotalSensorScans);
        Assert.Equal(PowerOperationalState.Powered,
            test.Simulation.Entities.GetComponent<PowerConsumer>(radar).State);
        AdvanceBeforeScan(test, radar);
        Assert.Equal(scansAtOutage, test.Intelligence.Metrics.TotalSensorScans);
        test.Simulation.AdvanceOneTick();

        Assert.Equal(scansAtOutage + 1, test.Intelligence.Metrics.TotalSensorScans);
        Assert.True(Contact(test, target).IsCurrent);
        Assert.True(Contact(test, newcomer).IsCurrent);
        // Detection retains the coarse 32m report, never the exact hidden pose.
        Assert.Equal(new Vector3(352, 0, 64),
            Contact(test, target).LastKnownPosition);
        Assert.False(test.Store.IsEntityCurrentlyIdentified(BlueFaction, target));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(9.0, false)]
    [InlineData(17.99, false)]
    [InlineData(18.0, true)]
    public void RadarRequiresFullAllocatedDemand(double generation, bool expectedOperational)
    {
        TestWorld test = CreateWorld();
        EntityId generator = Build(test, BuildingIds.PowerPlant, new(128.0f, 0.0f, 64.0f));
        EntityId radar = Build(test, BuildingIds.Radar, RadarPosition);
        EntityId target = AddTarget(test, ContactPosition);
        test.Simulation.Entities.SetComponent(generator,
            new PowerGenerator(Math.Max(1.0, generation), enabled: generation > 0.0));
        test.Simulation.AdvanceOneTick();
        ulong before = test.Intelligence.Metrics.TotalSensorScans;

        test.Simulation.RunTicks(8, TestContext.Current.CancellationToken);

        Assert.Equal(before + (expectedOperational ? 2UL : 0UL),
            test.Intelligence.Metrics.TotalSensorScans);
        Assert.Equal(generation / 18.0,
            test.Simulation.Entities.GetComponent<PowerConsumer>(radar).OperationalScale, 6);
        Assert.Equal(expectedOperational, test.Store.TryGetContact(BlueFaction,
            IntelligenceContactKey.FromEntity(target), out _));
        Assert.Equal(expectedOperational ? 1 : 0, test.Intelligence.DebugSensors.Count);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("membership")]
    [InlineData("consumer")]
    public void InvalidPowerBindingPausesImmediatelyAndRestorationRespectsCadence(string outage)
    {
        TestWorld test = CreateWorld();
        _ = Build(test, BuildingIds.PowerPlant, new(128.0f, 0.0f, 64.0f));
        EntityId radar = Build(test, BuildingIds.Radar, RadarPosition);
        EntityId target = AddTarget(test, ContactPosition);
        AdvanceToScan(test, radar);
        AdvanceBeforeScan(test, radar);
        PowerConsumer consumer = test.Simulation.Entities.GetComponent<PowerConsumer>(radar);
        PowerNetworkMembership membership = test.Simulation.Entities.GetComponent<PowerNetworkMembership>(radar);
        IntelligenceContact observed = Contact(test, target);
        ulong before = test.Intelligence.Metrics.TotalSensorScans;

        switch (outage)
        {
            case "disabled":
                test.Simulation.Entities.SetComponent(radar, consumer.WithEnabled(false));
                break;
            case "membership":
                Assert.True(test.Simulation.Entities.RemoveComponent<PowerNetworkMembership>(radar));
                break;
            case "consumer":
                Assert.True(test.Simulation.Entities.RemoveComponent<PowerConsumer>(radar));
                break;
        }

        test.Simulation.RunTicks(8, TestContext.Current.CancellationToken);

        Assert.Equal(before, test.Intelligence.Metrics.TotalSensorScans);
        Assert.False(Contact(test, target).IsCurrent);
        Assert.Equal(observed.LastSeenTick, Contact(test, target).LastSeenTick);
        Assert.Empty(test.Intelligence.DebugSensors);

        if (outage == "membership")
            test.Simulation.Entities.AddComponent(radar, membership);
        else if (outage == "consumer")
            test.Simulation.Entities.AddComponent(radar, consumer.WithEnabled(true));
        else
            test.Simulation.Entities.SetComponent(radar, consumer.WithEnabled(true));

        test.Simulation.AdvanceOneTick();
        Assert.Equal(before, test.Intelligence.Metrics.TotalSensorScans);
        AdvanceBeforeScan(test, radar);
        Assert.Equal(before, test.Intelligence.Metrics.TotalSensorScans);
        test.Simulation.AdvanceOneTick();
        Assert.Equal(before + 1, test.Intelligence.Metrics.TotalSensorScans);
        Assert.True(Contact(test, target).IsCurrent);
    }

    [Fact]
    public void PassiveVisualAndBareMobileRadarSensorsKeepTheirExistingPowerIndependentBehavior()
    {
        TestWorld test = CreateWorld();
        EntityId radar = test.Simulation.Entities.CreateEntity();
        test.Simulation.Entities.AddComponent(radar,
            new WorldTransform(RadarPosition, Quaternion.Identity, Vector3.One));
        test.Simulation.Entities.AddComponent(radar,
            new RadarSensorState(BlueFaction, 600.0f, 0.0f, 1));
        EntityId visual = test.Simulation.Entities.CreateEntity();
        test.Simulation.Entities.AddComponent(visual,
            new WorldTransform(RadarPosition, Quaternion.Identity, Vector3.One));
        test.Simulation.Entities.AddComponent(visual, new VisualSensorState(BlueFaction, 30.0f));
        test.Simulation.Entities.AddComponent(visual, new PowerConsumer(10.0, enabled: false));
        EntityId distant = AddTarget(test, ContactPosition);
        EntityId nearby = AddTarget(test, RadarPosition + new Vector3(10.0f, 0.0f, 0.0f));

        test.Simulation.AdvanceOneTick();

        Assert.True(test.Store.IsEntityCurrentlyDetected(BlueFaction, distant));
        Assert.False(test.Store.IsEntityCurrentlyIdentified(BlueFaction, distant));
        Assert.True(test.Store.IsEntityCurrentlyIdentified(BlueFaction, nearby));
        Assert.False(test.Store.IsEntityCurrentlyDetected(RedFaction, distant));
    }

    private static TestWorld CreateWorld()
    {
        var simulation = new SimulationCoordinator();
        var definitions = InitialBuildingDefinitions.CreateCatalog();
        var inventories = new InventoryStore();
        var spatial = new SpatialGridIndex(new SpatialGridSettings
        {
            World = new WorldGridSettings(),
            CellSizeMeters = 16.0f
        });
        var placement = new BuildingPlacementService(definitions, new FlatTerrain(), spatial);
        var commands = new BuildingCommandProcessingSystem(definitions, placement, inventories, spatial);
        var store = new FactionIntelligenceStore();
        var intelligence = new BattlefieldIntelligenceSystem(store) { DebugCaptureEnabled = true };
        simulation.RegisterSystem(commands);
        simulation.RegisterSystem(intelligence);
        simulation.RegisterSystem(new PowerNetworkSystem());
        simulation.RegisterSystem(new BuildingConstructionSystem(definitions, inventories, spatial));

        EntityId source = simulation.Entities.CreateEntity();
        InventoryId stock = inventories.CreateInventory(new InventorySpecification(100_000.0));
        simulation.Entities.AddComponent(source, new InventoryStorage(stock));
        simulation.Entities.AddComponent(source, new StorageDepot(stock, BlueFaction));
        foreach (ResourceId resource in new[]
                 { ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Volatiles, ResourceIds.Steel, ResourceIds.Electronics })
        {
            Assert.True(inventories.Add(stock, resource, 10_000.0).Succeeded);
        }

        return new(simulation, definitions, commands, store, intelligence, source);
    }

    private static EntityId Build(TestWorld test, BuildingId building, Vector3 position)
    {
        test.Simulation.SubmitCommand(
            new BuildCommand(Player, building, position, BuildingOrientation.North,
                test.Source, test.Simulation.CurrentTick),
            new SimulationTick(test.Simulation.CurrentTick.Value + 1), new SimulationCommandSource(Player.Value));
        test.Simulation.AdvanceOneTick();
        Assert.Equal(BuildCommandRejectionReason.None, test.Commands.Metrics.LastRejection);
        EntityId result = test.Commands.Metrics.LastCreatedSite;
        Assert.True(result.IsValid);
        Assert.False(test.Simulation.Entities.HasComponent<RadarSensorState>(result));
        test.Simulation.RunTicks(test.Definitions[building].ConstructionTicks - 1,
            TestContext.Current.CancellationToken);
        Assert.True(test.Simulation.Entities.HasComponent<CompletedBuilding>(result));
        return result;
    }

    private static EntityId AddTarget(TestWorld test, Vector3 position)
    {
        EntityId target = test.Simulation.Entities.CreateEntity();
        test.Simulation.Entities.AddComponent(target,
            new WorldTransform(position, Quaternion.Identity, Vector3.One));
        test.Simulation.Entities.AddComponent(target,
            new IntelligenceSignature(RedFaction, target.Index + 1));
        return target;
    }

    private static IntelligenceContact Contact(TestWorld test, EntityId target)
    {
        Assert.True(test.Store.TryGetContact(BlueFaction,
            IntelligenceContactKey.FromEntity(target), out IntelligenceContact contact));
        return contact;
    }

    private static void AdvanceBeforeScan(TestWorld test, EntityId radar)
    {
        ulong interval = (ulong)test.Simulation.Entities.GetComponent<RadarSensorState>(radar).UpdateIntervalTicks;
        while ((test.Simulation.CurrentTick.Value + 1 + radar.Index % interval) % interval != 0)
            test.Simulation.AdvanceOneTick();
    }

    private static void AdvanceToScan(TestWorld test, EntityId radar)
    {
        // Newly completed consumers receive their first allocation after the next Sensors phase.
        test.Simulation.AdvanceOneTick();
        AdvanceBeforeScan(test, radar);
        test.Simulation.AdvanceOneTick();
    }

    private sealed record TestWorld(
        SimulationCoordinator Simulation,
        BuildingDefinitionCatalog Definitions,
        BuildingCommandProcessingSystem Commands,
        FactionIntelligenceStore Store,
        BattlefieldIntelligenceSystem Intelligence,
        EntityId Source);

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds { get; } = new(Vector3.Zero, new Vector3(1024.0f, 20.0f, 1024.0f));

        public bool TrySampleHeight(float worldX, float worldZ, out float height)
        {
            height = 0.0f;
            return worldX is >= 0.0f and <= 1024.0f && worldZ is >= 0.0f and <= 1024.0f;
        }

        public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal)
        {
            normal = Vector3.UnitY;
            return worldX is >= 0.0f and <= 1024.0f && worldZ is >= 0.0f and <= 1024.0f;
        }
    }
}
