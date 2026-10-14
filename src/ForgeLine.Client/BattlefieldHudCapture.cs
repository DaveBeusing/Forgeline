using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Explicit native inspection of authored copied HUD facts; never part of player startup.</summary>
internal static class BattlefieldHudCapture
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot, bool timingOnly = false)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Capture output must be empty.");
            Directory.CreateDirectory(output);
            var catalog = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixture = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain, runtimeAssets: catalog);
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE HUD Qualification", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false, EnableMemoryDiagnostics = true });
            using var hud = new GameplayHudRenderer(graphics, catalog);
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: catalog,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(catalog)) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, catalog);
            var results = new List<object>();
            foreach (var zoom in new[] { RtsReferenceZoom.NormalGameplay, RtsReferenceZoom.Strategic })
            {
                if (timingOnly && zoom != RtsReferenceZoom.NormalGameplay) continue;
                var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(fixture.Target, zoom));
                foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
                {
                    if (timingOnly && display != (1920, 1080)) continue;
                    // The complete display/DPI/scale matrix uses normal zoom; strategic adds the same
                    // three state inspections at the reference display without duplicating layout work.
                    if (zoom == RtsReferenceZoom.Strategic && display != (1920, 1080)) continue;
                    graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                    ground.Prepare(camera, display.Item1, display.Item2);
                    foreach (uint dpi in new uint[] { 96, 144, 192 })
                    foreach (float scale in new[] { .75f, 1f, 2f })
                    {
                        if (timingOnly && (dpi != 96 || scale != 1)) continue;
                        if (zoom == RtsReferenceZoom.Strategic && (dpi != 96 || scale != 1)) continue;
                        foreach (string state in new[] { "Mixed", "Supply", "Production" })
                        {
                            var snapshot = State(fixture.Snapshot, state);
                            var buffer = new PresentationSnapshotBuffer(); buffer.Publish(snapshot);
                            var world = new RenderWorld(); world.Update(buffer);
                            var operations = state == "Production" ? new OperationsView(true, Session: snapshot.SessionId,
                                Selected: snapshot.PlayerExperience!.Value.Selection.PrimaryEntity) : default;
                            void RecordHud(IGraphicsCommandContext c)
                            {
                                c.BeginPass(GraphicsFramePass.Overlay);
                                hud.Render(c, camera, snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                    default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, default,
                                    dpi, scale, operations: operations);
                            }
                            void Record(IGraphicsCommandContext c)
                            {
                                ground.Render(c, camera); meshes.Render(c, camera, world, 1);
                                RecordHud(c);
                            }
                            for (int i = 0; i < 4; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                            byte[] repeat = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                            if (!pixels.AsSpan().SequenceEqual(repeat)) throw new InvalidDataException("HUD world capture changed between repeats.");
                            var text = hud.TextDiagnostics;
                            if (!text.AtlasReady || text.MissingGlyphs != 0 || text.DroppedGlyphs != 0)
                                throw new InvalidDataException("Incomplete native HUD text.");
                            string key = FormattableString.Invariant($"{state}-{zoom}-{display.Item1}x{display.Item2}-{dpi}dpi-{scale:0.00}");
                            string? image = null;
                            if ((dpi == 96 && scale == 1) || (display == (1280, 720) && dpi == 192 && scale == 2))
                            {
                                image = key + ".png";
                                ReferenceCaptureImage.WritePng(Path.Combine(output, image), display.Item1, display.Item2, pixels);
                            }
                            object? timing = null;
                            if (display == (1920, 1080) && dpi == 96 && scale == 1 && zoom == RtsReferenceZoom.NormalGameplay)
                                timing = Measure(graphics, RecordHud);
                            results.Add(new { State = state, Zoom = zoom.ToString(), Width = display.Item1, Height = display.Item2,
                                Dpi = dpi, UiScale = scale, Image = image, Repeatable = true,
                                PixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)), HudVertices = hud.LastRenderedVertexCount,
                                Text = text, HudOnlyTiming = timing });
                            Console.WriteLine("[hud:capture] " + key);
                            platform.PumpEvents();
                        }
                    }
                }
            }
            graphics.WaitForIdle();
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native submission failed.");
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
            {
                Version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                PresentationVersion = typeof(GameplayHudRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                PresentationSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GameplayHudRenderer).Assembly.Location))),
                Fixture = fixture.Fingerprint, Device = graphics.Diagnostics.Device, WindowDpi = window.Dpi,
                Contract = "Authored owned snapshot states over the existing native battlefield fixture; not a natural match or approved golden. Layout DPI is not physical monitor certification.",
                TimingContract = "HUD-only complete clear/overlay/text GPU frame; no world. CPU submit includes waits/Present. Fixed paused snapshots; same device required for comparison.",
                Cases = results
            }, s_json));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[hud:failure] " + error); return 1; }
    }

    private static object Measure(IGraphicsDevice graphics, Action<IGraphicsCommandContext> record)
    {
        for (int i = 0; i < 64; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
        graphics.WaitForIdle();
        var cpu = new List<double>(128); var gpu = new List<double>(128);
        ulong fence = graphics.Diagnostics.Frame?.GpuSubmissionFence ?? 0;
        for (int i = 0; i < 128; i++)
        {
            long started = Stopwatch.GetTimestamp(); graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
            cpu.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds); AddGpu();
        }
        graphics.WaitForIdle(); AddGpu(); cpu.Sort(); gpu.Sort();
        void AddGpu()
        {
            var d = graphics.Diagnostics;
            if (d.Frame is { } frame && frame.GpuSubmissionFence > fence && d.GpuFrameMilliseconds is { } ms)
            { fence = frame.GpuSubmissionFence; gpu.Add(ms); }
        }
        return new { Warmups = 64, Samples = 128, SubmitP50Microseconds = cpu[cpu.Count / 2],
            GpuSamples = gpu.Count, GpuP50Milliseconds = gpu.Count > 0 ? (double?)gpu[gpu.Count / 2] : null };
    }

    internal static PresentationSnapshot State(PresentationSnapshot source, string state)
    {
        // These are explicit inspection inputs from permitted read-model fields, not newly inferred
        // gameplay facts. World intelligence and instances remain the canonical fixture's copies.
        var experience = source.PlayerExperience!.Value;
        var selection = experience.Selection;
        var owned = source.Instances.ToArray().First(x => x.Selectable.Owner == experience.Player).Entity;
        if (state != "Mixed")
            selection = PlayerSelectionSummary.Empty with
            {
                Count = 1, PrimaryEntity = owned, Kind = state == "Supply" ? PlayerSelectionKind.Unit : PlayerSelectionKind.Building,
                DisplayName = state == "Supply" ? "Main Battle Tank" : "Vehicle Factory",
                CommonUnitId = state == "Supply" ? UnitIds.MainBattleTank : default,
                CommonBuildingId = state == "Production" ? BuildingIds.VehicleFactory : default,
                HasHealth = true, HealthFraction = .23,
                HasSupply = state == "Supply", SupplyStatus = BattlefieldSupplyStatus.Critical,
                FuelFraction = .12, AmmunitionFraction = .08, HasReadiness = state == "Supply", Readiness = .31,
                HasPower = state == "Production", PowerState = PowerOperationalState.Brownout,
                Work = state == "Production" ? new(PlayerWorkKind.UnitProduction, PlayerWorkState.Blocked, .42, "MBT", "STEEL SHORTAGE") : PlayerWorkSummary.None
            };
        experience = experience with
        {
            Selection = selection, Resources = new(17800, 900, 450, 12, 40, 85, 120),
            Alerts = PlayerAlertState.SupplyCritical | PlayerAlertState.ProductionBlocked | PlayerAlertState.LowPower,
            CriticalSupplyUnits = 4, BlockedProductionFacilities = 2,
            Power = new(default, 300, 500, 300, 2, 0, 0)
        };
        var alerts = new AlertLifecycleTracker().Capture(source.SessionId, experience, [default, default, owned, owned, owned]);
        var operations = new OperationsSnapshot(source.SessionId, source.Tick, experience.Player,
            [new(owned, "Vehicle Factory", OperationsCategory.Production, "BLOCKED", "STEEL SHORTAGE",
                "Required steel is below the captured queue requirement.", 2, 12, 200, 500, 300, null, null, PlayerActionPanelMode.UnitProduction)],
            [new(ResourceIds.Steel, "Steel", 12, null)], [], 1, 0);
        return new(source.Tick, source.TickDuration, source.SimulationEntityCount, source.Instances,
            source.Intelligence, source.SessionId, experience, playerActions: source.PlayerActions,
            vfxMetrics: source.VfxMetrics, alerts: alerts, operations: operations);
    }
}
