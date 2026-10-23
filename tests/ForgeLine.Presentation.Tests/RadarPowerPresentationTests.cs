using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RadarPowerPresentationTests
{
    private static readonly FactionId LocalFaction = new(1);
    private static readonly FactionId EnemyFaction = new(2);

    [Theory]
    [InlineData("membership", BuildingPresentationState.Unpowered)]
    [InlineData("consumer", BuildingPresentationState.Unpowered)]
    [InlineData("disabled", BuildingPresentationState.Unpowered)]
    [InlineData("brownout", BuildingPresentationState.Unpowered)]
    [InlineData("powered", BuildingPresentationState.Operational)]
    public void RadarStateUsesTheSameOperationalContractWithoutPublishingHiddenEnemies(
        string condition, BuildingPresentationState expected)
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer, store, LocalFaction));
        EntityId radar = AddRadar(simulation, LocalFaction);
        if (condition == "membership") simulation.Entities.RemoveComponent<PowerNetworkMembership>(radar);
        if (condition == "consumer") simulation.Entities.RemoveComponent<PowerConsumer>(radar);
        if (condition == "disabled")
            simulation.Entities.SetComponent(radar, new PowerConsumer(18.0, enabled: false));
        if (condition == "brownout")
            simulation.Entities.SetComponent(radar, new PowerConsumer(18.0, allocatedPower: 9.0,
                state: PowerOperationalState.Brownout));
        EntityId hidden = AddRadar(simulation, EnemyFaction);

        simulation.AdvanceOneTick();

        Assert.True(buffer.TryReadLatest(out PresentationSnapshot snapshot));
        RenderInstance local = Assert.Single(snapshot.Instances.ToArray());
        Assert.Equal(radar, local.Entity);
        Assert.Equal(expected, local.BuildingFeature.State);
        Assert.DoesNotContain(snapshot.Instances.ToArray(), instance => instance.Entity == hidden);
        Assert.NotNull(snapshot.Intelligence);
        Assert.Empty(snapshot.Intelligence.Contacts);
    }

    [Fact]
    public void RadarOutageRemovesCurrentEnemyRenderDataAndPreservesOnlyLastKnownContact()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(store));
        simulation.RegisterTickObserver(new PresentationExtractor(buffer, store, LocalFaction));
        EntityId radar = AddRadar(simulation, LocalFaction);
        EntityId enemy = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(enemy,
            new WorldTransform(new Vector3(10.0f, 0.0f, 0.0f), Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(enemy, new VisualIdentity(1));
        simulation.Entities.AddComponent(enemy, new IntelligenceSignature(EnemyFaction, 42));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out PresentationSnapshot observed));
        Assert.Contains(observed.Instances.ToArray(), instance => instance.Entity == enemy);

        simulation.Entities.SetComponent(radar, new PowerConsumer(18.0, enabled: false));
        simulation.Entities.SetComponent(enemy,
            new WorldTransform(new Vector3(30.0f, 0.0f, 0.0f), Quaternion.Identity, Vector3.One));
        simulation.AdvanceOneTick();

        Assert.True(buffer.TryReadLatest(out PresentationSnapshot hidden));
        RenderInstance local = Assert.Single(hidden.Instances.ToArray());
        Assert.Equal(radar, local.Entity);
        Assert.Equal(BuildingPresentationState.Unpowered, local.BuildingFeature.State);
        Assert.NotNull(hidden.Intelligence);
        IntelligenceContact contact = Assert.Single(hidden.Intelligence.Contacts);
        Assert.False(contact.IsCurrent);
        Assert.Equal(new Vector3(10.0f, 0.0f, 0.0f), contact.LastKnownPosition);
        Assert.Equal(observed.Tick, contact.LastSeenTick);
    }

    [Fact]
    public void SensorOverlayUsesOperationalOwnedRadarAndKeepsPassiveVisualCoverage()
    {
        using MatchRuntime runtime = MatchRuntime.Create(
            CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay),
            TestContext.Current.CancellationToken);
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity);
        var interaction = new PresentationInteractionState();
        interaction.SetStrategicOverlay(StrategicOverlayMode.Sensors);
        var context = new PresentationExtractionContext(runtime, new PlayerId(1), interaction, gateway);
        var buffer = new PresentationSnapshotBuffer();
        runtime.Simulation.RegisterTickObserver(new PresentationExtractor(buffer, context));
        EntityId powered = AddRadar(runtime.Simulation, LocalFaction);
        EntityId offline = AddRadar(runtime.Simulation, LocalFaction);
        runtime.Simulation.Entities.RemoveComponent<PowerNetworkMembership>(offline);
        runtime.Simulation.Entities.AddComponent(offline, new VisualSensorState(LocalFaction, 25.0f));
        EntityId foreign = AddRadar(runtime.Simulation, EnemyFaction);
        runtime.Simulation.Entities.SetComponent(foreign, new PowerNetworkMembership(new PowerNetworkId(101)));
        EntityId generator = runtime.Simulation.Entities.CreateEntity();
        runtime.Simulation.Entities.AddComponent(generator, new PowerGenerator(18.0));
        runtime.Simulation.Entities.AddComponent(generator, new PowerNetworkMembership(new PowerNetworkId(100)));

        runtime.Simulation.AdvanceOneTick();

        Assert.True(buffer.TryReadLatest(out PresentationSnapshot snapshot));
        Assert.NotNull(snapshot.StrategicOverlay);
        StrategicSensorReadModel active = Assert.Single(snapshot.StrategicOverlay.Sensors, sensor => sensor.Entity == powered);
        Assert.Equal(100.0f, active.RadarRangeMeters);
        StrategicSensorReadModel passive = Assert.Single(snapshot.StrategicOverlay.Sensors, sensor => sensor.Entity == offline);
        Assert.Equal(25.0f, passive.VisualRangeMeters);
        Assert.Equal(0.0f, passive.RadarRangeMeters);
        Assert.Equal(0.0f, passive.IdentificationRangeMeters);
        Assert.DoesNotContain(snapshot.StrategicOverlay.Sensors, sensor => sensor.Entity == foreign);
    }

    private static EntityId AddRadar(SimulationCoordinator simulation, FactionId faction)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, new VisualIdentity(113));
        simulation.Entities.AddComponent(entity, new CompletedBuilding(BuildingIds.Radar,
            new PlayerId(faction.Value), SimulationTick.Zero));
        simulation.Entities.AddComponent(entity, new IntelligenceSignature(faction, 113));
        simulation.Entities.AddComponent(entity, new RadarSensorState(faction, 100.0f, 50.0f, 1));
        simulation.Entities.AddComponent(entity, new PowerConsumer(18.0, PowerPriority.Critical,
            allocatedPower: 18.0, state: PowerOperationalState.Powered));
        simulation.Entities.AddComponent(entity, new PowerNetworkMembership(new PowerNetworkId(100)));
        return entity;
    }
}
