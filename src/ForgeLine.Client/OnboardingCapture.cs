using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Native injected-time round trips through the production input and presentation owner.</summary>
internal static class OnboardingCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot, bool lessons = false, bool practice = false)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Onboarding capture output must be empty.");
            Directory.CreateDirectory(output);
            var assets = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain, runtimeAssets: assets);
            var snapshot = UiReferenceStates.Hud(scene.Snapshot, UiReferenceScenario.Onboarding, 1920, 1080, 96, 1).Snapshot;
            var guidance = new EarlyGameGuidanceController().Update(snapshot, true);
            using var platform = new WindowsPlatform();
            using var window = platform.CreateWindow(new("FORGELINE Onboarding Qualification", 1920, 1080));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false });
            using var hud = new GameplayHudRenderer(graphics, assets);
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: assets,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(assets)) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, assets);
            var publication = new PresentationSnapshotBuffer(); publication.Publish(snapshot);
            var world = new RenderWorld(); world.Update(publication);
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target, RtsReferenceZoom.NormalGameplay));
            var records = new List<object>();
            if(practice)
            {
                foreach(var display in new[]{(1280,720,192u,2f),(1920,1080,96u,1f)})
                {
                    graphics.WaitForIdle();graphics.Resize(display.Item1,display.Item2);
                    var layout=GameplayHudLayout.Create(display.Item1,display.Item2,display.Item3,display.Item4);
                    foreach(var lesson in PracticeLessons.All)
                    foreach(var status in new[]{PracticeStatus.Active,PracticeStatus.Complete,PracticeStatus.Failed})
                    {
                        var tick=new ForgeLine.Simulation.SimulationTick(400);
                        var experience=snapshot.PlayerExperience!.Value with {Tick=tick};
                        var times=Array.AsReadOnly(lesson.Objectives.Select(_=>status==PracticeStatus.Complete?(ulong?)100:null).ToArray());
                        var facts=new PracticeSnapshot(lesson.Key,1,experience.Player,snapshot.SessionId,tick,status,times,
                            status==PracticeStatus.Failed?"An owned practice unit was destroyed. Retry from Pause.":"");
                        var view=new PresentationSnapshot(tick,snapshot.TickDuration,snapshot.SimulationEntityCount,snapshot.Instances,
                            sessionId:snapshot.SessionId,playerExperience:experience,
                            guidance:new PlayerGuidanceSummary(snapshot.SessionId,tick,experience.Player,default){Practice=facts});
                        var guide=new EarlyGameGuidanceController();var card=guide.Update(view,true,layout:layout);
                        int pages=card.PageCount;
                        for(int page=0;page<pages;page++)
                        {
                            card=guide.Update(view,true,layout:layout);
                            var state=OnboardingPresentation.Resolve(layout,0,detailed:true);
                            var ux=default(PreAlphaUxView) with {ShowOnboarding=true,Guidance=card,Onboarding=state};
                            ground.Prepare(camera,display.Item1,display.Item2);
                            void Draw(IGraphicsCommandContext c)
                            {
                                ground.Render(c,camera);meshes.Render(c,camera,world,1);c.BeginPass(GraphicsFramePass.Overlay);
                                hud.Render(c,camera,view,terrain.WorldBounds,RtsInformationLayerView.Empty,default,default,
                                    FormationTemplate.Compact,CombatGroupOverviewView.Empty,ux,display.Item3,display.Item4);
                            }
                            graphics.RenderFrame(GraphicsColor.ForgeLineClear,Draw);
                            var pixels=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Draw);
                            var repeated=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Draw);
                            if(!pixels.AsSpan().SequenceEqual(repeated)||hud.TextDiagnostics.MissingGlyphs!=0||hud.TextDiagnostics.DroppedGlyphs!=0||
                                hud.SkinDiagnostics.MissingAssets!=0||hud.SkinDiagnostics.DroppedQuads!=0)throw new InvalidDataException("Practice guide capture lost content or changed on repeat.");
                            string image=$"{display.Item1}x{display.Item2}-{lesson.Key}-{status}-page{page+1}.png";
                            ReferenceCaptureImage.WritePng(Path.Combine(output,image),display.Item1,display.Item2,pixels);
                            records.Add(new{Image=image,Lesson=lesson.Key,Status=status.ToString(),Page=page+1,Pages=pages,
                                Body=card.Context,Text=hud.TextDiagnostics,Skin=hud.SkinDiagnostics,
                                PixelSha256=Convert.ToHexString(SHA256.HashData(pixels))});
                            guide.Navigate(GuidanceNavigation.NextPage);platform.PumpEvents();
                        }
                    }
                }
                File.WriteAllText(Path.Combine(output,"practice-guidance.json"),JsonSerializer.Serialize(new{
                    Scope="Native authored practice-card states using the existing renderer and guide controller. Simulation completion is qualified separately.",
                    Source=typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                    Captures=records,Diagnostics=graphics.Diagnostics},JsonOptions));
                return 0;
            }
            if (lessons)
            {
                foreach (var display in new[] { (1280, 720, 192u, 2f), (1920, 1080, 96u, 1f) })
                {
                    graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                    var layout = GameplayHudLayout.Create(display.Item1, display.Item2, display.Item3, display.Item4);
                    foreach (string key in new[] { "smelter-policy", "truck-roles", "mobile-loading", "belt-production", "research" })
                    {
                        var guide = new EarlyGameGuidanceController();
                        var card = guide.Update(snapshot, true, layout: layout);
                        for (int i = 0; card.ContentKey != key && i < 20; i++)
                        { guide.Navigate(GuidanceNavigation.NextSection); card = guide.Update(snapshot, true, layout: layout); }
                        if (card.ContentKey != key) throw new InvalidDataException("Missing lesson: " + key);
                        int pages = card.PageCount;
                        for (int page = 0; page < pages; page++)
                        {
                            card = guide.Update(snapshot, true, layout: layout);
                            var state = OnboardingPresentation.Resolve(layout, 0, detailed: true);
                            var ux = default(PreAlphaUxView) with { ShowOnboarding = true, Guidance = card, Onboarding = state };
                            ground.Prepare(camera, display.Item1, display.Item2);
                            void Draw(IGraphicsCommandContext context)
                            {
                                ground.Render(context, camera); meshes.Render(context, camera, world, 1);
                                context.BeginPass(GraphicsFramePass.Overlay);
                                hud.Render(context, camera, snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                    default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, ux, display.Item3, display.Item4);
                            }
                            graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
                            byte[] first = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
                            byte[] second = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
                            if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("Lesson repeat differs.");
                            if (!hud.TextDiagnostics.AtlasReady || hud.TextDiagnostics.MissingGlyphs != 0 || hud.TextDiagnostics.DroppedGlyphs != 0 ||
                                hud.SkinDiagnostics.MissingAssets != 0 || hud.SkinDiagnostics.DroppedQuads != 0)
                                throw new InvalidDataException("Incomplete lesson content.");
                            string name = $"{display.Item1}x{display.Item2}-{key}-page{page + 1}";
                            ReferenceCaptureImage.WritePng(Path.Combine(output, name + ".png"), display.Item1, display.Item2, first);
                            records.Add(new { Key = name, card.ContentKey, card.Page, card.PageCount, Body = card.Context,
                                State = state, RasterDpi = display.Item3, UiScale = display.Item4, TextSize = GuidanceCardLayout.TextSize(layout),
                                PixelSha256 = Convert.ToHexString(SHA256.HashData(first)), Text = hud.TextDiagnostics, Skin = hud.SkinDiagnostics });
                            guide.Navigate(GuidanceNavigation.NextPage);
                            platform.PumpEvents();
                        }
                    }
                }
                graphics.WaitForIdle();
                if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native lesson submission fault.");
                File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
                {
                    Source = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                    Device = graphics.Diagnostics.Device, Fixture = scene.Fingerprint, Cases = records, Health = graphics.Diagnostics.Health,
                    Contract = "All pages of five logistics and research lessons; two exact native repeats. Authored fixture only, no world commands. Human visual and live-interaction acceptance remains pending."
                }, JsonOptions));
                return 0;
            }
            foreach (var display in new[] { (1280, 720, 192u, 2f), (1920, 1080, 96u, 1f) })
            foreach (bool reduced in new[] { false, true })
            foreach (int hz in new[] { 30, 60, 144 })
            {
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                var layout = GameplayHudLayout.Create(display.Item1, display.Item2, display.Item3, display.Item4);
                guidance = new EarlyGameGuidanceController().Update(snapshot, true, layout: layout);
                var times = Enumerable.Range(0, (int)Math.Ceiling(.22 * hz) + 1).Select(i => Math.Min(220, i * 1000.0 / hz))
                    .Concat(new double[] { 0, 55, 110, 165, 220 }).Distinct().Order().ToArray();
                var pixels = new Dictionary<string, byte[]>();
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var owner = new GameplayGuidanceInteraction(); var input = new InputState();
                    owner.Update(input, snapshot.SessionId, true, layout, TimeSpan.Zero, !reduced, detailed: true);
                    for (int direction = 0; direction < 2; direction++)
                    {
                        double start = 1 + direction * 230;
                        var hit = owner.View.Tab.IsEmpty ? owner.View.VisiblePanel : owner.View.Tab;
                        input.BeginFrame();
                        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
                            PlatformMouseButton.Left, (int)hit.X + 5, (int)hit.Y + 5));
                        owner.Update(input, snapshot.SessionId, true, layout, TimeSpan.FromMilliseconds(start), !reduced, detailed: true);
                        if (!owner.BlocksGameplayThisFrame) throw new InvalidDataException("Native toggle did not retain input ownership.");
                        input.BeginFrame();
                        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, 1, 1));
                        foreach (double ms in times)
                        {
                            owner.Update(input, snapshot.SessionId, true, layout, TimeSpan.FromMilliseconds(start + ms), !reduced, detailed: true);
                            input.BeginFrame();
                            var ux = default(PreAlphaUxView) with { ShowOnboarding = true, Guidance = guidance, Onboarding = owner.View };
                            // Slowly traverse the frozen permitted fixture; never mutate simulation or dispatch a command.
                            camera.CenterOn(scene.Target + new Vector3((float)(direction * .22 + ms / 1000) * 4, 0, 0));
                            ground.Prepare(camera, display.Item1, display.Item2);
                            void Record(IGraphicsCommandContext c)
                            {
                                ground.Render(c, camera); meshes.Render(c, camera, world, 1);
                                c.BeginPass(GraphicsFramePass.Overlay);
                                hud.Render(c, camera, snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                    default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, ux, display.Item3, display.Item4);
                            }
                            graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                            if (!hud.TextDiagnostics.AtlasReady || hud.TextDiagnostics.MissingGlyphs != 0 || hud.TextDiagnostics.DroppedGlyphs != 0 ||
                                hud.SkinDiagnostics.MissingAssets != 0 || hud.SkinDiagnostics.DroppedQuads != 0)
                                throw new InvalidDataException("Incomplete native onboarding content.");
                            if (ms is 0 or 55 or 110 or 165 or 220)
                            {
                                string key = $"{display.Item1}-{reduced}-{hz}-{direction}-{ms}";
                                byte[] image = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                                if (repeat == 0) pixels.Add(key, image);
                                else if (!image.AsSpan().SequenceEqual(pixels[key])) throw new InvalidDataException("Native onboarding sequence differs: " + key);
                                if (repeat == 1)
                                {
                                    if (hz == 60) ReferenceCaptureImage.WritePng(Path.Combine(output, key + ".png"), display.Item1, display.Item2, image);
                                    records.Add(new { Key = key, Milliseconds = ms, Direction = direction == 0 ? "Closing" : "Opening", InjectedHz = hz,
                                        ReducedMotion = reduced, State = owner.View, PixelSha256 = Convert.ToHexString(SHA256.HashData(image)),
                                        Text = hud.TextDiagnostics, Skin = hud.SkinDiagnostics });
                                }
                            }
                            platform.PumpEvents();
                        }
                        if (owner.View.Collapse != (direction == 0 ? 1 : 0)) throw new InvalidDataException("Native transition did not settle exactly.");
                    }
                }
                Console.WriteLine($"[ui:onboarding:capture] {display.Item1}x{display.Item2} reduced={reduced} samplingHz={hz}");
            }
            graphics.WaitForIdle();
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native submission fault.");
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
            {
                Source = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Device = graphics.Diagnostics.Device, Fixture = scene.Fingerprint,
                Contract = "Injected presentation sampling through production controller; two identical native repeats over a slowly traversed frozen fixture. No gameplay commands. Rates are test inputs, not measured FPS. Human visual/motion acceptance is pending.",
                Cases = records, Health = graphics.Diagnostics.Health
            }, JsonOptions));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[ui:onboarding:failure] " + error); return 1; }
    }
}
