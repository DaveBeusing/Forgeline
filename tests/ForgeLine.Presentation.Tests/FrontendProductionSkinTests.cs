using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class FrontendProductionSkinTests
{
    [Fact]
    public void AllNormalKindsUsePreparedSkinWithoutGrowingTexturesAcrossFramesOrSessions()
    {
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE frontend skin",1280,720));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=true});
        var catalog=RuntimeAssetCatalog.Load(RuntimeRoot());
        for(int session=0;session<3;session++)
        {
            using(var renderer=new FrontendOverlayRenderer(device))
            {
                renderer.PrepareSkin(catalog); Assert.True(renderer.SkinReady);
                Assert.Equal(2,device.Diagnostics.Resources.LoadedTextureCount);
                long uploads=device.Diagnostics.Resources.TextureUploadCount;
                foreach(var kind in Enum.GetValues<FrontendSurfaceKind>())
                {
                    if(kind==FrontendSurfaceKind.StudioSplash)continue;
                    var view=View(kind);
                    for(int repeat=0;repeat<4;repeat++)device.RenderFrame(GraphicsColor.ForgeLineClear,c=>renderer.Render(c,view));
                    Assert.True(renderer.SkinDiagnostics.Vertices>0);Assert.Equal(0,renderer.SkinDiagnostics.MissingAssets);
                    Assert.Equal(0,renderer.SkinDiagnostics.DroppedQuads);Assert.Equal(0,renderer.TextDiagnostics.DroppedGlyphs);
                    Assert.Equal(uploads,device.Diagnostics.Resources.TextureUploadCount);
                    renderer.PrepareSkin(catalog);Assert.Equal(uploads,device.Diagnostics.Resources.TextureUploadCount);
                }
            }
            device.WaitForIdle();Assert.Equal(0,device.Diagnostics.Resources.LoadedTextureCount);
            Assert.Equal(0,device.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        }
        Assert.Equal(0,device.Diagnostics.Health.SubmissionFaultCount);
    }
    [Fact]
    public void PauseBackingPreventsBrightUnderlyingContentFromLeakingBetweenControls()
    {
        using var platform=new WindowsPlatform();using IWindow window=platform.CreateWindow(new("FORGELINE pause skin",1280,720));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false});
        using var renderer=new FrontendOverlayRenderer(device);renderer.PrepareSkin(RuntimeAssetCatalog.Load(RuntimeRoot()));
        var view=View(FrontendSurfaceKind.PauseMenu);
        int capturedWidth=0;
        byte[] Capture(float clear)=>device.CaptureFrame(new(clear,clear,clear,1),c=>{capturedWidth=c.Width;renderer.Render(c,view);});
        var dark=Capture(0);var bright=Capture(1);var layout=renderer.LastLayout;
        int x=(int)(layout.PauseBackdrop.X+layout.PauseBackdrop.Width/2);
        int y=(int)(layout.MenuRow(0).Bottom+2);
        int offset=(y*capturedWidth+x)*4;
        Assert.Equal(dark.AsSpan(offset,4).ToArray(),bright.AsSpan(offset,4).ToArray());
        int outside=(y*capturedWidth+capturedWidth-4)*4;
        Assert.NotEqual(dark.AsSpan(outside,4).ToArray(),bright.AsSpan(outside,4).ToArray());
    }
    [Fact]
    public void MenuRevealRemainsVisibleAtSupportedScales()
    {
        using var platform=new WindowsPlatform();using IWindow window=platform.CreateWindow(new("FORGELINE reveal skin",1280,720));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false});
        using var renderer=new FrontendOverlayRenderer(device);renderer.PrepareSkin(RuntimeAssetCatalog.Load(RuntimeRoot()));
        foreach(float scale in new[]{.75f,1f,2f})
        {
            var view=View(FrontendSurfaceKind.MainMenu);
            byte[] Capture(bool revealing)=>device.CaptureFrame(GraphicsColor.ForgeLineClear,c=>renderer.Render(c,
                view with { Layout=FrontendGeometry.Resolve(c.Width,c.Height,scale,96),
                    Motion=revealing ? new UiMotionView(.5f,.5f,0) : UiMotionView.Settled }));
            var settled=Capture(false);var animated=Capture(true);
            Assert.False(settled.AsSpan().SequenceEqual(animated));
        }
    }
    [Fact]
    public void MissingRequiredSkinHasExplicitFailedStateAndUsableEmergencyRendering()
    {
        string root=Path.Combine(Path.GetTempPath(),"forgeline-missing-frontend-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var manifest=RuntimeAssetIntegrity.SealManifest(new(){CompilerVersion="test"});
            File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest,RuntimeAssetCatalog.CreateJsonOptions()));
            using var platform=new WindowsPlatform();using IWindow window=platform.CreateWindow(new("FORGELINE emergency skin",1280,720));
            using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false});
            using var renderer=new FrontendOverlayRenderer(device);renderer.PrepareSkin(RuntimeAssetCatalog.Load(root));
            Assert.False(renderer.SkinReady);Assert.Equal("Failed",renderer.SkinState);Assert.Contains("Reinstall",renderer.SkinFailure,StringComparison.Ordinal);
            device.RenderFrame(GraphicsColor.ForgeLineClear,c=>renderer.Render(c,View(FrontendSurfaceKind.MainMenu)));
            Assert.True(renderer.LastRenderedVertexCount>0);Assert.Equal(0,renderer.SkinDiagnostics.Vertices);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static FrontendSurfaceView View(FrontendSurfaceKind kind)=>kind switch
    {
        FrontendSurfaceKind.MainMenu=>FrontendSurfaceView.MainMenu([new("start","New Game",true,true)]),
        FrontendSurfaceKind.PauseMenu=>FrontendSurfaceView.PauseMenu([new("resume","Resume",true,true),new("settings","Settings",true,false)]),
        FrontendSurfaceKind.Loading=>FrontendSurfaceView.Loading("Preparing actual session",true,.6f),
        _=>FrontendSurfaceView.Detail("Settings / ÄÖÜ",[new("Sound","ON",IsFocused:true,CanDecrease:true,CanIncrease:true,ValueKind:FrontendValueKind.Toggle,IsOn:true)],"ESC BACK","APPLY") with{Kind=kind}
    };
    private static string RuntimeRoot()
    {for(var p=new DirectoryInfo(AppContext.BaseDirectory);p is not null;p=p.Parent)if(File.Exists(Path.Combine(p.FullName,"assets/runtime/manifest.json")))return Path.Combine(p.FullName,"assets/runtime");throw new DirectoryNotFoundException();}
}
