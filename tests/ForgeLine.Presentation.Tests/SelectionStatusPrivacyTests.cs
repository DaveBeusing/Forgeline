using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class SelectionStatusPrivacyTests
{
    [Theory]
    [InlineData(1600,900)]
    [InlineData(1028,749)]
    public void NativeRingOpacityChangesScenePixelsAndZeroOpacityLeavesNoPaint(int width,int height)
    {
        using var platform=new WindowsPlatform();
        using var window=platform.CreateWindow(new("FORGELINE ring opacity",1600,900));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false});
        device.Resize(width,height);
        device.RenderFrame(GraphicsColor.ForgeLineClear);
        device.WaitForIdle();
        using var renderer=new PlayerActionRingHudRenderer(device,null);
        var snapshot=HoverTooltipRenderingTests.StatusSnapshot();
        ActionRingSlots slots=default;
        slots[0]=new(default,default,ActionRingNavigation.Back);
        int frameWidth=0,frameHeight=0;
        Vector2 point=default;
        byte[] Capture(float opacity) => device.CaptureFrame(new GraphicsColor(.8f,.7f,.6f,1),c=>{
            frameWidth=c.Width;frameHeight=c.Height;
            var layout=GameplayHudLayout.Create(c.Width,c.Height,96);
            var center=new Vector2(c.Width/2f,c.Height/2f);
            var geometry=new RadialActionLayout(center,center,60,150,8);
            var ring=new ObjectActionRingView(new(11,1),snapshot.SessionId,1,1,true,true,geometry,true) {Slots=slots};
            var panel=default(PlayerActionPanelView) with {Ring=ring with {Opacity=opacity}};
            point=geometry.Center+RadialActionLayout.Direction(geometry.Start(0)+.1f)*(geometry.InnerRadius+10);
            c.BeginPass(GraphicsFramePass.Overlay);
            renderer.Render(c,snapshot,panel,default,default,layout);
        });
        var clear=Capture(0);var full=Capture(1);var half=Capture(.5f);
        Assert.Equal(frameWidth*frameHeight*4,clear.Length);
        Assert.Equal(clear.Length,full.Length);
        Assert.Equal(clear.Length,half.Length);
        int offset=((int)point.Y*frameWidth+(int)point.X)*4;
        Assert.InRange(offset,0,clear.Length-4);
        for(int channel=0;channel<3;channel++) {
            Assert.True(full[offset+channel]<half[offset+channel]);
            Assert.True(half[offset+channel]<clear[offset+channel]);
        }
    }

    [Fact]
    public void NativeHiddenOperationalFieldsDoNotChangeTooltipPixelsAndEmptySelectionClearsCommands()
    {
        using var platform=new WindowsPlatform();
        using var window=platform.CreateWindow(new("FORGELINE selection status privacy",1600,900));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false});
        using var tooltip=new WorldHoverTooltipSurface(device);
        var original=HoverTooltipRenderingTests.StatusSnapshot();
        var hidden=original.PlayerExperience!.Value.Selection with { HasHealth=false,HasSupply=false,
            HasReadiness=false,HasPower=false,HasInventory=false };
        byte[] Capture(PlayerSelectionSummary selection) => device.CaptureFrame(GraphicsColor.ForgeLineClear,c => {
            var snapshot=new PresentationSnapshot(original.Tick,original.TickDuration,1,original.Instances,sessionId:original.SessionId,
                playerExperience:original.PlayerExperience.Value with { Selection=selection });
            var layout=GameplayHudLayout.Create(c.Width,c.Height,96);
            tooltip.Render(new GameplayHudRenderContext(c,new RtsCamera(),snapshot,new AxisAlignedBounds(new Vector3(-1000),new Vector3(1000)),
                RtsInformationLayerView.Empty,HoverTooltipRenderingTests.StatusPanel(snapshot),default,default,
                CombatGroupOverviewView.Empty,default,layout,96,1,null,
                HoverTooltip:HoverTooltipRenderingTests.StatusView(snapshot,layout)));
        });
        var before=Capture(hidden);
        var after=Capture(hidden with {HealthFraction=.99,FuelFraction=.99,AmmunitionFraction=.99,
            Readiness=.99,InventoryQuantity=100000});
        Assert.True(before.AsSpan().SequenceEqual(after));
        Assert.False(Capture(hidden with {DisplayName=new string('W',300)}).AsSpan().SequenceEqual(
            Capture(hidden with {DisplayName=string.Empty})));
        Capture(PlayerSelectionSummary.Empty);
        Assert.Equal(0,tooltip.LastRenderedVertexCount);
        Assert.Equal(0,tooltip.SkinDiagnostics.Vertices);
    }
}
