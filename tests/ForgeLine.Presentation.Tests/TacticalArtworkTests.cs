using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class TacticalArtworkTests
{
    [Theory]
    [InlineData(1280,720,96u,.75f)] [InlineData(1920,1080,144u,1f)]
    [InlineData(2560,1440,192u,2f)] [InlineData(3440,1440,144u,1f)] [InlineData(3840,2160,192u,1f)]
    public void CameraFootprintUsesExistingFittedMapAndStaysInsideItsHitBounds(int width,int height,uint dpi,float scale)
    {
        var layout=GameplayHudLayout.Create(width,height,dpi,scale);
        var rect=RtsMinimapInteractionLayout.GetMapRect(layout);
        var bounds=new AxisAlignedBounds(new(-500,0,-1000),new(500,20,1000));
        Span<Vector2> points=stackalloc Vector2[4];
        foreach (float yaw in new[]{0f,MathF.PI/4,MathF.PI/2})
        {
            var camera=new RtsCamera(new RtsCameraSettings{InitialYawRadians=yaw});
            Assert.True(RtsMinimapCameraFootprint.TryProject(camera,rect,bounds,width,height,points));
            for(int i=0;i<4;i++) Assert.True(rect.Contains(points[i]));
            Assert.True(Vector2.DistanceSquared(points[0],points[1])>1);
            Assert.True(Vector2.DistanceSquared(points[0],points[3])>1);
        }
    }
    [Fact]
    public void NativeWorldStatusRequiresCurrentOwnedSingleEntityFactsAndNeverAnAggregateOrForeignDetails()
    {
        string root=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(root,"ForgeLine.sln")))root=Directory.GetParent(root)!.FullName;
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE tactical privacy",1280,720));
        using var device=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false});
        using var renderer=new RtsInformationOverlayRenderer(device,RuntimeAssetCatalog.Load(Path.Combine(root,"assets/runtime")));
        EntityId entity=new(1,1);
        var instance=new RenderInstance(entity,new(Vector3.Zero,Quaternion.Identity,new(12,6,40)),new(1),RenderMaterialHandle.Default,
            RenderVisibilityMask.World,Selectable:new(new PlayerId(1),ControllableEntityCategory.Unit));
        var selection=PlayerSelectionSummary.Empty with {Count=1,PrimaryEntity=entity,Kind=PlayerSelectionKind.Unit,
            HasHealth=true,HealthFraction=.2,HasSupply=true,FuelFraction=.3,AmmunitionFraction=.5};
        var experience=default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(1),Selection=selection};
        var view=RtsInformationLayerView.Empty with {MinimapEnabled=false,SelectedEntities=[entity]};
        var camera=new RtsCamera();
        Render(instance,experience);
        Assert.Equal(4,renderer.LastWorldCorners); Assert.Equal(3,renderer.LastWorldMeters);
        Render(instance,experience with {Selection=selection with {Count=2}});
        Assert.Equal(4,renderer.LastWorldCorners); Assert.Equal(0,renderer.LastWorldMeters);
        Render(instance,experience with {Selection=selection with {HasHealth=false,HasSupply=false}});
        Assert.Equal(0,renderer.LastWorldMeters);
        Render(instance,experience with {Selection=selection with {HealthFraction=double.NaN,FuelFraction=double.PositiveInfinity}});
        Assert.Equal(0,renderer.LastWorldMeters);
        Render(instance,experience with {Tick=new(0)});
        Assert.Equal(0,renderer.LastWorldCorners); Assert.Equal(0,renderer.LastWorldMeters);
        Render(instance with {Visibility=RenderVisibilityMask.None},experience);
        Assert.Equal(0,renderer.LastWorldCorners); Assert.Equal(0,renderer.LastWorldMeters);
        Render(instance with {Selectable=new(new PlayerId(2),ControllableEntityCategory.Unit)},experience);
        Assert.Equal(0,renderer.LastWorldCorners); Assert.Equal(0,renderer.LastWorldMeters);
        // Repeated thousand-unit map/group updates reuse atlas pages and bounded buffers.
        var army = Enumerable.Range(1,1000).Select(i => instance with { Entity=new EntityId(i,1),
            Transform=instance.Transform with {Position=new Vector3((i%40-20)*8,0,(i/40-12)*8)} }).ToArray();
        var armyView=view with {MinimapEnabled=true,SelectedEntities=army.Select(i=>i.Entity).ToArray()};
        var armySnapshot=new PresentationSnapshot(new SimulationTick(1),TimeSpan.FromMilliseconds(50),1000,army,
            playerExperience:experience with {Selection=selection with {Count=1000}});
        long uploads=device.Diagnostics.Resources.TextureUploadCount;
        int buffers=renderer.SkinDiagnostics.Buffers;
        for(int frame=0;frame<12;frame++)
        {
            device.RenderFrame(GraphicsColor.ForgeLineClear,c=>renderer.Render(c,camera,armySnapshot,new(new(-500),new(500)),armyView,CombatGroupOverviewView.Empty,96));
            Assert.Equal(0,renderer.SkinDiagnostics.DroppedQuads);
            Assert.Equal(0,renderer.LastWorldMeters);
            Assert.Equal(buffers,renderer.SkinDiagnostics.Buffers);
            Assert.Equal(uploads,device.Diagnostics.Resources.TextureUploadCount);
        }
        void Render(RenderInstance item,PlayerExperienceSnapshot facts)
        {
            var snapshot=new PresentationSnapshot(new SimulationTick(1),TimeSpan.FromMilliseconds(50),1,[item],playerExperience:facts);
            device.RenderFrame(GraphicsColor.ForgeLineClear,c=>renderer.Render(c,camera,snapshot,new(new(-500),new(500)),view,CombatGroupOverviewView.Empty,96));
            Assert.Equal(0,renderer.SkinDiagnostics.DroppedQuads); Assert.Equal(0,renderer.SkinDiagnostics.MissingAssets);
        }
    }
}
