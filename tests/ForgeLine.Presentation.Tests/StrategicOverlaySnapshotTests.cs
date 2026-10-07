using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class StrategicOverlaySnapshotTests
{
    [Fact]
    public void PlayerFacingOverlayDoesNotRequireDevelopmentDebugSnapshot()
    {
        Assert.Equal(
            DebugOverlayCategory.None,
            DebugOverlayPolicy.ResolveRequiredData(
                DebugOverlayCategory.None,
                StrategicOverlayMode.All));

        Assert.Equal(
            DebugOverlayCategory.Sensors,
            DebugOverlayPolicy.ResolveRequiredData(
                DebugOverlayCategory.Sensors,
                StrategicOverlayMode.Power));
    }

    [Fact]
    public void PowerReadModelsExposeLogicalMembershipWithoutPhysicalLinks()
    {
        var entity =
            new EntityId(10, 1);
        var model =
            new StrategicPowerEntityReadModel(
                entity,
                new Vector3(
                    20.0f,
                    0.0f,
                    40.0f),
                new PowerNetworkId(7),
                IsGenerator: false,
                MaximumGeneration: 0.0,
                GeneratorEnabled: false,
                GeneratorState:
                    PowerGeneratorState.Offline,
                IsConsumer: true,
                Demand: 10.0,
                AllocatedPower: 5.0,
                Priority:
                    PowerPriority.Industrial,
                ConsumerEnabled: true,
                ConsumerState:
                    PowerOperationalState.Brownout);
        var network =
            new StrategicPowerNetworkReadModel(
                new PowerNetworkId(7),
                Generation: 5.0,
                Demand: 10.0,
                AllocatedPower: 5.0,
                Deficit: 5.0,
                GeneratorCount: 1,
                ActiveGeneratorCount: 1,
                ConsumerCount: 1,
                PoweredConsumerCount: 0,
                BrownoutConsumerCount: 1,
                OfflineConsumerCount: 0);
        var snapshot =
            new StrategicOverlaySnapshot(
                new SimulationTick(4),
                StrategicOverlayMode.Power,
                [],
                [],
                [],
                [],
                [],
                [],
                [model],
                [network]);

        Assert.Single(
            snapshot.PowerEntities);
        Assert.Equal(
            new PowerNetworkId(7),
            snapshot.PowerEntities[0].NetworkId);
        Assert.Equal(
            PowerOperationalState.Brownout,
            snapshot.PowerEntities[0].ConsumerState);
        Assert.True(
            snapshot.PowerNetworks[0].IsConstrained);
        Assert.Empty(
            snapshot.LogisticsLinks);
    }
}
