using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RtsStrategicOverlayVisualizationTests
{
    [Fact]
    public void PowerOverlayLabelsLogicalNetworkAndDoesNotConnectEntities()
    {
        var generator =
            new StrategicPowerEntityReadModel(
                new EntityId(1, 1),
                new Vector3(
                    10.0f,
                    0.0f,
                    10.0f),
                new PowerNetworkId(3),
                true,
                20.0,
                true,
                PowerGeneratorState.Generating,
                false,
                0.0,
                0.0,
                PowerPriority.Industrial,
                false,
                PowerOperationalState.Offline);
        var consumer =
            new StrategicPowerEntityReadModel(
                new EntityId(2, 1),
                new Vector3(
                    40.0f,
                    0.0f,
                    40.0f),
                new PowerNetworkId(3),
                false,
                0.0,
                false,
                PowerGeneratorState.Offline,
                true,
                10.0,
                10.0,
                PowerPriority.Industrial,
                true,
                PowerOperationalState.Powered);
        var network =
            new StrategicPowerNetworkReadModel(
                new PowerNetworkId(3),
                20.0,
                10.0,
                10.0,
                0.0,
                1,
                1,
                1,
                1,
                0,
                0);
        var snapshot =
            new StrategicOverlaySnapshot(
                new SimulationTick(1),
                StrategicOverlayMode.Power,
                [],
                [],
                [],
                [],
                [],
                [],
                [generator, consumer],
                [network]);
        var draw =
            new DebugDraw
            {
                Enabled = true
            };

        RtsStrategicOverlayVisualization.Draw(
            draw,
            StrategicOverlayMode.Power,
            snapshot);

        Assert.Equal(
            6,
            draw.Lines.Length);
        Assert.Contains(
            draw.Labels,
            label =>
                label.Text.Contains(
                    "PWR N3 STABLE",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AllOverlayUsesBoundedLayerCounts()
    {
        StrategicNavigationSectorReadModel[] sectors =
            Enumerable.Range(
                    0,
                    80)
                .Select(
                    index =>
                        new StrategicNavigationSectorReadModel(
                            new Navigation.NavigationSectorCoordinate(
                                index,
                                0),
                            new World.AxisAlignedBounds(
                                new Vector3(
                                    index * 10.0f,
                                    0.0f,
                                    0.0f),
                                new Vector3(
                                    index * 10.0f + 8.0f,
                                    1.0f,
                                    8.0f)),
                            2))
                .ToArray();
        var snapshot =
            new StrategicOverlaySnapshot(
                new SimulationTick(1),
                StrategicOverlayMode.All,
                [],
                [],
                [],
                [],
                sectors,
                [],
                [],
                []);
        var draw =
            new DebugDraw
            {
                Enabled = true
            };

        RtsStrategicOverlayVisualization.Draw(
            draw,
            StrategicOverlayMode.All,
            snapshot);

        Assert.True(
            draw.Lines.Length <
            80 * 12);
    }
}
