using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

/// <summary>Explicit offline art export; never called by interactive startup.</summary>
internal static class MenuSceneAuthoring
{
    internal static int Run(string output, string runtimeRoot)
    {
        try
        {
            var assets = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var industrial = VisualReferenceScene.Create(VisualReferenceSceneKind.IndustrialBase, terrain, runtimeAssets: assets);
            var armor = VisualReferenceScene.Create(VisualReferenceSceneKind.MainBattleTankProduction, terrain, runtimeAssets: assets);
            var instances = industrial.Snapshot.Instances.ToArray().ToList();
            uint id = 100000;
            foreach (var tank in armor.Snapshot.Instances.ToArray().Where(t => t.UnitFeature.IsSpecified).Take(3))
                    instances.Add(tank with { Entity = new EntityId(id++, 1),
                        Transform = tank.Transform with { Position = new Vector3(industrial.Target.X + ((int)id - 100001 - 1) * 8, tank.Transform.Position.Y, industrial.Target.Z - 54 + ((int)id % 2) * 5) } });
            var snapshot = new PresentationSnapshot(new SimulationTick(32), TimeSpan.FromMilliseconds(50), instances.Count, instances.ToArray());
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Menu Art Export", 2048, 1152));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false });
            var lighting = SceneLightingSettings.Default with { DirectionToLight = Vector3.Normalize(new Vector3(-.7f, .4f, -.3f)),
                DirectionalColor = new(1.0f, .72f, .42f), AmbientColor = new(.35f, .48f, .62f), AmbientIntensity = .58f, DirectionalIntensity = 2.3f };
            graphics.ConfigureSceneOutput(new(true, 1.15f, true));
            graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.Medium));
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = AtmosphereQuality.Distance,
                Color = new(.07f, .10f, .14f), Density = .0012f, StartDistance = 85, MaximumOpacity = .45f });
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: assets,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(assets), lighting: lighting) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, assets, lighting, lodDiameterScale: 1.25f);
            var camera = new RtsCamera(new() { InitialTarget = industrial.Target + new Vector3(0, 2, -35), InitialDistance = 48,
                InitialPitchRadians = -.12f, MaximumPitchRadians = -.1f, InitialYawRadians = -.22f, VerticalFieldOfViewRadians = .72f });
            var buffer = new PresentationSnapshotBuffer(); buffer.Publish(snapshot); var world = new RenderWorld(); world.Update(buffer);
            graphics.Resize(2048, 1152); ground.Prepare(camera, 2048, 1152);
            void Draw(IGraphicsCommandContext c)
            {
                var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), lighting, graphics.DirectionalShadows);
                c.BeginDirectionalShadowPass(shadow); ground.RenderShadow(c, shadow, 128); meshes.RenderShadow(c, camera, world, 1, shadow, 1024, 128); c.EndDirectionalShadowPass();
                c.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera, c.Width, c.Height));
                ground.Render(c, camera); meshes.Render(c, camera, world, 1); c.BeginPass(GraphicsFramePass.Overlay);
            }
            for (int i = 0; i < 6; i++) graphics.RenderFrame(new GraphicsColor(.055f, .075f, .10f, 1), Draw);
            var pixels = graphics.CaptureFrame(new GraphicsColor(.055f, .075f, .10f, 1), Draw);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            ReferenceCaptureImage.WritePng(output, 2048, 1152, pixels);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"[menu-art:export] {error}"); return 1; }
    }
}
