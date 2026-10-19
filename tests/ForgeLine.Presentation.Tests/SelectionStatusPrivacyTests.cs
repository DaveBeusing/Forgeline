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
