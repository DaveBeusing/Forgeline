using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CommandDockLayoutTests
{
    [Theory]
    [InlineData(1280,720,96u,.75f)]
    [InlineData(1280,720,192u,2f)]
    [InlineData(3440,1440,144u,1f)]
    public void RingSlotsUsePublishedGeometryAndLeaveCenterAndAbsentSlotsFree(int width,int height,uint dpi,float scale)
    {
        var snapshot=GameplayRebindingTests.Snapshot();
        var layout=GameplayHudLayout.Create(width,height,dpi,scale);
        var panel=ActionRingFixture.Present(snapshot,layout,default(PlayerActionPanelView) with {Mode=PlayerActionPanelMode.Construction});
        var ring=panel.Ring;
        Assert.True(ring.Geometry.IsValid);
        Assert.False(ring.TryHit(ring.Geometry.Center,out _));
        Assert.True(ring.TryHit(ring.Geometry.LabelCenter(0),out var item));
        Assert.Equal(0,item.Target.ItemIndex);
        Assert.False(ring.TryHit(ring.Geometry.LabelCenter(6),out _));
        Assert.True(ring.TryHit(ring.Geometry.LabelCenter(7),out var back));
        Assert.Equal(ActionRingNavigation.Back,back.Navigation);
    }
    [Fact]
    public void PendingRequestsDisableActionsWithoutHidingCards()
    {
        var actions = new PlayerActionSnapshot(new ForgeLine.Simulation.SimulationSessionId(1),
            new ForgeLine.Simulation.SimulationTick(1), [], 1, null, null);
        Assert.Equal("REQUEST PENDING", PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Supply, 0, actions).DisabledReason);
        Assert.False(PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Activate,
            PlayerActionPanelMode.Supply, 0, default, actions));
    }
    private static void Inside(HudRect outer, HudRect inner)
    {
        Assert.False(inner.IsEmpty);
        Assert.True(inner.X >= outer.X - .01f && inner.Y >= outer.Y - .01f);
        Assert.True(inner.Right <= outer.Right + .01f && inner.Bottom <= outer.Bottom + .01f);
    }
}
