using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GroundMovementDebugVisualizationTests
{
    [Fact]
    public void MovementSnapshotBuildsVelocityTargetAndNeighborhoodGeometry()
    {
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        var snapshot = new GroundMovementDebugSnapshot(
        [
            new GroundMovementDebugAgent(
                new EntityId(7, 1),
                new Vector3(10.0f, 2.0f, 20.0f),
                new Vector3(3.0f, 0.0f, 1.0f),
                new Vector3(30.0f, 2.0f, 24.0f),
                Radius: 1.0f,
                SeparationRadius: 4.0f,
                GroundMovementStatus.Moving,
                HasTarget: true)
        ]);

        GroundMovementDebugVisualization.Draw(
            debugDraw,
            snapshot,
            maximumAgents: 1);

        Assert.True(debugDraw.Lines.Length > 24);
    }

    [Fact]
    public void EmptyMovementSnapshotProducesNoGeometry()
    {
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        GroundMovementDebugVisualization.Draw(
            debugDraw,
            GroundMovementDebugSnapshot.Empty);

        Assert.True(debugDraw.Lines.IsEmpty);
    }
}
