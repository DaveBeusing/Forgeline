using System.Numerics;

namespace ForgeLine.Presentation.Tests;

internal static class ActionRingFixture
{
    internal static PlayerActionPanelView Present(PresentationSnapshot snapshot, GameplayHudLayout layout, PlayerActionPanelView panel)
    {
        var geometry = RadialActionLayout.Fit(new(layout.SafeArea.X+layout.SafeArea.Width/2,layout.SafeArea.Y+layout.SafeArea.Height/2),
            0,layout,[layout.TopStatusBar,layout.Minimap,layout.AlertStack]);
        ActionRingSlots slots=default;
        for(int i=0;i<Math.Min(4,PlayerActionDockInteractionLayout.GetItemCount(panel.Mode,snapshot.PlayerActions));i++)
            slots[i]=new(new(PlayerActionDockControlKind.Item,panel.Mode,i),ObjectActionRingView.ItemKey(panel.Mode,i,snapshot.PlayerActions));
        slots[7]=new(default,default,ActionRingNavigation.Back);
        return panel with { Ring=new(default,snapshot.SessionId,1,1,false,panel.IsOpen,geometry,panel.IsOpen,
            HoveredSlot:panel.HoveredIndex,Pressed:panel.PointerPressed) { Global=true,Slots=slots } };
    }
}
