using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PrototypeBattlefieldDebugVisualizationTests
{
    [Fact]
    public void PrototypeOverlayDrawsStrategicMapLayers()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        var states =
            new Dictionary<
                string,
                StrategicInfrastructureOperationalState>
            {
                ["crossing.north_bridge"] =
                    StrategicInfrastructureOperationalState.Disabled,
                ["crossing.south_ford"] =
                    StrategicInfrastructureOperationalState.Operational
            };
        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        PrototypeBattlefieldDebugVisualization.Draw(
            debugDraw,
            definition,
            states);

        Assert.True(
            debugDraw.Lines.Length > 50);
        Assert.True(
            debugDraw.Labels.Count >=
            definition.Starts.Count +
            definition.Resources.Count +
            definition.Sites.Count +
            definition.Crossings.Count +
            definition.Objectives.Count);
        Assert.Contains(
            debugDraw.Labels,
            label =>
                label.Text.Contains(
                    "North Bridge [Disabled]",
                    StringComparison.Ordinal));
        Assert.Contains(
            debugDraw.Labels,
            label =>
                label.Text.Contains(
                    "Command Core P1",
                    StringComparison.Ordinal));
    }
}
