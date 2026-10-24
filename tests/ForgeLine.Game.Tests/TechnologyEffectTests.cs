using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyEffectTests
{
    [Fact]
    public void ArtilleryRequiresMechanizedSystemsWhileCoreForceAndTransportRemainFree()
    {
        var units = DirectorateContent.CreateUnitCatalog();
        Assert.Equal(TechnologyCapabilityIds.MechanizedSystems, units[UnitIds.MobileArtillery].RequiredTechnologyCapability);
        Assert.Equal(TechnologyCapabilityIds.FieldEngineering, units[UnitIds.CombatEngineer].RequiredTechnologyCapability);
        foreach (var id in new[] { UnitIds.MainBattleTank, UnitIds.CargoTruck, UnitIds.SupplyTruck, UnitIds.ScoutVehicle, UnitIds.RifleSquad })
            Assert.False(units[id].RequiredTechnologyCapability.IsSpecified);
    }

    [Fact]
    public void FusionUsesBaseRangeEveryScanAndNeverExtendsDetectionOrAnotherOwnersRadar()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var intelligence = new BattlefieldIntelligenceSystem(store) { DebugCaptureEnabled = true };
        simulation.RegisterSystem(intelligence);
        var blue = new FactionId(1);
        EntityId radar = AddRadar(1, blue);
        EntityId foreign = AddRadar(2, new FactionId(2));
        EntityId identifiedLater = AddTarget(210);
        EntityId detectedOnly = AddTarget(250);
        EntityId outside = AddTarget(301);
        simulation.AdvanceOneTick();
        Assert.False(store.IsEntityCurrentlyIdentified(blue, identifiedLater));
        Assert.True(store.IsEntityCurrentlyDetected(blue, identifiedLater));
        EntityId unlock = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(unlock, new TechnologyCapabilityUnlock(new PlayerId(1),
            TechnologyCapabilityIds.SensorFusion, TechnologyIds.SensorFusion, simulation.CurrentTick));
        simulation.RunTicks(20, TestContext.Current.CancellationToken);
        Assert.True(store.IsEntityCurrentlyIdentified(blue, identifiedLater));
        Assert.False(store.IsEntityCurrentlyIdentified(blue, detectedOnly));
        Assert.True(store.IsEntityCurrentlyDetected(blue, detectedOnly));
        Assert.False(store.IsEntityCurrentlyDetected(blue, outside));
        Assert.Equal(180, simulation.Entities.GetComponent<RadarSensorState>(radar).IdentificationRangeMeters);
        Assert.Equal(225, intelligence.DebugSensors.Single(s => s.Entity == radar).IdentificationRangeMeters);
        Assert.Equal(180, intelligence.DebugSensors.Single(s => s.Entity == foreign).IdentificationRangeMeters);
        EntityId newRadar = AddRadar(1, blue);
        simulation.AdvanceOneTick();
        Assert.Equal(225, intelligence.DebugSensors.Single(s => s.Entity == newRadar).IdentificationRangeMeters);
        simulation.Entities.SetComponent(radar, new RadarSensorState(blue, 200, 180, 1));
        simulation.AdvanceOneTick();
        Assert.Equal(200, intelligence.DebugSensors.Single(s => s.Entity == radar).IdentificationRangeMeters);
        simulation.Entities.SetComponent(radar, new PowerConsumer(18, enabled: false));
        simulation.Entities.SetComponent(newRadar, new PowerConsumer(18, enabled: false));
        EntityId unseen = AddTarget(190);
        simulation.AdvanceOneTick();
        Assert.False(store.IsEntityCurrentlyDetected(blue, unseen));
        Assert.DoesNotContain(intelligence.DebugSensors, s => s.Entity == radar);

        EntityId AddRadar(uint player, FactionId faction)
        {
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new CompletedBuilding(BuildingIds.Radar, new PlayerId(player), SimulationTick.Zero));
            simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(entity, new RadarSensorState(faction, 300, 180, 1));
            simulation.Entities.AddComponent(entity, new PowerConsumer(18, allocatedPower: 18, state: PowerOperationalState.Powered));
            simulation.Entities.AddComponent(entity, new PowerNetworkMembership(new PowerNetworkId(1)));
            return entity;
        }
        EntityId AddTarget(float distance)
        {
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(new(distance, 0, 0), Quaternion.Identity, Vector3.One));
            simulation.Entities.AddComponent(entity, new IntelligenceSignature(new FactionId(3), 7));
            return entity;
        }
    }
}
