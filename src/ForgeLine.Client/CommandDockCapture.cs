using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

/// <summary>Explicit copied-state inspection, independent of normal startup and command execution.</summary>
internal static class CommandDockCapture
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot, bool referenceOnly = false)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Capture output must be empty.");
            Directory.CreateDirectory(output);
            var assets = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain, runtimeAssets: assets);
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Command Dock Qualification", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false, EnableMemoryDiagnostics = true });
            using var hud = new GameplayHudRenderer(graphics, assets);
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: assets,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(assets)) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, assets);
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target, RtsReferenceZoom.NormalGameplay));
            var results = new List<object>();
            foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
            {
                if (referenceOnly && display != (1280, 720) && display != (1920, 1080)) continue;
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                foreach (uint dpi in new uint[] { 96, 144, 192 })
                foreach (float scale in new[] { .75f, 1f, 2f })
                {
                    bool reference = display == (1920, 1080) && dpi == 96 && scale == 1;
                    bool compact = display == (1280, 720) && dpi == 192 && scale == 2;
                    if (referenceOnly && !reference && !compact) continue;
                    foreach (var mode in Enum.GetValues<PlayerActionPanelMode>())
                    {
                        if (mode == PlayerActionPanelMode.Closed) continue;
                        foreach (string state in reference ? new[] { "Ready", "Blocked", "Pending", "Queue", "Empty", "Accepted", "Rejected" } : new[] { "Ready" })
                        {
                            if (state == "Queue" && mode is not (PlayerActionPanelMode.Production or PlayerActionPanelMode.UnitProduction or PlayerActionPanelMode.Technology)) continue;
                            var (snapshot, panel) = Fixture(scene.Snapshot, mode, state, display.Item1, display.Item2, dpi, scale);
                            var published = new PresentationSnapshotBuffer(); published.Publish(snapshot);
                            var world = new RenderWorld(); world.Update(published);
                            if (reference) ground.Prepare(camera, display.Item1, display.Item2);
                            void Overlay(IGraphicsCommandContext c)
                            {
                                c.BeginPass(GraphicsFramePass.Overlay);
                                hud.Render(c, camera, snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                    panel, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, dpi, scale);
                            }
                            void Record(IGraphicsCommandContext c)
                            {
                                if (reference) { ground.Render(c, camera); meshes.Render(c, camera, world, 1); }
                                Overlay(c);
                            }
                            for (int i = 0; i < 4; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                            byte[] repeated = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                            if (!pixels.AsSpan().SequenceEqual(repeated)) throw new InvalidDataException("Dock pixels changed between fixed repeats.");
                            var text = hud.TextDiagnostics;
                            if (!text.AtlasReady || text.MissingGlyphs != 0 || text.DroppedGlyphs != 0)
                                throw new InvalidDataException("Incomplete command text.");
                            string key = FormattableString.Invariant($"{mode}-{state}-{display.Item1}x{display.Item2}-{dpi}dpi-{scale:0.00}");
                            string? image = null;
                            if (reference || compact)
                            { image = key + ".png"; ReferenceCaptureImage.WritePng(Path.Combine(output, image), display.Item1, display.Item2, pixels); }
                            object? timing = reference && state == "Ready" ? Measure(graphics, Overlay) : null;
                            results.Add(new { Mode = mode.ToString(), State = state, Width = display.Item1, Height = display.Item2,
                                Dpi = dpi, UiScale = scale, World = reference, Image = image, Repeatable = true,
                                PixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)), Vertices = hud.LastRenderedVertexCount,
                                Text = text, Timing = timing });
                            Console.WriteLine("[dock:capture] " + key); platform.PumpEvents();
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
                Device = graphics.Diagnostics.Device, WindowDpi = window.Dpi, Fixture = scene.Fingerprint,
                Contract = "Authored owned copied-state inputs, never dispatched; original world intelligence retained. World appears only in reference 1080p cases. Layout DPI is not physical monitor certification. No accepted pixel golden.",
                TimingContract = "Complete clear/HUD/text GPU frame without world; CPU submit includes waits/Present. Distinct completed fences after warmup. Sequential same-device comparison only.",
                Cases = results
            }, s_json));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[dock:failure] " + error); return 1; }
    }

    private static object Measure(IGraphicsDevice graphics, Action<IGraphicsCommandContext> record)
    {
        for (int i = 0; i < 64; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
        graphics.WaitForIdle(); ulong fence = graphics.Diagnostics.Frame?.GpuSubmissionFence ?? 0;
        var cpu = new List<double>(128); var gpu = new List<double>(128);
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
        return new { Warmups = 64, Samples = 128, SubmitP50Microseconds = cpu[64], GpuSamples = gpu.Count,
            GpuP50Milliseconds = gpu.Count > 0 ? (double?)gpu[gpu.Count / 2] : null };
    }

    internal static (PresentationSnapshot Snapshot, PlayerActionPanelView Panel) Fixture(PresentationSnapshot source,
        PlayerActionPanelMode mode, string state, int width, int height, uint dpi, float scale, EntityId entityOverride = default)
    {
        var entity = entityOverride.IsValid ? entityOverride : new EntityId(10, 1); var request = new EntityId(20, 1);
        bool blocked = state == "Blocked"; bool pending = state == "Pending"; bool empty = state == "Empty";
        PlayerActionResourceAmount[] cost = [new(ResourceIds.Steel, "Steel", 80, blocked ? 12 : 200), new(ResourceIds.Fuel, "Fuel", 20, 90)];
        var recipes = new[] { new PlayerProductionRecipeActionReadModel(RecipeIds.Steel, "Steel / Directorate industrial processing recipe", cost,
            [new(ResourceIds.Steel, "Steel", 25, 0)]) };
        var production = new PlayerProductionFacilityActionReadModel(entity, request, RecipeIds.Steel, blocked ? ProductionStatus.NoInput : ProductionStatus.Running,
            blocked ? ProductionBlockReason.NoInput : ProductionBlockReason.None, .42, recipes,
            [new(request, RecipeIds.Steel, "Steel", ProductionPriority.Normal, ProductionRequestMode.Repeat, false, true),
             new(new(21,1), RecipeIds.Steel, "Steel / waiting queue", ProductionPriority.Normal, ProductionRequestMode.OneShot, true, false)]);
        var units = new PlayerUnitProductionFacilityActionReadModel(entity, request, UnitIds.MainBattleTank, blocked ? UnitProductionStatus.NoInput : UnitProductionStatus.Running,
            blocked ? UnitProductionBlockReason.NoInput : UnitProductionBlockReason.None, .63,
            [new(UnitIds.MainBattleTank, "Main Battle Tank / armored production", cost, 80),
             new(UnitIds.CombatEngineer, "Combat Engineer", cost, 40, false, TechnologyCapabilityIds.FieldEngineering)],
            [new(request, UnitIds.MainBattleTank, "Main Battle Tank", ProductionPriority.Normal, false, true)], new Vector3(500,0,500));
        var resources = InitialResourceDefinitions.CreateCatalog();
        var scoutDefinition = DirectorateContent.CreateUnitCatalog()[UnitIds.ScoutVehicle];
        var scoutInputs = scoutDefinition.Costs.Select(c => new PlayerActionResourceAmount(c.ResourceId,
            resources[c.ResourceId].DisplayName, c.Quantity, 0)).ToArray();
        var logistics = new PlayerLogisticsActionReadModel(entity,
            scoutInputs.Select((c,i) => new PlayerStockPolicyActionReadModel(new((uint)(30+i),1), c.ResourceId, c.DisplayName,
                i == 0 ? 12 : 0, 5, 10, 20, LogisticsStockPriority.Normal, true,
                blocked ? PlayerDistributionActionState.Waiting : PlayerDistributionActionState.Traveling,
                blocked ? LogisticsTransportRequestFailureReason.NoRoute : LogisticsTransportRequestFailureReason.None,
                LogisticsBottleneckReason.None, new(31,1)) { ReservedQuantity = i == 0 ? 4 : 0, RequestedQuantity = 30, AnnouncedQuantity = blocked ? 0 : 20 }).ToArray(),
            null, 1000, 12, requirements: [new(2,scoutDefinition.Id.Value,scoutDefinition.DisplayName,scoutInputs)]);
        var supply = new PlayerSupplyActionReadModel(entity, BattlefieldSupplyStatus.Critical, .15, .08, true, .4, .5,
            new(31,1), blocked ? PlayerSupplyProviderState.Blocked : PlayerSupplyProviderState.Traveling, 50, 40, default);
        var tactical = new PlayerTacticalActionReadModel([entity], 2, 1, 1, 1, 0, false, true, default, default,
            [], [new(entity, blocked ? 0 : 15, 20, 10, 500, FireMissionStatus.Complete, 0, 3)]);
        var technology = new PlayerTechnologyActionReadModel(TechnologyIds.MechanizedSystems, "directorate.technology.mechanized_systems",
            "Mechanized Systems", TechnologyDomain.Warfare, TechnologyPhase.MechanizedWarfare, 180, cost,
            [new(TechnologyIds.IndustrialStandardization, "Industrial Standardization", !blocked)],
            BuildingIds.VehicleFactory, "Vehicle Factory", entity, new(40,1), 1, blocked ? .4 : 1,
            blocked ? PlayerTechnologyState.Locked : state == "Queue" ? PlayerTechnologyState.Researching : PlayerTechnologyState.Available,
            blocked ? TechnologyResearchBlockReason.UnmetPrerequisite : TechnologyResearchBlockReason.None, .35,
            state == "Queue" ? request : default, [TechnologyCapabilityIds.MechanizedSystems]);
        var actions = new PlayerActionSnapshot(source.SessionId, source.Tick,
            empty ? [] : [new(BuildingIds.PowerPlant, "Power Plant", cost, false),
                new(BuildingIds.VehicleFactory, "Vehicle Factory with a deliberately long qualification label", cost, false),
                new(BuildingIds.Extractor, "Extractor", cost, true)], pending ? 1 : 0,
            !empty && mode == PlayerActionPanelMode.Production ? production : null,
            !empty && mode == PlayerActionPanelMode.UnitProduction ? units : null,
            !empty && mode == PlayerActionPanelMode.Logistics ? logistics : null,
            !empty && mode == PlayerActionPanelMode.Supply ? supply : null,
            !empty && mode == PlayerActionPanelMode.Tactical ? tactical : null,
            !empty && mode == PlayerActionPanelMode.Technology ? [technology] : []);
        var experience = source.PlayerExperience!.Value with
        {
            Selection = empty ? PlayerSelectionSummary.Empty : PlayerSelectionSummary.Empty with
            { Count = mode == PlayerActionPanelMode.Tactical ? 2 : 1, PrimaryEntity = entity,
                DisplayName = mode == PlayerActionPanelMode.Production ? "Smelter" : mode == PlayerActionPanelMode.Tactical ? "Mixed tactical selection" : "Vehicle Factory",
                Kind = mode == PlayerActionPanelMode.Tactical ? PlayerSelectionKind.Mixed : PlayerSelectionKind.Building },
            Feedback = state is "Accepted" or "Rejected" ? PlayerCommandFeedback.None with
            { Kind = PlayerCommandFeedbackKind.UnitProduction, State = state == "Accepted" ? PlayerCommandFeedbackState.Accepted : PlayerCommandFeedbackState.Rejected,
                AcceptedTargets = state == "Accepted" ? 1 : 0, RejectedTargets = state == "Rejected" ? 1 : 0, ResolvedAtTick = source.Tick } : PlayerCommandFeedback.None
        };
        var snapshot = new PresentationSnapshot(source.Tick, source.TickDuration, source.SimulationEntityCount, source.Instances,
            source.Intelligence, source.SessionId, experience, playerActions: actions, vfxMetrics: source.VfxMetrics);
        var panel = new PlayerActionPanelController().CreateView(width, height, actions, dpi, scale) with
        {
            Mode = mode, SelectedIndex = state == "Queue" ? mode == PlayerActionPanelMode.UnitProduction ? 2 : mode == PlayerActionPanelMode.Technology ? 0 : 1 : 0, Priority = ProductionPriority.Normal,
            ProductionMode = ProductionRequestMode.Repeat, StockMinimum = 20, StockTarget = 100, StockMaximum = 180,
            AutomaticResupplyEnabled = true, AutomaticFuelThreshold = .4, AutomaticAmmunitionThreshold = .5,
            ContextualPending = pending, ContextualActivationTick = pending ? source.Tick : null, ContextualSessionId = source.SessionId
        };
        return (snapshot, panel);
    }
}
