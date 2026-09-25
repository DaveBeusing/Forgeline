using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResourceDepositDebugVisualizationTests
{
    [Fact]
    public void DrawDepositsShowsBoundsTypeAndRemainingQuantity()
    {
        var debugDraw = new DebugDraw { Enabled = true };
        var bounds = new AxisAlignedBounds(
            Vector3.Zero,
            new Vector3(4.0f, 1.0f, 4.0f));
        var snapshot = new ResourceExtractionDebugSnapshot(
            [
                new ResourceDepositReadModel(
                    new EntityId(1, 1),
                    ResourceIds.FerrousOre,
                    "resource.ferrous_ore",
                    bounds,
                    1_000.0,
                    625.0,
                    1.25,
                    FactionId.None,
                    ResourceDepositState.Available)
            ],
            [],
            default);

        ResourceDepositDebugVisualization.DrawDeposits(
            debugDraw,
            snapshot,
            Vector4.One,
            Vector4.Zero);

        Assert.Equal(12, debugDraw.Lines.Length);
        Assert.Single(debugDraw.Labels);
        Assert.Contains(
            "resource.ferrous_ore",
            debugDraw.Labels[0].Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "625.0/1000.0",
            debugDraw.Labels[0].Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "r=1.25",
            debugDraw.Labels[0].Text,
            StringComparison.Ordinal);
    }
}
