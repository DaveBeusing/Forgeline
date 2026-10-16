using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RadialActionLayoutTests
{
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void PaintedSectorsUseExactAnnularOwnership(int count)
    {
        var geometry = new RadialActionLayout(new(500,400), new(500,400), 60, 150, count);
        Assert.Equal(-1, geometry.Hit(geometry.Center));
        Assert.Equal(-1, geometry.Hit(new(float.NaN, 400)));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, geometry.Hit(geometry.LabelCenter(i)));
            float seam = geometry.Start(i) - RadialActionLayout.GapRadians / 2;
            Assert.Equal(-1, geometry.Hit(geometry.Center + RadialActionLayout.Direction(seam) * 100));
            Assert.Equal(-1, geometry.Hit(geometry.Center + RadialActionLayout.Direction(geometry.Start(i) + .1f) * 59));
            Assert.Equal(-1, geometry.Hit(geometry.Center + RadialActionLayout.Direction(geometry.Start(i) + .1f) * 151));
        }
    }
    [Theory] [InlineData(96,1)] [InlineData(144,1.25f)] [InlineData(192,2)]
    public void FitKeepsCompleteRingInsideSafeAreaAndAvoidsControls(uint dpi, float scale)
    {
        var hud = GameplayHudLayout.Create(3840,2160,dpi,scale);
        HudRect[] obstacles = [hud.TopStatusBar, hud.SelectionInspector, hud.Minimap, hud.VisibleActionDock(false), hud.OnboardingBounds];
        foreach (Vector2 anchor in new Vector2[] { new(hud.SafeArea.X + 1,hud.SafeArea.Y + 1), new(1900,1000), new(hud.SafeArea.Right-1,hud.SafeArea.Bottom-1) })
        {
            var fit = RadialActionLayout.Fit(anchor, 500, hud, obstacles);
            Assert.True(fit.IsValid);
            Assert.True(hud.SafeArea.Contains(fit.Center - new Vector2(fit.OuterRadius)));
            Assert.True(hud.SafeArea.Contains(fit.Center + new Vector2(fit.OuterRadius)));
            foreach (var rect in obstacles)
            {
                Vector2 nearest = new(Math.Clamp(fit.Center.X,rect.X,rect.Right),Math.Clamp(fit.Center.Y,rect.Y,rect.Bottom));
                Assert.True(Vector2.Distance(nearest,fit.Center) >= fit.OuterRadius);
            }
        }
    }
    [Fact]
    public void InvalidOrUnavailableGeometryNeverOwnsInput()
    {
        var hud = GameplayHudLayout.Create(1920,1080,96);
        Assert.False(RadialActionLayout.Fit(new(-1,0),0,hud,[]).IsValid);
        Assert.False(RadialActionLayout.Fit(new(500,500),float.NaN,hud,[]).IsValid);
        Assert.False(RadialActionLayout.Fit(new(500,500),0,hud,[hud.SafeArea]).IsValid);
        Assert.Equal(-1, new RadialActionLayout(default, default, 100, 90, 8).Hit(new(90,0)));
        Assert.Equal(-1, default(RadialActionLayout).Hit(default));
    }
    [Fact]
    public void CloseReleasesOwnershipAtOnceWhileAlphaFadesFor160Milliseconds()
    {
        var motion = new ObjectActionRingMotion();
        var open = new ObjectActionRingView(default,default,1,1,true,true,new(new(500,500),new(500,500),60,150,8),true);
        var start = TimeSpan.FromSeconds(1);
        motion.Update(open,start,true);
        var closing = motion.Update(open with { Open=false },start + TimeSpan.FromMilliseconds(16),true);
        Assert.False(closing.Open); Assert.Equal(1,closing.Opacity);
        Assert.InRange(motion.Update(open with { Open=false },start+TimeSpan.FromMilliseconds(96),true).Opacity,.49f,.51f);
        Assert.Equal(0,motion.Update(open with { Open=false },start+TimeSpan.FromMilliseconds(176),true).Opacity);
        Assert.Equal(1,motion.Update(open,start+TimeSpan.FromMilliseconds(192),false).Opacity);
    }
}
