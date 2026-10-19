using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Sequential same-device HUD comparison and bounded context churn; no gameplay commands.</summary>
internal static class ObjectActionRingRuntimeCapture
{
    private static readonly JsonSerializerOptions JsonOptions=new() { WriteIndented=true,IncludeFields=true };
    internal static int Run(string output,string runtimeRoot)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Ring capture output must be empty.");
            Directory.CreateDirectory(output);
            var assets=RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var scene=VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat,terrain,runtimeAssets:assets);
            using var platform=new WindowsPlatform(); using var window=platform.CreateWindow(new("FORGELINE Action Ring Qualification",1920,1080));
            using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new() { EnableVSync=false,EnableMemoryDiagnostics=true });
            using var hud=new GameplayHudRenderer(graphics,assets);
            var camera=new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target,RtsReferenceZoom.NormalGameplay));
            var layout=GameplayHudLayout.Create(1920,1080,96);
            var states=new[] { UiReferenceScenario.RingConstruction,UiReferenceScenario.RingProduction,UiReferenceScenario.RingUnits,
                UiReferenceScenario.RingLogistics,UiReferenceScenario.RingSupply,UiReferenceScenario.RingTactical,UiReferenceScenario.RingTechnology,UiReferenceScenario.RingParameters,UiReferenceScenario.GroupRing,UiReferenceScenario.GroupRingTools,UiReferenceScenario.GroupRingPartial };
            var frames=states.Select(state=>
            {
                var snapshot=ObjectActionRingCapture.Fixture(scene.Snapshot,state,1920,1080,96,1).Snapshot;
                var buffer=new PresentationSnapshotBuffer();buffer.Publish(snapshot);var world=new RenderWorld();world.Update(buffer);
                var selection=new SelectionSet();
                if(snapshot.PlayerActions?.Tactical is {} tactical) selection.Replace(tactical.SelectedEntities.ToArray());
                var groups=SelectedCombatGroup.Resolve(snapshot) is {} members
                    ? new CombatGroupOverviewView([],[],SelectedCombatGroup.Create(members,selection)) : CombatGroupOverviewView.Empty;
                return (Snapshot:snapshot,Panel:ObjectActionRingCapture.View(snapshot,state,world,camera,layout),Groups:groups);
            }).ToArray();
            var active=frames[0];bool showStatus=true;
            void Record(IGraphicsCommandContext c)
            {
                c.BeginPass(GraphicsFramePass.Overlay);
                var ring=active.Panel.Ring;
                var hover=showStatus && ring.Open && ring.Geometry.IsValid
                    ? new HoverTooltipView(active.Snapshot.SessionId,default,default,ring.Geometry.Center,true,c.Width,c.Height,layout.Scale,
                        SelectionStatus:true,SelectionRevision:ring.SelectionRevision,MenuRevision:ring.Revision) : default;
                hud.Render(c,camera,active.Snapshot,terrain.WorldBounds,RtsInformationLayerView.Empty,active.Panel,
                    default,FormationTemplate.Compact,active.Groups,default,96,1,hoverTooltip:hover);
            }
            for(int i=0;i<64;i++) { active=frames[i%frames.Length];graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record); }
            graphics.WaitForIdle();
            var before=graphics.Diagnostics;var beforeSkin=hud.SkinDiagnostics;
            var comparisons=new List<object>();
            for(int kind=0;kind<6;kind++)
            {
                active=kind>=3 ? frames[8] : frames[1];
                showStatus=kind is 2 or 5;
                if(kind is 0 or 3) active=(active.Snapshot,active.Panel with { Mode=PlayerActionPanelMode.Closed,Ring=default },active.Groups);
                for(int i=0;i<32;i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record);
                graphics.WaitForIdle();
                ulong lastFence=graphics.Diagnostics.Frame?.GpuSubmissionFence ?? 0;
                var cpu=new double[128];var gpu=new List<double>();
                for(int i=0;i<cpu.Length;i++)
                {
                    long start=Stopwatch.GetTimestamp();graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record);
                    cpu[i]=Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                    var d=graphics.Diagnostics;
                    if(d.Frame is {} frame && frame.GpuSubmissionFence>lastFence && d.GpuFrameMilliseconds is {} ms)
                    { lastFence=frame.GpuSubmissionFence;gpu.Add(ms); }
                }
                graphics.WaitForIdle();Array.Sort(cpu);gpu.Sort();
                comparisons.Add(new { Kind=kind==0 ? "SingleClosed":kind==1 ? "SingleRing":kind==2 ? "SingleRingWithStatus":kind==3 ? "GroupClosed":kind==4 ? "GroupRing":"GroupRingWithStatus",
                    Warmups=32,Samples=128,CpuSubmitP50Microseconds=cpu[64],CpuSubmitP95Microseconds=cpu[121],
                    GpuSamples=gpu.Count,GpuP50Milliseconds=gpu.Count>0 ? (double?)gpu[gpu.Count/2]:null,
                    Skin=hud.SkinDiagnostics,Text=hud.TextDiagnostics,Vertices=hud.LastRenderedVertexCount });
            }
            var motion=new ObjectActionRingMotion();var transitions=new List<object>();
            foreach(int motionContext in new[] {0,8})
            foreach(int hz in new[] {30,60,144}) foreach(bool reduced in new[] {false,true})
            {
                var ring=frames[motionContext].Panel.Ring;
                motion.Update(ring with {Open=false},TimeSpan.Zero,!reduced);
                for(int direction=0;direction<2;direction++)
                {
                    bool intermediate=false;
                    for(int i=0;i<=(int)Math.Ceiling(.16*hz);i++)
                    {
                        double ms=Math.Min(160,i*1000.0/hz);
                        var visual=motion.Update(ring with {Open=direction==0},TimeSpan.FromMilliseconds(10+direction*180+ms),!reduced);
                        intermediate |= visual.Opacity is > 0 and < 1;
                        active=(frames[motionContext].Snapshot,frames[motionContext].Panel with {Ring=visual},frames[motionContext].Groups);graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record);
                        transitions.Add(new {Group=motionContext==8,InjectedHz=hz,ReducedMotion=reduced,Direction=direction,Milliseconds=ms,visual.Opacity,visual.Open});
                    }
                    if (!reduced && !intermediate) throw new InvalidDataException("Ring opening/closing skipped its continuous alpha track.");
                    if (reduced && intermediate) throw new InvalidDataException("Reduced motion did not settle immediately.");
                }
            }
            for(int cycle=0;cycle<128;cycle++)
            {
                active=frames[cycle%frames.Length];graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record);
                active=(active.Snapshot,active.Panel with {Ring=active.Panel.Ring with {Open=false,Geometry=default},Mode=PlayerActionPanelMode.Closed},active.Groups);
                graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record);platform.PumpEvents();
            }
            graphics.WaitForIdle();var after=graphics.Diagnostics;var skin=hud.SkinDiagnostics;
            if(after.Health.LiveResourceCount!=before.Health.LiveResourceCount || skin.Buffers!=beforeSkin.Buffers ||
                after.Resources.LoadedTextureCount!=before.Resources.LoadedTextureCount || after.Resources.ShaderResourceDescriptorsUsed!=before.Resources.ShaderResourceDescriptorsUsed ||
                after.Health.SubmissionFaultCount!=0 || skin.MissingAssets!=0 || skin.DroppedQuads!=0)
                throw new InvalidDataException("Ring context churn changed retained resources or lost content.");
            File.WriteAllText(Path.Combine(output,"qualification.json"),JsonSerializer.Serialize(new {
                Source=typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Contract="Same-device current closed/ring/status HUD comparison, world excluded; CPU includes waits/Present. Sampling Hz is injected presentation time, not measured FPS. Human motion/visual acceptance pending.",
                GroupAggregation=GroupActionRingMeasurements.Capture(),ContextCycles=128,Before=before,After=after,BeforeSkin=beforeSkin,AfterSkin=skin,Comparisons=comparisons,Transitions=transitions },JsonOptions));
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine("[ui:ring:failure] "+error);return 1; }
    }
}
