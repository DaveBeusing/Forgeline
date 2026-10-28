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

/// <summary>Explicit native result-view qualification using authored bounded summaries.</summary>
internal static class MatchDebriefCaptureQualification
{
    private static readonly JsonSerializerOptions s_json=new(){WriteIndented=true};
    internal static int Run(string output,string runtimeRoot)
    {
        try
        {
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Capture output must be empty.");
            Directory.CreateDirectory(output);
            var catalog=RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixture=VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat,terrain,runtimeAssets:catalog);
            using var platform=new WindowsPlatform();
            using IWindow window=platform.CreateWindow(new("FORGELINE After Action Qualification",1280,720));
            using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new(){EnableVSync=false,EnableMemoryDiagnostics=true});
            using var hud=new GameplayHudRenderer(graphics,catalog);
            using var ground=new TerrainRenderer(graphics,terrain,runtimeAssets:catalog,presentationProfile:TerrainPresentationProfile.CreateCentralDivide(catalog)){DebugChunksEnabled=false};
            using var meshes=new SimpleInstanceRenderer(graphics,catalog);
            var results=new List<object>();
            var report=Report(fixture.Snapshot.SessionId.Value,fixture.Snapshot.PlayerExperience!.Value.Player.Value);
            string savedPath=Path.Combine(output,"fixture.debrief.json");MatchDebriefSerializer.Write(savedPath,report);
            if(!MatchDebriefSerializer.TryRead(savedPath,report.Player,out var saved,out _))throw new InvalidDataException("Fixture reload failed.");
            string corruptPath=Path.Combine(output,"corrupt.debrief.json");File.WriteAllText(corruptPath,"{broken");
            if(MatchDebriefSerializer.TryRead(corruptPath,report.Player,out _,out var failure)||failure!=MatchDebriefReadFailure.InvalidData)throw new InvalidDataException("Corrupt fixture accepted.");
            foreach(var display in new[]{(1280,720,96u,1f),(1280,720,192u,2f),(1920,1080,144u,1.5f),(3440,1440,192u,.75f)})
            {
                graphics.WaitForIdle();graphics.Resize(display.Item1,display.Item2);
                var camera=new RtsCamera(RtsVisualReference.CreateCameraSettings(fixture.Target,RtsReferenceZoom.NormalGameplay));
                ground.Prepare(camera,display.Item1,display.Item2);
                foreach(string state in new[]{"Timeline","Details","Coverage","EmptyForces","Missing","Corrupt","Saved"})
                {
                    var original=fixture.Snapshot;
                    var experience=original.PlayerExperience!.Value with {Tick=new(120),MatchStatus=PlayerMatchStatus.Victory,Selection=PlayerSelectionSummary.Empty};
                    var snapshot=new PresentationSnapshot(new(120),original.TickDuration,original.SimulationEntityCount,original.Instances,
                        original.Intelligence,original.SessionId,experience){MatchDebrief=state=="Missing"?null:report};
                    var view=new MatchDebriefView(true,original.SessionId,Selected:state=="Details"?0:-1,Coverage:state=="Coverage",
                        Filter:state=="EmptyForces"?MatchDebriefCategory.Forces:MatchDebriefCategory.All,
                        SavedReport:state=="Saved"?saved:null,Feedback:state=="Corrupt"?"SAVED REPORT INVALID / CURRENT EVIDENCE RETAINED":"");
                    var ux=default(PreAlphaUxView) with {Debrief=view};
                    var buffer=new PresentationSnapshotBuffer();buffer.Publish(snapshot);var world=new RenderWorld();world.Update(buffer);
                    void Record(IGraphicsCommandContext c)
                    {
                        ground.Render(c,camera);meshes.Render(c,camera,world,1);c.BeginPass(GraphicsFramePass.Overlay);
                        hud.Render(c,camera,snapshot,terrain.WorldBounds,RtsInformationLayerView.Empty,default,default,FormationTemplate.Compact,
                            CombatGroupOverviewView.Empty,ux,display.Item3,display.Item4);
                    }
                    for(int i=0;i<4;i++)graphics.RenderFrame(GraphicsColor.ForgeLineClear,Record);
                    byte[] pixels=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Record);
                    if(!pixels.AsSpan().SequenceEqual(graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Record)))throw new InvalidDataException("Result capture changed between repeats.");
                    var text=hud.TextDiagnostics;if(!text.AtlasReady||text.MissingGlyphs!=0||text.DroppedGlyphs!=0)throw new InvalidDataException("Incomplete result text.");
                    string key=FormattableString.Invariant($"{state}-{display.Item1}x{display.Item2}-{display.Item3}dpi-{display.Item4:0.00}");
                    ReferenceCaptureImage.WritePng(Path.Combine(output,key+".png"),display.Item1,display.Item2,pixels);
                    // Exercise the exact hit rectangles in the native viewport. Platform events are authored,
                    // not a claim of physical mouse/monitor certification.
                    foreach(int control in new[]{0,1})
                    {
                        var controller=new MatchDebriefController();var input=new ForgeLine.Input.InputState();
                        var layout=GameplayHudLayout.Create(display.Item1,display.Item2,display.Item3,display.Item4);controller.Update(input,snapshot,layout);
                        if(state=="Corrupt")controller.ShowSaved(null,"SAVED REPORT INVALID");
                        var rect=MatchDebriefLayout.Control(layout,control);int x=(int)(rect.X+rect.Width/2),y=(int)(rect.Y+rect.Height/2);
                        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,x,y));controller.Update(input,snapshot,layout);
                        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Left,x,y));
                        var action=controller.Update(input,snapshot,layout);if(control==0?!action.Return:!action.Restart)throw new InvalidDataException("Native viewport exit control failed.");
                    }
                    results.Add(new{State=state,Width=display.Item1,Height=display.Item2,Dpi=display.Item3,UiScale=display.Item4,Image=key+".png",
                        Repeatable=true,ReturnAndRestart=true,PixelSha256=Convert.ToHexString(SHA256.HashData(pixels)),HudVertices=hud.LastRenderedVertexCount,Text=text});
                    Console.WriteLine("[debrief:capture] "+key);platform.PumpEvents();
                }
            }
            graphics.WaitForIdle();if(graphics.Diagnostics.Health.SubmissionFaultCount!=0)throw new InvalidDataException("Native submission failed.");
            File.WriteAllText(Path.Combine(output,"qualification.json"),JsonSerializer.Serialize(new{
                Version=typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                ClientSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))),
                PresentationSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GameplayHudRenderer).Assembly.Location))),
                GameSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MatchDebriefSummary).Assembly.Location))),
                Fixture=fixture.Fingerprint,Device=graphics.Diagnostics.Device,WindowDpi=window.Dpi,
                Contract="Authored player-owned bounded first/latest summaries over the native battlefield fixture. Not a natural match, approved golden, physical-input or monitor certification. Real result authority is covered by lifecycle integration tests.",
                Cases=results},s_json));
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("[debrief:failure] "+error);return 1;}
    }
    private static MatchDebriefSummary Report(ulong session,ulong player)=>new(1,session,player,"qualification.fixture",20,0,120,120,
        MatchDebriefOutcome.Victory,MatchDebriefReason.CommandCoreDestroyed,false,
        [new(20,MatchDebriefEventKind.WorkBlocked,1,"Production facility",null,MatchDebriefEvidence.First),
         new(50,MatchDebriefEventKind.WorkResumed,1,"Production facility",null,MatchDebriefEvidence.Latest),
         new(80,MatchDebriefEventKind.ScoutProduced,2,"Scout",null,MatchDebriefEvidence.First),
         new(100,MatchDebriefEventKind.FieldAmmunitionTransferred,3,"Ammunition",25,MatchDebriefEvidence.Latest),
         new(120,MatchDebriefEventKind.MatchEnded,0,"PUBLIC MATCH RESULT",null,MatchDebriefEvidence.PublicResult)]);
}
