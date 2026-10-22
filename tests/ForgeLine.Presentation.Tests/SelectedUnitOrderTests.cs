using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectedUnitOrderTests
{
    private static readonly SimulationSessionId Session = new(51);
    private static readonly EntityId Unit = new(7, 1);
    private static readonly EntityId Second = new(8, 1);
    private static readonly Vector3 Destination = new(70, 5, 80);

    [Fact]
    public void SelectionAndSessionChangesInvalidatePublishedOrders()
    {
        EntityId[] selection = [Unit];
        SelectedUnitOrderView[] entries =
            [new(Unit, Vector3.Zero, SelectedUnitOrderKind.Move, "MOVE", Destination, true)];
        var snapshot = new SelectedUnitOrderSnapshot(Session, new SimulationTick(4),
            selection, entries, "MOVE");

        selection[0] = Second;
        entries[0] = entries[0] with { Target = Vector3.Zero };
        Assert.Equal(Unit, snapshot.Orders[0].Entity);
        Assert.Equal(Destination, snapshot.Orders[0].Target);
        Assert.True(snapshot.MatchesSelection(Session, [Unit]));
        Assert.False(snapshot.MatchesSelection(Session, [Second]));
        Assert.False(snapshot.MatchesSelection(new SimulationSessionId(52), [Unit]));
        Assert.False(snapshot.MatchesSelection(Session, []));
    }

    [Fact]
    public void SharedDestinationsYieldOnePulseButSeparateUnitConnections()
    {
        EntityId[] selection = [Unit, Second];
        var orders = new SelectedUnitOrderSnapshot(Session, new SimulationTick(4),
            selection,
            [new(Unit, new(10, 0, 15), SelectedUnitOrderKind.Move, "MOVE", Destination, true),
             new(Second, new(20, 0, 15), SelectedUnitOrderKind.Move, "MOVE", Destination, true)],
            "MOVE 2");
        PresentationSnapshot presentation = Snapshot(selection, orders);
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(presentation);
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        var draw = new DebugDraw { Enabled = true };
        var overlay = new SelectedUnitOrderOverlay();
        overlay.Draw(draw, world, selection, Session, new RtsCamera(), 1, 900, 1,
            TimeSpan.FromSeconds(2), reducedMotion: true);
        Assert.Equal(2, overlay.LastConnectionCount);
        Assert.Equal(1, overlay.LastUniqueTargetCount);
        Assert.Equal(9, draw.Lines.Length);
        Assert.All(draw.Lines.ToArray(), x => Assert.True(float.IsFinite(x.Start.X)));

        overlay.Draw(draw, world, [Unit], Session, new RtsCamera(), 1, 900, 1,
            TimeSpan.FromSeconds(3), reducedMotion: false);
        Assert.Equal(0, overlay.LastConnectionCount);
        Assert.Empty(draw.Lines.ToArray());
    }

    [Fact]
    public void NoTargetNeverRendersWorldOriginOrStaleMarkers()
    {
        var orders = new SelectedUnitOrderSnapshot(Session, new SimulationTick(8),
            [Unit], [new(Unit, new(10, 0, 10), SelectedUnitOrderKind.Hold,
                "HOLD POSITION", default, false)], "HOLD POSITION");
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(Snapshot([Unit], orders));
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        var draw = new DebugDraw { Enabled = true };
        var overlay = new SelectedUnitOrderOverlay();
        overlay.Draw(draw, world, [Unit], Session, new RtsCamera(), 1, 900, 1,
            TimeSpan.FromSeconds(42), reducedMotion: false);
        Assert.Empty(draw.Lines.ToArray());
    }

    [Fact]
    public void ThousandSelectedMembersRemainBoundedWithTenThousandWorldInstances()
    {
        EntityId[] selected = Enumerable.Range(1, 1000)
            .Select(i => new EntityId((uint)i, 1)).ToArray();
        SelectedUnitOrderView[] orders = selected.Select(id =>
            new SelectedUnitOrderView(id, new(10, 0, 10),
                SelectedUnitOrderKind.Move, "MOVE", Destination, true)).ToArray();
        var model = new SelectedUnitOrderSnapshot(Session, new SimulationTick(20),
            selected, orders, "MOVE 1000");
        RenderInstance[] instances = Enumerable.Range(1, 10000).Select(i =>
            new RenderInstance(new EntityId((uint)i, 1),
                new RenderTransform(new Vector3(i, 0, 10),
                    Quaternion.Identity, Vector3.One),
                new RenderMeshHandle(1), RenderMaterialHandle.Default,
                RenderVisibilityMask.World)).ToArray();
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new PresentationSnapshot(new SimulationTick(20),
            TimeSpan.FromMilliseconds(50), instances.Length, instances, sessionId: Session)
        { SelectedOrders = model });
        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        var overlay = new SelectedUnitOrderOverlay();
        var draw = new DebugDraw { Enabled = true };
        var camera = new RtsCamera();

        overlay.Draw(draw, world, selected, Session, camera, 1, 900, 1,
            TimeSpan.FromSeconds(1), reducedMotion: true);
        Assert.Equal(1000, overlay.LastConnectionCount);
        Assert.Equal(1, overlay.LastUniqueTargetCount);
        Assert.Equal(1007, draw.Lines.Length);
        DebugLine[] still = draw.Lines.ToArray();
        overlay.Draw(draw, world, selected, Session, camera, 1, 900, 1,
            TimeSpan.FromSeconds(60), reducedMotion: true);
        Assert.Equal(still, draw.Lines.ToArray());

        overlay.Draw(draw, world, selected, Session, camera, 1, 900, 1,
            TimeSpan.FromSeconds(0.3), reducedMotion: false);
        Assert.NotEqual(still[1000].Color.W, draw.Lines[1000].Color.W);
    }

    private static PresentationSnapshot Snapshot(
        EntityId[] selected, SelectedUnitOrderSnapshot orders)
    {
        var instances = selected.Select((id, index) =>
            new RenderInstance(id,
                new RenderTransform(new Vector3(10 + 10 * index, 0, 15),
                    Quaternion.Identity, Vector3.One),
                new RenderMeshHandle(1), RenderMaterialHandle.Default,
                RenderVisibilityMask.World)).ToArray();
        return new PresentationSnapshot(new SimulationTick(orders.Tick.Value),
            TimeSpan.FromMilliseconds(50), instances.Length, instances, sessionId: Session)
        { SelectedOrders = orders };
    }
}
