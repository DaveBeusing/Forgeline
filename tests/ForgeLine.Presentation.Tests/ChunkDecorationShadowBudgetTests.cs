using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class ChunkDecorationShadowBudgetTests
{
    [Fact]
    public void DecorationConsumesRemainingSubmittedPartsAfterAnArticulatedVehicle()
    {
        string root=Path.Combine(Path.GetTempPath(),"forgeline-shadow-parts-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tank=UnitPresentationCatalog.Get(UnitIds.MainBattleTank);
            var catalog=ChunkDecorationNativeTests.CreateCatalog(root,
                ["mesh.world.vegetation.grass_clump",tank.MeshAssetId,tank.TurretMeshAssetId!]);
            var decorations=new ChunkDecorationSnapshot(ChunkDecorationFixture.Generate(10000));
            var buffer=new PresentationSnapshotBuffer();
            var unit=new RenderInstance(new EntityId(1,1),new(new(128,0,128),Quaternion.Identity,new(10)),
                new(1),RenderMaterialHandle.Default,RenderVisibilityMask.World,
                UnitFeature:new(UnitIds.MainBattleTank,UnitPresentationDamageState.Intact));
            buffer.Publish(new(new SimulationTick(1),TimeSpan.FromMilliseconds(50),1,[unit]){ChunkDecorations=decorations});
            var world=new RenderWorld();world.Update(buffer);
            using var platform=new WindowsPlatform();
            using var window=platform.CreateWindow(new("FORGELINE Shared Shadow Budget",320,240));
            using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new()
                {ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=false});
            graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Low));
            var camera=ChunkDecorationFixture.Camera(new(128,0,128),12);
            using var renderer=new SimpleInstanceRenderer(graphics,catalog);
            var frame=DirectionalShadowProjection.Create(camera.CaptureState(),SceneLightingSettings.Default,graphics.DirectionalShadows);
            renderer.Prepare(camera,world,new(320,240,1,frame,1024,2.0));
            graphics.RenderFrame(GraphicsColor.ForgeLineClear,context=>
            {
                context.BeginDirectionalShadowPass(frame);
                renderer.RenderShadow(context,camera,world,1,frame,1024,256,2.0);
                context.EndDirectionalShadowPass();
            });
            renderer.Submit();renderer.Retire();graphics.WaitForIdle();
            Assert.Equal(1,renderer.LastShadowDiagnostics.VisibleInstances);
            Assert.Equal(2,renderer.WorkMetrics.DrawnParts);
            Assert.Equal(1022,renderer.DecorationDiagnostics.Shadow.RuntimeMeshInstances);
            Assert.Equal(1024,graphics.Diagnostics.DirectionalShadows!.Value.SubmittedCasters);
            Assert.Equal(0,graphics.Diagnostics.Health.SubmissionFaultCount);
        }
        finally{Directory.Delete(root,true);}
    }
}
