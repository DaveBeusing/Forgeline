using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class FormationMovementDebugVisualizationTests
{
    [Fact]
    public void FormationSnapshotBuildsGroupSlotAssignmentAndRouteGeometry()
    {
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        EntityId group = new(20, 1);
        EntityId member = new(7, 1);

        var snapshot = new FormationMovementDebugSnapshot(
        [
            new FormationMovementDebugGroup(
                group,
                FormationTemplate.Wedge,
                new Vector3(20.0f, 1.0f, 20.0f),
                new Vector3(10.0f, 0.0f, 10.0f),
                new Vector3(30.0f, 4.0f, 30.0f),
                Vector3.UnitZ,
                new Vector3(20.0f, 1.0f, 36.0f),
                MemberCount: 12,
                CompressionScale: 0.75f,
                SplitCohortCount: 1)
        ],
        [
            new FormationMovementDebugSlot(
                group,
                member,
                SlotIndex: 0,
                new Vector3(18.0f, 1.0f, 18.0f),
                new Vector3(20.0f, 1.0f, 24.0f))
        ],
        [
            new FormationMovementDebugRoutePoint(
                group,
                Index: 0,
                new Vector3(20.0f, 1.0f, 28.0f)),
            new FormationMovementDebugRoutePoint(
                group,
                Index: 1,
                new Vector3(20.0f, 1.0f, 36.0f))
        ]);

        FormationMovementDebugVisualization.Draw(
            debugDraw,
            snapshot,
            maximumSlots: 8);

        Assert.True(debugDraw.Lines.Length > 12);
        Assert.Single(debugDraw.Labels);
    }

    [Fact]
    public void EmptyFormationSnapshotProducesNoGeometry()
    {
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        FormationMovementDebugVisualization.Draw(
            debugDraw,
            FormationMovementDebugSnapshot.Empty);

        Assert.True(debugDraw.Lines.IsEmpty);
        Assert.Empty(debugDraw.Labels);
    }
}
