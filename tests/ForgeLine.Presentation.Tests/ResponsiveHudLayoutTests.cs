using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResponsiveHudLayoutTests
{
    [Fact]
    public void RemainingOwnersReflowAcrossEveryDisplayProfileWithoutDockReservations()
    {
        foreach(var size in new[]{(1280,720),(1920,1080),(2560,1440),(3440,1440),(3840,2160)})
        foreach(uint dpi in new uint[]{96,144,192}) foreach(float scale in new[]{.75f,1f,2f})
        {
            var layout=GameplayHudLayout.Create(size.Item1,size.Item2,dpi,scale);
            Assert.Equal(layout.SafeArea.Right,layout.TopStatusBar.Right,3);
            Assert.Equal(layout.SafeArea.Right,layout.AlertStack.Right,3);
            Assert.Equal(layout.TopStatusBar.Bottom+layout.Gap,layout.AlertStack.Y,3);
            HudRect[] regions=[layout.TopStatusBar,layout.AlertStack,layout.Minimap,layout.SecondaryView,layout.OnboardingBounds];
            for(int i=0;i<regions.Length;i++) {
                Assert.False(regions[i].IsEmpty);
                Assert.True(layout.SafeArea.Contains(new(regions[i].X,regions[i].Y)));
                Assert.True(layout.SafeArea.Contains(new(regions[i].Right,regions[i].Bottom)));
                for(int j=i+1;j<regions.Length;j++) Assert.False(regions[i].Intersects(regions[j]));
            }
            Assert.Equal(layout.NotificationBounds(false),layout.NotificationBounds(true));
        }
    }
    [Theory]
    [InlineData(1280,720,96u,.75f)]
    [InlineData(1280,720,192u,2f)]
    [InlineData(3440,1440,144u,1f)]
    public void AssignedGroupOverviewUsesPublishedLeftBoundsAndCannotCoverRingOrOperations(int width,int height,uint dpi,float scale)
    {
        var layout=GameplayHudLayout.Create(width,height,dpi,scale);
        var empty=CombatGroupOverviewView.Empty;
        Assert.True(layout.GroupOverviewBounds(empty).IsEmpty);
        var groups=new CombatGroupOverviewView([default(CombatGroupSummaryReadModel) with {Slot=1,MemberCount=3,Label="Armoured support"}],[]);
        var bounds=layout.GroupOverviewBounds(groups);
        Assert.False(bounds.IsEmpty);
        Assert.True(layout.SecondaryView.Contains(new(bounds.X,bounds.Y)));
        Assert.True(layout.SecondaryView.Contains(new(bounds.Right,bounds.Bottom)));
        Assert.False(bounds.Intersects(GameplayGuidanceLayout.Resolve(layout,true)));
        Span<HudRect> occupied=stackalloc HudRect[8];
        RadialActionLayout.ResolveObstacles(occupied,layout,groups:groups);
        Assert.Equal(bounds,occupied[1]);
        Assert.True(occupied[7].IsEmpty);
        var ring=RadialActionLayout.Fit(new(width/2f,height/2f),0,layout,occupied);
        Assert.True(ring.IsValid);
        var nearest=new Vector2(Math.Clamp(ring.Center.X,bounds.X,bounds.Right),Math.Clamp(ring.Center.Y,bounds.Y,bounds.Bottom));
        Assert.True(Vector2.Distance(nearest,ring.Center)>=ring.OuterRadius);
    }
    [Fact]
    public void ClosedRingDoesNotAllocateGeometryOrDrawAnyReplacementPanel()
    {
        using var device=new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer=new PlayerActionRingHudRenderer(device,null);
        var context=new SelectionOverlayRenderingTests.RecordingContext();
        renderer.Render(context,GameplayRebindingTests.Snapshot(),default,default,FormationTemplate.Compact,
            GameplayHudLayout.Create(context.Width,context.Height,96));
        Assert.Equal(0,renderer.LastRenderedVertexCount);
        Assert.Empty(device.Buffers);
        Assert.True(device.Pipeline!.AlphaBlendEnabled);
        Assert.DoesNotContain(typeof(GameplayHudLayout).GetProperties(),p=>p.Name is "ActionDock" or "SelectionInspector" or "PrimaryCommands");
    }
    [Theory] [InlineData(0,0)] [InlineData(0,720)] [InlineData(1280,0)] [InlineData(1,1)] [InlineData(200,160)]
    public void UnusableClientsExposeNoInvisibleInputControls(int width,int height)
    {
        var layout=GameplayHudLayout.Create(width,height,192,2);
        Assert.True(layout.SafeArea.IsEmpty);
        Assert.True(layout.AlertStack.IsEmpty);
        Assert.True(layout.OnboardingBounds.IsEmpty);
        Assert.False(HudInteractionContext.BlocksWorldPointer(Vector2.Zero,layout,true));
    }
}
