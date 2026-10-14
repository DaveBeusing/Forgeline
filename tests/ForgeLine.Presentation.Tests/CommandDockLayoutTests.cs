using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CommandDockLayoutTests
{
    [Fact]
    public void EveryModeFooterAndSelectedPageFitTheDisplayMatrix()
    {
        foreach (var size in new[] { (1280,720), (1920,1080), (2560,1440), (3440,1440), (3840,2160) })
        foreach (uint dpi in new uint[] { 96,144,192 })
        foreach (float scale in new[] { .75f,1f,2f })
        {
            var layout = GameplayHudLayout.Create(size.Item1, size.Item2, dpi, scale);
            var bar = PlayerActionDockInteractionLayout.GetModeBarRect(layout);
            var header = PlayerActionDockInteractionLayout.GetHeaderRect(layout);
            var cards = PlayerActionDockInteractionLayout.GetCardsRect(layout);
            var footer = PlayerActionDockInteractionLayout.GetFooterRect(layout);
            Inside(layout.ActionDock, bar); Inside(layout.ActionDock, header); Inside(layout.ActionDock, cards); Inside(layout.ActionDock, footer);
            Assert.False(bar.Intersects(header)); Assert.False(header.Intersects(cards)); Assert.False(cards.Intersects(footer));
            Assert.True(PlayerActionDockInteractionLayout.MaximumVisibleItems(layout) > 0);
            for (int i = 0; i < 7; i++) Inside(bar, PlayerActionDockInteractionLayout.GetModeButtonRect(layout, i));
            for (int i = 0; i < 6; i++) Inside(footer, PlayerActionDockInteractionLayout.GetFooterButtonRect(layout, i));
            for (int selected = 0; selected < 31; selected++)
            {
                int first = PlayerActionDockInteractionLayout.FirstVisibleItem(layout, selected, 31);
                var rect = PlayerActionDockInteractionLayout.GetCardRect(layout, selected, first);
                Inside(cards, rect);
                Assert.True(PlayerActionDockInteractionLayout.TryHit(new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                    layout, true, 31, out var hit, selected));
                Assert.Equal(selected, hit.ItemIndex);
                if (first > 0) Assert.True(PlayerActionDockInteractionLayout.GetCardRect(layout, first - 1, first).IsEmpty);
            }
            var collapsed = PlayerActionDockInteractionLayout.GetCollapsedRect(layout);
            Assert.False(HudInteractionContext.BlocksWorldPointer(new Vector2(collapsed.X + 2, collapsed.Bottom + 2), layout, false, false));
        }
    }
    [Fact]
    public void PageButtonsNavigateOnlyVisibleWindows()
    {
        var layout = GameplayHudLayout.Create(1280, 720, 192, 2);
        int capacity = PlayerActionDockInteractionLayout.MaximumVisibleItems(layout);
        var next = PlayerActionDockInteractionLayout.GetPageButtonRect(layout, 1);
        Assert.True(PlayerActionDockInteractionLayout.TryHit(new(next.X + 1, next.Y + 1),
            layout, true, 31, out var hit));
        Assert.Equal(PlayerActionDockControlKind.Page, hit.Kind);
        Assert.Equal(capacity, hit.ItemIndex);
        var previous = PlayerActionDockInteractionLayout.GetPageButtonRect(layout, 0);
        Assert.False(PlayerActionDockInteractionLayout.TryHit(new(previous.X + 1, previous.Y + 1),
            layout, true, 31, out _));
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
