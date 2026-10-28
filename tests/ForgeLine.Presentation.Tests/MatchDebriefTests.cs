using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MatchDebriefTests
{
    [Theory]
    [InlineData(1u)] [InlineData(2u)]
    public void PublishedTerminalSnapshotCapturesRealOutcomeOnceAndAcknowledgementKeepsIt(uint loser)
    {
        using var runtime=CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation,7511) with
            {Participants=CentralDivideScenario.CreateDefaultParticipants(false,false)},TestContext.Current.CancellationToken);
        var gateway=new PlayerCommandGateway(runtime.Simulation,runtime.Services.BuildingCommands,runtime.BattlefieldRuntime.MatchStateEntity);
        var buffer=new PresentationSnapshotBuffer();
        var context=new PresentationExtractionContext(runtime,new(1),new PresentationInteractionState(),gateway);
        runtime.Simulation.RegisterTickObserver(gateway);
        runtime.Simulation.RegisterTickObserver(new PresentationExtractor(buffer,context));
        runtime.Simulation.AdvanceOneTick();Assert.True(buffer.TryReadLatest(out var active));Assert.Null(active.MatchDebrief);
        Assert.True(runtime.Simulation.Entities.DestroyEntity(runtime.GetBase(new(loser)).CommandCore));
        runtime.Simulation.AdvanceOneTick();Assert.True(buffer.TryReadLatest(out var terminal));
        var report=Assert.IsType<MatchDebriefSummary>(terminal.MatchDebrief);
        Assert.Equal(loser==1?MatchDebriefOutcome.Defeat:MatchDebriefOutcome.Victory,report.Outcome);
        Assert.Equal(terminal.SessionId.Value,report.SessionId);Assert.Equal(1UL,report.Player);
        Assert.Equal(runtime.GetMatchState().CompletedAtTick.Value,report.ResultAtTick);
        Assert.All(report.Events,item=>Assert.InRange(item.Tick,report.StartedAtTick,report.ResultAtTick!.Value));
        runtime.Simulation.AdvanceOneTick();Assert.True(buffer.TryReadLatest(out var repeated));Assert.Same(report,repeated.MatchDebrief);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrReloadFailedEvidenceNeverDisablesReturnAndRestart(bool restart)
    {
        var controller=new MatchDebriefController();var input=new InputState();var snapshot=Snapshot(null);
        var layout=GameplayHudLayout.Create(1280,720,192,2);controller.Update(input,snapshot,layout);
        controller.ShowSaved(null,"SAVED REPORT INVALID");
        var rect=MatchDebriefLayout.Control(layout,restart?1:0);
        Edge(input,PlatformInputEventKind.MouseButtonDown,rect);controller.Update(input,snapshot,layout);
        Edge(input,PlatformInputEventKind.MouseButtonUp,rect);var action=controller.Update(input,snapshot,layout);
        Assert.Equal(restart,action.Restart);Assert.Equal(!restart,action.Return);
        Assert.True(controller.View.Open);Assert.Null(controller.View.Resolve(snapshot));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void HeldRowsCannotRetargetAcrossReportSessionOrViewportReplacement(int change)
    {
        var controller=new MatchDebriefController();var input=new InputState();var snapshot=Snapshot(Report());
        var layout=GameplayHudLayout.Create(1600,900,96);controller.Update(input,snapshot,layout);
        var rect=MatchDebriefLayout.Control(layout,10);
        Edge(input,PlatformInputEventKind.MouseButtonDown,rect);controller.Update(input,snapshot,layout);
        var after=change switch {0=>Snapshot(Report()),1=>Snapshot(Report(session:10),session:10),_=>snapshot};
        var display=change==2?GameplayHudLayout.Create(1920,1080,144):layout;
        Edge(input,PlatformInputEventKind.MouseButtonUp,rect);controller.Update(input,after,display);
        Assert.Equal(-1,controller.View.Selected);
    }

    [Fact]
    public void ExitRemainsAvailableWhenOptionalEvidenceArrivesDuringAHeldGesture()
    {
        var controller=new MatchDebriefController();var input=new InputState();var layout=GameplayHudLayout.Create(1600,900,96);
        var absent=Snapshot(null);controller.Update(input,absent,layout);var rect=MatchDebriefLayout.Control(layout,0);
        Edge(input,PlatformInputEventKind.MouseButtonDown,rect);controller.Update(input,absent,layout);
        Edge(input,PlatformInputEventKind.MouseButtonUp,rect);Assert.True(controller.Update(input,Snapshot(Report()),layout).Return);
    }

    [Fact]
    public void TerminalEntryAndBlockedFramesConsumeOldHeldInputAndRestartClearsSavedView()
    {
        var controller=new MatchDebriefController();var input=new InputState();var layout=GameplayHudLayout.Create(1600,900,96);
        var snapshot=Snapshot(Report());var rect=MatchDebriefLayout.Control(layout,0);
        Edge(input,PlatformInputEventKind.MouseButtonDown,rect);Assert.True(controller.Update(input,snapshot,layout).SuppressTerminalKeys);
        Edge(input,PlatformInputEventKind.MouseButtonUp,rect);Assert.False(controller.Update(input,snapshot,layout).Return);
        Edge(input,PlatformInputEventKind.MouseButtonDown,rect);controller.Update(input,snapshot,layout,blocked:true);
        input.BeginFrame();controller.Update(input,snapshot,layout);
        Edge(input,PlatformInputEventKind.MouseButtonUp,rect);Assert.False(controller.Update(input,snapshot,layout).Return);
        controller.ShowSaved(Report(),"loaded");controller.Update(input,Snapshot(null,status:PlayerMatchStatus.Active,session:10),layout);
        Assert.False(controller.View.Open);Assert.Null(controller.View.SavedReport);
    }

    [Fact]
    public void ForeignStaleFutureAndMismatchedOutcomeReportsAreUnavailableAndNoMapTargetsExist()
    {
        var snapshot=Snapshot(Report());var view=new MatchDebriefView(true,new(9));
        Assert.NotNull(view.Resolve(snapshot));
        Assert.Null(view.Resolve(Snapshot(Report(player:2))));
        Assert.Null(view.Resolve(Snapshot(Report(session:10))));
        Assert.Null(view.Resolve(Snapshot(Report(outcome:MatchDebriefOutcome.Defeat))));
        Assert.Null((view with {SavedReport=Report(player:2)}).Resolve(snapshot));
        Assert.Equal(-1,MatchDebriefLayout.EventIndex(null,view,0));
        Assert.Null(typeof(MatchDebriefEvent).GetProperty("Position"));Assert.Null(typeof(MatchDebriefEvent).GetProperty("Entity"));
    }

    [Theory]
    [InlineData(1280,720,96u,.75f)] [InlineData(1280,720,192u,2f)]
    [InlineData(1920,1080,144u,1.5f)] [InlineData(3440,1440,192u,2f)]
    public void ControlsShareSafePanelBoundsAtAllSupportedScales(int width,int height,uint dpi,float scale)
    {
        var layout=GameplayHudLayout.Create(width,height,dpi,scale);var panel=MatchDebriefLayout.Panel(layout);
        foreach(int index in new[]{0,1,2,3,4,5,6,7,10,11,12,13,14,15})
        {
            var rect=MatchDebriefLayout.Control(layout,index);
            Assert.InRange(rect.X,panel.X,panel.Right);Assert.InRange(rect.Right,panel.X,panel.Right+.01f);
            Assert.InRange(rect.Y,panel.Y,panel.Bottom);Assert.InRange(rect.Bottom,panel.Y,panel.Bottom+.01f);
        }
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)]
    public void SharedResultRendererFitsItsExistingBudgetWithoutWarmAllocation(bool missing,bool coverage)
    {
        var report=Report();var snapshot=Snapshot(missing?null:report);var layout=GameplayHudLayout.Create(1600,900,96);
        var graphics=new SelectionOverlayRenderingTests.RecordingContext{Width=1600,Height=900};
        var ux=default(PreAlphaUxView) with {Debrief=new(true,new(9),Selected:0,Coverage:coverage,Feedback:"")};
        var context=new GameplayHudRenderContext(graphics,new RtsCamera(),snapshot,default,RtsInformationLayerView.Empty,
            default,default,default,CombatGroupOverviewView.Empty,ux,layout,96,1,null);
        using var device=new RetainedDevice();using var renderer=new OperationsHudSurface(device);
        for(int i=0;i<128;i++)renderer.Render(context);
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<256;i++)renderer.Render(context);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.InRange(renderer.LastRenderedVertexCount,1,OperationsHudSurface.MaxVertices);
        Assert.True(renderer.LastPanelTextSize>=14);
    }

    internal static MatchDebriefSummary Report(ulong session=9,ulong player=1,MatchDebriefOutcome outcome=MatchDebriefOutcome.Victory)=>
        new(1,session,player,"fixture",20,0,120,120,outcome,MatchDebriefReason.CommandCoreDestroyed,false,
        [new(20,MatchDebriefEventKind.WorkBlocked,1,"Production facility",null,MatchDebriefEvidence.First),
         new(50,MatchDebriefEventKind.WorkResumed,1,"Production facility",null,MatchDebriefEvidence.Latest),
         new(80,MatchDebriefEventKind.ScoutProduced,2,"Scout",null,MatchDebriefEvidence.First),
         new(100,MatchDebriefEventKind.FieldFuelTransferred,3,"Fuel",10,MatchDebriefEvidence.Latest),
         new(120,MatchDebriefEventKind.MatchEnded,0,"PUBLIC MATCH RESULT",null,MatchDebriefEvidence.PublicResult)]);
    internal static PresentationSnapshot Snapshot(MatchDebriefSummary? report,ulong session=9,PlayerMatchStatus status=PlayerMatchStatus.Victory)=>
        new(new(120),TimeSpan.FromMilliseconds(50),0,[],sessionId:new(session),
            playerExperience:default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(120),MatchStatus=status}){MatchDebrief=report};
    private static void Edge(InputState input,PlatformInputEventKind kind,HudRect rect)
    {input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(kind,PlatformMouseButton.Left,(int)(rect.X+rect.Width/2),(int)(rect.Y+rect.Height/2)));}
    private sealed class RetainedDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new Pipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => new Buffer(description);
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) { }
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    { public GraphicsBufferDescription Description => description; public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { } public void Dispose() { } }
}
