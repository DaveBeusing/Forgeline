using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal static class NavigationDiagnosticsCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Diagnostic capture output must be empty.");
            Directory.CreateDirectory(output);
            var assets = RuntimeAssetCatalog.Load(runtimeRoot);
            using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
                { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) });
            var interaction = new PresentationInteractionState();
            var buffer = new PresentationSnapshotBuffer();
            var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands, runtime.BattlefieldRuntime.MatchStateEntity);
            runtime.Simulation.RegisterTickObserver(new PresentationExtractor(buffer,
                new PresentationExtractionContext(runtime, new PlayerId(1), interaction, gateway), assets));
            EntityId selected = default;
            foreach (var entity in runtime.Simulation.Entities.Query<UnitIdentity, ControllableEntity>())
                if (runtime.Simulation.Entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck &&
                    runtime.Simulation.Entities.GetComponent<ControllableEntity>(entity).Owner == new PlayerId(1)) selected = entity;
            if (!selected.IsValid) throw new InvalidDataException("No owned Cargo Truck.");
            var pose = runtime.Simulation.Entities.GetComponent<WorldTransform>(selected);
            // Authored inspection command; the diagnostic observer never creates commands.
            var order = new MovementOrder(new PlayerId(1), pose.Position + new Vector3(-35, 0, 30),
                runtime.Simulation.CurrentTick, runtime.Simulation.CurrentTick);
            if (runtime.Simulation.Entities.HasComponent<MovementOrder>(selected)) runtime.Simulation.Entities.SetComponent(selected, order);
            else runtime.Simulation.Entities.AddComponent(selected, order);
            interaction.SetSelection([selected]); interaction.SetNavigationDiagnosticsOpen(true);
            runtime.Simulation.RunTicks(10);
            if (!buffer.TryReadLatest(out var snapshot)) throw new InvalidDataException("No diagnostic publication.");
            var view = new NavigationDiagnosticsView(true, true, runtime.Simulation.SessionId, selected);
            if (!NavigationDiagnosticsViewModel.TryGetVehicle(snapshot, view, out _)) throw new InvalidDataException("Diagnostic source unavailable.");
            var world = new RenderWorld(); world.Update(buffer);
            using var platform = new WindowsPlatform();
            using var window = platform.CreateWindow(new("FORGELINE Navigation Diagnostics Qualification", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new()
                { EnableVSync = false, EnableMemoryDiagnostics = true, ForceSoftwareAdapter = true });
            using var hud = new GameplayHudRenderer(graphics, assets);
            using var ground = new TerrainRenderer(graphics, runtime.Terrain, runtimeAssets: assets,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(assets)) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, assets);
            using var debugRenderer = new DebugDrawRenderer(graphics, depthEnabled: false, maximumLines: 1024);
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(pose.Position, RtsReferenceZoom.NormalGameplay));
            var results = new List<object>();
            foreach (var display in new[] { (1280, 720, 1f, 0), (1280, 720, 2f, 0), (1280, 720, 2f, 8), (1920, 1080, 1f, 0) })
            {
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                view = view with { Scroll = display.Item4 };
                var draw = new DebugDraw();
                NavigationDiagnosticsViewModel.AppendLayers(draw, snapshot, view);
                if (draw.Lines.Length > 1024) throw new InvalidDataException("Diagnostic line bound exceeded.");
                void Record(IGraphicsCommandContext context)
                {
                    ground.Render(context, camera); meshes.Render(context, camera, world, 1);
                    debugRenderer.Render(context, camera, draw);
                    context.BeginPass(GraphicsFramePass.Overlay);
                    hud.Render(context, camera, snapshot, runtime.Terrain.WorldBounds, RtsInformationLayerView.Empty,
                        default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, default,
                        96, display.Item3, navigationDiagnostics: view);
                }
                for (int i = 0; i < 12; i++)
                {
                    ground.Prepare(camera, display.Item1, display.Item2);
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                    platform.PumpEvents();
                }
                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                var text = hud.TextDiagnostics;
                if (!text.AtlasReady || text.MissingGlyphs != 0 || text.DroppedGlyphs != 0)
                    throw new InvalidDataException("Diagnostic panel text is incomplete.");
                string name = $"navigation-{display.Item1}x{display.Item2}-scale{display.Item3}-scroll{display.Item4}.png";
                ReferenceCaptureImage.WritePng(Path.Combine(output, name), display.Item1, display.Item2, pixels);
                results.Add(new { Image = name, Text = text, Lines = draw.Lines.Length, HudVertices = hud.LastRenderedVertexCount });
            }
            if (!NavigationDiagnosticReportEncoder.TryEncode(snapshot, runtime.Simulation.SessionId, snapshot.Tick,
                new PlayerId(1), selected, runtime.Battlefield.Metadata.Key, runtime.RuntimeSettings.Seed, out var report))
                throw new InvalidDataException("Current observation export rejected.");
            var exported = NavigationDiagnosticReportExporter.WriteReport(output, report);
            if (!exported.Exported) throw new IOException($"Observation export failed: {exported.Failure}");
            graphics.WaitForIdle();
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native submission failure.");
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new {
                Contract = "Actual copied own-vehicle source over native WARP; authored command fixture. Structural and text evidence, not natural-gameplay, performance, full manual acceptance or debug-layer certification.",
                Device = graphics.Diagnostics.Device, Frames = results, Export = Path.GetFileName(exported.Path),
                Session = snapshot.SessionId.Value, Tick = snapshot.Tick.Value
            }, JsonOptions));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
