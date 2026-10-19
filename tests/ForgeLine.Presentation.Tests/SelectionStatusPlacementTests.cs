using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectionStatusPlacementTests
{
    [Theory]
    [InlineData(1280,720,96u,.75f,false)]
    [InlineData(1280,720,192u,2f,true)]
    [InlineData(1920,1080,144u,1f,true)]
    [InlineData(3440,1440,192u,2f,true)]
    [InlineData(3840,2160,192u,2f,false)]
    public void StatusAvoidsPaintedSectorsGuidanceNotificationsAndMinimap(int width,int height,uint dpi,float scale,bool guide)
    {
        var layout=GameplayHudLayout.Create(width,height,dpi,scale);
        var snapshot=HoverTooltipRenderingTests.StatusSnapshot();
        var notifications=ActionableAlertLayout.Resolve(snapshot,layout);
        var onboarding=guide ? OnboardingPresentation.Resolve(layout,0,detailed:true) : (OnboardingPresentation?)null;
        Span<HudRect> occupied=stackalloc HudRect[8];
        RadialActionLayout.ResolveObstacles(occupied,layout,onboarding,notifications:notifications);
        var geometry=RadialActionLayout.Fit(new(width/2f,height/2f),0,layout,occupied);
        Assert.True(geometry.IsValid);
        foreach(bool full in new[]{false,true}) {
            ActionRingSlots slots=default;
            for(int i=0;i<8;i++) if(full || i is 0 or 7) slots[i]=new(default,default,ActionRingNavigation.Back);
            var ring=new ObjectActionRingView(new(11,1),snapshot.SessionId,1,1,true,true,geometry,true) {Slots=slots};
            HudRect bounds=default;
            for(float fit=1.25f;fit>=.75f;fit-=.125f) {
                bounds=HoverTooltipPlacement.BesideRing(ring,layout,occupied,330*fit,226*fit);
                if(!bounds.IsEmpty) break;
            }
            Assert.False(bounds.IsEmpty);
            Assert.True(layout.SafeArea.Contains(new(bounds.X,bounds.Y)));
            Assert.True(layout.SafeArea.Contains(new(bounds.Right,bounds.Bottom)));
            foreach(var obstacle in occupied) Assert.False(bounds.Intersects(obstacle));
            for(int slot=0;slot<8;slot++) if(slots[slot].Present) Assert.False(bounds.Intersects(geometry.SectorBounds(slot)));
        }
    }

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void SectorBoundsContainEveryPaintedEdgeWithoutReservingAbsentSectors(int count)
    {
        var geometry=new RadialActionLayout(new(500,500),new(500,500),60,156,count);
        for(int slot=0;slot<count;slot++) {
            var bounds=geometry.SectorBounds(slot);
            for(int sample=0;sample<=20;sample++) foreach(float radius in new[]{59f,157f}) {
                float angle=geometry.Start(slot)+(geometry.End(slot)-geometry.Start(slot))*sample/20;
                var point=geometry.Center+RadialActionLayout.Direction(angle)*radius;
                Assert.InRange(point.X,bounds.X-.001f,bounds.Right+.001f);
                Assert.InRange(point.Y,bounds.Y-.001f,bounds.Bottom+.001f);
            }
        }
    }

    [Fact]
    public void EmptyNotificationsDoNotReserveAnInvisibleRingObstacle()
    {
        var layout=GameplayHudLayout.Create(1920,1080,96);
        Span<HudRect> occupied=stackalloc HudRect[8];
        RadialActionLayout.ResolveObstacles(occupied,layout,notifications:default(NotificationStackLayout));
        Assert.True(occupied[4].IsEmpty);
        var snapshot=HoverTooltipRenderingTests.StatusSnapshot();
        var notifications=ActionableAlertLayout.Resolve(snapshot,layout);
        RadialActionLayout.ResolveObstacles(occupied,layout,notifications:notifications);
        Assert.Equal(notifications.PaintedBounds,occupied[4]);
        Assert.True(occupied[4].Height<layout.AlertStack.Height);
    }
}
